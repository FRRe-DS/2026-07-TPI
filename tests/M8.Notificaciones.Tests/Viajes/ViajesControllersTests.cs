using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using M8.Notificaciones.Api.Features.Viajes.Consultas;
using M8.Notificaciones.Api.Features.Viajes.Controllers;
using M8.Notificaciones.Api.Features.Viajes.Dtos;

namespace M8.Notificaciones.Tests.Viajes;

/// <summary>Los controladores traducen bien cada resultado a su código HTTP.</summary>
public class ViajesControllersTests
{
    private static EventosController Eventos(EscenarioViajes esc) => new(esc.Handler);
    private static ViajesEventStoreController EventStore(EscenarioViajes esc) => new(new ConsultasEventStore(esc.Store));
    private static ViajesHistorialController Historial(EscenarioViajes esc) => new(new ConsultasHistorial(esc.Db, esc.Reloj));

    private static ProblemDetails AssertProblema(IActionResult resultado, int status, string type)
    {
        var obj = Assert.IsType<ObjectResult>(resultado);
        Assert.Equal(status, obj.StatusCode);
        Assert.Contains("application/problem+json", obj.ContentTypes);
        var problema = Assert.IsType<ProblemDetails>(obj.Value);
        Assert.Equal(status, problema.Status);
        Assert.Equal(type, problema.Type);
        return problema;
    }

    [Fact]
    public async Task Post_eventos_valido_devuelve_202_con_la_version()
    {
        using var esc = new EscenarioViajes();

        var resultado = await Eventos(esc).Registrar(Solicitudes.Solicitado(Guid.NewGuid()), CancellationToken.None);

        var aceptado = Assert.IsType<AcceptedResult>(resultado);
        Assert.Equal(202, aceptado.StatusCode);
        Assert.Equal(1, Assert.IsType<EventoRegistradoResponse>(aceptado.Value).Version);
    }

    [Fact]
    public async Task Post_eventos_repetido_devuelve_202_otra_vez_con_la_misma_respuesta()
    {
        using var esc = new EscenarioViajes();
        var request = Solicitudes.Solicitado(Guid.NewGuid());

        var primera = Assert.IsType<AcceptedResult>(await Eventos(esc).Registrar(request, CancellationToken.None));
        var segunda = Assert.IsType<AcceptedResult>(await Eventos(esc).Registrar(request, CancellationToken.None));

        Assert.Equal(primera.Value, segunda.Value);
    }

    [Fact]
    public async Task Finalizar_sin_asignar_devuelve_409_problem_json()
    {
        using var esc = new EscenarioViajes();
        var viajeId = Guid.NewGuid();
        await esc.Registrar(Solicitudes.Solicitado(viajeId));

        var resultado = await Eventos(esc).Registrar(Solicitudes.Finalizado(viajeId), CancellationToken.None);

        var problema = AssertProblema(resultado, 409, "/errores/transicion-de-viaje-invalida");
        Assert.Contains("ASIGNADO", problema.Detail);
    }

    [Fact]
    public async Task Conflicto_de_concurrencia_devuelve_409_problem_json()
    {
        // PostgreSQL rechaza el INSERT por el índice único (ViajeId, Version): otro comando ganó la carrera.
        using var esc = new EscenarioViajes(db: FallaAlGuardar.CrearDb());

        var resultado = await Eventos(esc).Registrar(Solicitudes.Solicitado(Guid.NewGuid()), CancellationToken.None);

        AssertProblema(resultado, 409, "/errores/conflicto-de-concurrencia");
    }

    [Fact]
    public async Task EventoId_reutilizado_con_otro_cuerpo_devuelve_422_problem_json()
    {
        using var esc = new EscenarioViajes();
        var viajeId = Guid.NewGuid();
        var eventoId = Guid.NewGuid();
        await esc.Registrar(Solicitudes.Solicitado(viajeId, eventoId));

        var otroCuerpo = Solicitudes.Solicitado(viajeId, eventoId) with { Origen = "M6" };
        var resultado = await Eventos(esc).Registrar(otroCuerpo, CancellationToken.None);

        AssertProblema(resultado, 422, "/errores/evento-id-reutilizado");
    }

    [Fact]
    public async Task Tipo_no_soportado_devuelve_422_problem_json()
    {
        using var esc = new EscenarioViajes();
        var request = Solicitudes.Solicitado(Guid.NewGuid()) with { Tipo = "PAGO_CAPTURADO" };

        var resultado = await Eventos(esc).Registrar(request, CancellationToken.None);

        AssertProblema(resultado, 422, "/errores/evento-no-soportado");
    }

    [Fact]
    public async Task Cuerpo_invalido_devuelve_400_problem_json()
    {
        using var esc = new EscenarioViajes();
        var request = Solicitudes.Solicitado(Guid.NewGuid()) with { ViajeId = null };

        var resultado = await Eventos(esc).Registrar(request, CancellationToken.None);

        AssertProblema(resultado, 400, "about:blank");
    }

