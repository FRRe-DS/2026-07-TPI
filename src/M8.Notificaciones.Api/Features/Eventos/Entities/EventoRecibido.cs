namespace M8.Notificaciones.Api.Features.Eventos.Entities;

/// <summary>
/// Bandeja de entrada (patrón "inbox"): un registro por eventoId recibido.
///
/// Es la base de la idempotencia del consumidor (RNF-09): la PK es el eventoId,
/// así que aunque el mismo evento llegue dos veces (reintento HTTP, reentrega de
/// RabbitMQ, o por los dos caminos a la vez) solo uno se registra y solo ese
/// genera notificaciones. Se guarda el hash del cuerpo para distinguir
/// "mismo evento repetido" (respuesta original) de "eventoId reutilizado" (422).
/// No guarda datos personales: solo ids, tipo y hash.
/// </summary>
public class EventoRecibido
{
    public Guid EventoId { get; set; }
    public string Tipo { get; set; } = string.Empty;
    public string Origen { get; set; } = string.Empty;
    public string HashCuerpo { get; set; } = string.Empty;

    /// <summary>Por dónde llegó: HTTP o RABBITMQ.</summary>
    public string Via { get; set; } = string.Empty;

    /// <summary>
    /// Quién lo informó (RNF-19): el client del token si llegó por HTTP (por
    /// ejemplo "grupo-05"), o el app_id del mensaje si llegó por RabbitMQ.
    /// Null si no se pudo saber.
    /// </summary>
    public string? EmitidoPor { get; set; }

    public DateTimeOffset OcurridoEn { get; set; }
    public DateTimeOffset RecibidoEn { get; set; }
    public int NotificacionesGeneradas { get; set; }
}
