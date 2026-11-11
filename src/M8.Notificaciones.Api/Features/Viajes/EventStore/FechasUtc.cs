namespace M8.Notificaciones.Api.Features.Viajes.EventStore;

/// <summary>
/// Normaliza fechas antes de guardarlas.
///
/// Dos motivos concretos:
///  1. Npgsql exige que los DateTimeOffset que van a una columna timestamptz
///     tengan offset 0 (UTC). Un "2026-11-11T10:00:00-03:00" se convierte a
///     "2026-11-11T13:00:00Z".
///  2. PostgreSQL guarda microsegundos; .NET tiene "ticks" de 100 ns. Si no
///     truncamos, la fecha que mandó el cliente (7 decimales) no es igual a la
///     que después leemos de la base (6 decimales), y la comparación de
///     idempotencia ("¿es el mismo cuerpo?") daría un falso 422.
/// </summary>
public static class FechasUtc
{
    private const long TicksPorMicrosegundo = TimeSpan.TicksPerMillisecond / 1000;

    public static DateTimeOffset Normalizar(DateTimeOffset fecha)
    {
        var utc = fecha.ToUniversalTime();
        return new DateTimeOffset(utc.Ticks - (utc.Ticks % TicksPorMicrosegundo), TimeSpan.Zero);
    }
}
