using System.Diagnostics.Metrics;
using System.Net;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using M8.Notificaciones.Api.Features.Documentos.Services;
using M8.Notificaciones.Api.Features.Eventos;
using M8.Notificaciones.Api.Features.Notificaciones;
using M8.Notificaciones.Api.Features.Notificaciones.Dominio;
using M8.Notificaciones.Api.Features.Notificaciones.Envio;
using M8.Notificaciones.Api.Features.Notificaciones.Entities;
using M8.Notificaciones.Api.Infrastructure.Persistence;
using M8.Notificaciones.Tests.Notificaciones.Apoyo;
using M8.Notificaciones.Tests.Viajes;
using StackExchange.Redis;

namespace M8.Notificaciones.Tests.Observability;

[CollectionDefinition(nameof(MedicionesTests), DisableParallelization = true)]
[Collection(nameof(MedicionesTests))]
public class MedicionesTests
{
    [Theory]
    [InlineData(0, "enviada", "no")]
    [InlineData(1, "enviada", "si")]
    [InlineData(3, "fallida", "si")]
    public async Task Solo_el_primer_resultado_terminal_persistido_aporta_a_las_metricas(int fallos, string resultado, string reintentada)
    {
        using var muestras = new Muestras("m8.notificaciones.finalizadas");
        await using var db = TestDb.Crear();
        var reloj = new RelojFijo(Eventos.Momento);
        var opciones = Options.Create(new OpcionesNotificaciones
        {
            Envio = new OpcionesEnvio { MaxIntentos = 3, EsperasSegundos = [1, 1] }
        });
        var ingesta = new IngestaEventos(db, opciones, [], reloj, NullLogger<IngestaEventos>.Instance);
        var evento = Eventos.Viaje();
        await ingesta.ProcesarAsync(evento, "HTTP", default);
        var llamadas = 0;
        var proveedor = new ProveedorDePrueba { Falla = _ => ++llamadas <= fallos ? "detalle no exportable" : null };
        var envio = new ServicioEnvio(db, [new ProveedorDePrueba.PorCanal(Canal.EMAIL, proveedor)], opciones, NullLogger<ServicioEnvio>.Instance);
        var n = await db.Notificaciones.SingleAsync();
        Assert.Equal(Canal.EMAIL, n.Canal);

        var terminal = Math.Min(fallos + 1, 3);
        for (var intento = 1; intento <= terminal; intento++)
        {
            Assert.Equal(1, await envio.ProcesarPendientesAsync(reloj.Ahora, default));
            if (intento < terminal) Assert.Empty(muestras.Valores);
            reloj.Avanzar(TimeSpan.FromMinutes(1));
        }
        Assert.Equal(EstadoIngesta.Duplicado, (await ingesta.ProcesarAsync(evento, "RABBITMQ", default)).Estado);
        Assert.Equal(0, await envio.ProcesarPendientesAsync(reloj.Ahora, default));

        if (resultado == "fallida")
        {
            // Mismo cambio persistido que realiza el endpoint de reintentos manuales.
            n.Estado = EstadoNotificacion.PENDIENTE;
            n.MaxIntentos = n.Intentos + 1;
            n.ProximoIntentoEn = reloj.Ahora;
            n.Version++;
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            Assert.Equal(1, await envio.ProcesarPendientesAsync(reloj.Ahora, default));
            Assert.Equal(EstadoNotificacion.ENVIADA, (await db.Notificaciones.SingleAsync()).Estado);
        }

        var (valor, etiquetas) = Assert.Single(muestras.Valores);
        Assert.Equal(1, valor);
        Assert.Equal(new[] { "reintentada", "resultado" }, etiquetas.Keys.Order());
        Assert.Equal(resultado, etiquetas["resultado"]);
        Assert.Equal(reintentada, etiquetas["reintentada"]);
    }

