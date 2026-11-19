using M8.Notificaciones.Api.Features.Notificaciones.Dominio;

namespace M8.Notificaciones.Api.Features.Notificaciones.Entities;

/// <summary>
/// Un intento de entrega. Es historial: solo se agregan filas, nunca se
/// modifican ni se borran (RF-8.6). (NotificacionId, Numero) es único, así que
/// dos procesos no pueden registrar el mismo intento.
/// </summary>
public class IntentoNotificacion
{
    public long Id { get; set; }
    public Guid NotificacionId { get; set; }
    public int Numero { get; set; }
    public Canal Canal { get; set; }
    public ResultadoIntento Resultado { get; set; }

    /// <summary>Solo si Resultado es ERROR. Sin datos personales.</summary>
    public string? Error { get; set; }

    public DateTimeOffset RealizadoEn { get; set; }
}
