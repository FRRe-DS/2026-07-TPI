using Microsoft.AspNetCore.Mvc;
using M8.Notificaciones.Api.Features.Documentos.Dtos;
using M8.Notificaciones.Api.Features.Documentos.Services;
using Microsoft.EntityFrameworkCore;
using M8.Notificaciones.Api.Infrastructure.Persistence;
using M8.Notificaciones.Api.Features.Documentos.Errors;
using Microsoft.AspNetCore.Authorization;
using M8.Notificaciones.Api.Infrastructure.Seguridad;
using M8.Notificaciones.Api.Features.Notificaciones.Dominio;

namespace M8.Notificaciones.Api.Features.Documentos.Controllers;

[ApiController]
[Route("api/[controller]")]
[Tags("Documentos y QR (Célula 2)")]
[Authorize(Roles = Roles.Cliente + "," + Roles.Operador)]
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
    [Authorize(Roles = Roles.Cliente + "," + Roles.Operador)]
    public async Task<ActionResult<PaginaComprobantesResponse>> ListarComprobantes(
        [FromQuery] Guid? viajeId,
        [FromQuery] Guid? clienteId,
        [FromQuery] int page = 0,
        [FromQuery] int size = 20)
    {
        if (page < 0 || size is < 1 or > 100)
        {
            return BadRequest(Problemas.SolicitudInvalida(
                "'page' debe ser 0 o mayor y 'size' estar entre 1 y 100."));
        }

        var consulta = _db.Comprobantes.AsQueryable();

        if (viajeId.HasValue)
            consulta = consulta.Where(c => c.ViajeId == viajeId.Value);

        if (clienteId.HasValue)
            consulta = consulta.Where(c => c.ClienteId == clienteId.Value);

        var totalElementos = await consulta.CountAsync();

        var comprobantes = await consulta
            .OrderByDescending(c => c.CreadoEn)
            .Skip(page * size)
            .Take(size)
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
            c.CreadoEn)).ToList();

        var totalPaginas = totalElementos == 0
            ? 0
            : (int)Math.Ceiling(totalElementos / (double)size);

        return Ok(new PaginaComprobantesResponse(
            items,
            page,
            size,
            totalElementos,
            totalPaginas));
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
    [HttpPost("~/api/v1/comprobantes/{comprobanteId:guid}/reenvios")]
    [Authorize(Roles = Roles.Cliente + "," + Roles.Operador)]
    public async Task<IActionResult> ReenviarComprobante(
        Guid comprobanteId,
        [FromBody] ReenviarComprobanteRequest request)
    {
        if (request.Canal is null)
        {
            return BadRequest(Problemas.SolicitudInvalida(
                "El campo 'canal' es obligatorio."));
        }

        var comprobante = await _db.Comprobantes
            .AsNoTracking()
            .SingleOrDefaultAsync(c => c.Id == comprobanteId);

        if (comprobante is null)
            return NotFound(Problemas.NoEncontrado("El comprobante no existe."));

        if (comprobante.Estado != "GENERADO")
        {
            return Problemas.Crear(
                StatusCodes.Status409Conflict,
                "/errores/comprobante-no-generado",
                "El comprobante todavía no está generado",
                "El comprobante debe estar en estado GENERADO antes de reenviarlo.");
        }

        _logger.LogInformation(
            "Reenvío solicitado para el comprobante {ComprobanteId} por canal {Canal}",
            comprobanteId,
            request.Canal);

        return Accepted(new
        {
            Mensaje = $"Reenvío del comprobante {comprobanteId} encolado por {request.Canal}"
        });
    }
}