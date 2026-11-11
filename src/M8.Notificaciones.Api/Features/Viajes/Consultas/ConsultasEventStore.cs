using System.Text.Json;
using M8.Notificaciones.Api.Features.Viajes.Dominio;
using M8.Notificaciones.Api.Features.Viajes.Dtos;
using M8.Notificaciones.Api.Features.Viajes.EventStore;

namespace M8.Notificaciones.Api.Features.Viajes.Consultas;

/// <summary>
/// Consultas del lado EVENT SOURCING: leen directamente el event store.
///
/// - ObtenerEstadoAsync: rehidrata el agregado (reproduce todos los eventos) y
///   devuelve el estado. Siempre está al día (consistencia inmediata), pero el
///   costo crece con la cantidad de eventos del flujo: es lo que en un sistema
///   con flujos largos se resuelve con snapshots.
/// - ObtenerFlujoAsync: el historial "tal cual pasó", evento por evento.
///
/// Contraste con ConsultasHistorial (lado CQRS), que nunca toca EventosViaje.
/// </summary>
public sealed class ConsultasEventStore
{
    private readonly EventStoreViajes _store;

    public ConsultasEventStore(EventStoreViajes store)
    {
        _store = store;
    }

    /// <returns>null si el viaje no tiene eventos (→ 404).</returns>
    public async Task<EstadoViajeResponse?> ObtenerEstadoAsync(Guid viajeId, CancellationToken ct = default)
    {
        var flujo = await _store.LeerFlujoAsync(viajeId, ct);
        if (flujo.Count == 0)
        {
            return null;
        }

        var viaje = Viaje.Rehidratar(flujo.Select(SerializadorEventos.Deserializar));

        return new EstadoViajeResponse(
            viaje.Id,
            viaje.Estado!.Value.ToString(),
            viaje.ConductorId,
            viaje.Version,
            EventosReproducidos: flujo.Count,
            new HitosViajeResponse(viaje.SolicitadoEn, viaje.AsignadoEn, viaje.FinalizadoEn));
    }

    /// <returns>null si el viaje no tiene eventos (→ 404).</returns>
    public async Task<FlujoEventosResponse?> ObtenerFlujoAsync(Guid viajeId, CancellationToken ct = default)
    {
        var flujo = await _store.LeerFlujoAsync(viajeId, ct);
        if (flujo.Count == 0)
        {
            return null;
        }

        var eventos = flujo
            .Select(e => new EventoDelFlujoResponse(
                e.Seq, e.EventoId, e.Version, e.Tipo, e.Origen, e.OcurridoEn, e.RegistradoEn,
                // Devolvemos el payload como objeto JSON, no como un string con comillas escapadas.
                JsonDocument.Parse(e.Payload).RootElement.Clone()))
            .ToList();

        return new FlujoEventosResponse(viajeId, eventos.Count, eventos);
    }
}
