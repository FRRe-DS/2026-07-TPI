using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using M8.Notificaciones.Api.Features.Viajes.Dominio;
using M8.Notificaciones.Api.Features.Viajes.Dtos;
using M8.Notificaciones.Api.Features.Viajes.EventStore;
using M8.Notificaciones.Api.Infrastructure.Persistence;

namespace M8.Notificaciones.Api.Features.Viajes.Proyecciones;

/// <summary>
/// La PROYECCIÓN: convierte eventos (lado de escritura) en filas de la vista
/// de historial (lado de lectura). Es el puente entre Event Sourcing y CQRS.
///
/// Está separada del BackgroundService a propósito: acá no hay timers ni
/// hilos, solo "dado este 'ahora', proyectá lo que corresponda". Por eso los
/// tests pueden inyectar el tiempo y probar el retraso sin Thread.Sleep.
///
/// Garantías:
///  - Orden: procesa por Seq creciente y avanza el checkpoint.
///  - Atomicidad: las filas nuevas y el checkpoint se guardan en UN SaveChanges,
///    y EF Core ejecuta cada SaveChanges dentro de una transacción.
///  - Idempotencia: si la fila (ViajeId, Version) ya existe, se saltea. Así,
///    reprocesar un evento (por un reinicio a mitad de camino) no duplica nada.
/// </summary>
public sealed class ProyeccionHistorialViajes
{
    public const string NombreCheckpoint = "HistorialViajes";
    private const int TamanioLote = 100;

    // Evita que, dentro de este proceso, el proyector y una reconstrucción
    // escriban la vista al mismo tiempo. (Con varias instancias de la API
    // habría que usar un lock de base de datos; para la demo hay una sola.)
    private static readonly SemaphoreSlim Candado = new(1, 1);

    private readonly M8DbContext _db;
    private readonly OpcionesProyeccion _opciones;

    public ProyeccionHistorialViajes(M8DbContext db, IOptions<OpcionesProyeccion> opciones)
    {
        _db = db;
        _opciones = opciones.Value;
    }

    /// <summary>
    /// Proyecta los eventos registrados hace al menos RetrasoMs. Devuelve
    /// cuántos eventos procesó.
    ///
    /// El retraso es una CARACTERÍSTICA de CQRS, no un error: el modelo de
    /// lectura se actualiza después que el de escritura (consistencia eventual).
    /// Acá lo hacemos configurable y determinista para poder mostrarlo y medirlo.
    /// </summary>
    public async Task<int> ProyectarPendientesAsync(DateTimeOffset ahora, CancellationToken ct = default)
    {
        await Candado.WaitAsync(ct);
        try
        {
            var limite = ahora - TimeSpan.FromMilliseconds(_opciones.RetrasoMs);
            return await ProyectarLoteAsync(limite, ahora, ct);
        }
        finally
        {
            Candado.Release();
        }
    }

    /// <summary>
    /// Borra la vista, pone el checkpoint en 0 y vuelve a proyectar TODO desde
    /// los eventos (sin retraso: es una operación de administración).
    ///
    /// Esto demuestra que la vista es descartable: la verdad está en los eventos.
    /// También es lo que se haría para agregar una vista nueva o corregir un
    /// bug en la proyección. Si se corta a mitad de camino, no pasa nada: el
    /// proyector sigue desde el checkpoint y la proyección es idempotente.
    /// </summary>
    public async Task<int> ReconstruirAsync(DateTimeOffset ahora, CancellationToken ct = default)
    {
        await Candado.WaitAsync(ct);
        try
        {
            // Paso 1: vaciar la vista y reiniciar el checkpoint (un SaveChanges = una transacción).
            // Se cargan y borran con RemoveRange (y no con ExecuteDelete) para que funcione también
            // con EF InMemory en los tests; para una demo el volumen es chico.
            _db.ViajesHistorialVista.RemoveRange(await _db.ViajesHistorialVista.ToListAsync(ct));
            var checkpoint = await ObtenerCheckpointAsync(ct);
            checkpoint.UltimaSeq = 0;
            checkpoint.ActualizadoEn = ahora;
            await _db.SaveChangesAsync(ct);
            _db.ChangeTracker.Clear();

            // Paso 2: reproducir todos los eventos, lote por lote.
            var total = 0;
            int procesados;
            do
            {
                procesados = await ProyectarLoteAsync(DateTimeOffset.MaxValue, ahora, ct);
                total += procesados;
            } while (procesados > 0);

            return total;
        }
        finally
        {
            Candado.Release();
        }
    }

