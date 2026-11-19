using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using M8.Notificaciones.Api.Features.Eventos;
using M8.Notificaciones.Api.Features.Eventos.Dtos;
using M8.Notificaciones.Api.Features.Notificaciones;
using M8.Notificaciones.Api.Features.Notificaciones.Dominio;
using M8.Notificaciones.Api.Features.Notificaciones.Entities;
using M8.Notificaciones.Api.Features.Notificaciones.Envio;
using M8.Notificaciones.Api.Infrastructure.Persistence;
using M8.Notificaciones.Tests.Notificaciones.Apoyo;
using M8.Notificaciones.Tests.Viajes;

namespace M8.Notificaciones.Tests.Notificaciones;

/// <summary>Ingesta de eventos y política de envío, sin HTTP (EF InMemory y reloj controlado).</summary>
public class IngestaYEnvioTests
{
    private static readonly OpcionesNotificaciones Opciones = new()
    {
        Envio = new OpcionesEnvio { MaxIntentos = 3, EsperasSegundos = new[] { 5, 30 }, TimeoutSegundos = 1, TamanoLote = 50 }
    };

    private sealed class ReaccionQueFalla : IReaccionEvento
    {
        public int Llamadas;
        public Task ReaccionarAsync(EventoEntrada evento, CancellationToken ct)
        {
            Llamadas++;
            throw new InvalidOperationException("falla a propósito");
        }
    }

    private static IngestaEventos Ingesta(M8DbContext db, RelojFijo reloj, params IReaccionEvento[] reacciones) =>
        new(db, Options.Create(Opciones), reacciones, reloj, NullLogger<IngestaEventos>.Instance);

    private static ServicioEnvio Envio(M8DbContext db, ProveedorDePrueba proveedor) =>
        new(db, Enum.GetValues<Canal>().Select(c => (IProveedorCanal)new ProveedorDePrueba.PorCanal(c, proveedor)),
            Options.Create(Opciones), NullLogger<ServicioEnvio>.Instance);

    // ------------------------------------ Ingesta ------------------------------------

    [Fact]
    public async Task Un_evento_nuevo_queda_registrado_con_una_notificacion_PENDIENTE_por_destinatario()
    {
        using var db = TestDb.Crear();
        var reloj = new RelojFijo(Eventos.Momento);
        var evento = Eventos.Viaje(TipoEvento.VIAJE_ASIGNADO, destinatarios: new[] { Eventos.Cliente(), Eventos.Conductor() });

        var r = await Ingesta(db, reloj).ProcesarAsync(evento, "HTTP", default);

        Assert.Equal(EstadoIngesta.Registrado, r.Estado);
        Assert.Equal(2, r.NotificacionesGeneradas);
        Assert.Equal(evento.EventoId, r.Respuesta!.EventoId);
        var notificaciones = await db.Notificaciones.ToListAsync();
        Assert.Equal(2, notificaciones.Count);
        Assert.All(notificaciones, n =>
        {
            Assert.Equal(EstadoNotificacion.PENDIENTE, n.Estado);
            Assert.Equal(evento.EventoId, n.EventoId);
            Assert.Equal(TipoEvento.VIAJE_ASIGNADO, n.TipoEvento);
            Assert.Equal(TipoReferencia.VIAJE, n.ReferenciaTipo);
            Assert.Equal(evento.ViajeId, n.ReferenciaId);
            Assert.Equal(0, n.Intentos);
        });
        Assert.Contains(notificaciones, n => n.Rol == RolDestinatario.CONDUCTOR && n.Canal == Canal.SMS);
        var recibido = await db.EventosRecibidos.SingleAsync();
        Assert.Equal("HTTP", recibido.Via);
        Assert.Equal(64, recibido.HashCuerpo.Length);
    }

    [Fact]
    public async Task El_mismo_evento_dos_veces_no_duplica_avisos_y_devuelve_la_respuesta_original()
    {
        using var db = TestDb.Crear();
        var reloj = new RelojFijo(Eventos.Momento);
        var evento = Eventos.Viaje();

        var primera = await Ingesta(db, reloj).ProcesarAsync(evento, "HTTP", default);
        reloj.Avanzar(TimeSpan.FromMinutes(1));
        var segunda = await Ingesta(db, reloj).ProcesarAsync(evento, "RABBITMQ", default);

        Assert.Equal(EstadoIngesta.Duplicado, segunda.Estado);
        Assert.Equal(primera.Respuesta, segunda.Respuesta);
        Assert.Equal(1, await db.Notificaciones.CountAsync());
        Assert.Equal(1, await db.EventosRecibidos.CountAsync());
    }

