namespace M8.Notificaciones.Api.Infrastructure.Http;

/// <summary>
/// Controles básicos de vulnerabilidades web (RNF-12, TP3). M8 es una API JSON:
/// ninguna respuesta tiene que poder ejecutarse, embeberse ni cachearse.
///
/// - X-Content-Type-Options: nosniff → el navegador no "adivina" el tipo (evita
///   que un JSON se interprete como HTML/script).
/// - X-Frame-Options: DENY y frame-ancestors 'none' → no se puede embeber (clickjacking).
/// - Referrer-Policy: no-referrer → las URLs de M8 no se filtran a terceros.
/// - Content-Security-Policy: default-src 'none' → nada que cargar ni ejecutar.
///   En Development se omite para que funcione Swagger UI.
/// - Cache-Control: no-store en /api → avisos, tickets y comprobantes no quedan
///   en cachés intermedias (RNF-19). /health no se toca.
///
/// HTTPS: en entornos publicados lo termina el proxy/ingress; la API agrega HSTS
/// (UseHsts) fuera de Development y redirige a HTTPS cuando conoce el puerto.
/// </summary>
public sealed class EncabezadosSeguridadMiddleware
{
    private readonly RequestDelegate _siguiente;
    private readonly bool _aplicarCsp;

    public EncabezadosSeguridadMiddleware(RequestDelegate siguiente, IWebHostEnvironment entorno)
    {
        _siguiente = siguiente;
        _aplicarCsp = !entorno.IsDevelopment();
    }

    public Task InvokeAsync(HttpContext contexto)
    {
        contexto.Response.OnStarting(() =>
        {
            var h = contexto.Response.Headers;
            h["X-Content-Type-Options"] = "nosniff";
            h["X-Frame-Options"] = "DENY";
            h["Referrer-Policy"] = "no-referrer";
            if (_aplicarCsp)
            {
                h["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";
            }
            if (contexto.Request.Path.StartsWithSegments("/api"))
            {
                h.CacheControl = "no-store";
            }
            return Task.CompletedTask;
        });
        return _siguiente(contexto);
    }
}
