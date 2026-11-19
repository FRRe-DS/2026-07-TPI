using System.Text.Json.Serialization;
using M8.Notificaciones.Api.Features.Notificaciones.Dominio;
using M8.Notificaciones.Api.Features.Notificaciones.Entities;

namespace M8.Notificaciones.Api.Features.Notificaciones.Dtos;

// DTOs con los nombres y formas EXACTOS del contrato de M8 (esquemas
// NotificacionNueva, Notificacion, Intento, Referencia y las páginas).

/// <summary>Cuerpo de POST /api/v1/notificaciones (esquema NotificacionNueva).</summary>
public sealed record NotificacionNuevaRequest(
    Guid? DestinatarioId,
    RolDestinatario? Rol,
    Canal? Canal,
    string? Asunto,
    string? Mensaje,
    ReferenciaDto? Referencia);

public sealed record ReferenciaDto(TipoReferencia? Tipo, Guid? Id);

/// <summary>Esquema Notificacion. Los campos opcionales que no aplican no se envían.</summary>
public sealed record NotificacionResponse(
    Guid NotificacionId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? EventoId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] TipoEvento? TipoEvento,
    Guid DestinatarioId,
    Canal Canal,
    string Asunto,
    EstadoNotificacion Estado,
    int Intentos,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ReferenciaDto? Referencia,
    DateTimeOffset CreadoEn,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? EnviadoEn,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? UltimoError)
{
    /// <summary>Nunca expone el contacto (email/teléfono): no está en el contrato y es un dato personal.</summary>
    public static NotificacionResponse De(Notificacion n) => new(
        n.Id,
        n.EventoId,
        n.TipoEvento,
        n.DestinatarioId,
        n.Canal,
        n.Asunto,
        n.Estado,
        n.Intentos,
        n.ReferenciaTipo is { } t && n.ReferenciaId is { } id ? new ReferenciaDto(t, id) : null,
        n.CreadoEn,
        n.EnviadoEn,
        n.Estado == EstadoNotificacion.ENVIADA ? null : n.UltimoError);
}

/// <summary>Esquema Intento.</summary>
public sealed record IntentoResponse(
    int Numero,
    Canal Canal,
    ResultadoIntento Resultado,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Error,
    DateTimeOffset RealizadoEn)
{
    public static IntentoResponse De(IntentoNotificacion i) => new(i.Numero, i.Canal, i.Resultado, i.Error, i.RealizadoEn);
}

/// <summary>Esquema Pagina + items (PaginaNotificaciones, PaginaIntentos).</summary>
public sealed record Pagina<T>(IReadOnlyList<T> Items, int Page, int Size, int TotalElementos, int TotalPaginas)
{
    public static Pagina<T> Crear(IReadOnlyList<T> items, int page, int size, int total) =>
        new(items, page, size, total, total == 0 ? 0 : (int)Math.Ceiling(total / (double)size));
}
