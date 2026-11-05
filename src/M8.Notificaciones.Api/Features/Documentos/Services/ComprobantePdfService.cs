using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using M8.Notificaciones.Api.Features.Documentos.Entities;

namespace M8.Notificaciones.Api.Features.Documentos.Services;

public class ComprobantePdfService : IComprobantePdfService
{
    public byte[] Generar(Comprobante comprobante)
    {
        return Document.Create(documento =>
        {
            documento.Page(pagina =>
            {
                pagina.Size(PageSizes.A4);
                pagina.Margin(40);

                pagina.Header()
                    .Text("COMPROBANTE DE VIAJE")
                    .FontSize(22)
                    .Bold();

                pagina.Content().Column(columna =>
                {
                    columna.Spacing(10);

                    columna.Item().Text($"Comprobante: {comprobante.Id}");
                    columna.Item().Text($"Viaje: {comprobante.ViajeId}");
                    columna.Item().Text($"Cliente: {comprobante.ClienteId}");

                    if (comprobante.PagoId.HasValue)
                    {
                        columna.Item().Text($"Pago: {comprobante.PagoId}");
                    }

                    columna.Item().Text($"Motivo: {comprobante.Motivo}");
                    columna.Item().Text(
                        $"Fecha de emisión: {comprobante.FechaEmision:dd/MM/yyyy HH:mm}");

                    columna.Item()
                        .AlignRight()
                        .Text($"Importe: {comprobante.MontoTotal:N2} {comprobante.Moneda}")
                        .FontSize(16)
                        .Bold();
                });

                pagina.Footer()
                    .AlignCenter()
                    .Text("Comprobante generado por M8");
            });
        }).GeneratePdf();
    }
}