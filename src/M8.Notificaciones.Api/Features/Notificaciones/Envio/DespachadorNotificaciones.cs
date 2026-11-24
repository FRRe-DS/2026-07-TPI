using Microsoft.Extensions.Options;
using M8.Notificaciones.Api.Infrastructure.Http;

namespace M8.Notificaciones.Api.Features.Notificaciones.Envio;

/// <summary>
/// Proceso en segundo plano que, cada IntervaloSondeoMs, le pide a
/// <see cref="ServicioEnvio"/> que haga los intentos vencidos.
///
/// Es la pieza que hace que enviar avisos "nunca bloquee al módulo que avisa":
/// la ingesta solo deja la notificación PENDIENTE y responde; este proceso la
/// entrega después. Si la base no responde, espera y vuelve a probar sin
/// tumbar la API (RNF-13).
/// </summary>
public sealed class DespachadorNotificaciones : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly OpcionesEnvio _opciones;
    private readonly TimeProvider _reloj;
    private readonly ILogger<DespachadorNotificaciones> _logger;

    public DespachadorNotificaciones(IServiceScopeFactory scopes, IOptions<OpcionesNotificaciones> opciones, TimeProvider reloj,
        ILogger<DespachadorNotificaciones> logger)
    {
        _scopes = scopes;
        _opciones = opciones.Value.Envio;
        _reloj = reloj;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_opciones.Habilitado)
        {
            _logger.LogInformation("Despachador de notificaciones deshabilitado por configuración");
            return;
        }

        _logger.LogInformation("Despachador de notificaciones iniciado (cada {Intervalo} ms, hasta {Max} intentos)",
            _opciones.IntervaloSondeoMs, _opciones.MaxIntentos);

        var espera = TimeSpan.FromMilliseconds(_opciones.IntervaloSondeoMs);
        while (!stoppingToken.IsCancellationRequested)
        {
            var hechos = 0;
            try
            {
                // Un scope por pasada: el BackgroundService es singleton y el DbContext es scoped.
                await using var scope = _scopes.CreateAsyncScope();
                var servicio = scope.ServiceProvider.GetRequiredService<ServicioEnvio>();
                hechos = await servicio.ProcesarPendientesAsync(_reloj.GetUtcNow(), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                if (ProblemasHttp.EsIndisponibilidad(ex))
                    _logger.LogWarning("Despachador: la base no está disponible; se reintenta en 5 s");
                else
                    _logger.LogError(ex, "Despachador: error inesperado; se reintenta en 5 s");
                espera = TimeSpan.FromSeconds(5);
            }

            // Si el lote vino lleno, seguimos enseguida; si no, esperamos el intervalo.
            if (hechos < _opciones.TamanoLote)
            {
                try { await Task.Delay(espera, _reloj, stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
            espera = TimeSpan.FromMilliseconds(_opciones.IntervaloSondeoMs);
        }
    }
}
