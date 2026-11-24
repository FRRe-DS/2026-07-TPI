using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using M8.Notificaciones.Api.Features.Notificaciones.Envio;
using M8.Notificaciones.Api.Infrastructure.Idempotencia;
using M8.Notificaciones.Api.Infrastructure.Persistence;

namespace M8.Notificaciones.Tests.Notificaciones.Apoyo;

/// <summary>
/// Levanta la API completa en memoria (pipeline HTTP real: middlewares,
/// autenticación JWT, autorización por rol, controladores, EF Core) con:
///  - base EF InMemory propia de cada instancia;
///  - tokens firmados por el test (en vez de Keycloak);
///  - Idempotency-Key en memoria (en vez de Redis);
///  - RabbitMQ y el despachador apagados (el test llama al envío a mano).
/// </summary>
public class ApiDePrueba : WebApplicationFactory<Program>
{
    public const string Emisor = "https://emisor-de-prueba";

    private static readonly SymmetricSecurityKey Clave = new(Encoding.UTF8.GetBytes("clave-de-prueba-de-m8-que-tiene-mas-de-32-bytes!"));

    private readonly string _nombreBase = "m8-api-" + Guid.NewGuid();
    private readonly InMemoryDatabaseRoot _raiz = new();
    private readonly Dictionary<string, string?> _configuracion = new();

    public ProveedorDePrueba Proveedor { get; } = new();

    /// <summary>Permite a un test cambiar la configuración antes de crear el cliente.</summary>
    public ApiDePrueba Con(string clave, string? valor)
    {
        _configuracion[clave] = valor;
        return this;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:PostgresConnection", "Host=no-se-usa");
        builder.UseSetting("RabbitMQ:Habilitado", "false");
        builder.UseSetting("Notificaciones:Envio:Habilitado", "false");
        builder.UseSetting("Notificaciones:Envio:TamanoLote", "1000");
        builder.UseSetting("Auth:Emisor", Emisor);
        builder.UseSetting("Logging:LogLevel:Default", "Warning");
        foreach (var (clave, valor) in _configuracion)
        {
            builder.UseSetting(clave, valor);
        }

        builder.ConfigureTestServices(services =>
        {
            // PostgreSQL → EF InMemory (una base por instancia de la API).
            services.RemoveAll<DbContextOptions<M8DbContext>>();
            services.RemoveAll<M8DbContext>();
            services.AddDbContext<M8DbContext>(o => o.UseInMemoryDatabase(_nombreBase, _raiz));

            services.RemoveAll<IAlmacenIdempotencia>();
            services.AddSingleton<IAlmacenIdempotencia, AlmacenIdempotenciaEnMemoria>();

            // Proveedores de canal controlados por el test.
            services.RemoveAll<IProveedorCanal>();
            foreach (var canal in Enum.GetValues<M8.Notificaciones.Api.Features.Notificaciones.Dominio.Canal>())
            {
                services.AddSingleton<IProveedorCanal>(new ProveedorDePrueba.PorCanal(canal, Proveedor));
            }

            // JWT: en lugar de bajar las claves de Keycloak, se valida con la clave del test.
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o =>
            {
                o.Configuration = new OpenIdConnectConfiguration { Issuer = Emisor };
                o.Configuration.SigningKeys.Add(Clave);
                o.TokenValidationParameters.IssuerSigningKey = Clave;
                o.TokenValidationParameters.ValidIssuer = Emisor;
            });
        });
    }

    /// <summary>Token como los de Keycloak: roles en realm_access.roles.</summary>
    public static string Token(IEnumerable<string> rolesRealm, string? sub = null, string? azp = null, string? emisor = null,
        DateTime? vence = null)
    {
        var claims = new Dictionary<string, object>
        {
            ["sub"] = sub ?? Guid.NewGuid().ToString(),
            ["azp"] = azp ?? "grupo-de-prueba",
            ["realm_access"] = new Dictionary<string, object> { ["roles"] = rolesRealm.ToArray() }
        };
        var ahora = DateTime.UtcNow;
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = emisor ?? Emisor,
            Claims = claims,
            IssuedAt = ahora.AddMinutes(-1),
            NotBefore = ahora.AddMinutes(-1),
            Expires = vence ?? ahora.AddMinutes(5),
            SigningCredentials = new SigningCredentials(Clave, SecurityAlgorithms.HmacSha256)
        });
    }

    public HttpClient ClienteCon(params string[] rolesRealm)
    {
        var cliente = CreateClient();
        if (rolesRealm.Length > 0)
        {
            cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(rolesRealm));
        }
        return cliente;
    }

    /// <summary>M6 (Grupo 5): rol de Keycloak "viajes-be" → SERVICIO.</summary>
    public HttpClient ClienteServicio() => ClienteCon("viajes-be");

    public HttpClient ClienteOperador() => ClienteCon("OPERADOR");

    public async Task<T> EnScopeAsync<T>(Func<IServiceProvider, Task<T>> accion)
    {
        await using var scope = Services.CreateAsyncScope();
        return await accion(scope.ServiceProvider);
    }

    /// <summary>Ejecuta una pasada del despachador (lo que en producción hace el BackgroundService).</summary>
    public Task<int> DespacharAsync(DateTimeOffset? ahora = null) =>
        EnScopeAsync(sp => sp.GetRequiredService<ServicioEnvio>().ProcesarPendientesAsync(ahora ?? DateTimeOffset.UtcNow, CancellationToken.None));
}

