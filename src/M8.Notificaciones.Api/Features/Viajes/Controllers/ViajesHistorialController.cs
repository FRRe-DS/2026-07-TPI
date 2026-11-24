using Microsoft.AspNetCore.Mvc;
using M8.Notificaciones.Api.Features.Viajes.Consultas;
using M8.Notificaciones.Api.Features.Viajes.Dtos;

namespace M8.Notificaciones.Api.Features.Viajes.Controllers;

/// <summary>
/// LECTURA DEL LADO CQRS: solo lee la vista proyectada ViajesHistorialVista,
/// nunca la tabla de eventos. Es rápida y simple, pero puede estar atrasada.
/// </summary>
[ApiController]
[Route("api/v1/viajes/{viajeId:guid}")]
[Tags("Viajes · Lectura proyectada (CQRS)")]
public class ViajesHistorialController : ControllerBase
{
    private readonly ConsultasHistorial _consultas;

    public ViajesHistorialController(ConsultasHistorial consultas)
    {
        _consultas = consultas;
    }

    /// <summary>
    /// Historial del viaje según la vista de lectura. Si la proyección todavía
    /// no llegó, responde 200 con items vacío (no 404): eso es el retraso de CQRS.
    /// </summary>
    [HttpGet("historial")]
    [ProducesResponseType(typeof(HistorialViajeResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<HistorialViajeResponse>> ObtenerHistorial(Guid viajeId, CancellationToken ct) =>
        Ok(await _consultas.ObtenerAsync(viajeId, ct));
}
