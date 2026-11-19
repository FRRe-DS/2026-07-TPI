using M8.Notificaciones.Api.Features.Notificaciones.Dominio;

namespace M8.Notificaciones.Api.Features.Notificaciones.Servicios;

/// <summary>
/// Elige el canal de una notificación y el contacto a usar.
///
/// Regla: se recorren los canales preferidos en orden (si no hay, primero el
/// canal por defecto y después el resto) y se toma el primero para el que hay
/// contacto. EMAIL necesita email, SMS necesita teléfono y PUSH se direcciona
/// por el usuarioId (siempre se puede). Si ninguno sirve, se devuelve el primer
/// candidato sin contacto: la notificación queda FALLIDA (lo dice el contrato).
/// </summary>
public static class SelectorCanal
{
    public sealed record Eleccion(Canal Canal, string? Contacto, bool Utilizable);

    public static Eleccion Elegir(IReadOnlyList<Canal>? preferidos, Canal porDefecto, string? email, string? telefono)
    {
        var candidatos = preferidos is { Count: > 0 }
            ? preferidos
            : new[] { porDefecto }.Concat(Enum.GetValues<Canal>().Where(c => c != porDefecto)).ToList();

        foreach (var canal in candidatos)
        {
            var contacto = ContactoPara(canal, email, telefono);
            if (canal == Canal.PUSH || contacto is not null)
            {
                return new Eleccion(canal, contacto, true);
            }
        }
        return new Eleccion(candidatos[0], null, false);
    }

    /// <summary>Para un canal ya decidido (notificación manual o reenvío).</summary>
    public static Eleccion ParaCanal(Canal canal, string? email, string? telefono)
    {
        var contacto = ContactoPara(canal, email, telefono);
        return new Eleccion(canal, contacto, canal == Canal.PUSH || contacto is not null);
    }

    private static string? ContactoPara(Canal canal, string? email, string? telefono) => canal switch
    {
        Canal.EMAIL => string.IsNullOrWhiteSpace(email) ? null : email,
        Canal.SMS => string.IsNullOrWhiteSpace(telefono) ? null : telefono,
        _ => null
    };
}
