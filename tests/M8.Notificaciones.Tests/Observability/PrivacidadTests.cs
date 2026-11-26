using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using M8.Notificaciones.Api.Features.Documentos.Services;
using M8.Notificaciones.Api.Features.Eventos;
using M8.Notificaciones.Api.Features.Notificaciones.Dominio;
using M8.Notificaciones.Api.Infrastructure.Observability;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Trace;
using StackExchange.Redis;

namespace M8.Notificaciones.Tests.Observability;

public class PrivacidadTests
{
    [Fact]
    public async Task La_exportacion_omite_identificadores_excepciones_y_logs_generales()
    {
        const string privado = "NO_EXPORTAR";
        var spans = new Captura<Activity, SpanCapturado>(s => new(s.DisplayName, s.TraceId, s.SpanId, s.ParentSpanId,
            s.TraceStateString, s.Parent?.TraceStateString, s.Events.Any(), s.TagObjects.ToDictionary(t => t.Key, t => t.Value)));
        var logs = new Captura<LogRecord, LogRecord>(l => l);
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration["Observabilidad:Habilitada"] = "true";
        builder.AddObservabilidadM8();
        builder.Services.AddOpenTelemetry().WithTracing(t => t.AddProcessor(spans));
        builder.Logging.AddOpenTelemetry(o => o.AddProcessor(logs));
        await using var app = builder.Build();
        app.MapGet("/seguro/{id}", async (ILogger<PrivacidadTests> logger, Telemetria telemetria) =>
        {
            using var scope = logger.BeginScope(new Dictionary<string, object> { ["RequestId"] = privado });
            logger.LogError(new InvalidOperationException(privado), "Mensaje de otra categoría: {Privado}", privado);
            var redis = MedicionesTests.RedisProxy.Crear<IConnectionMultiplexer>(_ => throw new InvalidOperationException(privado));
            try { await new QrService(redis, telemetria).GenerarAsync(Guid.NewGuid(), Guid.NewGuid(), 60); }
            catch (InvalidOperationException) { return Results.StatusCode(503); }
            return Results.Ok();
        });
        await app.StartAsync();
        using var cliente = app.GetTestClient();
        using var solicitud = new HttpRequestMessage(HttpMethod.Get, $"/seguro/{privado}?token={privado}");
        solicitud.Headers.Add("traceparent", "00-0123456789abcdef0123456789abcdef-0123456789abcdef-01");
        solicitud.Headers.Add("tracestate", $"externo={privado}");
        solicitud.Headers.Add("baggage", $"externo={privado}");
        solicitud.Headers.Add("Authorization", $"Bearer {privado}");
        using var respuesta = await cliente.SendAsync(solicitud);
        Assert.Equal(503, (int)respuesta.StatusCode);
        using var sinRuta = await cliente.GetAsync($"/no-existe/{privado}?token={privado}");
        Assert.Equal(404, (int)sinRuta.StatusCode);
        await app.StopAsync();

        var log = Assert.Single(logs.Items);
        Assert.Equal(typeof(Telemetria).FullName, log.CategoryName);
        Assert.Null(log.Exception);
        Assert.DoesNotContain(privado, string.Join(' ', log.Attributes!));
        var qr = Assert.Single(spans.Items, s => s.DisplayName == "m8.qr.generar");
        var http = Assert.Single(spans.Items, s => s.Tags.GetValueOrDefault("http.route") as string == "/seguro/{id}");
        Assert.Equal($"externo={privado}", qr.EstadoPadre);
        Assert.Equal("0123456789abcdef0123456789abcdef", http.TraceId.ToString());
        Assert.Equal(http.SpanId, qr.ParentSpanId);
        Assert.Equal(http.TraceId, qr.TraceId);
        Assert.Equal(qr.TraceId, log.TraceId);
        Assert.Equal(qr.SpanId, log.SpanId);
        Assert.Contains(spans.Items, s => s.DisplayName == "HTTP sin ruta");
        Assert.All(spans.Items, span =>
        {
            Assert.True(string.IsNullOrEmpty(span.Estado));
            Assert.False(span.TieneEventos);
            Assert.DoesNotContain(privado, span.DisplayName);
            Assert.DoesNotContain(privado, string.Join(' ', span.Tags));
        });
    }

    [Fact]
    public async Task El_exportador_ignora_logs_comunes_aunque_la_configuracion_los_habilite()
    {
        const string privado = "NO_EXPORTAR_LOG_GENERAL";
        var cuerpos = new ConcurrentQueue<string>();
        var receptor = WebApplication.CreateBuilder();
        receptor.WebHost.ConfigureKestrel(o => o.Listen(IPAddress.Loopback, 0, l => l.Protocols = HttpProtocols.Http2));
        await using var colector = receptor.Build();
        colector.MapPost("/opentelemetry.proto.collector.logs.v1.LogsService/Export", async (HttpContext http) =>
        {
            using var datos = new MemoryStream();
            await http.Request.Body.CopyToAsync(datos);
            cuerpos.Enqueue(Encoding.UTF8.GetString(datos.ToArray()));
            http.Response.ContentType = "application/grpc";
            await http.Response.Body.WriteAsync(new byte[5]); // respuesta gRPC con protobuf vacío
            http.Response.AppendTrailer("grpc-status", "0");
        });
        await colector.StartAsync();

        var builder = WebApplication.CreateBuilder();
        builder.Configuration["Observabilidad:Habilitada"] = "true";
        builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] = colector.Urls.Single();
        builder.Configuration[$"Logging:OpenTelemetry:LogLevel:{typeof(PrivacidadTests).FullName}"] = "Trace";
        builder.AddObservabilidadM8();
        await using (var app = builder.Build())
        {
            app.Services.GetRequiredService<ILogger<PrivacidadTests>>().LogError("No exportar: {Dato}", privado);
            var log = app.Services.GetRequiredService<ILogger<Telemetria>>();
            log.LogWarning("QR: generación fallida en {Etapa}", privado);
            log.LogWarning("Evento nuevo en la categoría dedicada: {Dato}", privado);
            log.LogWarning("Notificaciones: intento fallido; estado {Estado}", EstadoNotificacion.FALLIDA);
            log.LogWarning("RabbitMQ: evento rechazado ({Estado})", EstadoIngesta.Invalido);
            var redis = MedicionesTests.RedisProxy.Crear<IConnectionMultiplexer>(_ => throw new InvalidOperationException(privado));
            var qr = new QrService(redis, app.Services.GetRequiredService<Telemetria>());
            await Assert.ThrowsAsync<InvalidOperationException>(() => qr.GenerarAsync(Guid.NewGuid(), Guid.NewGuid(), 60));
        }

        var exportado = string.Concat(cuerpos);
        Assert.Contains("QR: generación fallida en redis", exportado);
        Assert.Contains("Notificaciones: intento fallido; estado FALLIDA", exportado);
        Assert.Contains("RabbitMQ: evento rechazado (Invalido)", exportado);
        Assert.DoesNotContain(privado, exportado);
    }

    private sealed record SpanCapturado(string DisplayName, ActivityTraceId TraceId, ActivitySpanId SpanId,
        ActivitySpanId ParentSpanId, string? Estado, string? EstadoPadre, bool TieneEventos, Dictionary<string, object?> Tags);

    private sealed class Captura<T, TMuestra>(Func<T, TMuestra> tomarMuestra) : BaseProcessor<T> where T : class
    {
        public ConcurrentQueue<TMuestra> Items { get; } = new();
        public override void OnEnd(T dato) => Items.Enqueue(tomarMuestra(dato));
    }
}
