using System.Diagnostics;
using M8.Notificaciones.Api.Features.Eventos;
using M8.Notificaciones.Api.Features.Notificaciones.Dominio;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace M8.Notificaciones.Api.Infrastructure.Observability;

public static class ObservabilidadExtensions
{
    public static void AddObservabilidadM8(this WebApplicationBuilder builder)
    {
        if (!builder.Configuration.GetValue<bool>("Observabilidad:Habilitada")) return;

        builder.Services.AddSingleton<Telemetria>();
        builder.Services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(Telemetria.Nombre, autoGenerateServiceInstanceId: false))
            .WithTracing(t => t.AddSource(Telemetria.Nombre)
                .AddAspNetCoreInstrumentation(o => o.RecordException = false)
                .AddProcessor(new HttpSeguro()).AddOtlpExporter(ConfigurarOtlp))
            .WithMetrics(m => m.AddMeter(Telemetria.Nombre, "Microsoft.AspNetCore.Hosting")
                .AddView("http.server.request.duration", new ExplicitBucketHistogramConfiguration
                {
                    TagKeys = ["http.request.method", "http.response.status_code", "http.route"],
                    Boundaries = [0.005, 0.01, 0.025, 0.05, 0.075, 0.1, 0.25, 0.5, 0.75, 1, 2.5, 5, 7.5, 10]
                })
                .AddView("http.server.active_requests", MetricStreamConfiguration.Drop)
                .AddOtlpExporter(ConfigurarOtlp));

        builder.Logging.AddFilter<OpenTelemetryLoggerProvider>((categoria, _) => categoria == typeof(Telemetria).FullName);
        builder.Logging.AddOpenTelemetry(o =>
        {
            o.IncludeScopes = false;
            o.IncludeFormattedMessage = true;
            o.SetResourceBuilder(ResourceBuilder.CreateEmpty().AddService(Telemetria.Nombre, autoGenerateServiceInstanceId: false));
            var opciones = new OtlpExporterOptions();
            ConfigurarOtlp(opciones);
            if (builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] is { Length: > 0 } endpoint)
                opciones.Endpoint = new Uri(endpoint);
            o.AddProcessor(new LogSeguro(new OtlpLogExporter(opciones)));
        });
    }

    private static void ConfigurarOtlp(OtlpExporterOptions opciones)
    {
        // El SDK lee OTEL_EXPORTER_OTLP_ENDPOINT (por defecto http://localhost:4317).
        opciones.Protocol = OtlpExportProtocol.Grpc;
        opciones.ExportProcessorType = ExportProcessorType.Batch;
        opciones.TimeoutMilliseconds = 3000;
    }

    // El filtro de ILogger reduce trabajo, pero una regla por categoría puede
    // sobreescribirlo. Verificar de nuevo antes de encolar para OTLP.
    private sealed class LogSeguro(OtlpLogExporter exporter) : BatchLogRecordExportProcessor(exporter)
    {
        public override void OnEnd(LogRecord log)
        {
            if (log.CategoryName != typeof(Telemetria).FullName || log.Exception is not null) return;

            var campos = log.Attributes?.Where(a => a.Key != "{OriginalFormat}").ToArray() ?? [];
            var mensaje = log.Body switch
            {
                "Notificaciones: no se pudo persistir el intento" or "RabbitMQ: JSON inválido"
                    or "RabbitMQ: error transitorio; se reencola" when campos.Length == 0 => log.Body,
                "Notificaciones: intento fallido; estado {Estado}"
                    when campos is [{ Key: "Estado", Value: EstadoNotificacion.PENDIENTE or EstadoNotificacion.FALLIDA }]
                    => $"Notificaciones: intento fallido; estado {campos[0].Value}",
                "RabbitMQ: evento rechazado ({Estado})" when campos is [{ Key: "Estado", Value: EstadoIngesta estado }]
                    && Enum.IsDefined(estado) => $"RabbitMQ: evento rechazado ({estado})",
                "QR: generación fallida en {Etapa}" when campos is [{ Key: "Etapa", Value: "redis" or "imagen" }]
                    => $"QR: generación fallida en {campos[0].Value}",
                _ => null
            };
            if (mensaje is not null && log.FormattedMessage == mensaje
                && (log.Attributes?.All(a => a.Key != "{OriginalFormat}" || Equals(a.Value, log.Body)) ?? true))
                base.OnEnd(log);
        }
    }

    private sealed class HttpSeguro : BaseProcessor<Activity>
    {
        public override void OnEnd(Activity span)
        {
            // null volvería a heredar el tracestate de un padre aún activo.
            // Se conservan los IDs W3C para correlación.
            span.TraceStateString = string.Empty;
            if (span.Kind != ActivityKind.Server) return;

            var ruta = span.GetTagItem("http.route") as string;
            span.DisplayName = ruta is { Length: > 0 } ? $"HTTP {ruta}" : "HTTP sin ruta";
            span.SetStatus(span.Status);
            foreach (var (clave, _) in span.TagObjects.ToArray())
            {
                if (clave is not ("http.request.method" or "http.response.status_code" or "http.route"))
                    span.SetTag(clave, null);
            }
        }
    }
}