    [Fact]
    public async Task La_misma_fecha_en_otro_huso_horario_es_el_mismo_evento()
    {
        using var db = TestDb.Crear();
        var reloj = new RelojFijo(Eventos.Momento);
        var evento = Eventos.Viaje();
        var mismaHoraEnArgentina = evento with { OcurridoEn = evento.OcurridoEn!.Value.ToOffset(TimeSpan.FromHours(-3)) };

        await Ingesta(db, reloj).ProcesarAsync(evento, "HTTP", default);
        var r = await Ingesta(db, reloj).ProcesarAsync(mismaHoraEnArgentina, "HTTP", default);

        Assert.Equal(EstadoIngesta.Duplicado, r.Estado);
    }

    [Fact]
    public async Task Reusar_el_eventoId_con_otro_cuerpo_es_un_error()
    {
        using var db = TestDb.Crear();
        var reloj = new RelojFijo(Eventos.Momento);
        var evento = Eventos.Viaje();

        await Ingesta(db, reloj).ProcesarAsync(evento, "HTTP", default);
        var r = await Ingesta(db, reloj).ProcesarAsync(evento with { Tipo = TipoEvento.VIAJE_INICIADO }, "HTTP", default);

        Assert.Equal(EstadoIngesta.EventoIdReutilizado, r.Estado);
        Assert.Equal(1, await db.Notificaciones.CountAsync());
    }

    [Fact]
    public async Task Un_evento_invalido_no_registra_nada()
    {
        using var db = TestDb.Crear();
        var r = await Ingesta(db, new RelojFijo(Eventos.Momento)).ProcesarAsync(Eventos.Viaje() with { ViajeId = null }, "HTTP", default);

        Assert.Equal(EstadoIngesta.Invalido, r.Estado);
        Assert.NotEmpty(r.Errores!);
        Assert.Empty(db.EventosRecibidos);
        Assert.Empty(db.Notificaciones);
    }

    [Fact]
    public async Task Recuerda_el_contacto_y_lo_usa_en_eventos_siguientes()
    {
        using var db = TestDb.Crear();
        var reloj = new RelojFijo(Eventos.Momento);
        var cliente = Guid.NewGuid();

        await Ingesta(db, reloj).ProcesarAsync(Eventos.Viaje(destinatarios: Eventos.Cliente(cliente, "primero@ejemplo.com")), "HTTP", default);
        await Ingesta(db, reloj).ProcesarAsync(Eventos.Viaje(destinatarios: Eventos.Cliente(cliente, email: null)), "HTTP", default);

        var contacto = await db.ContactosDestinatarios.SingleAsync();
        Assert.Equal("primero@ejemplo.com", contacto.Email);
        Assert.All(await db.Notificaciones.ToListAsync(), n => Assert.Equal("primero@ejemplo.com", n.Contacto));
    }

    [Fact]
    public async Task Si_una_reaccion_falla_el_evento_igual_queda_registrado()
    {
        using var db = TestDb.Crear();
        var reaccion = new ReaccionQueFalla();

        var r = await Ingesta(db, new RelojFijo(Eventos.Momento), reaccion).ProcesarAsync(Eventos.Viaje(), "HTTP", default);

        Assert.Equal(EstadoIngesta.Registrado, r.Estado);
        Assert.Equal(1, reaccion.Llamadas);
        Assert.Equal(1, await db.Notificaciones.CountAsync());
    }

    [Fact]
    public async Task Los_hitos_de_viaje_tambien_alimentan_el_historial_de_event_sourcing()
    {
        using var esc = new EscenarioViajes();
        var reaccion = new ReaccionHistorialViajes(esc.Handler, NullLogger<ReaccionHistorialViajes>.Instance);
        var viaje = Guid.NewGuid();

        await Ingesta(esc.Db, esc.Reloj, reaccion).ProcesarAsync(Eventos.Viaje(TipoEvento.VIAJE_SOLICITADO, viajeId: viaje), "HTTP", default);
        // VIAJE_CANCELADO no es parte del historial: se notifica igual y el historial lo ignora.
        await Ingesta(esc.Db, esc.Reloj, reaccion).ProcesarAsync(Eventos.Viaje(TipoEvento.VIAJE_CANCELADO, viajeId: viaje), "HTTP", default);
        // VIAJE_FINALIZADO sin asignar: el historial lo rechaza (regla), pero el aviso sale igual.
        await Ingesta(esc.Db, esc.Reloj, reaccion).ProcesarAsync(Eventos.Viaje(TipoEvento.VIAJE_FINALIZADO, viajeId: viaje), "HTTP", default);

        Assert.Equal(1, await esc.Db.EventosViaje.CountAsync(x => x.ViajeId == viaje));
        Assert.Equal(3, await esc.Db.Notificaciones.CountAsync());
    }

