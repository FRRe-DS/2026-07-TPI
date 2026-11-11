namespace M8.Notificaciones.Api.Features.Viajes.EventStore;

/// <summary>
/// Una fila de la tabla EventosViaje: el registro de eventos (event store).
///
/// La tabla es APPEND-ONLY: el código solo inserta filas, nunca las actualiza
/// ni las borra (no existe ningún método para eso). El estado actual de un
/// viaje NO está guardado en ningún lado: se calcula reproduciendo estas filas.
///
/// Columnas:
///  - Seq: número global y creciente (identidad de PostgreSQL). Da el orden
///    total de todos los eventos y es lo que usa el proyector como checkpoint
///    ("ya procesé hasta la Seq 57").
///  - EventoId: lo genera quien emite el evento (M5/M6). Índice único: es la
///    clave de idempotencia del contrato.
///  - ViajeId + Version: posición del evento dentro del flujo de su viaje.
///    Índice único: si dos comandos quieren escribir la versión 3 del mismo
///    viaje a la vez, uno falla (concurrencia optimista, sin bloqueos).
///  - Tipo + Payload: el evento de dominio serializado a JSON. Se guarda como
///    texto (no jsonb) para que los tests con EF InMemory funcionen igual.
///  - OcurridoEn: cuándo pasó en el mundo real (lo dice el emisor).
///  - RegistradoEn: cuándo lo guardó M8. El proyector usa esta fecha para el
///    retraso de la proyección.
/// </summary>
public sealed class EventoAlmacenado
{
    public long Seq { get; set; }
    public Guid EventoId { get; set; }
    public Guid ViajeId { get; set; }
    public int Version { get; set; }
    public string Tipo { get; set; } = string.Empty;

    /// <summary>Módulo que informó el evento (M5, M6...). Sirve para auditoría.</summary>
    public string Origen { get; set; } = string.Empty;

    public string Payload { get; set; } = string.Empty;
    public DateTimeOffset OcurridoEn { get; set; }
    public DateTimeOffset RegistradoEn { get; set; }
}
