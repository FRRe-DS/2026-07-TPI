using Microsoft.AspNetCore.Mvc;
using M8.Notificaciones.Api.Features.Viajes.Consultas;
using M8.Notificaciones.Api.Features.Viajes.Dtos;

namespace M8.Notificaciones.Api.Features.Viajes.Controllers;

/// <summary>
/// LECTURA DEL LADO EVENT SOURCING: estas consultas leen el event store.
///  - /estado reconstruye el viaje reproduciendo sus eventos (rehidratación).
///  - /eventos muestra el flujo crudo, el "historial según Event Sourcing".
/// Siempre están al día, pero cuestan más a medida que el flujo crece.
/// </summary>
[ApiController]
[Route("api/v1/viajes/{viajeId:guid}")]
[Tags("Viajes · Lectura del event store (Event Sourcing)")]
public class ViajesEventStoreController : ControllerBase
{
    private readonly ConsultasEventStore _consultas;

    public ViajesEventStoreController(ConsultasEventStore consultas)
    {
        _consultas = consultas;
    }

    /// <summary>Estado actual del viaje, reconstruido reproduciendo todos sus eventos.</summary>
    [HttpGet("estado")]
    [ProducesResponseType(typeof(EstadoViajeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, Problemas.ContentType)]
    public async Task<IActionResult> ObtenerEstado(Guid viajeId, CancellationToken ct)
    {
        var estado = await _consultas.ObtenerEstadoAsync(viajeId, ct);
        return estado is null
            ? Problemas.NoEncontrado($"El viaje {viajeId} no tiene eventos registrados.", $"/api/v1/viajes/{viajeId}/estado")
            : Ok(estado);
    }

    /// <summary>Flujo de eventos del viaje, en orden de versión.</summary>
    [HttpGet("eventos")]
    [ProducesResponseType(typeof(FlujoEventosResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, Problemas.ContentType)]
    public async Task<IActionResult> ObtenerEventos(Guid viajeId, CancellationToken ct)
    {
        var flujo = await _consultas.ObtenerFlujoAsync(viajeId, ct);
        return flujo is null
            ? Problemas.NoEncontrado($"El viaje {viajeId} no tiene eventos registrados.", $"/api/v1/viajes/{viajeId}/eventos")
            : Ok(flujo);
    }
}