    /// <summary>Números para GET /api/v1/lab/proyeccion/estado.</summary>
    public async Task<EstadoProyeccionResponse> ObtenerDiagnosticoAsync(CancellationToken ct = default)
    {
        var checkpoint = await _db.ProyeccionCheckpoints.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Nombre == NombreCheckpoint, ct);
        var ultimaSeq = checkpoint?.UltimaSeq ?? 0;

        var eventosEnStore = await _db.EventosViaje.CountAsync(ct);
        var pendientes = await _db.EventosViaje.CountAsync(e => e.Seq > ultimaSeq, ct);
        var proyectados = await _db.ViajesHistorialVista.CountAsync(ct);

        return new EstadoProyeccionResponse(eventosEnStore, proyectados, pendientes, _opciones.RetrasoMs, checkpoint?.ActualizadoEn);
    }

    private async Task<int> ProyectarLoteAsync(DateTimeOffset limiteRegistro, DateTimeOffset ahora, CancellationToken ct)
    {
        var checkpoint = await ObtenerCheckpointAsync(ct);

        var candidatos = await _db.EventosViaje
            .AsNoTracking()
            .Where(e => e.Seq > checkpoint.UltimaSeq)
            .OrderBy(e => e.Seq)
            .Take(TamanioLote)
            .ToListAsync(ct);

        var procesados = 0;
        foreach (var evento in candidatos)
        {
            // Todavía es "muy nuevo": paramos acá y NO seguimos con los siguientes,
            // para que el checkpoint nunca se adelante dejando un evento atrás.
            if (evento.RegistradoEn > limiteRegistro)
            {
                break;
            }

            var yaProyectado = await _db.ViajesHistorialVista.AnyAsync(
                v => v.ViajeId == evento.ViajeId && v.Version == evento.Version, ct);
            if (!yaProyectado)
            {
                _db.ViajesHistorialVista.Add(CrearFila(evento, ahora));
            }

            checkpoint.UltimaSeq = evento.Seq;
            checkpoint.ActualizadoEn = ahora;
            procesados++;
        }

        if (procesados > 0)
        {
            // Filas de la vista + checkpoint: todo en la misma transacción.
            await _db.SaveChangesAsync(ct);
        }

        return procesados;
    }

    private async Task<ProyeccionCheckpoint> ObtenerCheckpointAsync(CancellationToken ct)
    {
        var checkpoint = await _db.ProyeccionCheckpoints.FirstOrDefaultAsync(c => c.Nombre == NombreCheckpoint, ct);
        if (checkpoint is null)
        {
            checkpoint = new ProyeccionCheckpoint { Nombre = NombreCheckpoint, UltimaSeq = 0 };
            _db.ProyeccionCheckpoints.Add(checkpoint);
        }
        return checkpoint;
    }

    /// <summary>Traduce un evento a una fila "lista para mostrar".</summary>
    private static ViajeHistorialItem CrearFila(EventoAlmacenado fila, DateTimeOffset ahora)
    {
        var evento = SerializadorEventos.Deserializar(fila);
        var (descripcion, estado, conductorId) = evento switch
        {
            ViajeSolicitado => ($"Viaje solicitado (informado por {fila.Origen})", EstadoViaje.SOLICITADO, (Guid?)null),
            ConductorAsignado a => ($"Conductor {a.ConductorId.ToString()[..8]} asignado (informado por {fila.Origen})", EstadoViaje.ASIGNADO, a.ConductorId),
            _ => ($"Viaje finalizado (informado por {fila.Origen})", EstadoViaje.FINALIZADO, (Guid?)null)
        };

        return new ViajeHistorialItem
        {
            ViajeId = fila.ViajeId,
            Version = fila.Version,
            Evento = fila.Tipo,
            Descripcion = descripcion,
            EstadoResultante = estado.ToString(),
            ConductorId = conductorId,
            OcurridoEn = fila.OcurridoEn,
            RegistradoEn = fila.RegistradoEn,
            ProyectadoEn = FechasUtc.Normalizar(ahora)
        };
    }
}
