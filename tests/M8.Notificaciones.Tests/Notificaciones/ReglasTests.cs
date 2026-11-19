using System.Security.Claims;
using M8.Notificaciones.Api.Features.Eventos;
using M8.Notificaciones.Api.Features.Notificaciones;
using M8.Notificaciones.Api.Features.Notificaciones.Dominio;
using M8.Notificaciones.Api.Features.Notificaciones.Entities;
using M8.Notificaciones.Api.Features.Notificaciones.Servicios;
using M8.Notificaciones.Api.Infrastructure.Http;
using M8.Notificaciones.Api.Infrastructure.Seguridad;
using M8.Notificaciones.Tests.Notificaciones.Apoyo;

namespace M8.Notificaciones.Tests.Notificaciones;

/// <summary>Reglas puras: validación del evento, canal, plantillas, roles, X-Request-Id y esperas.</summary>
public class ReglasTests
{
    // ------------------------- Validación (contrato EventoEntrada) -------------------------

    [Fact]
    public void Un_evento_completo_de_viaje_es_valido()
    {
        Assert.Empty(ValidadorEvento.Validar(Eventos.Viaje()));
    }

    [Fact]
    public void Faltan_los_obligatorios()
    {
        var errores = ValidadorEvento.Validar(new(null, null, null, null, null, null, null, null, null));
        Assert.Contains(errores, e => e.Contains("eventoId"));
        Assert.Contains(errores, e => e.Contains("'tipo'"));
        Assert.Contains(errores, e => e.Contains("'origen'"));
        Assert.Contains(errores, e => e.Contains("ocurridoEn"));
        Assert.Contains(errores, e => e.Contains("al menos uno"));
    }

    [Fact]
    public void Cada_tipo_exige_su_identificador()
    {
        var reservaSinId = Eventos.Reserva() with { ReservaId = null, ViajeId = Guid.NewGuid() };
        Assert.Contains(ValidadorEvento.Validar(reservaSinId), e => e.Contains("reservaId"));

        var pagoSinViaje = Eventos.Pago() with { ViajeId = null };
        Assert.Contains(ValidadorEvento.Validar(pagoSinViaje), e => e.Contains("'pagoId' y 'viajeId'"));
    }

    [Theory]
    [InlineData("no-es-un-mail", null, "email")]
    [InlineData(null, "3624000000", "E.164")]
    public void Valida_formato_de_contactos(string? email, string? telefono, string esperado)
    {
        var e = Eventos.Viaje(destinatarios: Eventos.Cliente(email: email, telefono: telefono));
        Assert.Contains(ValidadorEvento.Validar(e), x => x.Contains(esperado));
    }

    [Fact]
    public void Mas_de_cinco_destinatarios_o_lista_vacia_es_invalido()
    {
        var seis = Enumerable.Range(0, 6).Select(_ => Eventos.Cliente()).ToArray();
        Assert.Contains(ValidadorEvento.Validar(Eventos.Viaje(destinatarios: seis)), e => e.Contains("entre 1 y 5"));
        Assert.Contains(ValidadorEvento.Validar(Eventos.Viaje() with { Destinatarios = Array.Empty<M8.Notificaciones.Api.Features.Eventos.Dtos.DestinatarioEntrada>() }),
            e => e.Contains("entre 1 y 5"));
    }

    [Fact]
    public void Sin_destinatarios_es_valido_y_no_genera_avisos()
    {
        Assert.Empty(ValidadorEvento.Validar(Eventos.Viaje() with { Destinatarios = null }));
    }

    [Fact]
    public void Moneda_e_importe_se_validan()
    {
        var e = Eventos.Pago() with
        {
            Detalle = new(null, null, null, null, null, null, new(-1, "pesos"), null)
        };
        var errores = ValidadorEvento.Validar(e);
        Assert.Contains(errores, x => x.Contains("monto"));
        Assert.Contains(errores, x => x.Contains("ISO 4217"));
    }

    // ------------------------------- Elección de canal -------------------------------

    [Fact]
    public void Sin_preferencias_usa_el_canal_por_defecto_si_hay_contacto()
    {
        var r = SelectorCanal.Elegir(null, Canal.EMAIL, "a@b.com", "+5491100000000");
        Assert.Equal(Canal.EMAIL, r.Canal);
        Assert.Equal("a@b.com", r.Contacto);
        Assert.True(r.Utilizable);
    }

