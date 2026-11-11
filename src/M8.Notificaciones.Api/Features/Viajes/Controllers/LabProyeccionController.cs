using Microsoft.AspNetCore.Mvc;
using M8.Notificaciones.Api.Features.Viajes.Dtos;
using M8.Notificaciones.Api.Features.Viajes.Proyecciones;

namespace M8.Notificaciones.Api.Features.Viajes.Controllers;

/// <summary>
/// LABORATORIO: endpoints de diagnóstico y administración de la proyección.
/// NO forman parte del contrato de M8; existen para la demo y para operar
/// (ver docs/DECISIONES.md). En producción irían protegidos con rol OPERADOR.
/// </summary>
[ApiController]
[Route("api/v1/lab/proyeccion")]
[Tags("Laboratorio · Proyección (fuera del contrato)")]
public class LabProyeccionController : ControllerBase
{
    private readonly ProyeccionHistorialViajes _proyeccion;
    private readonly TimeProvider _reloj;

    public LabProyeccionController(ProyeccionHistorialViajes proyeccion, TimeProvider reloj)
    {
        _proyeccion = proyeccion;
        _reloj = reloj;
    }

    /// <summary>Cuántos eventos hay en el store, cuántos ya están en la vista y cuántos faltan.</summary>
    [HttpGet("estado")]
    [ProducesResponseType(typeof(EstadoProyeccionResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<EstadoProyeccionResponse>> ObtenerEstado(CancellationToken ct) =>
        Ok(await _proyeccion.ObtenerDiagnosticoAsync(ct));

    /// <summary>Borra la vista y la vuelve a armar reproduciendo todos los eventos.</summary>
    [HttpPost("reconstruir")]
    [ProducesResponseType(typeof(ReconstruccionResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ReconstruccionResponse>> Reconstruir(CancellationToken ct)
    {
        var ahora = _reloj.GetUtcNow();
        var reproducidos = await _proyeccion.ReconstruirAsync(ahora, ct);
        return Ok(new ReconstruccionResponse(reproducidos, ahora));
    }
}
