using M8.Notificaciones.Api.Infrastructure.Http;
using M8.Notificaciones.Api.Infrastructure.Persistence;

namespace M8.Notificaciones.Api.Infrastructure.Salud;

/// <summary>
/// GET /health del contrato (RNF-14). No exige token.
/// 200 {"estado":"ok"} si el servicio y su base responden; si la base no
/// responde dentro de 3 segundos, 503 con Retry-After (nunca queda colgado).
/// </summary>
public static class SaludEndpoints
{
    private static readonly TimeSpan Espera = TimeSpan.FromSeconds(3);

    public static IEndpointRouteBuilder MapSalud(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health", async (HttpContext contexto, M8DbContext db, ILoggerFactory loggers) =>
            {
                using var limite = CancellationTokenSource.CreateLinkedTokenSource(contexto.RequestAborted);
                limite.CancelAfter(Espera);

                bool baseDisponible;
                try
                {
                    baseDisponible = await db.Database.CanConnectAsync(limite.Token);
                }
                catch (Exception ex) when (ex is OperationCanceledException || ProblemasHttp.EsIndisponibilidad(ex))
                {
                    baseDisponible = false;
                }

                if (!baseDisponible)
                {
                    loggers.CreateLogger("M8.Salud").LogWarning("Health check: la base de M8 no responde");
                    contexto.Response.Headers.RetryAfter = "5";
                    await ProblemasHttp.EscribirAsync(contexto, StatusCodes.Status503ServiceUnavailable,
                        "La base de M8 no está disponible.");
                    return;
                }

                await contexto.Response.WriteAsJsonAsync(new { estado = "ok" });
            })
            .AllowAnonymous()
            .WithTags("Salud")
            .WithName("verificarSalud");

        return app;
    }
}
