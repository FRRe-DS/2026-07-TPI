namespace M8.Notificaciones.Api.Features.Viajes.Proyecciones;

/// <summary>
/// Configuración de la proyección (sección "Proyeccion" de la configuración).
/// En Docker se cambia con la variable de entorno Proyeccion__RetrasoMs.
/// </summary>
public sealed class OpcionesProyeccion
{
    public const string Seccion = "Proyeccion";

    /// <summary>
    /// Un evento recién se proyecta cuando pasaron al menos estos milisegundos
    /// desde que se registró. Simula, de forma determinista, la demora que en un
    /// sistema real meten la cola de mensajes y la carga del lector, para que
    /// la consistencia eventual se VEA en la demo. Con 0 se proyecta en la
    /// siguiente pasada.
    /// </summary>
    public int RetrasoMs { get; set; } = 3000;

    /// <summary>Cada cuánto el proyector busca eventos nuevos.</summary>
    public int IntervaloMs { get; set; } = 500;
}
