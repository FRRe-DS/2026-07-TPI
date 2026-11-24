namespace M8.Notificaciones.Api.Features.Documentos.Dtos;
using System.ComponentModel.DataAnnotations;

public record GenerarQrRequest( // viajeid en el endpoint
    Guid ClienteId,
    [property: Range(30, 3600)] int ValidezSegundos = 600
);

public record QrResponse(
    Guid QrId,
    Guid ViajeId,
    string Estado,
    string Token,
    string ImagenPng,
    DateTime ExpiraEn
);

public record ReenviarComprobanteRequest(
    string EmailDestino
);

public record ImporteResponse(
    decimal Monto,
    string Moneda
);

public record ComprobanteResponse(
    Guid ComprobanteId,
    Guid ViajeId,
    Guid ClienteId,
    Guid? PagoId,
    string Motivo,
    string Estado,
    ImporteResponse Importe,
    DateTime? EmitidoEn,
    string? UrlPdf,
    DateTime CreadoEn
);

public record PaginaComprobantesResponse(
    IReadOnlyCollection<ComprobanteResponse> Items,
    int Pagina,
    int Tamano,
    int Total
);

public record ValidarQrRequest(
    string Token
);

public record ValidarQrResponse(
    bool Valido,
    string ViajeId
);

