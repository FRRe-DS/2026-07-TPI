namespace M8.Notificaciones.Api.Features.Viajes.Proyecciones;

/// <summary>
/// Una fila de ViajesHistorialVista: el MODELO DE LECTURA de CQRS.
///
/// Es una tabla "desnormalizada" y pensada para la consulta: ya trae la
/// descripción en texto, el estado resultante y las fechas, así el endpoint de
/// historial hace un SELECT simple sin reproducir eventos ni hacer joins.
///
/// Es descartable: si se borra entera, se reconstruye desde EventosViaje
/// (POST /api/v1/lab/proyeccion/reconstruir). La fuente de verdad son los eventos.
///
/// Clave primaria compuesta (ViajeId, Version): es a la vez el índice único que
/// hace idempotente la proyección (el mismo evento no puede generar dos filas).
/// </summary>
public sealed class ViajeHistorialItem
{
    public Guid ViajeId { get; set; }
    public int Version { get; set; }

    /// <summary>Nombre del evento de dominio (ViajeSolicitado, ConductorAsignado, ViajeFinalizado).</summary>
    public string Evento { get; set; } = string.Empty;

    public string Descripcion { get; set; } = string.Empty;

    /// <summary>Estado del viaje después de este evento: permite armar el resumen sin recalcular nada.</summary>
    public string EstadoResultante { get; set; } = string.Empty;

    /// <summary>Solo se completa en la fila de ConductorAsignado.</summary>
    public Guid? ConductorId { get; set; }

    public DateTimeOffset OcurridoEn { get; set; }
    public DateTimeOffset RegistradoEn { get; set; }

    /// <summary>Cuándo la proyección escribió esta fila. ProyectadoEn - RegistradoEn = retraso visible de CQRS.</summary>
    public DateTimeOffset ProyectadoEn { get; set; }
}
