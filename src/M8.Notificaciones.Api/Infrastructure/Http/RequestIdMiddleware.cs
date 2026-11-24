using System.Text.RegularExpressions;

namespace M8.Notificaciones.Api.Infrastructure.Http;

/// <summary>
/// Correlación por X-Request-Id (contrato de M8, RNF-14).
///
/// - Si la solicitud trae un X-Request-Id válido (1 a 128 caracteres: letras,
///   números, '.', '_' y '-'), se reutiliza; si no trae o es inválido, se genera
///   uno nuevo. Nunca responde 400 por este encabezado.
/// - Toda respuesta (también las de error) lo devuelve.
/// - Se abre un scope de logging con "RequestId": cada línea de log que se
///   escriba durante la solicitud queda correlacionada.
///
/// Va primero en el pipeline para que hasta los 401, 404 y 500 lleven el encabezado.
/// </summary>
public sealed partial class RequestIdMiddleware
{
    public const string Encabezado = "X-Request-Id";

    private readonly RequestDelegate _siguiente;
    private readonly ILogger<RequestIdMiddleware> _logger;

    public RequestIdMiddleware(RequestDelegate siguiente, ILogger<RequestIdMiddleware> logger)
    {
        _siguiente = siguiente;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext contexto)
    {
        var recibido = contexto.Request.Headers[Encabezado].ToString();
        var requestId = EsValido(recibido) ? recibido : Generar();

        contexto.TraceIdentifier = requestId;
        contexto.Response.OnStarting(() =>
        {
            contexto.Response.Headers[Encabezado] = requestId;
            return Task.CompletedTask;
        });

        using (_logger.BeginScope(new Dictionary<string, object> { ["RequestId"] = requestId }))
        {
            await _siguiente(contexto);
        }
    }

    public static bool EsValido(string? valor) => !string.IsNullOrEmpty(valor) && FormatoValido().IsMatch(valor);

    public static string Generar() => Guid.NewGuid().ToString("N");

    [GeneratedRegex("^[A-Za-z0-9._-]{1,128}$")]
    private static partial Regex FormatoValido();
}
