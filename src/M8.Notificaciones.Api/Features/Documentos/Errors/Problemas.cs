using Microsoft.AspNetCore.Mvc;

namespace M8.Notificaciones.Api.Features.Documentos.Errors;

/// <summary>
/// Errores según RFC 9457 (application/problem+json), como define el contrato OpenAPI.
/// </summary>
public static class Problemas
{
    public static ObjectResult SolicitudInvalida(string? detalle = null) =>
        Crear(StatusCodes.Status400BadRequest, "about:blank", "Solicitud inválida", detalle);

    public static ObjectResult NoEncontrado(string? detalle = null) =>
        Crear(StatusCodes.Status404NotFound, "about:blank", "No encontrado", detalle);

    public static ObjectResult Crear(int status, string type, string title, string? detalle = null)
    {
        var problema = new ProblemDetails
        {
            Status = status,
            Type = type,
            Title = title,
            Detail = detalle
        };

        var resultado = new ObjectResult(problema) { StatusCode = status };
        resultado.ContentTypes.Add("application/problem+json");
        return resultado;
    }
}