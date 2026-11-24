using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using M8.Notificaciones.Api.Features.Viajes;
using M8.Notificaciones.Api.Features.Viajes.Comandos;
using M8.Notificaciones.Api.Features.Viajes.Consultas;
using M8.Notificaciones.Api.Features.Viajes.Proyecciones;
using M8.Notificaciones.Api.Infrastructure.Persistence;

namespace M8.Notificaciones.Tests.Viajes;

/// <summary>
/// El BackgroundService solo "llama" a la proyección; acá se prueba que lo
/// haga con un scope propio y que el registro de dependencias esté completo.
/// </summary>
public class ProyectorYRegistroTests
{
    /// <summary>Contenedor de servicios como el de la API, pero con base en memoria y reloj fijo.</summary>
    private static ServiceProvider CrearServicios(RelojFijo reloj, int retrasoMs, int intervaloMs = 500)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Proyeccion:RetrasoMs"] = retrasoMs.ToString(),
                ["Proyeccion:IntervaloMs"] = intervaloMs.ToString()
            })
            .Build();

        var raiz = new InMemoryDatabaseRoot();
        var nombre = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(reloj); // antes de AddViajesCqrsEs: TryAdd respeta este
        services.AddDbContext<M8DbContext>(o => o.UseInMemoryDatabase(nombre, raiz));
        services.AddViajesCqrsEs(config);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void AddViajesCqrsEs_registra_todas_las_piezas_y_lee_la_configuracion()
    {
        using var sp = CrearServicios(new RelojFijo(EscenarioViajes.Inicio), retrasoMs: 1234);
        using var scope = sp.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<RegistrarEventoViajeHandler>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ConsultasEventStore>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ConsultasHistorial>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ProyeccionHistorialViajes>());
        Assert.Contains(sp.GetServices<IHostedService>(), s => s is ProyectorHistorialViajes);
        Assert.Equal(1234, sp.GetRequiredService<IOptions<OpcionesProyeccion>>().Value.RetrasoMs);
        Assert.IsType<RelojFijo>(sp.GetRequiredService<TimeProvider>());
    }

    [Fact]
    public async Task Una_pasada_del_proyector_respeta_el_retraso_usando_el_reloj_inyectado()
    {
        var reloj = new RelojFijo(EscenarioViajes.Inicio);
        using var sp = CrearServicios(reloj, retrasoMs: 3000);
        var proyector = sp.GetServices<IHostedService>().OfType<ProyectorHistorialViajes>().Single();
        using (var scope = sp.CreateScope())
        {
            var handler = scope.ServiceProvider.GetRequiredService<RegistrarEventoViajeHandler>();
            await handler.ManejarAsync(Solicitudes.Solicitado(Guid.NewGuid()));
        }

        var antes = await proyector.EjecutarPasadaAsync(CancellationToken.None);   // t = 0 s
        reloj.Avanzar(TimeSpan.FromSeconds(3));
        var despues = await proyector.EjecutarPasadaAsync(CancellationToken.None); // t = 3 s

        Assert.Equal((0, 1), (antes, despues));
    }

    [Fact]
    public async Task Si_la_pasada_falla_el_proyector_registra_el_error_y_sigue_vivo()
    {
        // Contenedor SIN DbContext: la pasada va a fallar al pedir la proyección.
        var config = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddViajesCqrsEs(config);
        using var sp = services.BuildServiceProvider();
        var proyector = sp.GetServices<IHostedService>().OfType<ProyectorHistorialViajes>().Single();

        var procesados = await proyector.EjecutarPasadaAsync(CancellationToken.None);

        Assert.Equal(0, procesados);
    }

    [Fact]
    public async Task El_BackgroundService_proyecta_solo_mientras_la_app_esta_corriendo()
    {
        // Reloj real y retraso 0: probamos el ciclo completo Start → proyecta → Stop.
        using var sp = CrearServicios(new RelojFijo(EscenarioViajes.Inicio.AddHours(1)), retrasoMs: 0, intervaloMs: 20);
        var viajeId = Guid.NewGuid();
        using (var scope = sp.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<RegistrarEventoViajeHandler>()
                .ManejarAsync(Solicitudes.Solicitado(viajeId));
        }
        var proyector = sp.GetServices<IHostedService>().OfType<ProyectorHistorialViajes>().Single();

        await proyector.StartAsync(CancellationToken.None);
        var proyectado = false;
        for (var intento = 0; intento < 100 && !proyectado; intento++)
        {
            await Task.Delay(20);
            using var scope = sp.CreateScope();
            proyectado = await scope.ServiceProvider.GetRequiredService<M8DbContext>().ViajesHistorialVista.AnyAsync();
        }
        await proyector.StopAsync(CancellationToken.None);

        Assert.True(proyectado, "El proyector en segundo plano no proyectó el evento.");
    }
}
