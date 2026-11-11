namespace M8.Notificaciones.Api.Features.Viajes.Dominio;

/// <summary>
/// El comando no se puede aplicar en el estado actual del viaje (por ejemplo,
/// finalizar un viaje sin conductor asignado). El controlador la traduce a
/// 409 Conflict con application/problem+json.
///
/// Por qué una excepción propia del dominio: el agregado no sabe nada de HTTP;
/// solo dice "esto viola una regla". Qué código HTTP corresponde lo decide la
/// capa web.
/// </summary>
public sealed class ReglaDeNegocioException : Exception
{
    public ReglaDeNegocioException(string titulo, string detalle) : base(detalle)
    {
        Titulo = titulo;
    }

    /// <summary>Frase corta para el campo "title" del problem+json.</summary>
    public string Titulo { get; }
}
