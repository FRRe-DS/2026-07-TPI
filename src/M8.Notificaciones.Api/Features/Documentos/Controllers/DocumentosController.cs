using Microsoft.AspNetCore.Mvc;
using M8.Notificaciones.Api.Features.Documentos.Dtos;
using M8.Notificaciones.Api.Features.Documentos.Services;

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
    /// valida y consume un Qr solo una vez (RF.8.3)
    /// </summary>

    [HttpPost("qr/validar")]
    [ProducesResponseType(typeof(ValidarQrResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status410Gone)]

    public async Task<ActionResult<ValidarQrResponse>> ValidarQr([FromBody] ValidarQrRequest request)
    {
        var viajeId = await _qrService.ConsumirAsync(request.Token);

        if (viajeId is null)
        {
            return StatusCode(StatusCodes.Status410Gone,
                new{mensaje = "el QR no es valido"});
        }
        return Ok(new ValidarQrResponse(true, viajeId));
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