    [Fact]
    public async Task Una_fallida_al_crearse_cuenta_una_vez_sin_reintento_aunque_se_reabra()
    {
        using var muestras = new Muestras("m8.notificaciones.finalizadas");
        await using var db = TestDb.Crear();
        var reloj = new RelojFijo(Eventos.Momento);
        var opciones = Options.Create(new OpcionesNotificaciones());
        var ingesta = new IngestaEventos(db, opciones, [], reloj, NullLogger<IngestaEventos>.Instance);
        var evento = Eventos.Viaje(destinatarios: Eventos.Cliente(email: null, canales: Canal.EMAIL));

        Assert.Equal(EstadoIngesta.Registrado, (await ingesta.ProcesarAsync(evento, "HTTP", default)).Estado);
        db.ChangeTracker.Clear();
        var n = await db.Notificaciones.SingleAsync();
        Assert.Equal(EstadoNotificacion.FALLIDA, n.Estado);
        Assert.Equal(0, n.Intentos);
        Assert.Equal(EstadoIngesta.Duplicado, (await ingesta.ProcesarAsync(evento, "RABBITMQ", default)).Estado);
        var (valor, etiquetas) = Assert.Single(muestras.Valores);
        Assert.Equal(1, valor);
        Assert.Equal("fallida", etiquetas["resultado"]);
        Assert.Equal("no", etiquetas["reintentada"]);

        n.Estado = EstadoNotificacion.PENDIENTE;
        n.MaxIntentos = 1;
        n.ProximoIntentoEn = reloj.Ahora;
        n.Version++;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var envio = new ServicioEnvio(db, [], opciones, NullLogger<ServicioEnvio>.Instance);
        Assert.Equal(1, await envio.ProcesarPendientesAsync(reloj.Ahora, default));
        Assert.Single(muestras.Valores);
    }