/// <summary>Idempotency-Key en memoria, con la misma semántica que la de Redis.</summary>
public sealed class AlmacenIdempotenciaEnMemoria : IAlmacenIdempotencia
{
    private readonly ConcurrentDictionary<string, (string Hash, bool Completa, int? Status, string? Cuerpo)> _claves = new();

    public Task<ReservaIdempotencia> ReservarAsync(string ambito, string clave, string hashCuerpo, CancellationToken ct)
    {
        var k = $"{ambito}:{clave}";
        if (_claves.TryAdd(k, (hashCuerpo, false, null, null)))
            return Task.FromResult(new ReservaIdempotencia(ResultadoReserva.Nueva));
        var r = _claves[k];
        if (r.Hash != hashCuerpo) return Task.FromResult(new ReservaIdempotencia(ResultadoReserva.CuerpoDistinto));
        return Task.FromResult(r.Completa
            ? new ReservaIdempotencia(ResultadoReserva.Repetida, r.Status, r.Cuerpo)
            : new ReservaIdempotencia(ResultadoReserva.EnCurso));
    }

    public Task GuardarRespuestaAsync(string ambito, string clave, string hashCuerpo, int status, string cuerpoJson, CancellationToken ct)
    {
        _claves[$"{ambito}:{clave}"] = (hashCuerpo, true, status, cuerpoJson);
        return Task.CompletedTask;
    }

    public Task LiberarAsync(string ambito, string clave, CancellationToken ct)
    {
        _claves.TryRemove($"{ambito}:{clave}", out _);
        return Task.CompletedTask;
    }
}

/// <summary>Proveedor de canal programable: registra lo enviado y falla cuando el test lo pide.</summary>
public sealed class ProveedorDePrueba
{
    public ConcurrentQueue<MensajeSaliente> Enviados { get; } = new();

    /// <summary>Si devuelve un texto, el envío falla con ese error.</summary>
    public Func<MensajeSaliente, string?> Falla { get; set; } = _ => null;

    /// <summary>Demora artificial (para probar el timeout).</summary>
    public TimeSpan Demora { get; set; } = TimeSpan.Zero;

    public sealed class PorCanal : IProveedorCanal
    {
        private readonly ProveedorDePrueba _base;

        public PorCanal(M8.Notificaciones.Api.Features.Notificaciones.Dominio.Canal canal, ProveedorDePrueba @base)
        {
            Canal = canal;
            _base = @base;
        }

        public M8.Notificaciones.Api.Features.Notificaciones.Dominio.Canal Canal { get; }

        public async Task<ResultadoEnvio> EnviarAsync(MensajeSaliente mensaje, CancellationToken ct)
        {
            if (_base.Demora > TimeSpan.Zero) await Task.Delay(_base.Demora, ct);
            var error = _base.Falla(mensaje);
            if (error is not null) return ResultadoEnvio.Fallo(error);
            _base.Enviados.Enqueue(mensaje);
            return ResultadoEnvio.Ok;
        }
    }
}

/// <summary>Ayudas para leer respuestas JSON.</summary>
public static class Json
{
    public static async Task<JsonElement> LeerAsync(this HttpResponseMessage r) =>
        JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();

    public static HttpRequestMessage Post(string url, object? cuerpo, string? idempotencyKey = null)
    {
        var m = new HttpRequestMessage(HttpMethod.Post, url);
        if (cuerpo is not null) m.Content = JsonContent.Create(cuerpo);
        if (idempotencyKey is not null) m.Headers.Add("Idempotency-Key", idempotencyKey);
        return m;
    }
}