    // ------------------------------------- Envío -------------------------------------

    private static async Task<Notificacion> UnaPendiente(M8DbContext db, RelojFijo reloj, string email = "cliente@ejemplo.com")
    {
        await Ingesta(db, reloj).ProcesarAsync(Eventos.Viaje(destinatarios: Eventos.Cliente(email: email)), "HTTP", default);
        return await db.Notificaciones.SingleAsync();
    }

    [Fact]
    public async Task Un_envio_exitoso_deja_la_notificacion_ENVIADA_y_borra_el_contacto()
    {
        using var db = TestDb.Crear();
        var reloj = new RelojFijo(Eventos.Momento);
        var proveedor = new ProveedorDePrueba();
        var n = await UnaPendiente(db, reloj);

        Assert.Equal(1, await Envio(db, proveedor).ProcesarPendientesAsync(reloj.Ahora, default));

        await db.Entry(n).ReloadAsync();
        Assert.Equal(EstadoNotificacion.ENVIADA, n.Estado);
        Assert.Equal(1, n.Intentos);
        Assert.Equal(reloj.Ahora, n.EnviadoEn);
        Assert.Null(n.Contacto);
        Assert.Null(n.ProximoIntentoEn);
        var intento = await db.IntentosNotificacion.SingleAsync();
        Assert.Equal(ResultadoIntento.EXITO, intento.Resultado);
        Assert.Equal(1, intento.Numero);
        Assert.Equal("cliente@ejemplo.com", Assert.Single(proveedor.Enviados).Contacto);
    }

    [Fact]
    public async Task Tres_fallos_con_espera_creciente_dejan_la_notificacion_FALLIDA()
    {
        using var db = TestDb.Crear();
        var reloj = new RelojFijo(Eventos.Momento);
        var proveedor = new ProveedorDePrueba { Falla = _ => "buzón inexistente" };
        var n = await UnaPendiente(db, reloj);
        var envio = Envio(db, proveedor);

        await envio.ProcesarPendientesAsync(reloj.Ahora, default);
        await db.Entry(n).ReloadAsync();
        Assert.Equal(EstadoNotificacion.PENDIENTE, n.Estado);
        Assert.Equal(reloj.Ahora.AddSeconds(5), n.ProximoIntentoEn);

        // Antes de que venza la espera, no se intenta.
        reloj.Avanzar(TimeSpan.FromSeconds(4));
        Assert.Equal(0, await envio.ProcesarPendientesAsync(reloj.Ahora, default));

        reloj.Avanzar(TimeSpan.FromSeconds(1));
        Assert.Equal(1, await envio.ProcesarPendientesAsync(reloj.Ahora, default));
        await db.Entry(n).ReloadAsync();
        Assert.Equal(reloj.Ahora.AddSeconds(30), n.ProximoIntentoEn);

        reloj.Avanzar(TimeSpan.FromSeconds(30));
        await envio.ProcesarPendientesAsync(reloj.Ahora, default);
        await db.Entry(n).ReloadAsync();

        Assert.Equal(EstadoNotificacion.FALLIDA, n.Estado);
        Assert.Equal(3, n.Intentos);
        Assert.Null(n.ProximoIntentoEn);
        Assert.Equal("buzón inexistente", n.UltimoError);
        Assert.Equal(new[] { 1, 2, 3 }, await db.IntentosNotificacion.OrderBy(i => i.Numero).Select(i => i.Numero).ToListAsync());

        // Ya FALLIDA: no hay más intentos automáticos.
        reloj.Avanzar(TimeSpan.FromHours(1));
        Assert.Equal(0, await envio.ProcesarPendientesAsync(reloj.Ahora, default));
    }

    [Fact]
    public async Task Un_proveedor_que_no_responde_se_corta_por_timeout()
    {
        using var db = TestDb.Crear();
        var reloj = new RelojFijo(Eventos.Momento);
        var proveedor = new ProveedorDePrueba { Demora = TimeSpan.FromSeconds(30) };
        await UnaPendiente(db, reloj);

        var cronometro = System.Diagnostics.Stopwatch.StartNew();
        await Envio(db, proveedor).ProcesarPendientesAsync(reloj.Ahora, default);
        cronometro.Stop();

        Assert.True(cronometro.Elapsed < TimeSpan.FromSeconds(10), $"Tardó {cronometro.Elapsed}");
        var intento = await db.IntentosNotificacion.SingleAsync();
        Assert.Equal(ResultadoIntento.ERROR, intento.Resultado);
        Assert.Contains("no respondió", intento.Error);
    }

