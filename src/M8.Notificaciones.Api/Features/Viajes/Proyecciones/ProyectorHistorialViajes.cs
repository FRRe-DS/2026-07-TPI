using Microsoft.Extensions.Options;

namespace M8.Notificaciones.Api.Features.Viajes.Proyecciones;

/// <summary>
/// Proceso en segundo plano que mantiene actualizada la vista de historial.
///
/// Cada IntervaloMs (500 ms por defecto) crea un scope nuevo, pide la
/// ProyeccionHistorialViajes y le dice "proyectá lo pendiente con este ahora".
/// No tiene lógica propia: toda la lógica está en ProyeccionHistorialViajes.
///
/// Por qué IServiceScopeFactory: un BackgroundService es singleton (vive todo
/// lo que vive la app) y el DbContext es scoped (uno por operación). Si
/// inyectáramos el DbContext directo, quedaría uno solo, viejo, compartido para
/// siempre. Con un scope por pasada cada pasada usa un DbContext fresco.
///
/// Por qué NO usamos RabbitMQ acá (aunque está en el docker-compose): la
/// proyección "lee la tabla de eventos cada medio segundo" (polling). Es lo
/// más simple de explicar y de probar, y no agrega piezas que puedan fallar en
/// la demo. La evolución natural (TP2) es publicar cada evento en una cola
/// después de guardarlo (con el patrón outbox para no perder ninguno) y que el
/// proyector se suscriba; el resto del diseño (checkpoint, idempotencia,
/// vista descartable) no cambia.
///
/// Si el proyector se cae o la base no responde: se registra el error y se
/// reintenta en la siguiente pasada. No se pierde nada porque el checkpoint
/// solo avanza junto con las filas proyectadas.
/// </summary>
public sealed class ProyectorHistorialViajes : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly TimeProvider _reloj;
    private readonly OpcionesProyeccion _opciones;
    private readonly ILogger<ProyectorHistorialViajes> _logger;

    public ProyectorHistorialViajes(
        IServiceScopeFactory scopes,
        TimeProvider reloj,
        IOptions<OpcionesProyeccion> opciones,
        ILogger<ProyectorHistorialViajes> logger)
    {
        _scopes = scopes;
        _reloj = reloj;
        _opciones = opciones.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Proyector de historial de viajes iniciado: cada {IntervaloMs} ms, retraso {RetrasoMs} ms.",
            _opciones.IntervaloMs, _opciones.RetrasoMs);

        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_opciones.IntervaloMs));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await EjecutarPasadaAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // La aplicación se está apagando: salimos sin error.
        }
    }

    /// <summary>Una pasada del proyector. Pública para poder probarla sin esperar al timer.</summary>
    public async Task<int> EjecutarPasadaAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var proyeccion = scope.ServiceProvider.GetRequiredService<ProyeccionHistorialViajes>();
            var procesados = await proyeccion.ProyectarPendientesAsync(_reloj.GetUtcNow(), ct);
            if (procesados > 0)
            {
                _logger.LogInformation("Proyector: {Cantidad} evento(s) proyectado(s) a ViajesHistorialVista.", procesados);
            }
            return procesados;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Proyector: falló la pasada; se reintenta en la próxima.");
            return 0;
        }
    }
}
