using Microsoft.Extensions.DependencyInjection.Extensions;
using M8.Notificaciones.Api.Features.Viajes.Comandos;
using M8.Notificaciones.Api.Features.Viajes.Consultas;
using M8.Notificaciones.Api.Features.Viajes.EventStore;
using M8.Notificaciones.Api.Features.Viajes.Proyecciones;

namespace M8.Notificaciones.Api.Features.Viajes;

/// <summary>
/// Registro de dependencias de la feature Viajes (CQRS + Event Sourcing).
/// Program.cs solo llama a AddViajesCqrsEs: así el cambio en el archivo
/// compartido es una línea y todo lo de la feature queda en su carpeta.
/// </summary>
public static class ViajesServiceCollectionExtensions
{
    public static IServiceCollection AddViajesCqrsEs(this IServiceCollection services, IConfiguration configuration)
    {
        // Reloj inyectable: en producción es el reloj del sistema; en los tests,
        // uno fijo para controlar "ahora" sin Thread.Sleep.
        services.TryAddSingleton(TimeProvider.System);

        services.Configure<OpcionesProyeccion>(configuration.GetSection(OpcionesProyeccion.Seccion));

        // Scoped: comparten el DbContext de la request.
        services.AddScoped<EventStoreViajes>();
        services.AddScoped<RegistrarEventoViajeHandler>();   // escritura
        services.AddScoped<ConsultasEventStore>();           // lectura ES (rehidratación)
        services.AddScoped<ConsultasHistorial>();            // lectura CQRS (vista)
        services.AddScoped<ProyeccionHistorialViajes>();     // puente escritura → lectura

        // El proyector corre en segundo plano durante toda la vida de la API.
        services.AddHostedService<ProyectorHistorialViajes>();

        return services;
    }
}
