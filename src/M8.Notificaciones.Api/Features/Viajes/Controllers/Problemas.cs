using Microsoft.AspNetCore.Mvc;

namespace M8.Notificaciones.Api.Features.Viajes.Controllers;

/// <summary>
/// Arma respuestas de error application/problem+json (RFC 9457), como pide el
/// contrato de M8: los errores genéricos usan type "about:blank" y los de
/// dominio (409, 422) tienen su propio type.
/// </summary>
internal static class Problemas
{
    public const string ContentType = "application/problem+json";

    public static ObjectResult Crear(int status, string type, string title, string detail, string? instance = null)
    {
        var problema = new ProblemDetails
        {
            Status = status,
            Type = type,
            Title = title,
            Detail = detail,
            Instance = instance
        };

        var resultado = new ObjectResult(problema) { StatusCode = status };
        // Lo fijamos explícitamente para que nunca salga como application/json.
        resultado.ContentTypes.Add(ContentType);
        return resultado;
    }

    public static ObjectResult NoEncontrado(string detail, string? instance = null) =>
        Crear(StatusCodes.Status404NotFound, "about:blank", "No encontrado", detail, instance);
}
