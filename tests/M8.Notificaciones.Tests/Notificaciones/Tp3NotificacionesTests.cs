using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using M8.Notificaciones.Api.Features.Notificaciones;
using M8.Notificaciones.Api.Features.Notificaciones.Dominio;
using M8.Notificaciones.Api.Features.Notificaciones.Entities;
using M8.Notificaciones.Api.Features.Notificaciones.Servicios;
using M8.Notificaciones.Api.Infrastructure.Persistence;
using M8.Notificaciones.Tests.Notificaciones.Apoyo;

namespace M8.Notificaciones.Tests.Notificaciones;

/// <summary>
/// Lo que agrega el TP3 a Notificaciones: auditoría de operaciones manuales y del
/// emisor de cada evento (RNF-19), encabezados de seguridad (RNF-12), correlación
/// por viaje/reserva/pago en los logs (RNF-14) y el reenvío de comprobantes (RF-8.5).
/// </summary>
public class Tp3NotificacionesTests
{
    private static object Manual(Guid destinatario) => new
    {
        destinatarioId = destinatario, rol = "CLIENTE", canal = "EMAIL", asunto = "Aviso", mensaje = "Texto del aviso"
    };

    private static Task<T> EnDbAsync<T>(ApiDePrueba api, Func<M8DbContext, Task<T>> consulta) =>
        api.EnScopeAsync(sp => consulta(sp.GetRequiredService<M8DbContext>()));

    // ------------------------------- Auditoría (RNF-19) -------------------------------

    [Fact]
    public async Task Alta_manual_y_reintento_quedan_auditados_con_actor_y_request_id()
    {
        await using var api = new ApiDePrueba();
        using var operador = api.ClienteOperador();

        // Sin contacto registrado: nace FALLIDA, así se puede reintentar enseguida.
        var alta = Json.Post("/api/v1/notificaciones", Manual(Guid.NewGuid()), "clave-alta");
        alta.Headers.Add("X-Request-Id", "auditoria-alta-1");
        var r = await operador.SendAsync(alta);
        Assert.Equal(HttpStatusCode.Accepted, r.StatusCode);
        var id = (await r.LeerAsync()).GetProperty("notificacionId").GetGuid();

        var reintento = Json.Post($"/api/v1/notificaciones/{id}/reintentos", null, "clave-reintento");
        reintento.Headers.Add("X-Request-Id", "auditoria-reintento-1");
        Assert.Equal(HttpStatusCode.Accepted, (await operador.SendAsync(reintento)).StatusCode);

        // Un reintento rechazado (ya está PENDIENTE) no deja rastro de auditoría.
        Assert.Equal(HttpStatusCode.Conflict,
            (await operador.SendAsync(Json.Post($"/api/v1/notificaciones/{id}/reintentos", null, "clave-409"))).StatusCode);

        var auditoria = await EnDbAsync(api, db => db.AuditoriaNotificaciones.AsNoTracking()
            .Where(a => a.NotificacionId == id).OrderBy(a => a.Id).ToListAsync());
        Assert.Collection(auditoria,
            a =>
            {
                Assert.Equal(AccionAuditoria.ALTA_MANUAL, a.Accion);
                Assert.Equal("grupo-de-prueba", a.Actor);
                Assert.Equal("auditoria-alta-1", a.RequestId);
            },
            a =>
            {
                Assert.Equal(AccionAuditoria.REINTENTO_MANUAL, a.Accion);
                Assert.Equal("auditoria-reintento-1", a.RequestId);
            });
    }

    [Fact]
    public async Task El_evento_recibido_por_HTTP_registra_quien_lo_emitio()
    {
        await using var api = new ApiDePrueba();
        using var servicio = api.CreateClient();
        servicio.DefaultRequestHeaders.Authorization =
            new("Bearer", ApiDePrueba.Token(new[] { "viajes-be" }, azp: "grupo-05"));
        var evento = Eventos.Viaje();

        Assert.Equal(HttpStatusCode.Accepted, (await servicio.PostAsJsonAsync("/api/v1/eventos", evento)).StatusCode);

        var registro = await EnDbAsync(api, db => db.EventosRecibidos.AsNoTracking().SingleAsync(e => e.EventoId == evento.EventoId));
        Assert.Equal("grupo-05", registro.EmitidoPor);
        Assert.Equal("HTTP", registro.Via);
    }

    // ------------------------- Encabezados de seguridad (RNF-12) -------------------------

