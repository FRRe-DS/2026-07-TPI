using System.Text.Json;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using M8.Notificaciones.Api.Features.Eventos;
using M8.Notificaciones.Api.Features.Eventos.Dtos;
using M8.Notificaciones.Api.Infrastructure.Http;

namespace M8.Notificaciones.Api.Infrastructure.Mensajeria;

/// <summary>
/// Consumo asíncrono de eventos desde RabbitMQ (RF-8.8, RNF-10).
///
/// Topología que declara al arrancar (idempotente: si ya existe, no cambia nada):
///   exchange "movilidad.eventos" (topic, durable)
///     └─ binding viaje.# / reserva.# / pago.# / reintegro.#
///         └─ cola "m8.eventos" (quorum, durable, x-delivery-limit)
///              └─ dead-letter → exchange "m8.eventos.dlx" → cola "m8.eventos.dlq"
///
/// Qué hace con cada mensaje (confirmación manual, nunca autoAck):
///   - Evento nuevo o repetido (mismo eventoId y cuerpo)  → ack.
///   - JSON inválido, no cumple el contrato o eventoId
///     reutilizado con otro cuerpo                        → nack sin reencolar → DLQ.
///     Reintentarlo no lo arreglaría ("mensaje venenoso").
///   - Error transitorio (base caída)                     → espera y nack con reencolado.
///     La cola quorum cuenta las entregas: pasado el límite va a la DLQ, así
///     que nunca queda dando vueltas para siempre (RNF-13).
///
/// La idempotencia la da la ingesta (bandeja de entrada por eventoId): RabbitMQ
/// entrega "al menos una vez" y un mensaje puede llegar dos veces sin duplicar avisos.
///
/// Si RabbitMQ no está disponible, reintenta conectarse con espera creciente
/// (hasta 30 s) sin tumbar la API: el endpoint HTTP sigue funcionando.
/// </summary>
public sealed class ConsumidorEventosRabbitMq : BackgroundService
{
    public const string Via = "RABBITMQ";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IServiceScopeFactory _scopes;
    private readonly OpcionesRabbitMq _opciones;
    private readonly ILogger<ConsumidorEventosRabbitMq> _logger;

    public ConsumidorEventosRabbitMq(IServiceScopeFactory scopes, IOptions<OpcionesRabbitMq> opciones, ILogger<ConsumidorEventosRabbitMq> logger)
    {
        _scopes = scopes;
        _opciones = opciones.Value;
        _logger = logger;
    }

    /// <summary>true cuando la conexión y el consumidor están activos (lo usan los tests de integración).</summary>
    public bool Conectado { get; private set; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_opciones.Habilitado)
        {
            _logger.LogInformation("Consumidor de RabbitMQ deshabilitado por configuración");
            return;
        }

