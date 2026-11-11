using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using M8.Notificaciones.Api.Features.Viajes.Comandos;
using M8.Notificaciones.Api.Features.Viajes.Dtos;
using M8.Notificaciones.Api.Features.Viajes.EventStore;
using M8.Notificaciones.Api.Features.Viajes.Proyecciones;
using M8.Notificaciones.Api.Infrastructure.Persistence;

namespace M8.Notificaciones.Tests.Viajes;

/// <summary>
/// Reloj controlado por el test: "ahora" es lo que el test diga.
/// Así se prueba el retraso de la proyección sin Thread.Sleep.
/// </summary>
internal sealed class RelojFijo : TimeProvider
{
    public RelojFijo(DateTimeOffset ahora) => Ahora = ahora;

    public DateTimeOffset Ahora { get; set; }

    public override DateTimeOffset GetUtcNow() => Ahora;

    public void Avanzar(TimeSpan tiempo) => Ahora += tiempo;
}

/// <summary>Arma las piezas de la feature sobre una base en memoria.</summary>
internal sealed class EscenarioViajes : IDisposable
{
    public static readonly DateTimeOffset Inicio = new(2026, 11, 11, 13, 0, 0, TimeSpan.Zero);

    public EscenarioViajes(int retrasoMs = 3000, M8DbContext? db = null)
    {
        Db = db ?? TestDb.Crear();
        Reloj = new RelojFijo(Inicio);
        Opciones = Options.Create(new OpcionesProyeccion { RetrasoMs = retrasoMs });
        Store = new EventStoreViajes(Db);
        Handler = new RegistrarEventoViajeHandler(Store, Reloj);
        Proyeccion = new ProyeccionHistorialViajes(Db, Opciones);
    }

    public M8DbContext Db { get; }
    public RelojFijo Reloj { get; }
    public IOptions<OpcionesProyeccion> Opciones { get; }
    public EventStoreViajes Store { get; }
    public RegistrarEventoViajeHandler Handler { get; }
    public ProyeccionHistorialViajes Proyeccion { get; }

    public Task<EventoRegistradoResponse> Registrar(EventoEntranteRequest request) => Handler.ManejarAsync(request);

    /// <summary>Registra solicitado → asignado → finalizado para un viaje.</summary>
    public async Task<Guid> RegistrarViajeCompleto(Guid? viajeId = null, Guid? conductorId = null)
    {
        var id = viajeId ?? Guid.NewGuid();
        await Registrar(Solicitudes.Solicitado(id));
        await Registrar(Solicitudes.Asignado(id, conductorId ?? Guid.NewGuid()));
        await Registrar(Solicitudes.Finalizado(id));
        return id;
    }

    public void Dispose() => Db.Dispose();
}

/// <summary>Cuerpos de POST /api/v1/eventos listos para usar.</summary>
internal static class Solicitudes
{
    public static readonly DateTimeOffset Ocurrido = new(2026, 11, 11, 10, 0, 0, TimeSpan.FromHours(-3));

    public static EventoEntranteRequest Solicitado(Guid viajeId, Guid? eventoId = null) =>
        new(eventoId ?? Guid.NewGuid(), "VIAJE_SOLICITADO", "M5", Ocurrido, viajeId, null);

    public static EventoEntranteRequest Asignado(Guid viajeId, Guid conductorId, Guid? eventoId = null) =>
        new(eventoId ?? Guid.NewGuid(), "VIAJE_ASIGNADO", "M5", Ocurrido.AddMinutes(2), viajeId, new DetalleEventoRequest(conductorId));

    public static EventoEntranteRequest Finalizado(Guid viajeId, Guid? eventoId = null) =>
        new(eventoId ?? Guid.NewGuid(), "VIAJE_FINALIZADO", "M6", Ocurrido.AddMinutes(30), viajeId, null);
}

/// <summary>
/// Simula que PostgreSQL rechaza el INSERT por un índice único (lo que pasa
/// cuando dos comandos compiten). EF InMemory no hace cumplir índices, así
/// que lo forzamos con un interceptor de SaveChanges.
/// </summary>
internal sealed class FallaAlGuardar : SaveChangesInterceptor
{
    public static M8DbContext CrearDb() => new(
        new DbContextOptionsBuilder<M8DbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new FallaAlGuardar())
            .Options);

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
        throw new DbUpdateException("duplicate key value violates unique constraint \"IX_EventosViaje_ViajeId_Version\"");
}
