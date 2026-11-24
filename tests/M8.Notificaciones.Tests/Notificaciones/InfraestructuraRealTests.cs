using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using RabbitMQ.Client;
using StackExchange.Redis;
using M8.Notificaciones.Api.Features.Eventos;
using M8.Notificaciones.Api.Features.Notificaciones;
using M8.Notificaciones.Api.Features.Notificaciones.Dominio;
using M8.Notificaciones.Api.Features.Notificaciones.Envio;
using M8.Notificaciones.Api.Infrastructure.Idempotencia;
using M8.Notificaciones.Api.Infrastructure.Mensajeria;
using M8.Notificaciones.Api.Infrastructure.Persistence;
using M8.Notificaciones.Tests.Notificaciones.Apoyo;
using M8.Notificaciones.Tests.Viajes;

namespace M8.Notificaciones.Tests.Notificaciones;

/// <summary>
/// Test que corre solo si está definida la variable de entorno indicada (si no,
/// queda "omitido" y la CI local sigue en verde). En GitHub Actions se definen
/// las tres y los servicios corren como contenedores (ver .github/workflows/ci.yml).
/// </summary>
public sealed class InfraFactAttribute : FactAttribute
{
    public InfraFactAttribute(string variable)
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(variable)))
        {
            Skip = $"Definí {variable} para correr esta prueba contra la infraestructura real.";
        }
    }
}

/// <summary>
/// Pruebas de integración distribuida (RNF-07, TP2): M8 contra RabbitMQ, Redis
/// y PostgreSQL de verdad. Prueban lo que EF InMemory y los fakes no pueden:
/// la topología de colas, la cola de errores, el SET NX de Redis y los índices
/// únicos de PostgreSQL bajo concurrencia.
/// </summary>
public class InfraestructuraRealTests
{
    public const string VarRabbit = "M8_TEST_RABBITMQ"; // amqp://usuario:clave@localhost:5672
    public const string VarRedis = "M8_TEST_REDIS";     // localhost:6379
    public const string VarPostgres = PostgresFactAttribute.Variable;

    // ------------------------------------- RabbitMQ -------------------------------------

    private sealed class ApiConRabbit : ApiDePrueba
    {
        public ApiConRabbit(Uri amqp, string sufijo)
        {
            var usuario = Uri.UnescapeDataString(amqp.UserInfo.Split(':')[0]);
            var clave = Uri.UnescapeDataString(amqp.UserInfo.Split(':').ElementAtOrDefault(1) ?? "");
            Con("RabbitMQ:Habilitado", "true")
                .Con("RabbitMQ:Host", amqp.Host)
                .Con("RabbitMQ:Port", amqp.Port.ToString())
                .Con("RabbitMQ:User", usuario)
                .Con("RabbitMQ:Password", clave)
                // Nombres propios de esta corrida: no se mezcla con el M8 que pueda estar corriendo.
                .Con("RabbitMQ:Exchange", $"test.eventos.{sufijo}")
                .Con("RabbitMQ:Cola", $"test.m8.{sufijo}")
                .Con("RabbitMQ:ExchangeErrores", $"test.m8.{sufijo}.dlx")
                .Con("RabbitMQ:ColaErrores", $"test.m8.{sufijo}.dlq");
        }
    }

    private static async Task<bool> EsperarAsync(Func<Task<bool>> condicion, TimeSpan maximo)
    {
        var limite = DateTime.UtcNow + maximo;
        while (DateTime.UtcNow < limite)
        {
            if (await condicion()) return true;
            await Task.Delay(200);
        }
        return await condicion();
    }

