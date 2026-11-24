using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace M8.Notificaciones.Api.Features.Soporte.Dtos;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReferenciaTicket(
    [Required, AllowedValues("VIAJE", "RESERVA", "PAGO")] string Tipo,
    [property: JsonRequired] Guid Id);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TicketNuevo(
    [Required] ReferenciaTicket Referencia,
    [Required, StringLength(150, MinimumLength = 1)] string Titulo,
    [Required, StringLength(2000, MinimumLength = 1)] string Descripcion);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TicketCambioEstado(
    [Required, AllowedValues("ABIERTO", "EN_REVISION", "RESUELTO", "CERRADO")] string Estado,
    [StringLength(1000)] string? Resolucion,
    [StringLength(500)] string? Motivo);

public sealed record TicketResponse(Guid TicketId, Guid UsuarioId, string Rol, ReferenciaTicket Referencia,
    string Titulo, string Descripcion, string Estado, DateTime CreadoEn, DateTime ActualizadoEn,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Resolucion,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTime? CerradoEn);

public sealed record CambioTicketResponse(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? EstadoAnterior,
    string EstadoNuevo, string ActorId, DateTime CambiadoEn,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Motivo);

public sealed record MetricasSoporteResponse(
    int TotalTickets,
    int Abiertos,
    int EnRevision,
    int Resueltos,
    int Cerrados,
    double TiempoMedioResolucionMinutos);

public sealed record Pagina<T>(int Page, int Size, int TotalElementos, int TotalPaginas, List<T> Items);
