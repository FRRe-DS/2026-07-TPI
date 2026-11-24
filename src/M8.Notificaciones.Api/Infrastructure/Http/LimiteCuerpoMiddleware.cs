using Microsoft.AspNetCore.Http.Features;

namespace M8.Notificaciones.Api.Infrastructure.Http;

/// <summary>
/// Los cuerpos admiten hasta 1 MiB (contrato de M8); más grande → 413.
///
/// Se corta temprano mirando Content-Length y, además, se fija el límite de
/// Kestrel para los cuerpos sin Content-Length (chunked). Lo hacemos acá y no
/// solo en Kestrel porque así también funciona en los tests con TestServer.
/// </summary>
public sealed class LimiteCuerpoMiddleware
{
    public const long MaximoBytes = 1024 * 1024;

    private readonly RequestDelegate _siguiente;

    public LimiteCuerpoMiddleware(RequestDelegate siguiente) => _siguiente = siguiente;

    public Task InvokeAsync(HttpContext contexto)
    {
        if (contexto.Request.ContentLength > MaximoBytes)
        {
            return ProblemasHttp.EscribirAsync(contexto, StatusCodes.Status413PayloadTooLarge,
                "El cuerpo supera el máximo de 1 MiB.");
        }

        var limite = contexto.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (limite is { IsReadOnly: false })
        {
            limite.MaxRequestBodySize = MaximoBytes;
        }

        return _siguiente(contexto);
    }
}
