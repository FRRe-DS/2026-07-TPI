using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using M8.Notificaciones.Api.Features.Notificaciones.Dominio;
using M8.Notificaciones.Api.Features.Notificaciones.Entities;
using M8.Notificaciones.Api.Infrastructure.Observability;
using M8.Notificaciones.Api.Infrastructure.Persistence;
using M8.Notificaciones.Api.Infrastructure.Tiempo;

namespace M8.Notificaciones.Api.Features.Notificaciones.Envio;

/// <summary>
/// Hace los intentos de entrega de las notificaciones PENDIENTE (RF-8.6, RNF-13).
///
/// Por cada notificación a la que le toca un intento:
///  1. La "toma" (lease con concurrencia optimista): si otra instancia de M8 la
///     tomó primero, la saltea. Así no se envía dos veces en paralelo (RNF-08).
///  2. Llama al proveedor del canal con un tiempo máximo (timeout): nunca queda
///     esperando indefinidamente.
///  3. Registra el intento (historial que no se modifica) y actualiza el estado:
///     - éxito → ENVIADA (y se borra el contacto guardado, RNF-19);
///     - error con intentos disponibles → sigue PENDIENTE, con espera creciente;
///     - error en el último intento → FALLIDA (un OPERADOR puede reintentarla).
///
/// Garantía de entrega: "al menos una vez". Si la API se cae justo después de que
/// el proveedor aceptó el envío y antes de guardar el resultado, el lease vence
/// y el aviso se vuelve a enviar.
///
/// Es una clase común que recibe "ahora": se prueba sin esperar ni dormir.
/// </summary>
public sealed class ServicioEnvio
{
    private readonly M8DbContext _db;
    private readonly IReadOnlyDictionary<Canal, IProveedorCanal> _proveedores;
    private readonly OpcionesEnvio _opciones;
    private readonly ILogger<ServicioEnvio> _logger;
    private readonly Telemetria? _telemetria;

    public ServicioEnvio(M8DbContext db, IEnumerable<IProveedorCanal> proveedores, IOptions<OpcionesNotificaciones> opciones,
        ILogger<ServicioEnvio> logger, Telemetria? telemetria = null)
    {
        _db = db;
        // Si hay más de un proveedor para un canal, gana el último registrado.
        _proveedores = proveedores.GroupBy(p => p.Canal).ToDictionary(g => g.Key, g => g.Last());
        _opciones = opciones.Value.Envio;
        _logger = logger;
        _telemetria = telemetria;
    }

    /// <summary>Procesa un lote de notificaciones vencidas. Devuelve cuántos intentos hizo.</summary>
    public async Task<int> ProcesarPendientesAsync(DateTimeOffset ahora, CancellationToken ct)
    {
        ahora = Utc.Normalizar(ahora);
        var candidatas = await _db.Notificaciones
            .Where(n => n.Estado == EstadoNotificacion.PENDIENTE
                        && n.ProximoIntentoEn != null && n.ProximoIntentoEn <= ahora
                        && (n.BloqueadaHasta == null || n.BloqueadaHasta < ahora))
            .OrderBy(n => n.ProximoIntentoEn)
            .Take(_opciones.TamanoLote)
            .ToListAsync(ct);

        var realizados = 0;
        foreach (var notificacion in candidatas)
        {
            ct.ThrowIfCancellationRequested();
            if (await TomarAsync(notificacion, ahora, ct))
            {
                // Correlación por notificación y por viaje/reserva/pago en los logs del intento (RNF-14).
                using var scopeLog = _logger.BeginScope(ScopeLog.DeNotificacion(notificacion));
                using var span = Telemetria.Trazas.StartActivity("m8.notificaciones.intento");
                try
                {
                    await IntentarAsync(notificacion, ahora, span, ct);
                    realizados++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    span?.SetStatus(ActivityStatusCode.Error);
                    _telemetria?.Logs.LogError("Notificaciones: no se pudo persistir el intento");
                    throw;
                }
            }
        }
        return realizados;
    }

