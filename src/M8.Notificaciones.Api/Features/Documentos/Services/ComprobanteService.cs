using M8.Notificaciones.Api.Features.Documentos.Entities;
using M8.Notificaciones.Api.Infrastructure.Persistence;

namespace M8.Notificaciones.Api.Features.Documentos.Services;

public class ComprobanteService : IComprobanteService
{
    private readonly M8DbContext _db;
    private readonly IComprobantePdfService _pdfService;
    private readonly ILogger<ComprobanteService> _logger;

    public ComprobanteService(
        M8DbContext db,
        IComprobantePdfService pdfService,
        ILogger<ComprobanteService> logger)
    {
        _db = db;
        _pdfService = pdfService;
        _logger = logger;
    }

    public async Task<Comprobante> GenerarAsync(
        Guid viajeId,
        Guid clienteId,
        Guid? pagoId,
        string motivo,
        decimal montoTotal,
        string moneda)
    {
        var comprobante = new Comprobante
        {
            ViajeId = viajeId,
            ClienteId = clienteId,
            PagoId = pagoId,
            Motivo = motivo,
            Estado = "PENDIENTE",
            MontoTotal = montoTotal,
            Moneda = moneda,
            CreadoEn = DateTime.UtcNow
        };

        _db.Comprobantes.Add(comprobante);
        await _db.SaveChangesAsync();

        try
        {
            _pdfService.Generar(comprobante);

            comprobante.Estado = "GENERADO";
            comprobante.EmitidoEn = DateTime.UtcNow;
            comprobante.UrlPdf =
                $"/api/v1/comprobantes/{comprobante.Id}/pdf";
        }
        catch (Exception ex)
        {
            comprobante.Estado = "ERROR";
            _logger.LogError(
                ex,
                "No se pudo generar el comprobante {ComprobanteId}",
                comprobante.Id);
        }

        await _db.SaveChangesAsync();
        return comprobante;
    }
}