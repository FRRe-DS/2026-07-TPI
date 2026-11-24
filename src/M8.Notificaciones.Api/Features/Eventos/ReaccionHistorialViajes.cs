using M8.Notificaciones.Api.Features.Eventos.Dtos;
using M8.Notificaciones.Api.Features.Notificaciones.Dominio;
using M8.Notificaciones.Api.Features.Viajes.Comandos;
using M8.Notificaciones.Api.Features.Viajes.Dtos;

namespace M8.Notificaciones.Api.Features.Eventos;

/// <summary>
/// Puente con la demo de CQRS + Event Sourcing (Features/Viajes).
///
/// Antes, POST /api/v1/eventos era SOLO del historial de viajes y respondía 422
/// a cualquier tipo que no fuera VIAJE_SOLICITADO/ASIGNADO/FINALIZADO. En el TP2
/// ese endpoint pasa a ser la entrada de notificaciones (todos los tipos del
/// contrato) y el historial se alimenta desde acá, como una reacción más.
///
/// Las reglas del historial (por ejemplo "no se puede finalizar sin asignar")
/// ya no rechazan el evento: M8 tiene que notificar igual. Si el historial no
/// lo acepta, se loguea. La demo con sus 409 sigue disponible en
/// /api/v1/lab/viajes/eventos.
/// </summary>
public sealed class ReaccionHistorialViajes : IReaccionEvento
{
    private static readonly Dictionary<TipoEvento, string> Soportados = new()
    {
        [TipoEvento.VIAJE_SOLICITADO] = RegistrarEventoViajeHandler.ViajeSolicitadoContrato,
        [TipoEvento.VIAJE_ASIGNADO] = RegistrarEventoViajeHandler.ViajeAsignadoContrato,
        [TipoEvento.VIAJE_FINALIZADO] = RegistrarEventoViajeHandler.ViajeFinalizadoContrato,
    };

    private readonly RegistrarEventoViajeHandler _handler;
    private readonly ILogger<ReaccionHistorialViajes> _logger;

    public ReaccionHistorialViajes(RegistrarEventoViajeHandler handler, ILogger<ReaccionHistorialViajes> logger)
    {
        _handler = handler;
        _logger = logger;
    }

    public async Task ReaccionarAsync(EventoEntrada evento, CancellationToken ct)
    {
        if (evento.Tipo is not { } tipo || !Soportados.TryGetValue(tipo, out var tipoContrato))
        {
            return;
        }

        var solicitud = new EventoEntranteRequest(
            evento.EventoId, tipoContrato, evento.Origen?.ToString(), evento.OcurridoEn, evento.ViajeId,
            new DetalleEventoRequest(evento.Detalle?.ConductorId));

        try
        {
            var r = await _handler.ManejarAsync(solicitud, ct);
            _logger.LogInformation("Historial de viajes: evento {EventoId} guardado en la versión {Version} del viaje {ViajeId}",
                r.EventoId, r.Version, r.ViajeId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not Microsoft.EntityFrameworkCore.DbUpdateException)
        {
            // Regla de negocio del historial, falta de conductorId, etc.: no es un error de M8.
            _logger.LogInformation("Historial de viajes no registró el evento {EventoId}: {Motivo}", evento.EventoId, ex.Message);
        }
    }
}
