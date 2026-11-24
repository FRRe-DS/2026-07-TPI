using Microsoft.EntityFrameworkCore;
using M8.Notificaciones.Api.Features.Viajes.Comandos;
using M8.Notificaciones.Api.Features.Viajes.Dominio;
using M8.Notificaciones.Api.Features.Viajes.Dtos;
using M8.Notificaciones.Api.Features.Viajes.EventStore;

namespace M8.Notificaciones.Tests.Viajes;

public class RegistrarEventoViajeHandlerTests
{
    [Fact]
    public async Task Registrar_los_tres_hitos_guarda_un_flujo_con_versiones_1_2_3()
    {
        using var esc = new EscenarioViajes();
        var conductor = Guid.NewGuid();

        var viajeId = await esc.RegistrarViajeCompleto(conductorId: conductor);

        var flujo = await esc.Store.LeerFlujoAsync(viajeId);
        Assert.Equal(new[] { 1, 2, 3 }, flujo.Select(e => e.Version));
        Assert.Equal(new[] { "ViajeSolicitado", "ConductorAsignado", "ViajeFinalizado" }, flujo.Select(e => e.Tipo));
        Assert.Equal(new[] { "M5", "M5", "M6" }, flujo.Select(e => e.Origen));
        Assert.Contains(conductor.ToString(), flujo[1].Payload);
    }

    [Fact]
    public async Task La_respuesta_trae_eventoId_viajeId_version_y_fechas_en_UTC()
    {
        using var esc = new EscenarioViajes();
        var viajeId = Guid.NewGuid();
        var request = Solicitudes.Solicitado(viajeId);

        var respuesta = await esc.Registrar(request);

        Assert.Equal(request.EventoId, respuesta.EventoId);
        Assert.Equal(viajeId, respuesta.ViajeId);
        Assert.Equal(1, respuesta.Version);
        Assert.Equal(EscenarioViajes.Inicio, respuesta.RegistradoEn);
        Assert.Equal(respuesta.RegistradoEn, respuesta.RecibidoEn);

        // ocurridoEn llegó con -03:00 y se guarda en UTC (Npgsql lo exige para timestamptz).
        var guardado = await esc.Db.EventosViaje.SingleAsync();
        Assert.Equal(TimeSpan.Zero, guardado.OcurridoEn.Offset);
        Assert.Equal(Solicitudes.Ocurrido.UtcDateTime, guardado.OcurridoEn.UtcDateTime);
    }

    [Fact]
    public async Task Mismo_eventoId_con_el_mismo_cuerpo_no_duplica_y_devuelve_la_respuesta_original()
    {
        using var esc = new EscenarioViajes();
        var request = Solicitudes.Solicitado(Guid.NewGuid());

        var primera = await esc.Registrar(request);
        esc.Reloj.Avanzar(TimeSpan.FromSeconds(10)); // el reintento llega más tarde
        var segunda = await esc.Registrar(request);

        Assert.Equal(primera, segunda);                   // misma respuesta, incluso registradoEn
        Assert.Equal(1, await esc.Db.EventosViaje.CountAsync()); // y un solo evento
    }

    [Fact]
    public async Task Mismo_eventoId_de_un_asignado_con_el_mismo_cuerpo_tambien_es_idempotente()
    {
        using var esc = new EscenarioViajes();
        var viajeId = Guid.NewGuid();
        await esc.Registrar(Solicitudes.Solicitado(viajeId));
        var asignado = Solicitudes.Asignado(viajeId, Guid.NewGuid());

        var primera = await esc.Registrar(asignado);
        var segunda = await esc.Registrar(asignado);

        Assert.Equal(primera, segunda);
        Assert.Equal(2, await esc.Db.EventosViaje.CountAsync());
    }

    [Fact]
    public async Task Mismo_eventoId_con_otro_cuerpo_lanza_EventoIdReutilizado()
    {
        using var esc = new EscenarioViajes();
        var viajeId = Guid.NewGuid();
        var eventoId = Guid.NewGuid();
        await esc.Registrar(Solicitudes.Solicitado(viajeId));
        await esc.Registrar(Solicitudes.Asignado(viajeId, Guid.NewGuid(), eventoId));

        // Mismo eventoId, pero otro conductor.
        await Assert.ThrowsAsync<EventoIdReutilizadoException>(
            () => esc.Registrar(Solicitudes.Asignado(viajeId, Guid.NewGuid(), eventoId)));
        Assert.Equal(2, await esc.Db.EventosViaje.CountAsync());
    }

    [Fact]
    public async Task Mismo_eventoId_con_otro_tipo_lanza_EventoIdReutilizado()
    {
        using var esc = new EscenarioViajes();
        var viajeId = Guid.NewGuid();
        var eventoId = Guid.NewGuid();
        await esc.Registrar(Solicitudes.Solicitado(viajeId, eventoId));

        await Assert.ThrowsAsync<EventoIdReutilizadoException>(
            () => esc.Registrar(Solicitudes.Finalizado(viajeId, eventoId)));
    }

