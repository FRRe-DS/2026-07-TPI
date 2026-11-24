using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using M8.Notificaciones.Api.Infrastructure.Http;

namespace M8.Notificaciones.Api.Infrastructure.Idempotencia;

/// <summary>
/// Envuelve un POST con la regla de Idempotency-Key del contrato:
///   - Falta la clave o tiene más de 255 caracteres → 400.
///   - Misma clave y mismo cuerpo → la respuesta original, sin repetir el efecto.
///   - Misma clave y otro cuerpo → 422.
///   - Misma clave mientras la primera sigue en curso → 409.
/// Solo se recuerdan las respuestas exitosas (2xx): si la operación falla, la
/// clave se libera y el cliente puede volver a intentar con la misma.
/// </summary>
public sealed class EjecucionIdempotente
{
    public const string Encabezado = "Idempotency-Key";

    private readonly IAlmacenIdempotencia _almacen;
    private readonly JsonSerializerOptions _json;

    public EjecucionIdempotente(IAlmacenIdempotencia almacen, IOptions<JsonOptions> json)
    {
        _almacen = almacen;
        _json = json.Value.JsonSerializerOptions;
    }

    /// <param name="ambito">Operación + quién llama: las claves de dos clientes distintos no chocan.</param>
    /// <param name="cuerpoCanonico">Lo que identifica "el mismo pedido" (el cuerpo, o el id del recurso si no hay cuerpo).</param>
    public async Task<IActionResult> EjecutarAsync(string ambito, string? clave, object? cuerpoCanonico, string instancia,
        Func<Task<IActionResult>> operacion, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(clave) || clave.Length > 255)
        {
            return ProblemasHttp.Generico(StatusCodes.Status400BadRequest,
                $"Falta el encabezado {Encabezado} (1 a 255 caracteres; se recomienda un UUID).", instancia);
        }

        var hash = HashIdempotencia.De(JsonSerializer.Serialize(cuerpoCanonico, _json));
        var reserva = await _almacen.ReservarAsync(ambito, clave, hash, ct);
        switch (reserva.Resultado)
        {
            case ResultadoReserva.Repetida:
                return new ContentResult
                {
                    StatusCode = reserva.Status,
                    Content = reserva.CuerpoJson,
                    ContentType = "application/json; charset=utf-8"
                };
            case ResultadoReserva.CuerpoDistinto:
                return ProblemasHttp.Dominio(StatusCodes.Status422UnprocessableEntity, "/errores/idempotency-key-reutilizada",
                    "Idempotency-Key reutilizada con otro cuerpo",
                    "Esa Idempotency-Key ya se usó con otro contenido. Generá una clave nueva para un pedido distinto.", instancia);
            case ResultadoReserva.EnCurso:
                return ProblemasHttp.Dominio(StatusCodes.Status409Conflict, "/errores/idempotencia-en-curso",
                    "Solicitud en curso", "Ya se está procesando una solicitud con esa Idempotency-Key. Reintentá en unos segundos.", instancia);
        }

        IActionResult resultado;
        try
        {
            resultado = await operacion();
        }
        catch
        {
            await _almacen.LiberarAsync(ambito, clave, CancellationToken.None);
            throw;
        }

        if (resultado is ObjectResult { StatusCode: >= 200 and < 300 } exito)
        {
            var json = JsonSerializer.Serialize(exito.Value, exito.Value?.GetType() ?? typeof(object), _json);
            await _almacen.GuardarRespuestaAsync(ambito, clave, hash, exito.StatusCode!.Value, json, ct);
        }
        else
        {
            await _almacen.LiberarAsync(ambito, clave, ct);
        }
        return resultado;
    }
}
