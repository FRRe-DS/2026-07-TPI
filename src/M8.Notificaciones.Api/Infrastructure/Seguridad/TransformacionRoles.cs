using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace M8.Notificaciones.Api.Infrastructure.Seguridad;

/// <summary>
/// Convierte los roles del token de Keycloak en roles del contrato.
///
/// Keycloak pone los roles del realm en el claim "realm_access" como JSON:
///   "realm_access": { "roles": ["viajes-be", "default-roles-ds-2026-realm"] }
/// ASP.NET no lo entiende solo. Acá se lee ese JSON (y también un claim "roles"
/// plano, por si M1 los emite así) y se agrega un ClaimTypes.Role por cada rol
/// del contrato que corresponda. Así [Authorize(Roles = "SERVICIO")] funciona.
/// </summary>
public sealed class TransformacionRoles : IClaimsTransformation
{
    private const string MarcaProcesado = "m8:roles-procesados";

    private readonly OpcionesAuth _opciones;

    public TransformacionRoles(IOptions<OpcionesAuth> opciones) => _opciones = opciones.Value;

    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        // La transformación puede correr más de una vez por solicitud.
        if (principal.Identity is not ClaimsIdentity { IsAuthenticated: true } identidad
            || principal.HasClaim(c => c.Type == MarcaProcesado))
        {
            return Task.FromResult(principal);
        }

        var rolesContrato = RolesDelContrato(LeerRolesDelToken(principal), _opciones.MapeoRoles);

        var agregada = new ClaimsIdentity();
        agregada.AddClaim(new Claim(MarcaProcesado, "1"));
        foreach (var rol in rolesContrato)
        {
            if (!identidad.HasClaim(ClaimTypes.Role, rol))
            {
                agregada.AddClaim(new Claim(ClaimTypes.Role, rol));
            }
        }
        principal.AddIdentity(agregada);
        return Task.FromResult(principal);
    }

    /// <summary>Roles crudos que trae el token: realm_access.roles y el claim "roles".</summary>
    public static IEnumerable<string> LeerRolesDelToken(ClaimsPrincipal principal)
    {
        foreach (var claim in principal.FindAll("realm_access"))
        {
            List<string> roles = new();
            try
            {
                using var json = JsonDocument.Parse(claim.Value);
                if (json.RootElement.TryGetProperty("roles", out var arreglo) && arreglo.ValueKind == JsonValueKind.Array)
                {
                    roles.AddRange(arreglo.EnumerateArray()
                        .Where(r => r.ValueKind == JsonValueKind.String)
                        .Select(r => r.GetString()!));
                }
            }
            catch (JsonException)
            {
                // Claim mal formado: lo ignoramos (el token sigue sin esos roles).
            }
            foreach (var rol in roles) yield return rol;
        }

        foreach (var claim in principal.FindAll("roles"))
        {
            yield return claim.Value;
        }
    }

    public static IReadOnlySet<string> RolesDelContrato(IEnumerable<string> rolesToken, IReadOnlyDictionary<string, string[]> mapeo)
    {
        var resultado = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rol in rolesToken)
        {
            var mayus = rol.ToUpperInvariant();
            if (Roles.Todos.Contains(mayus))
            {
                resultado.Add(mayus);
            }
            if (mapeo.TryGetValue(rol, out var traducidos))
            {
                foreach (var t in traducidos.Select(t => t.ToUpperInvariant()).Where(Roles.Todos.Contains))
                {
                    resultado.Add(t);
                }
            }
        }
        return resultado;
    }
}
