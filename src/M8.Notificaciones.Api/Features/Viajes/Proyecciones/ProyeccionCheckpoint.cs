namespace M8.Notificaciones.Api.Features.Viajes.Proyecciones;

/// <summary>
/// Hasta dónde llegó cada proyección ("ya procesé hasta la Seq 57").
///
/// Por qué existe: si la API se reinicia o el proyector se cae, al volver
/// sigue desde UltimaSeq + 1 en vez de empezar de cero o saltearse eventos.
/// Se actualiza en la MISMA transacción que las filas de la vista: o se
/// guardan las dos cosas o ninguna.
/// </summary>
public sealed class ProyeccionCheckpoint
{
    public string Nombre { get; set; } = string.Empty;
    public long UltimaSeq { get; set; }
    public DateTimeOffset ActualizadoEn { get; set; }
}
