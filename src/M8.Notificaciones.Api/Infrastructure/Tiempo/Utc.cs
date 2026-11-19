namespace M8.Notificaciones.Api.Infrastructure.Tiempo;

/// <summary>
/// Fechas listas para guardar en PostgreSQL (timestamptz): en UTC (Npgsql exige
/// offset 0) y truncadas a microsegundos (lo que guarda PostgreSQL), para que una
/// fecha releída de la base sea igual a la original. Mismo criterio que
/// Features/Viajes/EventStore/FechasUtc.
/// </summary>
public static class Utc
{
    private const long TicksPorMicrosegundo = TimeSpan.TicksPerMillisecond / 1000;

    public static DateTimeOffset Normalizar(DateTimeOffset fecha)
    {
        var utc = fecha.ToUniversalTime();
        return new DateTimeOffset(utc.Ticks - (utc.Ticks % TicksPorMicrosegundo), TimeSpan.Zero);
    }

    public static DateTimeOffset Ahora(this TimeProvider reloj) => Normalizar(reloj.GetUtcNow());
}
