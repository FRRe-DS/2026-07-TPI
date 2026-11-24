using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using M8.Notificaciones.Api.Features.Eventos.Dtos;
using M8.Notificaciones.Api.Infrastructure.Http;
using M8.Notificaciones.Api.Infrastructure.Seguridad;

namespace M8.Notificaciones.Api.Features.Eventos.Controllers;

/// <summary>
/// POST /api/v1/eventos (operationId recibirEvento). Solo rol SERVICIO.
/// M5, M6, M7 y M9 informan acá cada hito; M8 lo registra y responde 202 sin
/// esperar a enviar nada. El mismo evento también puede llegar por RabbitMQ.
/// </summary>
[ApiController]
[Route("api/v1/eventos")]
[Tags("Eventos")]
[Authorize(Roles = Roles.Servicio)]
public sealed class EventosController : ControllerBase
{
    private const string Instancia = "/api/v1/eventos";

    private readonly IngestaEventos _ingesta;

    public EventosController(IngestaEventos ingesta) => _ingesta = ingesta;

    /// <summary>Informar un hito de viaje, reserva o pago.</summary>
    [HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(EventoRecibidoResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest, ProblemasHttp.ContentType)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized, ProblemasHttp.ContentType)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden, ProblemasHttp.ContentType)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity, ProblemasHttp.ContentType)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable, ProblemasHttp.ContentType)]
    public async Task<IActionResult> Recibir([FromBody] EventoEntrada evento, CancellationToken ct)
    {
        var resultado = await _ingesta.ProcesarAsync(evento, "HTTP", ct);
        return resultado.Estado switch
        {
            EstadoIngesta.Registrado or EstadoIngesta.Duplicado => Accepted(resultado.Respuesta),
            EstadoIngesta.EventoIdReutilizado => ProblemasHttp.Dominio(StatusCodes.Status422UnprocessableEntity,
                "/errores/evento-id-reutilizado", "eventoId reutilizado con otro cuerpo", resultado.Errores![0], Instancia),
            _ => ProblemasHttp.Generico(StatusCodes.Status400BadRequest, string.Join(" ", resultado.Errores!), Instancia)
        };
    }
}
