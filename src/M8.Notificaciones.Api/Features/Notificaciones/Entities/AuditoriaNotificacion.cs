using System.Text.Json.Serialization;
using M8.Notificaciones.Api.Features.Notificaciones.Dominio;

namespace M8.Notificaciones.Api.Features.Notificaciones.Entities;

/// <summary>
/// Trazabilidad de las operaciones críticas hechas por una persona o un servicio
/// sobre una notificación (RNF-19, TP3): quién la pidió, cuándo y con qué
/// X-Request-Id, para poder cruzarlo con los logs.
///
/// Solo crece (no se modifica ni se borra) y no guarda datos personales: el
/// actor es el client/usuario del token (azp, client_id o sub), nunca un email.
/// Las notificaciones que nacen de un evento ya quedan trazadas por
/// EventosRecibidos (eventoId, vía y emisor).
/// </summary>
public class AuditoriaNotificacion
{
    public long Id { get; set; }
    public Guid NotificacionId { get; set; }
    public AccionAuditoria Accion { get; set; }
    public string Actor { get; set; } = string.Empty;
    public string? RequestId { get; set; }
    public DateTimeOffset RealizadaEn { get; set; }
}

/// <summary>Operaciones que se auditan (dato interno, no está en el contrato).</summary>
[JsonConverter(typeof(EnumContrato<AccionAuditoria>))]
public enum AccionAuditoria
{
    /// <summary>POST /api/v1/notificaciones de un OPERADOR.</summary>
    ALTA_MANUAL,
    /// <summary>POST /api/v1/notificaciones/{id}/reintentos de un OPERADOR.</summary>
    REINTENTO_MANUAL,
    /// <summary>Reenvío de un comprobante (RF-8.5), pedido desde Documentos.</summary>
    REENVIO
}
