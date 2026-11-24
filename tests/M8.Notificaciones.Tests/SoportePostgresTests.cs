using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using M8.Notificaciones.Api.Infrastructure.Persistence;
using M8.Notificaciones.Tests.Notificaciones.Apoyo;
using M8.Notificaciones.Tests.Viajes;

namespace M8.Notificaciones.Tests;

public class SoportePostgresTests
{
    [PostgresFact]
    public async Task Altas_y_cambios_concurrentes_generan_un_solo_efecto()
    {
        var cadena = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable(PostgresFactAttribute.Variable))
        {
            Database = "m8_test_soporte_" + Guid.NewGuid().ToString("N")
        }.ConnectionString;
        var opciones = new DbContextOptionsBuilder<M8DbContext>().UseNpgsql(cadena).Options;
        await using var db = new M8DbContext(opciones);
        try
        {
            using var baseApi = new ApiDePrueba();
            using var api = baseApi.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
            {
                services.RemoveAll<DbContextOptions<M8DbContext>>();
                services.RemoveAll<M8DbContext>();
                services.AddDbContext<M8DbContext>(o => o.UseNpgsql(cadena));
            }));
            using var cliente = api.CreateClient();
            cliente.DefaultRequestHeaders.Authorization = new("Bearer", ApiDePrueba.Token(["CLIENTE"]));
            var pedido = new { referencia = new { tipo = "VIAJE", id = Guid.NewGuid() }, titulo = "Viaje", descripcion = "Revisar" };
            var altas = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
                cliente.SendAsync(Json.Post("/api/v1/tickets", pedido, "misma-clave"))));
            Assert.All(altas, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
            Assert.Single((await Task.WhenAll(altas.Select(r => r.Content.ReadAsStringAsync()))).Distinct());
            var url = Assert.Single(altas.Select(r => r.Headers.Location).Distinct())!;
            using var operador = api.CreateClient();
            operador.DefaultRequestHeaders.Authorization = new("Bearer", ApiDePrueba.Token(["OPERADOR"]));
            var cambios = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
                operador.PatchAsJsonAsync(url, new { estado = "EN_REVISION" })));
            Assert.Single(cambios, r => r.StatusCode == HttpStatusCode.OK);
            Assert.Equal(7, cambios.Count(r => r.StatusCode == HttpStatusCode.Conflict));
            var historial = await (await cliente.GetAsync(url + "/historial")).LeerAsync();
            Assert.Equal(2, historial.GetProperty("totalElementos").GetInt32());
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }
}