    [Theory]
    [InlineData("/api/v1/notificaciones", true)]  // 401: hasta los errores llevan los encabezados
    [InlineData("/health", false)]
    public async Task Toda_respuesta_lleva_encabezados_de_seguridad(string ruta, bool esApi)
    {
        await using var api = new ApiDePrueba();
        using var cliente = api.CreateClient();

        var r = await cliente.GetAsync(ruta);

        Assert.Equal("nosniff", r.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", r.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("no-referrer", r.Headers.GetValues("Referrer-Policy").Single());
        Assert.Contains("frame-ancestors 'none'", r.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal(esApi, r.Headers.CacheControl?.NoStore == true);
    }

    // ------------------------------- Correlación (RNF-14) -------------------------------

    [Theory]
    [InlineData(TipoReferencia.VIAJE, "ViajeId")]
    [InlineData(TipoReferencia.RESERVA, "ReservaId")]
    [InlineData(TipoReferencia.PAGO, "PagoId")]
    public void El_scope_de_log_de_una_notificacion_lleva_su_referencia(TipoReferencia tipo, string clave)
    {
        var n = new Notificacion { ReferenciaTipo = tipo, ReferenciaId = Guid.NewGuid(), EventoId = Guid.NewGuid(), Contacto = "x@ejemplo.com" };

        var scope = ScopeLog.DeNotificacion(n);

        Assert.Equal(n.Id, scope["NotificacionId"]);
        Assert.Equal(n.EventoId, scope["EventoId"]);
        Assert.Equal(n.ReferenciaId, scope[clave]);
        Assert.DoesNotContain(scope.Values, v => v is string s && s.Contains('@')); // nunca el contacto
    }

    [Fact]
    public void El_scope_de_log_de_un_evento_lleva_sus_identificadores()
    {
        var pago = Eventos.Pago();

        var scope = ScopeLog.DeEvento(pago);

        Assert.Equal(pago.EventoId, scope["EventoId"]);
        Assert.Equal(pago.ViajeId, scope["ViajeId"]);
        Assert.Equal(pago.PagoId, scope["PagoId"]);
        Assert.False(scope.ContainsKey("ReservaId"));
    }

    // --------------------------- Reenvío de comprobantes (RF-8.5) ---------------------------

    [Fact]
    public async Task El_reenvio_va_al_contacto_registrado_queda_auditado_y_lo_envia_el_despachador()
    {
        await using var api = new ApiDePrueba();
        var cliente = Guid.NewGuid();
        // El contacto se aprende de un evento anterior.
        using (var servicio = api.ClienteServicio())
        {
            await servicio.PostAsJsonAsync("/api/v1/eventos", Eventos.Viaje(destinatarios: Eventos.Cliente(cliente, "registrado@ejemplo.com")));
        }
        var pagoId = Guid.NewGuid();

        var n = await api.EnScopeAsync(sp => sp.GetRequiredService<ReenviosNotificaciones>().RegistrarAsync(
            cliente, Canal.EMAIL, "Tu comprobante", "Descargalo desde la app.", TipoReferencia.PAGO, pagoId,
            "grupo-07", "req-reenvio", default));

        Assert.Equal(OrigenNotificacion.REENVIO, n.Origen);
        Assert.Equal(RolDestinatario.CLIENTE, n.Rol);
        Assert.Equal(EstadoNotificacion.PENDIENTE, n.Estado);
        var auditoria = await EnDbAsync(api, db => db.AuditoriaNotificaciones.AsNoTracking().SingleAsync(a => a.NotificacionId == n.Id));
        Assert.Equal(AccionAuditoria.REENVIO, auditoria.Accion);
        Assert.Equal("grupo-07", auditoria.Actor);

        await api.DespacharAsync();
        Assert.Contains(api.Proveedor.Enviados, m => m.NotificacionId == n.Id && m.Contacto == "registrado@ejemplo.com");
        // Es una notificación más: aparece en el seguimiento con su referencia.
        var listado = await (await api.ClienteOperador().GetAsync($"/api/v1/notificaciones?referenciaId={pagoId}")).LeerAsync();
        Assert.Equal("ENVIADA", listado.GetProperty("items")[0].GetProperty("estado").GetString());
    }

    [Fact]
    public async Task El_reenvio_sin_contacto_para_el_canal_nace_FALLIDA()
    {
        await using var api = new ApiDePrueba();

        var n = await api.EnScopeAsync(sp => sp.GetRequiredService<ReenviosNotificaciones>().RegistrarAsync(
            Guid.NewGuid(), Canal.SMS, "Tu comprobante", "Texto", null, null, "grupo-07", null, default));

        Assert.Equal(EstadoNotificacion.FALLIDA, n.Estado);
        Assert.Contains("Sin contacto registrado", n.UltimoError);
    }
}
