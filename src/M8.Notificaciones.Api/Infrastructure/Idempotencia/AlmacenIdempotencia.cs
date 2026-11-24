using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using StackExchange.Redis;

namespace M8.Notificaciones.Api.Infrastructure.Idempotencia;

/// <summary>Qué pasó al intentar reservar una Idempotency-Key.</summary>
public enum ResultadoReserva
{
    /// <summary>Clave nueva: quien llama ejecuta la operación y después guarda la respuesta.</summary>
    Nueva,
    /// <summary>Misma clave y mismo cuerpo, ya completada: devolver la respuesta guardada.</summary>
    Repetida,
    /// <summary>Misma clave, otro cuerpo → 422.</summary>
    CuerpoDistinto,
    /// <summary>Misma clave y mismo cuerpo, pero la primera solicitud todavía se está procesando → 409.</summary>
    EnCurso
}

public sealed record ReservaIdempotencia(ResultadoReserva Resultado, int? Status = null, string? CuerpoJson = null);

/// <summary>
/// Claves de idempotencia de los POST (encabezado Idempotency-Key del contrato, RNF-09).
/// La implementación real usa Redis con TTL (RNF-11).
/// </summary>
public interface IAlmacenIdempotencia
{
    Task<ReservaIdempotencia> ReservarAsync(string ambito, string clave, string hashCuerpo, CancellationToken ct);

    Task GuardarRespuestaAsync(string ambito, string clave, string hashCuerpo, int status, string cuerpoJson, CancellationToken ct);

    /// <summary>Libera una clave reservada cuya operación falló, para que el cliente pueda reintentar.</summary>
    Task LiberarAsync(string ambito, string clave, CancellationToken ct);
}

public static class HashIdempotencia
{
    /// <summary>SHA-256 en hexadecimal del texto (el cuerpo canónico de la solicitud).</summary>
    public static string De(string texto) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(texto)));
}

/// <summary>
/// Idempotencia en Redis.
///
/// - Reservar es atómico: SET NX. Si dos solicitudes con la misma clave llegan a
///   la vez, solo una obtiene "Nueva"; la otra ve "EnCurso" (y responde 409).
/// - Mientras se procesa, la reserva vive poco (TTL corto): si la API se cae a
///   mitad de camino, la clave se libera sola.
/// - Completada, la respuesta se guarda 24 h (TTL) y después Redis la descarta.
/// - La clave del cliente se guarda hasheada: nunca va en claro a Redis.
/// </summary>
public sealed class AlmacenIdempotenciaRedis : IAlmacenIdempotencia
{
    private static readonly TimeSpan TtlEnCurso = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan TtlCompleta = TimeSpan.FromHours(24);

    private readonly IConnectionMultiplexer _redis;

    public AlmacenIdempotenciaRedis(IConnectionMultiplexer redis) => _redis = redis;

    private sealed record Registro(string Hash, string Estado, int? Status, string? Cuerpo);

    private static RedisKey Clave(string ambito, string clave) => $"m8:idem:{ambito}:{HashIdempotencia.De(clave)}";

    public async Task<ReservaIdempotencia> ReservarAsync(string ambito, string clave, string hashCuerpo, CancellationToken ct)
    {
        var db = _redis.GetDatabase();
        var key = Clave(ambito, clave);
        var enCurso = JsonSerializer.Serialize(new Registro(hashCuerpo, "EN_CURSO", null, null));

        if (await db.StringSetAsync(key, enCurso, TtlEnCurso, When.NotExists))
        {
            return new ReservaIdempotencia(ResultadoReserva.Nueva);
        }

        var guardado = await db.StringGetAsync(key);
        if (!guardado.HasValue)
        {
            // Expiró justo entre el SET y el GET: se intenta una vez más.
            return await db.StringSetAsync(key, enCurso, TtlEnCurso, When.NotExists)
                ? new ReservaIdempotencia(ResultadoReserva.Nueva)
                : new ReservaIdempotencia(ResultadoReserva.EnCurso);
        }

        var registro = JsonSerializer.Deserialize<Registro>(guardado.ToString())!;
        if (registro.Hash != hashCuerpo)
        {
            return new ReservaIdempotencia(ResultadoReserva.CuerpoDistinto);
        }
        return registro.Estado == "COMPLETA"
            ? new ReservaIdempotencia(ResultadoReserva.Repetida, registro.Status, registro.Cuerpo)
            : new ReservaIdempotencia(ResultadoReserva.EnCurso);
    }

    public Task GuardarRespuestaAsync(string ambito, string clave, string hashCuerpo, int status, string cuerpoJson, CancellationToken ct)
    {
        var valor = JsonSerializer.Serialize(new Registro(hashCuerpo, "COMPLETA", status, cuerpoJson));
        return _redis.GetDatabase().StringSetAsync(Clave(ambito, clave), valor, TtlCompleta);
    }

    public Task LiberarAsync(string ambito, string clave, CancellationToken ct) =>
        _redis.GetDatabase().KeyDeleteAsync(Clave(ambito, clave));
}