    [Fact]
    public void Sin_email_cae_al_siguiente_canal_que_tenga_contacto()
    {
        var r = SelectorCanal.Elegir(null, Canal.EMAIL, null, "+5491100000000");
        Assert.Equal(Canal.SMS, r.Canal);
    }

    [Fact]
    public void Respeta_el_orden_de_preferencias()
    {
        var r = SelectorCanal.Elegir(new[] { Canal.SMS, Canal.EMAIL }, Canal.EMAIL, "a@b.com", "+5491100000000");
        Assert.Equal(Canal.SMS, r.Canal);
    }

    [Fact]
    public void Push_no_necesita_contacto()
    {
        var r = SelectorCanal.Elegir(new[] { Canal.EMAIL, Canal.PUSH }, Canal.EMAIL, null, null);
        Assert.Equal(Canal.PUSH, r.Canal);
        Assert.Null(r.Contacto);
        Assert.True(r.Utilizable);
    }

    [Fact]
    public void Si_ningun_canal_preferido_sirve_no_es_utilizable()
    {
        var r = SelectorCanal.Elegir(new[] { Canal.EMAIL }, Canal.EMAIL, null, "+5491100000000");
        Assert.False(r.Utilizable);
        Assert.Equal(Canal.EMAIL, r.Canal);
    }

    [Fact]
    public void Fabrica_marca_FALLIDA_la_notificacion_sin_contacto_util()
    {
        var fabrica = new FabricaNotificaciones(new OpcionesNotificaciones());
        var e = Eventos.Viaje(destinatarios: Eventos.Cliente(email: null, canales: Canal.EMAIL));

        var n = Assert.Single(fabrica.DesdeEvento(e, new Dictionary<Guid, ContactoDestinatario>(), Eventos.Momento));

        Assert.Equal(EstadoNotificacion.FALLIDA, n.Estado);
        Assert.Null(n.ProximoIntentoEn);
        Assert.Contains("Sin contacto", n.UltimoError);
    }

    [Fact]
    public void Fabrica_usa_el_contacto_registrado_si_el_evento_no_lo_trae()
    {
        var fabrica = new FabricaNotificaciones(new OpcionesNotificaciones());
        var usuario = Guid.NewGuid();
        var e = Eventos.Viaje(destinatarios: Eventos.Cliente(usuario, email: null));
        var registrados = new Dictionary<Guid, ContactoDestinatario>
        {
            [usuario] = new() { UsuarioId = usuario, Email = "registrado@ejemplo.com" }
        };

        var n = Assert.Single(fabrica.DesdeEvento(e, registrados, Eventos.Momento));

        Assert.Equal(Canal.EMAIL, n.Canal);
        Assert.Equal("registrado@ejemplo.com", n.Contacto);
        Assert.Equal(EstadoNotificacion.PENDIENTE, n.Estado);
        Assert.Equal(3, n.MaxIntentos);
    }

    [Fact]
    public void Fabrica_no_duplica_un_destinatario_repetido_en_el_evento()
    {
        var fabrica = new FabricaNotificaciones(new OpcionesNotificaciones());
        var id = Guid.NewGuid();
        var e = Eventos.Viaje(destinatarios: new[] { Eventos.Cliente(id), Eventos.Cliente(id) });

        Assert.Single(fabrica.DesdeEvento(e, new Dictionary<Guid, ContactoDestinatario>(), Eventos.Momento));
    }

    [Fact]
    public void La_referencia_depende_del_tipo_de_evento()
    {
        var pago = Eventos.Pago();
        Assert.Equal((TipoReferencia.PAGO, pago.PagoId), FabricaNotificaciones.Referencia(pago));
        var reserva = Eventos.Reserva();
        Assert.Equal((TipoReferencia.RESERVA, reserva.ReservaId), FabricaNotificaciones.Referencia(reserva));
    }

    // ------------------------------------ Plantillas ------------------------------------

    [Fact]
    public void Hay_plantilla_para_cada_tipo_y_rol()
    {
        foreach (var tipo in Enum.GetValues<TipoEvento>())
        foreach (var rol in Enum.GetValues<RolDestinatario>())
        {
            var (asunto, mensaje) = PlantillasMensajes.Redactar(tipo, rol, null);
            Assert.False(string.IsNullOrWhiteSpace(asunto));
            Assert.False(string.IsNullOrWhiteSpace(mensaje));
            Assert.True(asunto.Length <= 150 && mensaje.Length <= 1000);
        }
    }

