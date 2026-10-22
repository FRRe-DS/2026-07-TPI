using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using M8.Notificaciones.Api.Features.Soporte.Dtos;
using M8.Notificaciones.Api.Features.Soporte.Entities;
using M8.Notificaciones.Api.Infrastructure.Persistence;

namespace M8.Notificaciones.Api.Features.Soporte.Controllers;

[ApiController]
[Route("api/[controller]")]
[Tags("Soporte y Tickets (Célula 3)")]
public class SoporteController : ControllerBase
{
    private readonly M8DbContext _dbContext;
    private readonly ILogger<SoporteController> _logger;

    public SoporteController(M8DbContext dbContext, ILogger<SoporteController> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// Crea y registra un ticket de soporte asociado a un viaje o reclamo general (RF-8.7)
    /// </summary>
    [HttpPost("tickets")]
    public async Task<ActionResult<TicketResponse>> CrearTicket(
        [FromBody] CrearTicketRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Creando ticket para usuario {UsuarioId}, Tipo: {Tipo}", request.UsuarioId, request.Tipo);

        var ticket = new Ticket
        {
            Id = Guid.NewGuid(),
            UsuarioId = request.UsuarioId,
            ViajeId = request.ViajeId,
            Tipo = request.Tipo,
            Titulo = request.Titulo,
            Descripcion = request.Descripcion,
            Estado = EstadoTicket.Abierto,
            FechaCreacion = DateTime.UtcNow
        };

        _dbContext.Tickets.Add(ticket);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(ObtenerTicketPorId), new { id = ticket.Id }, ToResponse(ticket));
    }

    /// <summary>
    /// Obtiene el detalle y estado de un ticket por su identificador
    /// </summary>
    [HttpGet("tickets/{id:guid}")]
    public async Task<ActionResult<TicketResponse>> ObtenerTicketPorId(
        Guid id,
        CancellationToken cancellationToken)
    {
        var ticket = await _dbContext.Tickets
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

        if (ticket is null)
        {
            return NotFound(new { mensaje = $"No se encontró el ticket con Id {id}" });
        }

        return Ok(ToResponse(ticket));
    }

    private static TicketResponse ToResponse(Ticket ticket) => new(
        Id: ticket.Id,
        UsuarioId: ticket.UsuarioId,
        ViajeId: ticket.ViajeId,
        Tipo: ticket.Tipo,
        Titulo: ticket.Titulo,
        Descripcion: ticket.Descripcion,
        Estado: ticket.Estado,
        FechaCreacion: ticket.FechaCreacion,
        FechaCierre: ticket.FechaCierre
    );
}
