namespace M8.Notificaciones.Api.Features.Documentos.Dtos;
using System.ComponentModel.DataAnnotations;

public record GenerarQrRequest( // viajeid en el endpoint
    Guid ClienteId,
    [Range(30, 3600)] int ValidezSegundos = 600
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

//añado el viajeId
public record ValidacionQrNueva(
    [Required, StringLength(256, MinimumLength = 1)] string Token,
    Guid ViajeId
);

// respuesta 200
public record ValidacionQr(
    Guid QrId,
    Guid ViajeId,
    DateTime UsadoEn
);

public enum EstadoValidacionQr
{
    Exito,
    NoExiste,
    YaUsado,
    Reemplazado,
    Expirado,
    ViajeNoCoincide
}
public record ResultadoValidacionQr(
    EstadoValidacionQr Estado,
    ValidacionQr? Datos = null
);