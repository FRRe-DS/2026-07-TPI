using Microsoft.AspNetCore.Mvc;
using M8.Notificaciones.Api.Features.Documentos.Dtos;
using M8.Notificaciones.Api.Features.Documentos.Services;
using M8.Notificaciones.Api.Features.Documentos.Errors;

namespace M8.Notificaciones.Api.Features.Documentos.Controllers;

[ApiController]
[Route("api/[controller]")]
[Tags("Documentos y QR (Célula 2)")]
public class DocumentosController : ControllerBase
{
    private readonly ILogger<DocumentosController> _logger;
    private readonly IQrService _qrService;

    public DocumentosController(ILogger<DocumentosController> logger, IQrService qrService)
    {
        _logger = logger;
        _qrService = qrService;
    }

    /// <summary>
    /// Genera un código QR temporal y de un solo uso para verificar el inicio del viaje (RF-8.3)
    /// </summary>
    [HttpPost("~/api/v1/viajes/{viajeId}/qr")]
    public async Task<ActionResult<QrResponse>> GenerarQr(Guid viajeId, [FromBody] GenerarQrRequest request)
    {
        _logger.LogInformation("Generando token QR para Viaje {ViajeId}", viajeId);

        var respuesta = await _qrService.GenerarAsync(viajeId, request.ClienteId, request.ValidezSegundos);
        return StatusCode(StatusCodes.Status201Created, respuesta);

    }

    /// <summary>
    /// Valida y consume un QR de inicio de viaje, una sola vez (RF-8.3)
    /// </summary>
    [HttpPost("~/api/v1/qr/validaciones")]
    [ProducesResponseType(typeof(ValidacionQr), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<IActionResult> ValidarQr([FromBody] ValidacionQrNueva request)
    {
        if (request.ViajeId == Guid.Empty)
        {
            return Problemas.SolicitudInvalida("viajeId es obligatorio.");
        }

        var resultado = await _qrService.ConsumirAsync(request.Token, request.ViajeId);

        return resultado.Estado switch
        {
            EstadoValidacionQr.Exito => Ok(resultado.Datos),

            EstadoValidacionQr.NoExiste =>
                Problemas.NoEncontrado("El token del QR no existe."),

            EstadoValidacionQr.YaUsado =>
                Problemas.Crear(StatusCodes.Status409Conflict, "/errores/qr-usado",
                    "El QR ya fue usado"),

            EstadoValidacionQr.Reemplazado =>
                Problemas.Crear(StatusCodes.Status409Conflict, "/errores/qr-reemplazado",
                    "El QR fue reemplazado por uno más nuevo"),

            EstadoValidacionQr.Expirado =>
                Problemas.Crear(StatusCodes.Status422UnprocessableEntity, "/errores/qr-expirado",
                    "El QR expiró"),

            EstadoValidacionQr.ViajeNoCoincide =>
                Problemas.Crear(StatusCodes.Status422UnprocessableEntity, "/errores/qr-viaje-no-coincide",
                    "El QR no corresponde al viaje indicado"),

            _ => throw new InvalidOperationException($"Estado de validación no manejado: {resultado.Estado}")
        };
    }

    /// <summary>
    /// Descarga el comprobante PDF de un viaje finalizado (RF-8.4)
    /// </summary>
    [HttpGet("comprobantes/{viajeId}/pdf")]
    public IActionResult DescargarComprobantePdf(string viajeId)
    {
        _logger.LogInformation("Solicitada descarga de comprobante para viaje {ViajeId}", viajeId);

        // Retorno simulado mientras se implementa QuestPDF
        var fakePdfBytes = System.Text.Encoding.UTF8.GetBytes($"%PDF-1.4 Mock Comprobante Viaje: {viajeId}");
        return File(fakePdfBytes, "application/pdf", $"comprobante-{viajeId}.pdf");
    }

    /// <summary>
    /// Reenvía el comprobante por correo electrónico (RF-8.5)
    /// </summary>
    [HttpPost("comprobantes/{viajeId}/reenviar")]
    public IActionResult ReenviarComprobante(string viajeId, [FromBody] ReenviarComprobanteRequest request)
    {
        _logger.LogInformation("Reenviando comprobante del viaje {ViajeId} a {Email}", viajeId, request.EmailDestino);

        return Accepted(new { Mensaje = $"Comprobante del viaje {viajeId} encolado para reenvío a {request.EmailDestino}" });
    }
}