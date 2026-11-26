using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using M8.Notificaciones.Api.Features.Eventos;
using M8.Notificaciones.Api.Features.Notificaciones.Dominio;
using M8.Notificaciones.Api.Features.Notificaciones.Envio;
using M8.Notificaciones.Api.Infrastructure.Idempotencia;
using M8.Notificaciones.Api.Infrastructure.Mensajeria;

namespace M8.Notificaciones.Api.Features.Notificaciones;

/// <summary>Registro de dependencias de Notificaciones + Eventos (TP2).</summary>
public static class NotificacionesServiceCollectionExtensions
{
    public static IServiceCollection AddNotificacionesM8(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.Configure<OpcionesNotificaciones>(configuration.GetSection(OpcionesNotificaciones.Seccion));
        services.Configure<OpcionesRabbitMq>(configuration.GetSection(OpcionesRabbitMq.Seccion));

        // Ingesta de eventos (HTTP y RabbitMQ) y sus reacciones.
        services.AddScoped<IngestaEventos>();
        services.AddScoped<IReaccionEvento, ReaccionHistorialViajes>();

        // Idempotency-Key en Redis.
        services.TryAddSingleton<IAlmacenIdempotencia, AlmacenIdempotenciaRedis>();
        services.AddScoped<EjecucionIdempotente>();

        // Proveedores por canal: SMS y PUSH simulados; EMAIL por SMTP si está habilitado.
        services.AddSingleton<IProveedorCanal>(sp => Simulado(sp, Canal.SMS));
        services.AddSingleton<IProveedorCanal>(sp => Simulado(sp, Canal.PUSH));
        services.AddSingleton<IProveedorCanal>(sp =>
            sp.GetRequiredService<IOptions<OpcionesNotificaciones>>().Value.Smtp.Habilitado
                ? ActivatorUtilities.CreateInstance<ProveedorEmailSmtp>(sp)
                : Simulado(sp, Canal.EMAIL));

        // Envío asíncrono y consumo de la cola, en segundo plano.
        services.AddScoped<ServicioEnvio>();
        services.AddHostedService<DespachadorNotificaciones>();
        services.AddSingleton<ConsumidorEventosRabbitMq>();
        services.AddHostedService(sp => sp.GetRequiredService<ConsumidorEventosRabbitMq>());

        // TP3: punto de entrada para que Documentos pida el reenvío de un comprobante (RF-8.5).
        services.AddScoped<Servicios.ReenviosNotificaciones>();

        return services;
    }

    private static ProveedorSimulado Simulado(IServiceProvider sp, Canal canal) =>
        new(canal, sp.GetRequiredService<IOptions<OpcionesNotificaciones>>().Value.Simulacion,
            sp.GetRequiredService<ILogger<ProveedorSimulado>>());
}