    [Fact]
    public async Task Get_estado_reconstruye_el_viaje_reproduciendo_sus_eventos()
    {
        using var esc = new EscenarioViajes();
        var conductor = Guid.NewGuid();
        var viajeId = await esc.RegistrarViajeCompleto(conductorId: conductor);

        var resultado = await EventStore(esc).ObtenerEstado(viajeId, CancellationToken.None);

        var estado = Assert.IsType<EstadoViajeResponse>(Assert.IsType<OkObjectResult>(resultado).Value);
        Assert.Equal("FINALIZADO", estado.Estado);
        Assert.Equal(conductor, estado.ConductorId);
        Assert.Equal(3, estado.Version);
        Assert.Equal(3, estado.EventosReproducidos);
        Assert.NotNull(estado.Hitos.FinalizadoEn);
    }

    [Fact]
    public async Task Get_estado_y_eventos_de_un_viaje_sin_eventos_devuelven_404()
    {
        using var esc = new EscenarioViajes();
        var viajeId = Guid.NewGuid();

        AssertProblema(await EventStore(esc).ObtenerEstado(viajeId, CancellationToken.None), 404, "about:blank");
        AssertProblema(await EventStore(esc).ObtenerEventos(viajeId, CancellationToken.None), 404, "about:blank");
    }

    [Fact]
    public async Task Get_eventos_devuelve_el_flujo_en_orden_con_el_payload_como_objeto()
    {
        using var esc = new EscenarioViajes();
        var viajeId = await esc.RegistrarViajeCompleto();

        var resultado = await EventStore(esc).ObtenerEventos(viajeId, CancellationToken.None);

        var flujo = Assert.IsType<FlujoEventosResponse>(Assert.IsType<OkObjectResult>(resultado).Value);
        Assert.Equal(3, flujo.Cantidad);
        Assert.Equal(new[] { "ViajeSolicitado", "ConductorAsignado", "ViajeFinalizado" }, flujo.Eventos.Select(e => e.Tipo));
        Assert.Equal(JsonValueKind.Object, flujo.Eventos[1].Payload.ValueKind);
        Assert.True(flujo.Eventos[1].Payload.TryGetProperty("conductorId", out _));
    }

    [Fact]
    public async Task Get_historial_antes_de_proyectar_devuelve_200_con_items_vacio()
    {
        using var esc = new EscenarioViajes();
        var viajeId = await esc.RegistrarViajeCompleto();

        var resultado = await Historial(esc).ObtenerHistorial(viajeId, CancellationToken.None);

        var historial = Assert.IsType<HistorialViajeResponse>(Assert.IsType<OkObjectResult>(resultado.Result).Value);
        Assert.Empty(historial.Items);
        Assert.Null(historial.Resumen.Estado);
        Assert.Null(historial.Resumen.ConductorId);
    }

    [Fact]
    public async Task Get_historial_despues_de_proyectar_muestra_los_items_y_el_resumen()
    {
        using var esc = new EscenarioViajes(retrasoMs: 3000);
        var conductor = Guid.NewGuid();
        var viajeId = await esc.RegistrarViajeCompleto(conductorId: conductor);
        await esc.Proyeccion.ProyectarPendientesAsync(EscenarioViajes.Inicio.AddSeconds(4));

        var resultado = await Historial(esc).ObtenerHistorial(viajeId, CancellationToken.None);

        var historial = Assert.IsType<HistorialViajeResponse>(Assert.IsType<OkObjectResult>(resultado.Result).Value);
        Assert.Equal(3, historial.Items.Count);
        Assert.Equal("FINALIZADO", historial.Resumen.Estado);
        Assert.Equal(conductor, historial.Resumen.ConductorId);
        Assert.Contains("informado por M6", historial.Items[2].Descripcion);
        Assert.All(historial.Items, i => Assert.Equal(4000, i.RetrasoMs));
    }

    [Fact]
    public async Task Lab_estado_y_reconstruir_devuelven_200()
    {
        using var esc = new EscenarioViajes(retrasoMs: 3000);
        await esc.RegistrarViajeCompleto();
        var lab = new LabProyeccionController(esc.Proyeccion, esc.Reloj);

        var antes = Assert.IsType<EstadoProyeccionResponse>(
            Assert.IsType<OkObjectResult>((await lab.ObtenerEstado(CancellationToken.None)).Result).Value);
        var reconstruccion = Assert.IsType<ReconstruccionResponse>(
            Assert.IsType<OkObjectResult>((await lab.Reconstruir(CancellationToken.None)).Result).Value);
        var despues = Assert.IsType<EstadoProyeccionResponse>(
            Assert.IsType<OkObjectResult>((await lab.ObtenerEstado(CancellationToken.None)).Result).Value);

        Assert.Equal(3, antes.Pendientes);
        Assert.Equal(3, reconstruccion.EventosReproducidos);
        Assert.Equal(EscenarioViajes.Inicio, reconstruccion.ReconstruidoEn);
        Assert.Equal((3, 0), (despues.EventosProyectados, despues.Pendientes));
    }
}
