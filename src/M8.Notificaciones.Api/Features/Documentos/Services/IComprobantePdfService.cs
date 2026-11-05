using M8.Notificaciones.Api.Features.Documentos.Entities;

namespace M8.Notificaciones.Api.Features.Documentos.Services;

public interface IComprobantePdfService
{
    byte[] Generar(Comprobante comprobante);
}