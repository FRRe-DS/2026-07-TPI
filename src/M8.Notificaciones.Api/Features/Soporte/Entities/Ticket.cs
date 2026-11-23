using System.ComponentModel.DataAnnotations;

namespace M8.Notificaciones.Api.Features.Soporte.Entities;

public sealed class Ticket
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UsuarioId { get; set; }
    public required string Rol { get; set; }
    public required string ReferenciaTipo { get; set; }
    public Guid ReferenciaId { get; set; }
    public required string Titulo { get; set; }
    public required string Descripcion { get; set; }
    public required string ClaveIdempotencia { get; set; }
    public string Estado { get; set; } = "ABIERTO";
    public string? Resolucion { get; set; }
    public DateTime CreadoEn { get; set; }
    public DateTime ActualizadoEn { get; set; }
    public DateTime? CerradoEn { get; set; }
    [ConcurrencyCheck] public int Version { get; set; }
}

public sealed class CambioTicket
{
    public long Id { get; set; }
    public Guid TicketId { get; set; }
    public string? EstadoAnterior { get; set; }
    public required string EstadoNuevo { get; set; }
    public required string ActorId { get; set; }
    public string? Motivo { get; set; }
    public DateTime CambiadoEn { get; set; }
}