    [Fact]
    public void La_fecha_de_la_reserva_se_muestra_en_su_zona_horaria()
    {
        var e = Eventos.Reserva(TipoEvento.RESERVA_RECORDATORIO);
        var (_, mensaje) = PlantillasMensajes.Redactar(e.Tipo!.Value, RolDestinatario.CLIENTE, e.Detalle);
        // 09:30 UTC = 06:30 en Argentina.
        Assert.Contains("01/12/2026 a las 06:30", mensaje);
        Assert.Contains("de Casa a Aeropuerto", mensaje);
    }

    [Fact]
    public void El_mensaje_de_pago_incluye_el_importe()
    {
        var e = Eventos.Pago();
        var (_, mensaje) = PlantillasMensajes.Redactar(e.Tipo!.Value, RolDestinatario.CLIENTE, e.Detalle);
        Assert.Contains("ARS", mensaje);
        Assert.Contains("1.534,50", mensaje);
    }

    // -------------------------------------- Roles --------------------------------------

    [Fact]
    public void Los_roles_be_de_keycloak_se_traducen_a_SERVICIO()
    {
        var mapeo = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase) { ["viajes-be"] = new[] { "SERVICIO" } };
        var roles = TransformacionRoles.RolesDelContrato(new[] { "viajes-be", "default-roles-ds-2026-realm" }, mapeo);
        Assert.Equal(new[] { "SERVICIO" }, roles);
    }

    [Fact]
    public void Un_rol_be_no_mapeado_no_da_permisos()
    {
        var roles = TransformacionRoles.RolesDelContrato(new[] { "clientes-be" }, new Dictionary<string, string[]>());
        Assert.Empty(roles);
    }

    [Fact]
    public void Los_roles_del_contrato_pasan_sin_traducir()
    {
        var roles = TransformacionRoles.RolesDelContrato(new[] { "operador", "CLIENTE" }, new Dictionary<string, string[]>());
        Assert.Equal(new[] { "CLIENTE", "OPERADOR" }, roles.OrderBy(r => r));
    }

    [Fact]
    public void Lee_realm_access_y_el_claim_roles()
    {
        var identidad = new ClaimsIdentity(new[]
        {
            new Claim("realm_access", "{\"roles\":[\"reservas-be\"]}"),
            new Claim("roles", "OPERADOR"),
            new Claim("realm_access", "esto no es json")
        }, "test");
        var roles = TransformacionRoles.LeerRolesDelToken(new ClaimsPrincipal(identidad)).ToList();
        Assert.Equal(new[] { "reservas-be", "OPERADOR" }, roles);
    }

    // ----------------------------------- X-Request-Id -----------------------------------

    [Theory]
    [InlineData("abc-123_X.9", true)]
    [InlineData("", false)]
    [InlineData("con espacio", false)]
    [InlineData("ñ", false)]
    public void Formato_de_X_Request_Id(string valor, bool valido)
    {
        Assert.Equal(valido, RequestIdMiddleware.EsValido(valor));
    }

    [Fact]
    public void X_Request_Id_de_mas_de_128_caracteres_no_es_valido()
    {
        Assert.True(RequestIdMiddleware.EsValido(new string('a', 128)));
        Assert.False(RequestIdMiddleware.EsValido(new string('a', 129)));
    }

    // ------------------------------- Espera entre intentos -------------------------------

    [Fact]
    public void La_espera_entre_intentos_es_creciente_y_repite_la_ultima()
    {
        var o = new OpcionesEnvio { EsperasSegundos = new[] { 5, 30 } };
        Assert.Equal(TimeSpan.FromSeconds(5), o.EsperaAntesDelIntento(2));
        Assert.Equal(TimeSpan.FromSeconds(30), o.EsperaAntesDelIntento(3));
        Assert.Equal(TimeSpan.FromSeconds(30), o.EsperaAntesDelIntento(7));
        Assert.Equal(TimeSpan.FromSeconds(5), new OpcionesEnvio().EsperaAntesDelIntento(2));
    }

    [Fact]
    public void Clave_de_enrutamiento_sugerida_por_tipo()
    {
        Assert.Equal("viaje.asignado", Api.Infrastructure.Mensajeria.ConsumidorEventosRabbitMq.ClaveDe("VIAJE_ASIGNADO"));
        Assert.Equal("viaje.arribo.conductor", Api.Infrastructure.Mensajeria.ConsumidorEventosRabbitMq.ClaveDe("VIAJE_ARRIBO_CONDUCTOR"));
        Assert.Equal("reintegro.registrado", Api.Infrastructure.Mensajeria.ConsumidorEventosRabbitMq.ClaveDe("REINTEGRO_REGISTRADO"));
    }
}
