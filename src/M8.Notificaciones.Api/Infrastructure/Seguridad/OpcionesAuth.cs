namespace M8.Notificaciones.Api.Infrastructure.Seguridad;

/// <summary>
/// Configuración de la autenticación (sección "Auth"). Todo viene de afuera
/// (appsettings / variables de entorno, RNF-06); no hay secretos acá: M8 solo
/// valida tokens con la clave pública que publica Keycloak (JWKS).
/// </summary>
public sealed class OpcionesAuth
{
    public const string Seccion = "Auth";

    /// <summary>
    /// Emisor de los tokens (claim "iss"). Con el Keycloak de la cátedra:
    /// http://localhost:8080/realms/ds-2026-realm
    /// </summary>
    public string Emisor { get; set; } = "http://localhost:8080/realms/ds-2026-realm";

    /// <summary>
    /// Dónde descargar la configuración OIDC (y de ahí las claves públicas).
    /// Si está vacío se usa {Emisor}/.well-known/openid-configuration.
    /// Dentro de Docker hace falta porque "localhost" no es el host:
    /// http://host.docker.internal:8080/realms/ds-2026-realm/.well-known/openid-configuration
    /// </summary>
    public string? MetadataAddress { get; set; }

    /// <summary>Audiencia exigida (claim "aud"). Vacío = no se valida (los tokens client_credentials de Keycloak traen "account").</summary>
    public string? Audiencia { get; set; }

    /// <summary>false solo en desarrollo local, donde Keycloak corre por HTTP.</summary>
    public bool RequiereHttpsMetadata { get; set; }

    /// <summary>
    /// Traducción de roles de Keycloak a roles del contrato. El realm de la cátedra
    /// le da a cada service account un rol "*-be" (viajes-be, reservas-be...), no
    /// los roles del contrato. Ejemplo: "viajes-be": ["SERVICIO"].
    /// Los roles que ya se llaman como en el contrato (CLIENTE, CONDUCTOR,
    /// OPERADOR, SERVICIO) pasan sin traducir.
    /// </summary>
    public Dictionary<string, string[]> MapeoRoles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
