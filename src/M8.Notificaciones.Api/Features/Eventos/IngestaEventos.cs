using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using M8.Notificaciones.Api.Features.Eventos.Dtos;
using M8.Notificaciones.Api.Features.Eventos.Entities;
using M8.Notificaciones.Api.Features.Notificaciones;
using M8.Notificaciones.Api.Features.Notificaciones.Entities;
using M8.Notificaciones.Api.Features.Notificaciones.Servicios;
using M8.Notificaciones.Api.Infrastructure.Idempotencia;
using M8.Notificaciones.Api.Infrastructure.Persistence;
using M8.Notificaciones.Api.Infrastructure.Tiempo;

namespace M8.Notificaciones.Api.Features.Eventos;

public enum EstadoIngesta
{
    /// <summary>Evento nuevo: registrado y con sus notificaciones creadas.</summary>
    Registrado,
    /// <summary>Mismo eventoId y mismo cuerpo que uno ya recibido: no se duplica nada.</summary>
    Duplicado,
    /// <summary>Mismo eventoId con otro cuerpo → 422.</summary>
    EventoIdReutilizado,
    /// <summary>No cumple el contrato → 400 (o cola de errores).</summary>
    Invalido
}

public sealed record ResultadoIngesta(
    EstadoIngesta Estado,
    EventoRecibidoResponse? Respuesta = null,
    IReadOnlyList<string>? Errores = null,
    int NotificacionesGeneradas = 0);

/// <summary>
/// Algo más que M8 hace con cada evento nuevo, además de notificar (por ejemplo,
/// el historial de viajes o, a futuro, disparar el comprobante en PAGO_CAPTURADO).
/// Corre DESPUÉS de registrar el evento: si falla, se loguea y no afecta la respuesta.
/// </summary>
public interface IReaccionEvento
{
    Task ReaccionarAsync(EventoEntrada evento, CancellationToken ct);
}

/// <summary>
/// Ingesta de eventos de M5, M6, M7 y M9 (RF-8.1, RF-8.2, RF-8.8).
///
/// La usan los dos caminos de entrada, con la misma semántica:
///   - POST /api/v1/eventos (HTTP)
///   - la cola m8.eventos de RabbitMQ (RNF-10)
///
/// Pasos:
///  1. Validar contra el contrato.
///  2. Idempotencia por eventoId (bandeja de entrada "EventosRecibidos", RNF-09).
///  3. Crear las notificaciones PENDIENTE (una por destinatario) en la MISMA
///     transacción que el registro del evento: o queda todo o no queda nada.
///  4. Recordar el contacto de cada destinatario (para manuales y reenvíos).
///  5. Avisar a las otras reacciones (historial de viajes).
///
/// No envía nada: el envío lo hace el despachador en segundo plano. Por eso
/// quien llama recibe 202 enseguida y nunca queda bloqueado por un proveedor.
/// </summary>
public sealed class IngestaEventos
{
    private static readonly JsonSerializerOptions JsonCanonico = new(JsonSerializerDefaults.Web);

    private readonly M8DbContext _db;
    private readonly FabricaNotificaciones _fabrica;
    private readonly IEnumerable<IReaccionEvento> _reacciones;
    private readonly TimeProvider _reloj;
    private readonly ILogger<IngestaEventos> _logger;

    public IngestaEventos(M8DbContext db, IOptions<OpcionesNotificaciones> opciones, IEnumerable<IReaccionEvento> reacciones,
        TimeProvider reloj, ILogger<IngestaEventos> logger)
    {
        _db = db;
        _fabrica = new FabricaNotificaciones(opciones.Value);
        _reacciones = reacciones;
        _reloj = reloj;
        _logger = logger;
    }

    public async Task<ResultadoIngesta> ProcesarAsync(EventoEntrada? evento, string via, CancellationToken ct)
    {
        // 1. Validación.
        var errores = ValidadorEvento.Validar(evento);
        if (errores.Count > 0)
        {
            return new ResultadoIngesta(EstadoIngesta.Invalido, Errores: errores);
        }
        var e = Normalizar(evento!);
        var eventoId = e.EventoId!.Value;
        var hash = HashIdempotencia.De(JsonSerializer.Serialize(e, JsonCanonico));

        // 2. Idempotencia: ¿ya lo recibimos?
        var existente = await _db.EventosRecibidos.AsNoTracking().FirstOrDefaultAsync(x => x.EventoId == eventoId, ct);
        if (existente is not null)
        {
            return CompararConExistente(existente, hash, via);
        }

        // 3. Evento + notificaciones en una sola transacción (un solo SaveChanges).
        var ahora = _reloj.Ahora();
        var ids = (e.Destinatarios ?? Array.Empty<DestinatarioEntrada>()).Select(d => d.UsuarioId!.Value).Distinct().ToList();
        var contactos = await _db.ContactosDestinatarios.AsNoTracking()
            .Where(c => ids.Contains(c.UsuarioId))
            .ToDictionaryAsync(c => c.UsuarioId, ct);

        var notificaciones = _fabrica.DesdeEvento(e, contactos, ahora);
        var registro = new EventoRecibido
        {
            EventoId = eventoId,
            Tipo = e.Tipo!.Value.ToString(),
            Origen = e.Origen!.Value.ToString(),
            HashCuerpo = hash,
            Via = via,
            OcurridoEn = e.OcurridoEn!.Value,
            RecibidoEn = ahora,
            NotificacionesGeneradas = notificaciones.Count
        };
        _db.EventosRecibidos.Add(registro);
        _db.Notificaciones.AddRange(notificaciones);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) when (!ct.IsCancellationRequested)
        {
            // Carrera: el mismo eventoId entró a la vez por otro camino (otra
            // solicitud HTTP o una reentrega de la cola). La PK lo rechazó acá;
            // releemos el que ganó y respondemos como a un duplicado.
            _db.ChangeTracker.Clear();
            var ganador = await _db.EventosRecibidos.AsNoTracking().FirstOrDefaultAsync(x => x.EventoId == eventoId, ct);
            if (ganador is null) throw;
            return CompararConExistente(ganador, hash, via);
        }

