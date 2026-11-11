using Microsoft.AspNetCore.Mvc;
using M8.Notificaciones.Api.Features.Viajes.Comandos;
using M8.Notificaciones.Api.Features.Viajes.Dominio;
using M8.Notificaciones.Api.Features.Viajes.Dtos;
using M8.Notificaciones.Api.Features.Viajes.EventStore;

namespace M8.Notificaciones.Api.Features.Viajes.Controllers;

/// <summary>
/// LADO DE ESCRITURA (comandos). POST /api/v1/eventos del contrato de M8.
///
/// El controlador es delgado: recibe, llama al handler y traduce el resultado
/// o la excepción a HTTP. Toda la lógica está en RegistrarEventoViajeHandler.
///
/// Autenticación: el contrato exige un JWT de rol SERVICIO emitido por M1.
/// Todavía no está implementada en el TPI (se simula en una entrega futura).
/// </summary>
[ApiController]
[Route("api/v1/eventos")]
[Tags("Viajes · Escritura (Event Sourcing)")]
public class EventosController : ControllerBase
{
    private readonly RegistrarEventoViajeHandler _handler;

    public EventosController(RegistrarEventoViajeHandler handler)
    {
        _handler = handler;
    }

    /// <summary>
    /// Registra un hito de viaje (VIAJE_SOLICITADO, VIAJE_ASIGNADO, VIAJE_FINALIZADO)
    /// como evento inmutable. Responde 202: la vista de historial se actualiza después.
    /// </summary>
    [HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(EventoRegistradoResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest, Problemas.ContentType)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, Problemas.ContentType)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity, Problemas.ContentType)]
    public async Task<IActionResult> Registrar([FromBody] EventoEntranteRequest request, CancellationToken ct)
    {
        const string instancia = "/api/v1/eventos";
        try
        {
            var respuesta = await _handler.ManejarAsync(request, ct);
            return Accepted(respuesta);
        }
        catch (SolicitudInvalidaException ex)
        {
            return Problemas.Crear(StatusCodes.Status400BadRequest, "about:blank", "Solicitud inválida", ex.Message, instancia);
        }
        catch (ReglaDeNegocioException ex)
        {
            // Ej.: finalizar un viaje sin conductor asignado.
            return Problemas.Crear(StatusCodes.Status409Conflict, "/errores/transicion-de-viaje-invalida", ex.Titulo, ex.Message, instancia);
        }
        catch (ConflictoDeConcurrenciaException ex)
        {
            // Dos comandos compitieron por la misma versión del flujo.
            return Problemas.Crear(StatusCodes.Status409Conflict, "/errores/conflicto-de-concurrencia", "Conflicto de concurrencia", ex.Message, instancia);
        }
        catch (EventoIdReutilizadoException ex)
        {
            return Problemas.Crear(StatusCodes.Status422UnprocessableEntity, "/errores/evento-id-reutilizado", "eventoId reutilizado con otro cuerpo", ex.Message, instancia);
        }
        catch (EventoNoSoportadoException ex)
        {
            return Problemas.Crear(StatusCodes.Status422UnprocessableEntity, "/errores/evento-no-soportado", "Tipo de evento no soportado", ex.Message, instancia);
        }
    }
}
