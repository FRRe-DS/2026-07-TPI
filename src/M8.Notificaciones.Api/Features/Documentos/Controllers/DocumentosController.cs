using Microsoft.AspNetCore.Mvc;
using M8.Notificaciones.Api.Features.Documentos.Dtos;
using M8.Notificaciones.Api.Features.Documentos.Services;
using Microsoft.EntityFrameworkCore;
using M8.Notificaciones.Api.Infrastructure.Persistence;

namespace M8.Notificaciones.Api.Features.Documentos.Controllers;

[ApiController]
[Route("api/[controller]")]
[Tags("Documentos y QR (Célula 2)")]
public class DocumentosController : ControllerBase
{
    private readonly ILogger<DocumentosController> _logger;
    private readonly IQrService _qrService;
    private readonly M8DbContext _db;
    private readonly IComprobantePdfService _pdfService;

    public DocumentosController(
        ILogger<DocumentosController> logger,
        IQrService qrService,
        M8DbContext db,
        IComprobantePdfService pdfService)
    {
        _logger = logger;
        _qrService = qrService;
        _db = db;
        _pdfService = pdfService;
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
    
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [HttpGet("~/api/v1/comprobantes/{comprobanteId:guid}/pdf")]
    public async Task<IActionResult> DescargarComprobantePdf(Guid comprobanteId)
    {
        _logger.LogInformation(
            "Solicitada descarga del comprobante {ComprobanteId}",
            comprobanteId);

        var comprobante = await _db.Comprobantes
            .SingleOrDefaultAsync(c => c.Id == comprobanteId);

        if (comprobante is null)
        {
            return NotFound();
        }

        if (comprobante.Estado != "GENERADO")
        {
            var problema = new ProblemDetails
            {
                Type = "/errores/comprobante-no-generado",
                Title = "El comprobante todavía no está generado",
                Detail = "El comprobante debe estar en estado GENERADO antes de descargarlo.",
                Status = StatusCodes.Status409Conflict,
                Instance = HttpContext.Request.Path
            };

            return Conflict(problema);
        }

        var pdfBytes = _pdfService.Generar(comprobante);

        return File(
            pdfBytes,
            "application/pdf",
            $"comprobante-{comprobante.Id}.pdf");
    }

    [HttpGet("~/api/v1/comprobantes")]
    public async Task<ActionResult<PaginaComprobantesResponse>> ListarComprobantes(
        Guid? viajeId,
        Guid? clienteId,
        int pagina = 1,
        int tamano = 20)
    {
        var consulta = _db.Comprobantes.AsQueryable();

        if (viajeId.HasValue)
            consulta = consulta.Where(c => c.ViajeId == viajeId.Value);

        if (clienteId.HasValue)
            consulta = consulta.Where(c => c.ClienteId == clienteId.Value);

        var total = await consulta.CountAsync();

        var comprobantes = await consulta
            .OrderByDescending(c => c.CreadoEn)
            .Skip((pagina - 1) * tamano)
            .Take(tamano)
            .ToListAsync();

        var items = comprobantes.Select(c => new ComprobanteResponse(
            c.Id,
            c.ViajeId,
            c.ClienteId,
            c.PagoId,
            c.Motivo,
            c.Estado,
            new ImporteResponse(c.MontoTotal, c.Moneda),
            c.EmitidoEn,
            c.UrlPdf,
            c.CreadoEn));

        return Ok(new PaginaComprobantesResponse(
            items.ToList(),
            pagina,
            tamano,
            total));
    }

    [HttpGet("~/api/v1/comprobantes/{comprobanteId:guid}")]
    [ProducesResponseType(typeof(ComprobanteResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ComprobanteResponse>> ObtenerComprobante(
        Guid comprobanteId)
    {
        var c = await _db.Comprobantes
            .SingleOrDefaultAsync(x => x.Id == comprobanteId);

        if (c is null)
            return NotFound();

        return Ok(new ComprobanteResponse(
            c.Id,
            c.ViajeId,
            c.ClienteId,
            c.PagoId,
            c.Motivo,
            c.Estado,
            new ImporteResponse(c.MontoTotal, c.Moneda),
            c.EmitidoEn,
            c.UrlPdf,
            c.CreadoEn));
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