    [Fact]
    public async Task Una_notificacion_tomada_por_otra_instancia_no_se_envia_dos_veces()
    {
        var nombre = Guid.NewGuid().ToString();
        var opciones = new DbContextOptionsBuilder<M8DbContext>().UseInMemoryDatabase(nombre).Options;
        var reloj = new RelojFijo(Eventos.Momento);
        var proveedor = new ProveedorDePrueba();
        await using (var db = new M8DbContext(opciones))
        {
            await UnaPendiente(db, reloj);
        }

        // Dos "instancias" de M8 leen la misma notificación a la vez...
        await using var db1 = new M8DbContext(opciones);
        await using var db2 = new M8DbContext(opciones);
        var copia1 = await db1.Notificaciones.SingleAsync();
        var copia2 = await db2.Notificaciones.SingleAsync();

        // ...la primera la toma y la envía.
        Assert.Equal(1, await Envio(db1, proveedor).ProcesarPendientesAsync(reloj.Ahora, default));
        // La segunda intenta guardar su "toma" con la versión vieja: la concurrencia optimista la frena.
        copia2.BloqueadaHasta = reloj.Ahora.AddMinutes(1);
        copia2.Version++;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => db2.SaveChangesAsync());

        Assert.Single(proveedor.Enviados);
        Assert.NotNull(copia1);
    }

    [Fact]
    public async Task Una_notificacion_bloqueada_no_se_toma_hasta_que_vence_el_lease()
    {
        using var db = TestDb.Crear();
        var reloj = new RelojFijo(Eventos.Momento);
        var n = await UnaPendiente(db, reloj);
        n.BloqueadaHasta = reloj.Ahora.AddSeconds(40);
        await db.SaveChangesAsync();
        var envio = Envio(db, new ProveedorDePrueba());

        Assert.Equal(0, await envio.ProcesarPendientesAsync(reloj.Ahora, default));
        reloj.Avanzar(TimeSpan.FromSeconds(41));
        Assert.Equal(1, await envio.ProcesarPendientesAsync(reloj.Ahora, default));
    }

    [Fact]
    public async Task Las_notificaciones_FALLIDAS_al_crearse_no_se_intentan()
    {
        using var db = TestDb.Crear();
        var reloj = new RelojFijo(Eventos.Momento);
        await Ingesta(db, reloj).ProcesarAsync(
            Eventos.Viaje(destinatarios: Eventos.Cliente(email: null, canales: Canal.EMAIL)), "HTTP", default);

        Assert.Equal(0, await Envio(db, new ProveedorDePrueba()).ProcesarPendientesAsync(reloj.Ahora, default));
        Assert.Equal(EstadoNotificacion.FALLIDA, (await db.Notificaciones.SingleAsync()).Estado);
    }

    [Fact]
    public async Task El_proveedor_SMTP_sin_servidor_devuelve_un_error_sin_datos_personales()
    {
        var opciones = new OpcionesNotificaciones { Smtp = new OpcionesSmtp { Habilitado = true, Host = "127.0.0.1", Puerto = 1 } };
        var smtp = new ProveedorEmailSmtp(Options.Create(opciones));

        var sinServidor = await smtp.EnviarAsync(new(Guid.NewGuid(), Guid.NewGuid(), Canal.EMAIL, "persona@ejemplo.com", "a", "b"), default);
        var sinEmail = await smtp.EnviarAsync(new(Guid.NewGuid(), Guid.NewGuid(), Canal.EMAIL, null, "a", "b"), default);
        var marcado = await smtp.EnviarAsync(new(Guid.NewGuid(), Guid.NewGuid(), Canal.EMAIL, "x@falla.test", "a", "b"), default);

        Assert.False(sinServidor.Exito);
        Assert.DoesNotContain("persona@ejemplo.com", sinServidor.Error);
        Assert.Equal("Sin email registrado.", sinEmail.Error);
        Assert.Contains("simulado", marcado.Error);
    }

    [Fact]
    public async Task El_proveedor_simulado_falla_con_el_marcador_y_no_loguea_el_contacto()
    {
        var simulado = new ProveedorSimulado(Canal.EMAIL, new OpcionesSimulacion { MarcadorFallo = "falla" },
            NullLogger<ProveedorSimulado>.Instance);

        var mal = await simulado.EnviarAsync(new(Guid.NewGuid(), Guid.NewGuid(), Canal.EMAIL, "x@falla.test", "a", "b"), default);
        var bien = await simulado.EnviarAsync(new(Guid.NewGuid(), Guid.NewGuid(), Canal.EMAIL, "x@ok.test", "a", "b"), default);

        Assert.False(mal.Exito);
        Assert.DoesNotContain("x@falla.test", mal.Error);
        Assert.True(bien.Exito);
    }
}