        _logger.LogInformation(
            "Evento {EventoId} {Tipo} de {Origen} recibido por {Via}: {Cantidad} notificaciones creadas",
            eventoId, registro.Tipo, registro.Origen, via, notificaciones.Count);

        // 4 y 5. Pasos secundarios: si fallan, el evento ya quedó registrado.
        await RecordarContactosAsync(e, ahora, ct);
        await EjecutarReaccionesAsync(e, ct);

        return new ResultadoIngesta(EstadoIngesta.Registrado, new EventoRecibidoResponse(eventoId, registro.RecibidoEn),
            NotificacionesGeneradas: notificaciones.Count);
    }

    private ResultadoIngesta CompararConExistente(EventoRecibido existente, string hash, string via)
    {
        if (existente.HashCuerpo == hash)
        {
            _logger.LogInformation("Evento {EventoId} repetido (por {Via}): se devuelve la respuesta original", existente.EventoId, via);
            return new ResultadoIngesta(EstadoIngesta.Duplicado, new EventoRecibidoResponse(existente.EventoId, existente.RecibidoEn));
        }
        _logger.LogWarning("Evento {EventoId} reutilizado con otro cuerpo (por {Via})", existente.EventoId, via);
        return new ResultadoIngesta(EstadoIngesta.EventoIdReutilizado,
            Errores: new[] { $"El eventoId {existente.EventoId} ya se recibió con otro contenido. Un eventoId identifica un único evento." });
    }

    /// <summary>Upsert del último email/teléfono conocido de cada destinatario.</summary>
    private async Task RecordarContactosAsync(EventoEntrada e, DateTimeOffset ahora, CancellationToken ct)
    {
        var conDatos = (e.Destinatarios ?? Array.Empty<DestinatarioEntrada>())
            .Where(d => d.Email is not null || d.Telefono is not null)
            .GroupBy(d => d.UsuarioId!.Value)
            .ToList();
        if (conDatos.Count == 0) return;

        try
        {
            var ids = conDatos.Select(g => g.Key).ToList();
            var existentes = await _db.ContactosDestinatarios.Where(c => ids.Contains(c.UsuarioId)).ToDictionaryAsync(c => c.UsuarioId, ct);
            foreach (var grupo in conDatos)
            {
                if (!existentes.TryGetValue(grupo.Key, out var contacto))
                {
                    contacto = new ContactoDestinatario { UsuarioId = grupo.Key };
                    _db.ContactosDestinatarios.Add(contacto);
                }
                contacto.Email = grupo.Select(d => d.Email).LastOrDefault(x => x is not null) ?? contacto.Email;
                contacto.Telefono = grupo.Select(d => d.Telefono).LastOrDefault(x => x is not null) ?? contacto.Telefono;
                contacto.ActualizadoEn = ahora;
            }
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            // Dos eventos del mismo usuario nuevo a la vez: uno gana y el otro no
            // actualiza. No es grave: el próximo evento lo refresca.
            _db.ChangeTracker.Clear();
            _logger.LogInformation(ex, "No se pudo actualizar el contacto registrado del evento {EventoId}", e.EventoId);
        }
    }

    private async Task EjecutarReaccionesAsync(EventoEntrada e, CancellationToken ct)
    {
        foreach (var reaccion in _reacciones)
        {
            try
            {
                await reaccion.ReaccionarAsync(e, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _db.ChangeTracker.Clear();
                _logger.LogWarning(ex, "La reacción {Reaccion} falló para el evento {EventoId}", reaccion.GetType().Name, e.EventoId);
            }
        }
    }

    /// <summary>Fechas a UTC/microsegundos: así el hash no cambia por cómo vino escrita la fecha.</summary>
    private static EventoEntrada Normalizar(EventoEntrada e) => e with
    {
        OcurridoEn = Utc.Normalizar(e.OcurridoEn!.Value),
        Detalle = e.Detalle is null ? null : e.Detalle with
        {
            FechaProgramada = e.Detalle.FechaProgramada is { } f ? Utc.Normalizar(f) : null
        }
    };
}