    [InfraFact(VarRabbit)]
    public async Task Un_evento_publicado_en_RabbitMQ_genera_sus_notificaciones_una_sola_vez_y_los_invalidos_van_a_la_DLQ()
    {
        var amqp = new Uri(Environment.GetEnvironmentVariable(VarRabbit)!);
        var sufijo = Guid.NewGuid().ToString("N")[..8];
        await using var api = new ApiConRabbit(amqp, sufijo);
        _ = api.CreateClient(); // arranca el host y los BackgroundService
        var consumidor = api.Services.GetRequiredService<ConsumidorEventosRabbitMq>();
        Assert.True(await EsperarAsync(() => Task.FromResult(consumidor.Conectado), TimeSpan.FromSeconds(20)), "El consumidor no se conectó");

        var o = api.Services.GetRequiredService<IOptions<OpcionesRabbitMq>>().Value;
        var fabrica = new ConnectionFactory { Uri = amqp };
        await using var conexion = await fabrica.CreateConnectionAsync();
        await using var canal = await conexion.CreateChannelAsync();

        try
        {
            var evento = Eventos.Viaje(TipoEvento.VIAJE_ASIGNADO, destinatarios: new[] { Eventos.Cliente(), Eventos.Conductor() });
            var cuerpo = JsonSerializer.SerializeToUtf8Bytes(evento, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            var propiedades = new BasicProperties { ContentType = "application/json", Persistent = true, CorrelationId = "corr-" + sufijo };

            // Se publica DOS veces (RabbitMQ entrega "al menos una vez").
            await canal.BasicPublishAsync(o.Exchange, "viaje.asignado", false, propiedades, cuerpo);
            await canal.BasicPublishAsync(o.Exchange, "viaje.asignado", false, propiedades, cuerpo);
            // Uno roto: no es JSON.
            await canal.BasicPublishAsync(o.Exchange, "viaje.asignado", false, propiedades, Encoding.UTF8.GetBytes("{roto"));
            // Uno que no cumple el contrato (VIAJE_* sin viajeId).
            var invalido = JsonSerializer.SerializeToUtf8Bytes(evento with { EventoId = Guid.NewGuid(), ViajeId = null, ReservaId = Guid.NewGuid() },
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            await canal.BasicPublishAsync(o.Exchange, "viaje.asignado", false, propiedades, invalido);

            var llegaron = await EsperarAsync(() => api.EnScopeAsync(async sp =>
                await sp.GetRequiredService<M8DbContext>().Notificaciones.CountAsync(n => n.EventoId == evento.EventoId) == 2),
                TimeSpan.FromSeconds(20));
            Assert.True(llegaron, "No aparecieron las notificaciones del evento publicado");

            var enDlq = await EsperarAsync(async () => (await canal.QueueDeclarePassiveAsync(o.ColaErrores)).MessageCount == 2,
                TimeSpan.FromSeconds(20));
            Assert.True(enDlq, "Los dos mensajes inválidos no llegaron a la cola de errores");

            await api.EnScopeAsync(async sp =>
            {
                var db = sp.GetRequiredService<M8DbContext>();
                Assert.Equal(2, await db.Notificaciones.CountAsync(n => n.EventoId == evento.EventoId)); // sin duplicar
                var recibido = await db.EventosRecibidos.SingleAsync(x => x.EventoId == evento.EventoId);
                Assert.Equal(ConsumidorEventosRabbitMq.Via, recibido.Via);
                return 0;
            });
        }
        finally
        {
            await canal.QueueDeleteAsync(o.Cola);
            await canal.QueueDeleteAsync(o.ColaErrores);
            await canal.ExchangeDeleteAsync(o.Exchange);
            await canal.ExchangeDeleteAsync(o.ExchangeErrores);
        }
    }

    // --------------------------------------- Redis ---------------------------------------

    [InfraFact(VarRedis)]
    public async Task Idempotency_Key_en_Redis_reserva_una_sola_vez_aunque_lleguen_juntas()
    {
        await using var redis = await ConnectionMultiplexer.ConnectAsync(Environment.GetEnvironmentVariable(VarRedis)!);
        var almacen = new AlmacenIdempotenciaRedis(redis);
        var ambito = "test:" + Guid.NewGuid();
        const string clave = "clave-1";

        // 10 solicitudes simultáneas con la misma clave: una sola es "Nueva".
        var reservas = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => almacen.ReservarAsync(ambito, clave, "hash-a", default)));
        Assert.Equal(1, reservas.Count(r => r.Resultado == ResultadoReserva.Nueva));
        Assert.Equal(9, reservas.Count(r => r.Resultado == ResultadoReserva.EnCurso));

        await almacen.GuardarRespuestaAsync(ambito, clave, "hash-a", 202, "{\"ok\":true}", default);
        var repetida = await almacen.ReservarAsync(ambito, clave, "hash-a", default);
        Assert.Equal(ResultadoReserva.Repetida, repetida.Resultado);
        Assert.Equal(202, repetida.Status);
        Assert.Equal("{\"ok\":true}", repetida.CuerpoJson);
        Assert.Equal(ResultadoReserva.CuerpoDistinto, (await almacen.ReservarAsync(ambito, clave, "hash-b", default)).Resultado);

        // La clave guardada tiene TTL (Redis la borra sola) y no está en claro.
        var claves = redis.GetServers().First().Keys(pattern: $"m8:idem:{ambito}:*").ToList();
        var unica = Assert.Single(claves);
        Assert.DoesNotContain(clave, unica.ToString());
        Assert.NotNull(await redis.GetDatabase().KeyTimeToLiveAsync(unica));

        await almacen.LiberarAsync(ambito, clave, default);
        Assert.Equal(ResultadoReserva.Nueva, (await almacen.ReservarAsync(ambito, clave, "hash-b", default)).Resultado);
        await almacen.LiberarAsync(ambito, clave, default);
    }

