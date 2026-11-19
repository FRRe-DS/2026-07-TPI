using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using M8.Notificaciones.Api.Infrastructure.Persistence;
using StackExchange.Redis;
using M8.Notificaciones.Api.Features.Documentos.Services;
using M8.Notificaciones.Api.Features.Notificaciones;
using M8.Notificaciones.Api.Features.Viajes;
using M8.Notificaciones.Api.Infrastructure.Http;
using M8.Notificaciones.Api.Infrastructure.Salud;
using M8.Notificaciones.Api.Infrastructure.Seguridad;

var builder = WebApplication.CreateBuilder(args);

// Cuerpos de hasta 1 MiB (contrato de M8). LimiteCuerpoMiddleware da el 413 en problem+json.
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = LimiteCuerpoMiddleware.MaximoBytes);

builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(o =>
    {
        // 400 en problem+json con type about:blank y título en español (contrato).
        o.InvalidModelStateResponseFactory = ProblemasHttp.RespuestaModeloInvalido;
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Módulo 8 - Notificaciones, Documentos y Soporte",
        Version = "v1",
        Description = "API del Módulo M8 para la plataforma de movilidad urbana (TPI Desarrollo de Software 2026 - Grupo 07). Contrato: docs/m8-openapi.yaml."
    });
    // Botón "Authorize" de Swagger: pegar el access_token de Keycloak.
    options.AddSecurityDefinition("bearerAuth", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "Token emitido por M1 (Keycloak de la cátedra)."
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "bearerAuth" } }] = Array.Empty<string>()
    });
});

// Entity Framework Core con PostgreSQL. La cadena viene de afuera (RNF-06).
var connectionString = builder.Configuration.GetConnectionString("PostgresConnection")
    ?? throw new InvalidOperationException("No se encontró la cadena de conexión 'PostgresConnection' en la configuración.");

builder.Services.AddDbContext<M8DbContext>(options =>
    options.UseNpgsql(connectionString, npgsql => npgsql.CommandTimeout(15)));

// Redis (QR temporales e Idempotency-Key). Si no está, la API arranca igual y
// las operaciones que lo necesitan responden 503 (RNF-13).
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
{
    var cs = builder.Configuration["Redis:ConnectionString"] ?? "localhost:6379";
    var opciones = ConfigurationOptions.Parse(cs);
    opciones.AbortOnConnectFail = false;
    opciones.ConnectTimeout = 5000;
    opciones.SyncTimeout = 5000;
    opciones.AsyncTimeout = 5000;
    return ConnectionMultiplexer.Connect(opciones);
});
// Servicio del QR (Célula 2).
builder.Services.AddSingleton<IQrService, QrService>();

// Viajes: CQRS + Event Sourcing (event store, handler, consultas y proyector en segundo plano).
builder.Services.AddViajesCqrsEs(builder.Configuration);

// Notificaciones (TP2): ingesta de eventos por HTTP y RabbitMQ, envío asíncrono con reintentos.
builder.Services.AddNotificacionesM8(builder.Configuration);

// Seguridad (TP2): JWT de Keycloak y roles del contrato.
builder.Services.AddSeguridadM8(builder.Configuration);

var app = builder.Build();

// Aplicar migraciones al iniciar (cada módulo es dueño de su base, RNF-05).
using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        var db = scope.ServiceProvider.GetRequiredService<M8DbContext>();
        if (db.Database.IsRelational())
        {
            logger.LogInformation("Verificando y aplicando migraciones pendientes de PostgreSQL...");
            db.Database.Migrate();
            logger.LogInformation("Base de datos de M8 sincronizada correctamente.");
        }
        else
        {
            db.Database.EnsureCreated(); // tests con base en memoria
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error al conectar o aplicar migraciones a PostgreSQL. Asegúrate de que el contenedor de Docker 'm8-postgres' esté corriendo.");
    }
}

// ---------------- Pipeline HTTP (el orden importa) ----------------
// 1. Correlación: primero, para que toda respuesta (incluso los errores) lleve X-Request-Id.
app.UseMiddleware<RequestIdMiddleware>();
// 2. Excepciones → 500 / 503 en problem+json, sin detalles internos.
app.UseExceptionHandler(errores => errores.Run(ProblemasHttp.ManejarExcepcion));
// 3. Errores sin cuerpo (401, 403, 404, 405, 415) → problem+json en español.
app.UseStatusCodePages(ProblemasHttp.CompletarRespuestaSinCuerpo);
// 4. Cuerpos de más de 1 MiB → 413.
app.UseMiddleware<LimiteCuerpoMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "M8 API v1");
        c.RoutePrefix = string.Empty; // Carga Swagger en http://localhost:PORT/ directamente
    });
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapSalud();

app.Run();

// Permite que los tests de integración levanten la API con WebApplicationFactory<Program>.
public partial class Program { }
