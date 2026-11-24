using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;

namespace M8.Notificaciones.Api.Infrastructure.Seguridad;

/// <summary>
/// Autenticación con JWT emitido por M1 (en el TPI, el Keycloak de la cátedra)
/// y autorización por rol (RNF-12).
///
/// M8 no guarda contraseñas ni secretos: valida la firma del token con las
/// claves públicas del realm (JWKS), el emisor y la vigencia.
/// </summary>
public static class SeguridadExtensions
{
    public static IServiceCollection AddSeguridadM8(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<OpcionesAuth>(o =>
        {
            configuration.GetSection(OpcionesAuth.Seccion).Bind(o);
            // Bind no respeta el comparador del diccionario: lo rearmamos sin distinguir mayúsculas.
            o.MapeoRoles = new Dictionary<string, string[]>(o.MapeoRoles, StringComparer.OrdinalIgnoreCase);
        });

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();

        // Se configura con IOptions para que los tests puedan reemplazar las claves.
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<OpcionesAuth>>((jwt, auth) =>
            {
                var a = auth.Value;
                jwt.Authority = a.Emisor;
                if (!string.IsNullOrWhiteSpace(a.MetadataAddress))
                {
                    jwt.MetadataAddress = a.MetadataAddress;
                }
                jwt.RequireHttpsMetadata = a.RequiereHttpsMetadata;
                // Mantener los nombres de los claims tal como vienen ("sub", "azp"...).
                jwt.MapInboundClaims = false;
                // Si Keycloak no responde, no esperar indefinidamente (RNF-13).
                jwt.BackchannelTimeout = TimeSpan.FromSeconds(10);

                jwt.TokenValidationParameters.ValidIssuer = a.Emisor;
                jwt.TokenValidationParameters.ValidateIssuer = true;
                jwt.TokenValidationParameters.ValidateLifetime = true;
                jwt.TokenValidationParameters.ClockSkew = TimeSpan.FromSeconds(30);
                jwt.TokenValidationParameters.ValidateAudience = !string.IsNullOrWhiteSpace(a.Audiencia);
                jwt.TokenValidationParameters.ValidAudience = a.Audiencia;
                jwt.TokenValidationParameters.NameClaimType = "sub";
                jwt.TokenValidationParameters.RoleClaimType = ClaimTypes.Role;
            });

        services.AddTransient<IClaimsTransformation, TransformacionRoles>();
        services.AddAuthorization();
        return services;
    }

    /// <summary>
    /// Quién llama, para auditoría y para separar claves de idempotencia por usuario.
    /// En los tokens de servicio de Keycloak el "sub" es un UUID técnico, así que
    /// preferimos "azp"/"client_id" (por ejemplo "grupo-05").
    /// </summary>
    public static string Actor(this ClaimsPrincipal usuario) =>
        usuario.FindFirstValue("azp")
        ?? usuario.FindFirstValue("client_id")
        ?? usuario.FindFirstValue("sub")
        ?? "anonimo";

    public static string? Sub(this ClaimsPrincipal usuario) => usuario.FindFirstValue("sub");
}