    [Fact]
    public async Task Finalizar_sin_asignar_lanza_regla_de_negocio_y_no_guarda_nada()
    {
        using var esc = new EscenarioViajes();
        var viajeId = Guid.NewGuid();
        await esc.Registrar(Solicitudes.Solicitado(viajeId));

        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => esc.Registrar(Solicitudes.Finalizado(viajeId)));
        Assert.Equal(1, await esc.Db.EventosViaje.CountAsync());
    }

    public static IEnumerable<object[]> SolicitudesInvalidas()
    {
        var viaje = Guid.NewGuid();
        var t = Solicitudes.Ocurrido;
        yield return new object[] { new EventoEntranteRequest(null, "VIAJE_SOLICITADO", "M5", t, viaje, null) };
        yield return new object[] { new EventoEntranteRequest(Guid.Empty, "VIAJE_SOLICITADO", "M5", t, viaje, null) };
        yield return new object[] { new EventoEntranteRequest(Guid.NewGuid(), " ", "M5", t, viaje, null) };
        yield return new object[] { new EventoEntranteRequest(Guid.NewGuid(), "VIAJE_SOLICITADO", null, t, viaje, null) };
        yield return new object[] { new EventoEntranteRequest(Guid.NewGuid(), "VIAJE_SOLICITADO", "M99", t, viaje, null) };
        yield return new object[] { new EventoEntranteRequest(Guid.NewGuid(), "VIAJE_SOLICITADO", "M5", null, viaje, null) };
        yield return new object[] { new EventoEntranteRequest(Guid.NewGuid(), "VIAJE_SOLICITADO", "M5", t, null, null) };
        yield return new object[] { new EventoEntranteRequest(Guid.NewGuid(), "VIAJE_ASIGNADO", "M5", t, viaje, null) };
        yield return new object[] { new EventoEntranteRequest(Guid.NewGuid(), "VIAJE_ASIGNADO", "M5", t, viaje, new DetalleEventoRequest(Guid.Empty)) };
    }

    [Theory]
    [MemberData(nameof(SolicitudesInvalidas))]
    public async Task Cuerpos_incompletos_o_invalidos_lanzan_SolicitudInvalida(EventoEntranteRequest request)
    {
        using var esc = new EscenarioViajes();

        await Assert.ThrowsAsync<SolicitudInvalidaException>(() => esc.Registrar(request));
    }

    [Theory]
    [InlineData("RESERVA_CREADA")]
    [InlineData("VIAJE_CANCELADO")]
    public async Task Tipos_del_contrato_que_no_modelamos_lanzan_EventoNoSoportado(string tipo)
    {
        using var esc = new EscenarioViajes();
        var request = new EventoEntranteRequest(Guid.NewGuid(), tipo, "M5", Solicitudes.Ocurrido, Guid.NewGuid(), null);

        var error = await Assert.ThrowsAsync<EventoNoSoportadoException>(() => esc.Registrar(request));
        Assert.Contains("VIAJE_SOLICITADO", error.Message);
    }

    [Fact]
    public async Task Agregar_en_una_version_ya_ocupada_lanza_conflicto_de_concurrencia()
    {
        // Simula la carrera: otro comando ya escribió la versión 1 de este viaje
        // entre que leímos el flujo y quisimos guardar.
        using var esc = new EscenarioViajes();
        var viajeId = Guid.NewGuid();
        await esc.Registrar(Solicitudes.Solicitado(viajeId));

        var competidor = new EventoAlmacenado
        {
            EventoId = Guid.NewGuid(), ViajeId = viajeId, Version = 1, Tipo = "ViajeSolicitado",
            Origen = "M5", Payload = "{}", OcurridoEn = EscenarioViajes.Inicio, RegistradoEn = EscenarioViajes.Inicio
        };

        await Assert.ThrowsAsync<ConflictoDeConcurrenciaException>(() => esc.Store.AgregarAsync(competidor));
        Assert.Equal(1, await esc.Db.EventosViaje.CountAsync());
    }

    [Fact]
    public async Task Si_la_base_rechaza_el_insert_por_indice_unico_se_traduce_a_conflicto()
    {
        using var esc = new EscenarioViajes(db: FallaAlGuardar.CrearDb());

        var error = await Assert.ThrowsAsync<ConflictoDeConcurrenciaException>(
            () => esc.Registrar(Solicitudes.Solicitado(Guid.NewGuid())));

        Assert.IsType<DbUpdateException>(error.InnerException);
        Assert.Empty(esc.Db.ChangeTracker.Entries()); // el contexto no queda "sucio"
    }
}