        var espera = TimeSpan.FromSeconds(1);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ConsumirAsync(stoppingToken);
                espera = TimeSpan.FromSeconds(1);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Conectado = false;
                _logger.LogWarning("RabbitMQ no disponible ({Error}); se reintenta en {Segundos} s", ex.Message, espera.TotalSeconds);
                try { await Task.Delay(espera, stoppingToken); }
                catch (OperationCanceledException) { break; }
                espera = TimeSpan.FromSeconds(Math.Min(espera.TotalSeconds * 2, 30));
            }
        }
        Conectado = false;
    }

    private async Task ConsumirAsync(CancellationToken ct)
    {
        var fabrica = new ConnectionFactory
        {
            HostName = _opciones.Host,
            Port = _opciones.Port,
            UserName = _opciones.User,
            Password = _opciones.Password,
            VirtualHost = _opciones.VirtualHost,
            ClientProvidedName = "m8-notificaciones",
            RequestedConnectionTimeout = TimeSpan.FromSeconds(10),
            AutomaticRecoveryEnabled = false // la reconexión la maneja el bucle de ExecuteAsync
        };

        await using var conexion = await fabrica.CreateConnectionAsync(ct);
        await using var canal = await conexion.CreateChannelAsync(cancellationToken: ct);

        await DeclararTopologiaAsync(canal, _opciones, ct);
        await canal.BasicQosAsync(0, _opciones.Prefetch, false, ct);

        var cerrado = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        conexion.ConnectionShutdownAsync += (_, _) =>
        {
            cerrado.TrySetResult();
            return Task.CompletedTask;
        };

        var consumidor = new AsyncEventingBasicConsumer(canal);
        consumidor.ReceivedAsync += (_, entrega) => ProcesarEntregaAsync(canal, entrega, ct);
        await canal.BasicConsumeAsync(_opciones.Cola, autoAck: false, consumer: consumidor, cancellationToken: ct);

        Conectado = true;
        _logger.LogInformation("Escuchando la cola {Cola} (exchange {Exchange}, claves {Claves})",
            _opciones.Cola, _opciones.Exchange, string.Join(", ", _opciones.Claves()));

        // Queda acá hasta que se apague la API o se corte la conexión.
        await Task.WhenAny(cerrado.Task, Task.Delay(Timeout.Infinite, ct));
        ct.ThrowIfCancellationRequested();
        throw new InvalidOperationException("Se cerró la conexión con RabbitMQ.");
    }

    public static async Task DeclararTopologiaAsync(IChannel canal, OpcionesRabbitMq o, CancellationToken ct)
    {
        await canal.ExchangeDeclareAsync(o.Exchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: ct);
        await canal.ExchangeDeclareAsync(o.ExchangeErrores, ExchangeType.Fanout, durable: true, autoDelete: false, cancellationToken: ct);
        await canal.QueueDeclareAsync(o.ColaErrores, durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);
        await canal.QueueBindAsync(o.ColaErrores, o.ExchangeErrores, routingKey: "", cancellationToken: ct);

        var argumentos = new Dictionary<string, object?>
        {
            ["x-queue-type"] = "quorum",
            ["x-delivery-limit"] = o.LimiteEntregas,
            ["x-dead-letter-exchange"] = o.ExchangeErrores
        };
        await canal.QueueDeclareAsync(o.Cola, durable: true, exclusive: false, autoDelete: false, arguments: argumentos, cancellationToken: ct);
        foreach (var clave in o.Claves())
        {
            await canal.QueueBindAsync(o.Cola, o.Exchange, clave, cancellationToken: ct);
        }
    }

    private async Task ProcesarEntregaAsync(IChannel canal, BasicDeliverEventArgs entrega, CancellationToken ct)
    {
        var correlacion = entrega.BasicProperties.CorrelationId;
        var requestId = RequestIdMiddleware.EsValido(correlacion) ? correlacion! : RequestIdMiddleware.Generar();

        using var scopeLog = _logger.BeginScope(new Dictionary<string, object>
        {
            ["RequestId"] = requestId,
            ["RoutingKey"] = entrega.RoutingKey
        });

        EventoEntrada? evento;
        try
        {
            evento = JsonSerializer.Deserialize<EventoEntrada>(entrega.Body.Span, Json);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning("Mensaje descartado a la cola de errores: JSON inválido ({Error})", ex.Message);
            await canal.BasicNackAsync(entrega.DeliveryTag, multiple: false, requeue: false, ct);
            return;
        }

        try
        {
            await using var scope = _scopes.CreateAsyncScope();
            var ingesta = scope.ServiceProvider.GetRequiredService<IngestaEventos>();
            var resultado = await ingesta.ProcesarAsync(evento, Via, ct);

            switch (resultado.Estado)
            {
                case EstadoIngesta.Registrado:
                case EstadoIngesta.Duplicado:
                    await canal.BasicAckAsync(entrega.DeliveryTag, multiple: false, ct);
                    break;
                default:
                    _logger.LogWarning("Evento {EventoId} descartado a la cola de errores ({Estado}): {Errores}",
                        evento?.EventoId, resultado.Estado, string.Join(" ", resultado.Errores ?? Array.Empty<string>()));
                    await canal.BasicNackAsync(entrega.DeliveryTag, multiple: false, requeue: false, ct);
                    break;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Se está apagando la API: el mensaje sin confirmar vuelve a la cola solo.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error transitorio procesando el evento {EventoId}; se reencola", evento?.EventoId);
            try { await Task.Delay(TimeSpan.FromSeconds(2), ct); } catch (OperationCanceledException) { }
            await canal.BasicNackAsync(entrega.DeliveryTag, multiple: false, requeue: true, CancellationToken.None);
        }
    }

    /// <summary>Clave de enrutamiento sugerida para un tipo: VIAJE_ASIGNADO → viaje.asignado.</summary>
    public static string ClaveDe(string tipo)
    {
        var partes = tipo.ToLowerInvariant().Split('_', 2);
        return partes.Length == 2 ? $"{partes[0]}.{partes[1].Replace('_', '.')}" : partes[0];
    }
}
