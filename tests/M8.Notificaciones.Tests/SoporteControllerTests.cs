using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using M8.Notificaciones.Tests.Notificaciones.Apoyo;

namespace M8.Notificaciones.Tests;

public class SoporteControllerTests
{
    private const string Ruta = "/api/v1/tickets";
    private static object Pedido(string tipo = "RESERVA") => new
    {
        referencia = new { tipo, id = Guid.NewGuid() }, titulo = "Cobro", descripcion = "Revisar el importe"
    };

    [Theory]
    [InlineData("CLIENTE", "VIAJE")]
    [InlineData("CONDUCTOR", "RESERVA")]
    [InlineData("CLIENTE", "PAGO")]
    public async Task Alta_transiciones_historial_y_repeticion_original(string rol, string tipo)
    {
        using var api = new ApiDePrueba();
        using var titular = api.ClienteCon(rol);
        using var operador = api.ClienteOperador();
        var pedido = Pedido(tipo);
        var alta = await titular.SendAsync(Json.Post(Ruta, pedido, "clave"));
        Assert.Equal(HttpStatusCode.Created, alta.StatusCode);
        var original = await alta.Content.ReadAsStringAsync();
        var url = alta.Headers.Location!;
        var ticket = await alta.LeerAsync();
        Assert.Equal(rol, ticket.GetProperty("rol").GetString());
        Assert.Equal(tipo, ticket.GetProperty("referencia").GetProperty("tipo").GetString());

        Assert.Equal(HttpStatusCode.Conflict, (await operador.PatchAsJsonAsync(url, new { estado = "CERRADO" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await operador.PatchAsJsonAsync(url, new { estado = "EN_REVISION" })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await operador.PatchAsJsonAsync(url, new { estado = "RESUELTO" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await operador.PatchAsJsonAsync(url, new { estado = "RESUELTO", resolucion = "Corregido" })).StatusCode);
        var reapertura = await operador.PatchAsJsonAsync(url, new { estado = "EN_REVISION", motivo = "Nueva evidencia" });
        Assert.False((await reapertura.LeerAsync()).TryGetProperty("resolucion", out _));
        await operador.PatchAsJsonAsync(url, new { estado = "RESUELTO", resolucion = "Confirmado" });
        var cierre = await operador.PatchAsJsonAsync(url, new { estado = "CERRADO" });
        Assert.True((await cierre.LeerAsync()).TryGetProperty("cerradoEn", out _));
        var repetida = await titular.SendAsync(Json.Post(Ruta, pedido, "clave"));
        Assert.Equal(HttpStatusCode.Created, repetida.StatusCode);
        Assert.Equal(url, repetida.Headers.Location);
        Assert.Equal(original, await repetida.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await titular.SendAsync(Json.Post(Ruta, Pedido(), "clave"))).StatusCode);
        var historial = await (await titular.GetAsync(url + "/historial?page=1&size=2")).LeerAsync();
        Assert.Equal(6, historial.GetProperty("totalElementos").GetInt32());
        Assert.Equal(3, historial.GetProperty("totalPaginas").GetInt32());
        Assert.Equal("Nueva evidencia", historial.GetProperty("items")[1].GetProperty("motivo").GetString());
        Assert.Equal("RESUELTO", historial.GetProperty("items")[1].GetProperty("estadoAnterior").GetString());

        // Alta, revisión, resolución, reapertura, segunda resolución y cierre.
        Assert.Equal(6, await api.DespacharAsync());
        Assert.Equal(0, await api.DespacharAsync());
        var entregados = api.Proveedor.Enviados.ToArray();
        Assert.Equal(6, entregados.Length);
        Assert.Equal(6, entregados.Select(n => n.NotificacionId).Distinct().Count());
        Assert.All(entregados, n => Assert.Equal(ticket.GetProperty("usuarioId").GetGuid(), n.DestinatarioId));
    }

    [Fact]
    public async Task Aisla_titulares_y_aplica_filtros_y_permisos()
    {
        using var api = new ApiDePrueba();
        using var uno = api.ClienteCon("CLIENTE");
        using var dos = api.ClienteCon("CONDUCTOR");
        using var operador = api.ClienteOperador();
        var pedido = Pedido("PAGO");
        var alta = await uno.SendAsync(Json.Post(Ruta, pedido, "compartida"));
        var ticket = await alta.LeerAsync();
        var url = alta.Headers.Location!;
        Assert.Equal(HttpStatusCode.Created, (await dos.SendAsync(Json.Post(Ruta, pedido, "compartida"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await dos.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await dos.GetAsync(url + "/historial")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await uno.PatchAsJsonAsync(url, new { estado = "EN_REVISION" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await operador.SendAsync(Json.Post(Ruta, pedido, "otro"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.CreateClient().GetAsync(Ruta)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await api.ClienteServicio().GetAsync(Ruta)).StatusCode);

        var usuario = ticket.GetProperty("usuarioId").GetGuid();
        var referencia = ticket.GetProperty("referencia").GetProperty("id").GetGuid();
        var filtros = $"?usuarioId={usuario}&referenciaId={referencia}&referenciaTipo=PAGO&estado=ABIERTO";
        Assert.Equal(HttpStatusCode.Forbidden, (await dos.GetAsync(Ruta + filtros)).StatusCode);
        var lista = await (await operador.GetAsync(Ruta + filtros)).LeerAsync();
        Assert.Equal(usuario, Assert.Single(lista.GetProperty("items").EnumerateArray()).GetProperty("usuarioId").GetGuid());
        var propios = await (await dos.GetAsync(Ruta)).LeerAsync();
        Assert.NotEqual(usuario, Assert.Single(propios.GetProperty("items").EnumerateArray()).GetProperty("usuarioId").GetGuid());
        var ausente = await uno.GetAsync(Ruta + "/" + Guid.NewGuid());
        Assert.Equal(HttpStatusCode.NotFound, ausente.StatusCode);
        Assert.Equal("application/problem+json", ausente.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task Valida_clave_cuerpo_y_paginacion()
    {
        using var api = new ApiDePrueba();
        using var cliente = api.ClienteCon("CLIENTE");
        var referencia = new { tipo = "VIAJE", id = Guid.NewGuid() };
        Assert.Equal(HttpStatusCode.BadRequest, (await cliente.PostAsJsonAsync(Ruta, Pedido())).StatusCode);
        foreach (var pedido in new object[] {
            new { referencia, titulo = "", descripcion = "texto" },
            new { referencia, titulo = "titulo", descripcion = new string('a', 2001) },
            new { referencia, titulo = "titulo", descripcion = "texto", usuarioId = Guid.NewGuid() },
            new { referencia = new { tipo = "PAGO" }, titulo = "titulo", descripcion = "texto" },
            Pedido("APP") })
            Assert.Equal(HttpStatusCode.BadRequest, (await cliente.SendAsync(Json.Post(Ruta, pedido, "clave"))).StatusCode);
        foreach (var query in new[] { "?page=-1", "?size=101", "?estado=Abierto", "?referenciaTipo=APP" })
            Assert.Equal(HttpStatusCode.BadRequest, (await cliente.GetAsync(Ruta + query)).StatusCode);
    }

    [Fact]
    public async Task Operador_puede_consultar_metricas_operativas()
    {
        using var api = new ApiDePrueba();
        using var cliente = api.ClienteCon("CLIENTE");
        using var operador = api.ClienteOperador();

        // Cliente no tiene permiso de ver métricas (RNF-12)
        var noAutorizado = await cliente.GetAsync(Ruta + "/metricas");
        Assert.Equal(HttpStatusCode.Forbidden, noAutorizado.StatusCode);

        // Operador sí puede consultar métricas
        var respuesta = await operador.GetAsync(Ruta + "/metricas");
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var metricas = await respuesta.LeerAsync();
        Assert.Equal(0, metricas.GetProperty("totalTickets").GetInt32());
        Assert.Equal(0, metricas.GetProperty("tiempoMedioResolucionMinutos").GetDouble());
    }

    private sealed class RelojDeSoporte : TimeProvider
    {
        public DateTimeOffset Ahora { get; set; } = new(2026, 11, 23, 9, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Ahora;
    }

    [Fact]
    public async Task Metrica_promedia_la_primera_resolucion_por_ticket_aunque_se_reabra_o_cierre_dias_despues()
    {
        var reloj = new RelojDeSoporte();
        using var baseApi = new ApiDePrueba();
        using var api = baseApi.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<TimeProvider>(reloj)));
        using var titular = api.CreateClient();
        titular.DefaultRequestHeaders.Authorization = new("Bearer", ApiDePrueba.Token(["CLIENTE"]));
        using var operador = api.CreateClient();
        operador.DefaultRequestHeaders.Authorization = new("Bearer", ApiDePrueba.Token(["OPERADOR"]));

        async Task<Uri> Abrir(string clave)
        {
            var alta = await titular.SendAsync(Json.Post(Ruta, Pedido(), clave));
            return alta.EnsureSuccessStatusCode().Headers.Location!;
        }

        async Task Cambiar(Uri url, string estado)
        {
            var r = await operador.PatchAsJsonAsync(url, new { estado, resolucion = "Verificado" });
            r.EnsureSuccessStatusCode();
        }

        var primero = await Abrir("primero");
        var segundo = await Abrir("segundo");
        await Abrir("pendiente");
        await Cambiar(primero, "EN_REVISION");
        await Cambiar(segundo, "EN_REVISION");
        reloj.Ahora = reloj.Ahora.AddMinutes(30);
        await Cambiar(primero, "RESUELTO");
        reloj.Ahora = reloj.Ahora.AddHours(1);
        await Cambiar(segundo, "RESUELTO");
        await Cambiar(primero, "EN_REVISION");
        reloj.Ahora = reloj.Ahora.AddDays(2);
        await Cambiar(primero, "RESUELTO");
        reloj.Ahora = reloj.Ahora.AddDays(2);
        await Cambiar(primero, "CERRADO");

        var metricas = await (await operador.GetAsync(Ruta + "/metricas")).EnsureSuccessStatusCode().LeerAsync();
        Assert.Equal(60, metricas.GetProperty("tiempoMedioResolucionMinutos").GetDouble());
        Assert.Equal(3, metricas.GetProperty("totalTickets").GetInt32());
        Assert.Equal(1, metricas.GetProperty("resueltos").GetInt32());
        Assert.Equal(1, metricas.GetProperty("cerrados").GetInt32());
    }
}
