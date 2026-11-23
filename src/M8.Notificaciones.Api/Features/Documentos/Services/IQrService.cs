using M8.Notificaciones.Api.Features.Documentos.Dtos;

namespace M8.Notificaciones.Api.Features.Documentos.Services;

public interface IQrService
{
    Task<QrResponse> GenerarAsync(Guid viajeId, Guid clienteId, int validezSegundos);
    Task<ResultadoValidacionQr> ConsumirAsync(string token, Guid viajeId);
}