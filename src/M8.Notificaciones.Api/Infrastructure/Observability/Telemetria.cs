using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text;
using M8.Notificaciones.Api.Features.Notificaciones.Dominio;
using M8.Notificaciones.Api.Features.Notificaciones.Entities;

namespace M8.Notificaciones.Api.Infrastructure.Observability;

public sealed class Telemetria(ILogger<Telemetria> logger)
{
    internal const string Nombre = "m8-api";
    internal static readonly ActivitySource Trazas = new(Nombre);
    private static readonly Meter Medidor = new(Nombre);
    private static readonly Counter<long> Notificaciones = Medidor.CreateCounter<long>("m8.notificaciones.finalizadas");
    private static readonly Counter<long> Qr = Medidor.CreateCounter<long>("m8.qr.finalizadas");

    // Categoría exclusiva para OTLP. Solo mensajes constantes y atributos acotados; nunca excepciones.
    internal ILogger Logs { get; } = logger;

    // Version es un token de concurrencia interno. El bit se persiste en la misma
    // escritura que el primer estado terminal y sobrevive a reaperturas y leases.
    private const int ResultadoTerminalRegistrado = 1 << 30;

    internal static bool MarcarPrimeraFinalizacion(Notificacion n)
    {
        if (n.Estado is not (EstadoNotificacion.ENVIADA or EstadoNotificacion.FALLIDA)
            || (n.Version & ResultadoTerminalRegistrado) != 0) return false;
        n.Version |= ResultadoTerminalRegistrado;
        return true;
    }

    internal static void NotificacionFinalizada(bool enviada, bool reintentada) => Notificaciones.Add(1,
        new("resultado", enviada ? "enviada" : "fallida"), new("reintentada", reintentada ? "si" : "no"));

    internal static void QrFinalizado(bool generado) => Qr.Add(1,
        new KeyValuePair<string, object?>("resultado", generado ? "generado" : "fallido"));

    internal static Activity? ContinuarRabbit(IDictionary<string, object?>? headers)
    {
        var padre = Activity.Current?.Context ?? default;
        if (headers?.TryGetValue("traceparent", out var valor) == true)
        {
            var traceparent = valor switch
            {
                byte[] bytes when bytes.Length is >= 55 and <= 128 => Encoding.ASCII.GetString(bytes),
                string texto when texto.Length is >= 55 and <= 128 => texto,
                _ => null
            };
            // No importar baggage ni tracestate de un mensaje externo.
            if (ActivityContext.TryParse(traceparent, null, isRemote: true, out var remoto)) padre = remoto;
        }
        return Trazas.StartActivity("m8.rabbitmq.consumir", ActivityKind.Consumer, padre);
    }
}
