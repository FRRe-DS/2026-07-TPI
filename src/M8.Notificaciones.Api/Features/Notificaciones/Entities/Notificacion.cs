using M8.Notificaciones.Api.Features.Notificaciones.Dominio;

namespace M8.Notificaciones.Api.Features.Notificaciones.Entities;

/// <summary>
/// Un aviso a un destinatario por un canal, con su seguimiento de entrega (RF-8.6).
///
/// Ciclo de vida:
///   PENDIENTE ──(intento exitoso)──▶ ENVIADA
///       │
///       └─(se agotan los intentos)──▶ FALLIDA ──(reintento del OPERADOR)──▶ PENDIENTE
/// </summary>
public class Notificacion
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Evento que la originó. Null en las manuales y en los reenvíos.</summary>
    public Guid? EventoId { get; set; }
    public TipoEvento? TipoEvento { get; set; }
    public OrigenNotificacion Origen { get; set; } = OrigenNotificacion.EVENTO;

    public Guid DestinatarioId { get; set; }
    public RolDestinatario Rol { get; set; }
    public Canal Canal { get; set; }

    /// <summary>
    /// Email o teléfono a usar en el envío. Es un dato personal: nunca se loguea
    /// ni se devuelve por la API, y se borra apenas la notificación queda ENVIADA (RNF-19).
    /// Null para PUSH (se direcciona por DestinatarioId).
    /// </summary>
    public string? Contacto { get; set; }

    public string Asunto { get; set; } = string.Empty;
    public string Mensaje { get; set; } = string.Empty;

    public TipoReferencia? ReferenciaTipo { get; set; }
    public Guid? ReferenciaId { get; set; }

    public EstadoNotificacion Estado { get; set; } = EstadoNotificacion.PENDIENTE;

    /// <summary>Intentos realizados (contrato: "intentos").</summary>
    public int Intentos { get; set; }

    /// <summary>Hasta cuántos intentos puede llegar antes de quedar FALLIDA. Un reintento manual lo sube en uno.</summary>
    public int MaxIntentos { get; set; }

    /// <summary>Cuándo le toca el próximo intento. Null si no tiene intentos pendientes.</summary>
    public DateTimeOffset? ProximoIntentoEn { get; set; }

    /// <summary>
    /// "Lease": mientras una instancia de M8 está enviando, otra no la toma.
    /// Si esa instancia se cae, el lease vence y la notificación vuelve a estar disponible.
    /// </summary>
    public DateTimeOffset? BloqueadaHasta { get; set; }

    public DateTimeOffset CreadoEn { get; set; }
    public DateTimeOffset? EnviadoEn { get; set; }

    /// <summary>Motivo del último intento fallido, sin datos personales.</summary>
    public string? UltimoError { get; set; }

    /// <summary>Token de concurrencia optimista: dos procesos no pueden actualizar la misma versión (RNF-08).</summary>
    public int Version { get; set; }

    public List<IntentoNotificacion> HistorialIntentos { get; set; } = new();
}