    // ------------------------------------- PostgreSQL -------------------------------------

    private static string BaseTemporal() => new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable(VarPostgres))
    {
        Database = "m8_test_" + Guid.NewGuid().ToString("N")[..12]
    }.ConnectionString;

    private static M8DbContext Contexto(string cadena) => new(new DbContextOptionsBuilder<M8DbContext>().UseNpgsql(cadena).Options);

    private static IngestaEventos Ingesta(M8DbContext db) =>
        new(db, Options.Create(new OpcionesNotificaciones()), Array.Empty<IReaccionEvento>(), TimeProvider.System,
            NullLogger<IngestaEventos>.Instance);

    [InfraFact(VarPostgres)]
    public async Task En_PostgreSQL_el_mismo_evento_simultaneo_se_registra_una_vez_y_se_envia_una_vez()
    {
        var cadena = BaseTemporal();
        await using (var db = Contexto(cadena)) await db.Database.MigrateAsync(); // la migración NotificacionesTp2 aplica

        try
        {
            // 8 entregas simultáneas del mismo evento (HTTP y cola a la vez), cada una con su conexión.
            var evento = Eventos.Viaje(destinatarios: new[] { Eventos.Cliente(), Eventos.Conductor() });
            var resultados = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
            {
                await using var db = Contexto(cadena);
                return (await Ingesta(db).ProcesarAsync(evento, "HTTP", default)).Estado;
            }));

            Assert.Equal(1, resultados.Count(r => r == EstadoIngesta.Registrado));
            Assert.Equal(7, resultados.Count(r => r == EstadoIngesta.Duplicado));
            await using (var db = Contexto(cadena))
            {
                Assert.Equal(2, await db.Notificaciones.CountAsync());
                Assert.Equal(1, await db.EventosRecibidos.CountAsync());
            }

            // 4 despachadores a la vez (como 4 réplicas de M8): cada aviso sale una sola vez.
            var proveedor = new ProveedorDePrueba();
            var opciones = Options.Create(new OpcionesNotificaciones());
            await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
            {
                await using var db = Contexto(cadena);
                var envio = new ServicioEnvio(db, Enum.GetValues<Canal>().Select(c => (IProveedorCanal)new ProveedorDePrueba.PorCanal(c, proveedor)),
                    opciones, NullLogger<ServicioEnvio>.Instance);
                await envio.ProcesarPendientesAsync(DateTimeOffset.UtcNow.AddSeconds(1), default);
            }));

            Assert.Equal(2, proveedor.Enviados.Count);
            Assert.Equal(2, proveedor.Enviados.Select(m => m.NotificacionId).Distinct().Count());
            await using (var db = Contexto(cadena))
            {
                Assert.Equal(2, await db.IntentosNotificacion.CountAsync());
                Assert.All(await db.Notificaciones.ToListAsync(), n =>
                {
                    Assert.Equal(EstadoNotificacion.ENVIADA, n.Estado);
                    Assert.Null(n.Contacto);
                });
            }
        }
        finally
        {
            await using var db = Contexto(cadena);
            await db.Database.EnsureDeletedAsync();
        }
    }
}