    [Fact]
    public async Task Una_manual_sin_contacto_cuenta_una_vez_aunque_se_repita_y_se_reabra()
    {
        using var muestras = new Muestras("m8.notificaciones.finalizadas");
        await using var api = new ApiDePrueba();
        using var operador = api.ClienteOperador();
        const string ruta = "/api/v1/notificaciones";
        var clave = Guid.NewGuid().ToString();
        var cuerpo = new { destinatarioId = Guid.NewGuid(), rol = "CLIENTE", canal = "SMS", asunto = "Prueba", mensaje = "Prueba" };

        using var creada = await operador.SendAsync(Json.Post(ruta, cuerpo, clave));
        using var repetida = await operador.SendAsync(Json.Post(ruta, cuerpo, clave));
        Assert.Equal(HttpStatusCode.Accepted, creada.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, repetida.StatusCode);
        var n = await creada.LeerAsync();
        var id = n.GetProperty("notificacionId").GetGuid();
        Assert.Equal("FALLIDA", n.GetProperty("estado").GetString());
        Assert.Equal(0, n.GetProperty("intentos").GetInt32());
        Assert.Equal(id, (await repetida.LeerAsync()).GetProperty("notificacionId").GetGuid());
        var (valor, etiquetas) = Assert.Single(muestras.Valores);
        Assert.Equal(1, valor);
        Assert.Equal("fallida", etiquetas["resultado"]);
        Assert.Equal("no", etiquetas["reintentada"]);

        using var reabierta = await operador.SendAsync(Json.Post($"{ruta}/{id}/reintentos", null, Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.Accepted, reabierta.StatusCode);
        Assert.Equal(1, await api.DespacharAsync());
        Assert.Single(muestras.Valores);
    }

    [Fact]
    public async Task Una_fallida_no_cuenta_si_la_ingesta_no_se_guarda()
    {
        using var muestras = new Muestras("m8.notificaciones.finalizadas");
        var opcionesDb = new DbContextOptionsBuilder<M8DbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString());
        await using var db = new M8DbContext(opcionesDb.AddInterceptors(new ConflictoAlGuardar<Notificacion>()).Options);
        var reloj = new RelojFijo(Eventos.Momento);
        var ingesta = new IngestaEventos(db, Options.Create(new OpcionesNotificaciones()), [], reloj, NullLogger<IngestaEventos>.Instance);
        var evento = Eventos.Viaje(destinatarios: Eventos.Cliente(email: null, canales: Canal.EMAIL));

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => ingesta.ProcesarAsync(evento, "HTTP", default));
        Assert.Empty(muestras.Valores);
        Assert.Empty(await db.Notificaciones.ToListAsync());
        Assert.Empty(await db.EventosRecibidos.ToListAsync());
    }

    [Fact]
    public async Task Un_conflicto_al_guardar_el_intento_no_cuenta_un_terminal()
    {
        using var muestras = new Muestras("m8.notificaciones.finalizadas");
        var opcionesDb = new DbContextOptionsBuilder<M8DbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString());
        await using var db = new M8DbContext(opcionesDb.AddInterceptors(new ConflictoAlGuardar<IntentoNotificacion>()).Options);
        var reloj = new RelojFijo(Eventos.Momento);
        var opciones = Options.Create(new OpcionesNotificaciones());
        await new IngestaEventos(db, opciones, [], reloj, NullLogger<IngestaEventos>.Instance)
            .ProcesarAsync(Eventos.Viaje(), "HTTP", default);
        var proveedor = new ProveedorDePrueba();
        var envio = new ServicioEnvio(db, [new ProveedorDePrueba.PorCanal(Canal.EMAIL, proveedor)], opciones, NullLogger<ServicioEnvio>.Instance);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => envio.ProcesarPendientesAsync(reloj.Ahora, default));
        Assert.Single(proveedor.Enviados);
        Assert.Empty(muestras.Valores);
        db.ChangeTracker.Clear();
        Assert.Empty(await db.IntentosNotificacion.ToListAsync());
        Assert.Equal(EstadoNotificacion.PENDIENTE, (await db.Notificaciones.SingleAsync()).Estado);
    }

    private sealed class ConflictoAlGuardar<T> : SaveChangesInterceptor where T : class
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData evento, InterceptionResult<int> resultado, CancellationToken ct = default) =>
            evento.Context!.ChangeTracker.Entries<T>().Any(e => e.State == EntityState.Added)
                ? throw new DbUpdateConcurrencyException()
                : base.SavingChangesAsync(evento, resultado, ct);
    }

    [Theory]
    [InlineData(false, "generado")]
    [InlineData(true, "fallido")]
    public async Task Qr_mide_su_resultado_sin_cambiar_la_respuesta_o_excepcion(bool falla, string resultado)
    {
        using var muestras = new Muestras("m8.qr.finalizadas");
        var tran = RedisProxy.Crear<ITransaction>(metodo => metodo switch
        {
            "AddCondition" => null!,
            "StringSetAsync" => Task.FromResult(true),
            "ExecuteAsync" => falla ? Task.FromException<bool>(new InvalidOperationException("dato privado")) : Task.FromResult(true),
            _ => throw new NotSupportedException(metodo)
        });
        var db = RedisProxy.Crear<IDatabase>(metodo => metodo switch
        {
            "StringGetAsync" => Task.FromResult(RedisValue.Null),
            "CreateTransaction" => tran,
            _ => throw new NotSupportedException(metodo)
        });
        var qr = new QrService(RedisProxy.Crear<IConnectionMultiplexer>(_ => db));

        if (falla)
            await Assert.ThrowsAsync<InvalidOperationException>(() => qr.GenerarAsync(Guid.NewGuid(), Guid.NewGuid(), 60));
        else
            Assert.StartsWith("data:image/png;base64,", (await qr.GenerarAsync(Guid.NewGuid(), Guid.NewGuid(), 60)).ImagenPng);

        var (valor, etiquetas) = Assert.Single(muestras.Valores);
        Assert.Equal(1, valor);
        Assert.Equal(resultado, Assert.Single(etiquetas).Value);
        Assert.True(etiquetas.ContainsKey("resultado"));
    }

    public class RedisProxy : DispatchProxy
    {
        private Func<string, object> _invocar = null!;

        public static T Crear<T>(Func<string, object> invocar) where T : class
        {
            var proxy = Create<T, RedisProxy>();
            ((RedisProxy)(object)proxy)._invocar = invocar;
            return proxy;
        }

        protected override object Invoke(MethodInfo? targetMethod, object?[]? args) => _invocar(targetMethod!.Name);
    }

    private sealed class Muestras : IDisposable
    {
        private readonly MeterListener _listener = new();
        public List<(long Valor, Dictionary<string, object?> Etiquetas)> Valores { get; } = [];

        public Muestras(string nombre)
        {
            _listener.InstrumentPublished = (instrumento, listener) =>
            {
                if (instrumento.Name == nombre) listener.EnableMeasurementEvents(instrumento);
            };
            _listener.SetMeasurementEventCallback<long>((_, valor, tags, _) =>
                Valores.Add((valor, tags.ToArray().ToDictionary(t => t.Key, t => t.Value))));
            _listener.Start();
        }

        public void Dispose() => _listener.Dispose();
    }
}
