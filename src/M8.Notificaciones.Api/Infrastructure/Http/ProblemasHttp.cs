using System.Net.Sockets;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using StackExchange.Redis;

namespace M8.Notificaciones.Api.Infrastructure.Http;

/// <summary>
/// Errores application/problem+json (RFC 9457) como pide el contrato de M8.
///
/// Los genéricos (400, 401, 403, 404, 405, 413, 415, 500, 503) usan
/// type "about:blank" y el título en español. Los de dominio (409, 422) los
/// arma cada controlador con su propio type (ver <see cref="Dominio"/>).
/// </summary>
public static class ProblemasHttp
{
    public const string ContentType = "application/problem+json";

    /// <summary>Títulos de los errores genéricos, tal como los define el contrato (esquema ProblemaGenerico).</summary>
    public static string TituloGenerico(int status) => status switch
    {
        400 => "Solicitud inválida",
        401 => "No autenticado",
        403 => "Sin permiso",
        404 => "No encontrado",
        405 => "Método no permitido",
        413 => "Cuerpo demasiado grande",
        415 => "Tipo de contenido no soportado",
        503 => "Servicio no disponible",
        _ when status >= 500 => "Error interno",
        _ => "Error"
    };

    public static ObjectResult Generico(int status, string? detalle, string? instancia) =>
        Crear(status, "about:blank", TituloGenerico(status), detalle, instancia);

    public static ObjectResult Dominio(int status, string type, string titulo, string? detalle, string? instancia) =>
        Crear(status, type, titulo, detalle, instancia);

    private static ObjectResult Crear(int status, string type, string titulo, string? detalle, string? instancia)
    {
        var problema = new ProblemDetails
        {
            Status = status,
            Type = type,
            Title = titulo,
            Detail = detalle,
            Instance = instancia
        };
        var resultado = new ObjectResult(problema) { StatusCode = status };
        resultado.ContentTypes.Add(ContentType);
        return resultado;
    }

    /// <summary>Escribe un problema genérico directo en la respuesta (para middlewares, fuera de MVC).</summary>
    public static Task EscribirAsync(HttpContext contexto, int status, string? detalle = null)
    {
        contexto.Response.StatusCode = status;
        var problema = new ProblemDetails
        {
            Status = status,
            Type = "about:blank",
            Title = TituloGenerico(status),
            Detail = detalle,
            Instance = contexto.Request.Path
        };
        return contexto.Response.WriteAsJsonAsync(problema, options: null, contentType: ContentType);
    }

    /// <summary>
    /// 400 por cuerpo o parámetros mal formados (incluye un id de ruta que no es UUID).
    /// Reemplaza al ValidationProblemDetails por defecto de ASP.NET para respetar el contrato.
    /// </summary>
    public static IActionResult RespuestaModeloInvalido(ActionContext contexto)
    {
        var errores = contexto.ModelState
            .Where(e => e.Value?.Errors.Count > 0)
            .Select(e => string.IsNullOrEmpty(e.Key) ? "cuerpo" : e.Key)
            .Distinct()
            .ToList();
        var detalle = errores.Count == 0
            ? "La solicitud tiene un formato inválido."
            : $"Campos con formato inválido: {string.Join(", ", errores)}.";
        return Generico(StatusCodes.Status400BadRequest, detalle, contexto.HttpContext.Request.Path);
    }

    /// <summary>
    /// Completa con problem+json las respuestas de error que salen sin cuerpo:
    /// 401 y 403 de la autenticación, 404 de rutas inexistentes, 405 (con Allow) y 415.
    /// </summary>
    public static Task CompletarRespuestaSinCuerpo(StatusCodeContext contexto)
    {
        var respuesta = contexto.HttpContext.Response;
        if (respuesta.HasStarted || respuesta.ContentLength > 0 || !string.IsNullOrEmpty(respuesta.ContentType))
        {
            return Task.CompletedTask;
        }
        return EscribirAsync(contexto.HttpContext, respuesta.StatusCode);
    }

    /// <summary>
    /// Manejador global de excepciones (RNF-13): nunca expone detalles internos.
    /// - Base de datos o Redis no disponibles → 503 con Retry-After.
    /// - Cuerpo demasiado grande leído por Kestrel → 413.
    /// - Cualquier otra → 500.
    /// </summary>
    public static async Task ManejarExcepcion(HttpContext contexto)
    {
        var excepcion = contexto.Features.Get<IExceptionHandlerFeature>()?.Error;
        var logger = contexto.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("M8.Errores");

        if (excepcion is BadHttpRequestException malFormada)
        {
            await EscribirAsync(contexto, malFormada.StatusCode);
            return;
        }

        if (EsIndisponibilidad(excepcion))
        {
            logger.LogWarning(excepcion, "Dependencia no disponible al atender {Ruta}", contexto.Request.Path);
            contexto.Response.Headers.RetryAfter = "5";
            await EscribirAsync(contexto, StatusCodes.Status503ServiceUnavailable,
                "Una dependencia de M8 no está disponible. Reintentá en unos segundos.");
            return;
        }

        logger.LogError(excepcion, "Error no controlado al atender {Ruta}", contexto.Request.Path);
        await EscribirAsync(contexto, StatusCodes.Status500InternalServerError);
    }

    /// <summary>¿La excepción (o alguna interna) indica que PostgreSQL o Redis no responden?</summary>
    public static bool EsIndisponibilidad(Exception? excepcion)
    {
        for (var e = excepcion; e is not null; e = e.InnerException)
        {
            if (e is NpgsqlException or RedisConnectionException or RedisTimeoutException or SocketException or TimeoutException)
            {
                return true;
            }
        }
        return false;
    }
}