    private async Task<bool> TomarAsync(Notificacion n, DateTimeOffset ahora, CancellationToken ct)
    {
        n.BloqueadaHasta = ahora + _opciones.DuracionBloqueo;
        n.Version++;
        try
        {
            await _db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            // Otra instancia la tomó (o la cambió un reintento manual): no es nuestra.
            _db.Entry(n).State = EntityState.Detached;
            return false;
        }
    }

    private async Task IntentarAsync(Notificacion n, DateTimeOffset ahora, Activity? span, CancellationToken ct)
    {
        var numero = n.Intentos + 1;
        _proveedores.TryGetValue(n.Canal, out var proveedor);
        var resultado = await LlamarProveedorAsync(n, proveedor, ct);
        span?.SetTag("m8.proveedor", resultado.Simulado || proveedor is ProveedorSimulado ? "simulado"
            : proveedor is ProveedorEmailSmtp ? "smtp" : proveedor is null ? "ninguno" : "otro");
        span?.SetTag("m8.reintentada", numero > 1);
        var realizadoEn = Utc.Normalizar(ahora);

        _db.IntentosNotificacion.Add(new IntentoNotificacion
        {
            NotificacionId = n.Id,
            Numero = numero,
            Canal = n.Canal,
            Resultado = resultado.Exito ? ResultadoIntento.EXITO : ResultadoIntento.ERROR,
            Error = resultado.Error,
            RealizadoEn = realizadoEn
        });

        n.Intentos = numero;
        n.BloqueadaHasta = null;
        n.Version++;
        if (resultado.Exito)
        {
            n.Estado = EstadoNotificacion.ENVIADA;
            n.EnviadoEn = realizadoEn;
            n.ProximoIntentoEn = null;
            n.Contacto = null; // Ya no hace falta: no guardamos datos personales de más.
        }
        else if (numero >= n.MaxIntentos)
        {
            n.Estado = EstadoNotificacion.FALLIDA;
            n.ProximoIntentoEn = null;
            n.UltimoError = resultado.Error;
        }
        else
        {
            n.ProximoIntentoEn = realizadoEn + _opciones.EsperaAntesDelIntento(numero + 1);
            n.UltimoError = resultado.Error;
        }

        var primeraFinalizacion = Telemetria.MarcarPrimeraFinalizacion(n);
        // El despachador no abre una transacción externa: SaveChanges confirma el
        // estado y su intento de forma atómica, también ante concurrencia del lease.
        await _db.SaveChangesAsync(ct);
        if (primeraFinalizacion) Telemetria.NotificacionFinalizada(resultado.Exito, numero > 1);

        span?.SetTag("m8.resultado", resultado.Exito ? "aceptada" : n.Estado == EstadoNotificacion.FALLIDA ? "fallida" : "pendiente");
        if (resultado.Exito)
            _logger.LogInformation("Notificación {NotificacionId} aceptada por el proveedor {Canal} en el intento {Numero}", n.Id, n.Canal, numero);
        else
        {
            span?.SetStatus(ActivityStatusCode.Error);
            _telemetria?.Logs.LogWarning("Notificaciones: intento fallido; estado {Estado}", n.Estado);
            _logger.LogWarning("Notificación {NotificacionId}: intento {Numero}/{Max} por {Canal} falló. Estado: {Estado}",
                n.Id, numero, n.MaxIntentos, n.Canal, n.Estado);
        }
    }

    private async Task<ResultadoEnvio> LlamarProveedorAsync(Notificacion n, IProveedorCanal? proveedor, CancellationToken ct)
    {
        if (proveedor is null)
        {
            return ResultadoEnvio.Fallo($"No hay proveedor configurado para el canal {n.Canal}.");
        }

        using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limite.CancelAfter(TimeSpan.FromSeconds(_opciones.TimeoutSegundos));
        try
        {
            return await proveedor.EnviarAsync(
                new MensajeSaliente(n.Id, n.DestinatarioId, n.Canal, n.Contacto, n.Asunto, n.Mensaje), limite.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return ResultadoEnvio.Fallo($"El proveedor no respondió en {_opciones.TimeoutSegundos} s.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError("Error inesperado del proveedor {Canal} para la notificación {NotificacionId}", n.Canal, n.Id);
            return ResultadoEnvio.Fallo("Error inesperado del proveedor.");
        }
    }
}
