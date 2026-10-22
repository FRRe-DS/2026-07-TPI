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

public record ComprobanteResponse(
    string ComprobanteId,
    string ViajeId,
    DateTime FechaEmision,
    decimal MontoTotal,
    string Moneda,
    string UrlDescargaPdf
);

public record ValidarQrRequest(
    string Token
);

public record ValidarQrResponse(
    bool Valido,
    string ViajeId
);

