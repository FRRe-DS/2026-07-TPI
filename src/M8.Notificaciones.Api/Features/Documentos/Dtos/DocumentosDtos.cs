namespace M8.Notificaciones.Api.Features.Documentos.Dtos;

public record GenerarQrRequest(
    string ViajeId,
    string ClienteId,
    int ValidezMinutos = 10
);

public record QrResponse(
    string ViajeId,
    string TokenSeguro,
    string QrBase64,
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

