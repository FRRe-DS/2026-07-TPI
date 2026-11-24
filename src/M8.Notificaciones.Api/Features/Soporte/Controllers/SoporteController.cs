using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using M8.Notificaciones.Api.Features.Soporte.Dtos;
using M8.Notificaciones.Api.Features.Soporte.Entities;
using M8.Notificaciones.Api.Infrastructure.Http;
using M8.Notificaciones.Api.Infrastructure.Persistence;
using M8.Notificaciones.Api.Infrastructure.Seguridad;

namespace M8.Notificaciones.Api.Features.Soporte.Controllers;

[ApiController]
[Route("api/v1/tickets")]
[Tags("Tickets")]
[Authorize(Roles = Roles.Cliente + "," + Roles.Conductor + "," + Roles.Operador)]
public sealed class SoporteController(M8DbContext db) : ControllerBase
{
    private bool EsOperador => User.IsInRole(Roles.Operador);

    [HttpPost]
    [Consumes("application/json")]
    [Authorize(Roles = Roles.Cliente + "," + Roles.Conductor)]
    public async Task<IActionResult> Crear(TicketNuevo pedido,
        [FromHeader(Name = "Idempotency-Key"), Required, StringLength(255)] string clave, CancellationToken ct)
    {
        if (!Guid.TryParse(User.Sub(), out var usuario)) return Forbid();
        var existentes = db.Tickets.AsNoTracking().Where(t => t.UsuarioId == usuario && t.ClaveIdempotencia == clave);
        var previo = await existentes.SingleOrDefaultAsync(ct);
        if (previo != null) return RepetirAlta(previo, pedido);

        var ticks = DateTime.UtcNow.Ticks;
        // PostgreSQL conserva microsegundos. El primer 201 y sus repeticiones deben coincidir.
        var ahora = new DateTime(ticks - ticks % 10, DateTimeKind.Utc);
        var ticket = new Ticket
        {
            UsuarioId = usuario, Rol = User.IsInRole(Roles.Cliente) ? Roles.Cliente : Roles.Conductor,
            ReferenciaTipo = pedido.Referencia.Tipo, ReferenciaId = pedido.Referencia.Id,
            Titulo = pedido.Titulo, Descripcion = pedido.Descripcion,
            ClaveIdempotencia = clave, CreadoEn = ahora, ActualizadoEn = ahora
        };
        db.Tickets.Add(ticket);
        RegistrarCambio(ticket, null, usuario.ToString(), null);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "UX_TicketsSoporte_Usuario_Clave" })
        {
            return RepetirAlta(await existentes.SingleAsync(ct), pedido);
        }
        return AltaOriginal(ticket);
    }

    private IActionResult RepetirAlta(Ticket ticket, TicketNuevo pedido)
    {
        if (ticket.ReferenciaTipo != pedido.Referencia.Tipo || ticket.ReferenciaId != pedido.Referencia.Id ||
            ticket.Titulo != pedido.Titulo || ticket.Descripcion != pedido.Descripcion)
            return ProblemasHttp.Dominio(422, "/errores/idempotency-key-reutilizada", "Clave reutilizada",
                "El contenido difiere del alta original.", Request.Path);
        return AltaOriginal(ticket);
    }

    private IActionResult AltaOriginal(Ticket ticket) => CreatedAtAction(nameof(Obtener), new { ticketId = ticket.Id },
        Respuesta(ticket) with { Estado = "ABIERTO", ActualizadoEn = ticket.CreadoEn, Resolucion = null, CerradoEn = null });

    [HttpGet]
    public async Task<IActionResult> Listar(
        [FromQuery, AllowedValues(null, "ABIERTO", "EN_REVISION", "RESUELTO", "CERRADO")] string? estado,
        [FromQuery, AllowedValues(null, "VIAJE", "RESERVA", "PAGO")] string? referenciaTipo,
        Guid? referenciaId, Guid? usuarioId, CancellationToken ct,
        [Range(0, int.MaxValue)] int page = 0, [Range(1, 100)] int size = 20)
    {
        if (!EsOperador)
        {
            if (usuarioId.HasValue || !Guid.TryParse(User.Sub(), out var propio)) return Forbid();
            usuarioId = propio;
        }
        var query = db.Tickets.AsNoTracking().AsQueryable();
        if (usuarioId.HasValue) query = query.Where(t => t.UsuarioId == usuarioId);
        if (referenciaId.HasValue) query = query.Where(t => t.ReferenciaId == referenciaId);
        if (referenciaTipo != null) query = query.Where(t => t.ReferenciaTipo == referenciaTipo);
        if (estado != null) query = query.Where(t => t.Estado == estado);
        var lista = query.OrderByDescending(t => t.CreadoEn).ThenBy(t => t.Id).Select(t => Respuesta(t));
        return Ok(await Paginar(lista, page, size, ct));
    }

    [HttpGet("{ticketId}")]
    public async Task<IActionResult> Obtener(Guid ticketId, CancellationToken ct)
    {
        var ticket = await db.Tickets.AsNoTracking().SingleOrDefaultAsync(t => t.Id == ticketId, ct);
        return ErrorDeLectura(ticket) ?? Ok(Respuesta(ticket!));
    }

    [HttpGet("{ticketId}/historial")]
    public async Task<IActionResult> Historial(Guid ticketId, CancellationToken ct,
        [Range(0, int.MaxValue)] int page = 0, [Range(1, 100)] int size = 20)
    {
        var ticket = await db.Tickets.AsNoTracking().SingleOrDefaultAsync(t => t.Id == ticketId, ct);
        if (ErrorDeLectura(ticket) is { } error) return error;
        var cambios = db.Set<CambioTicket>().AsNoTracking().Where(c => c.TicketId == ticketId)
            .OrderBy(c => c.Id).Select(c => new CambioTicketResponse(c.EstadoAnterior, c.EstadoNuevo,
                c.ActorId, c.CambiadoEn, c.Motivo));
        return Ok(await Paginar(cambios, page, size, ct));
    }

    private IActionResult? ErrorDeLectura(Ticket? ticket)
    {
        if (ticket == null) return ProblemasHttp.Generico(404, "El ticket no existe.", Request.Path);
        if (!EsOperador && (!Guid.TryParse(User.Sub(), out var usuario) || ticket.UsuarioId != usuario))
            return Forbid();
        return null;
    }

    [HttpPatch("{ticketId}")]
    [Consumes("application/json")]
    [Authorize(Roles = Roles.Operador)]
    public async Task<IActionResult> CambiarEstado(Guid ticketId, TicketCambioEstado cambio, CancellationToken ct)
    {
        var actor = User.Sub();
        if (string.IsNullOrWhiteSpace(actor)) return Forbid();
        var ticket = await db.Tickets.SingleOrDefaultAsync(t => t.Id == ticketId, ct);
        if (ticket == null) return ProblemasHttp.Generico(404, "El ticket no existe.", Request.Path);
        if ((ticket.Estado, cambio.Estado) is not (("ABIERTO", "EN_REVISION") or ("EN_REVISION", "RESUELTO")
            or ("RESUELTO", "CERRADO") or ("RESUELTO", "EN_REVISION")))
            return Conflicto();
        if (cambio.Estado == "RESUELTO" && string.IsNullOrWhiteSpace(cambio.Resolucion))
            return ProblemasHttp.Dominio(422, "/errores/resolucion-requerida", "Resolución requerida",
                "Indicá cómo se resolvió el ticket.", Request.Path);

        var anterior = ticket.Estado;
        ticket.Estado = cambio.Estado;
        ticket.ActualizadoEn = DateTime.UtcNow;
        ticket.Version++;
        if (ticket.Estado == "RESUELTO") ticket.Resolucion = cambio.Resolucion;
        if (ticket.Estado == "EN_REVISION") ticket.Resolucion = null;
        if (ticket.Estado == "CERRADO") ticket.CerradoEn = ticket.ActualizadoEn;
        RegistrarCambio(ticket, anterior, actor, cambio.Motivo);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflicto();
        }
        return Ok(Respuesta(ticket));
    }

    private IActionResult Conflicto() => ProblemasHttp.Dominio(409, "/errores/transicion-invalida",
        "Transición inválida", "El estado actual del ticket no admite ese cambio.", Request.Path);

    private void RegistrarCambio(Ticket ticket, string? anterior, string actor, string? motivo) =>
        db.Set<CambioTicket>().Add(new CambioTicket { TicketId = ticket.Id, EstadoAnterior = anterior,
            EstadoNuevo = ticket.Estado, ActorId = actor, CambiadoEn = ticket.ActualizadoEn, Motivo = motivo });

    private static TicketResponse Respuesta(Ticket t) => new(t.Id, t.UsuarioId, t.Rol,
        new(t.ReferenciaTipo, t.ReferenciaId), t.Titulo, t.Descripcion, t.Estado, t.CreadoEn,
        t.ActualizadoEn, t.Resolucion, t.CerradoEn);

    private static async Task<Pagina<T>> Paginar<T>(IQueryable<T> query, int page, int size, CancellationToken ct)
    {
        var total = await query.CountAsync(ct);
        var offset = (long)page * size;
        List<T> items = offset >= total ? [] : await query.Skip((int)offset).Take(size).ToListAsync(ct);
        return new(page, size, total, (int)Math.Ceiling((double)total / size), items);
    }
}
