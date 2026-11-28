using M8.Notificaciones.Api.Features.Documentos.Entities;

namespace M8.Notificaciones.Api.Features.Documentos.Services;

public interface IComprobanteService
{
    Task<Comprobante> GenerarAsync(
        Guid viajeId,
        Guid clienteId,
        Guid? pagoId,
        string motivo,
        decimal montoTotal,
        string moneda);
}