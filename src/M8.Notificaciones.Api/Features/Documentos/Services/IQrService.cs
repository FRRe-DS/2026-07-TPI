using M8.Notificaciones.Api.Features.Documentos.Dtos;

namespace M8.Notificaciones.Api.Features.Documentos.Services;

public interface IQrService
{
    Task<QrResponse> GenerarAsync(string viajeId, int validezMinutos);
    Task<string?> ConsumirAsync(string token);
}