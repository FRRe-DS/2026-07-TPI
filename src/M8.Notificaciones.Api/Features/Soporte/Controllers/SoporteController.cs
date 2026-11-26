using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using M8.Notificaciones.Api.Features.Notificaciones.Dominio;
using M8.Notificaciones.Api.Features.Notificaciones.Entities;
using M8.Notificaciones.Api.Features.Notificaciones.Servicios;
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
public sealed class SoporteController(M8DbContext db, FabricaNotificaciones fabricaNotificaciones, TimeProvider reloj) : ControllerBase
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

        var ticks = reloj.GetUtcNow().UtcDateTime.Ticks;
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

        // Notificación de confirmación de recepción de ticket al usuario (TP3)
        await NotificarRecepcionTicketAsync(ticket, ct);

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
        ticket.ActualizadoEn = reloj.GetUtcNow().UtcDateTime;
        ticket.Version++;
        if (ticket.Estado == "RESUELTO") ticket.Resolucion = cambio.Resolucion;
        if (ticket.Estado == "EN_REVISION") ticket.Resolucion = null;
        if (ticket.Estado == "CERRADO") ticket.CerradoEn = ticket.ActualizadoEn;
        RegistrarCambio(ticket, anterior, actor, cambio.Motivo);

        // Notificación automática al usuario cuando su ticket cambia de estado (TP3 - Célula 1 y 3)
        await NotificarCambioEstadoAsync(ticket, cambio.Estado, cambio.Resolucion, ct);

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

    [HttpGet("metricas")]
    [Authorize(Roles = Roles.Operador)]
    public async Task<IActionResult> ObtenerMetricas(CancellationToken ct)
    {
        var cantidades = await db.Tickets.GroupBy(t => t.Estado)
            .Select(g => new { Estado = g.Key, Cantidad = g.Count() })
            .ToDictionaryAsync(g => g.Estado, g => g.Cantidad, ct);

        // Cada ticket pesa una vez, aunque el operador lo reabra y vuelva a resolverlo.
        var primerasResoluciones = db.Set<CambioTicket>()
            .Where(c => c.EstadoNuevo == "RESUELTO")
            .GroupBy(c => c.TicketId)
            .Select(g => new { TicketId = g.Key, Fecha = g.Min(c => c.CambiadoEn) });
        var minutos = from r in primerasResoluciones
                      join t in db.Tickets on r.TicketId equals t.Id
                      select (double?)(r.Fecha - t.CreadoEn).TotalMinutes;
        var promedio = await minutos.AverageAsync(ct) ?? 0;

        return Ok(new MetricasSoporteResponse(
            cantidades.Values.Sum(),
            cantidades.GetValueOrDefault("ABIERTO"),
            cantidades.GetValueOrDefault("EN_REVISION"),
            cantidades.GetValueOrDefault("RESUELTO"),
            cantidades.GetValueOrDefault("CERRADO"),
            Math.Round(promedio, 2)));
    }

    private async Task NotificarRecepcionTicketAsync(Ticket ticket, CancellationToken ct)
    {
        var asunto = $"Ticket #{ticket.Id.ToString()[..8]} recibido";
        var mensaje = $"Recibimos tu ticket de soporte sobre {ticket.ReferenciaTipo} con título '{ticket.Titulo}'. Un operador lo revisará a la brevedad.";

        await AgregarAvisoAsync(ticket, asunto, mensaje, ct);
    }

    private async Task NotificarCambioEstadoAsync(Ticket ticket, string nuevoEstado, string? resolucion, CancellationToken ct)
    {
        var asunto = nuevoEstado switch
        {
            "EN_REVISION" => $"Tu ticket #{ticket.Id.ToString()[..8]} está en revisión",
            "RESUELTO" => $"Tu ticket #{ticket.Id.ToString()[..8]} fue resuelto",
            "CERRADO" => $"Tu ticket #{ticket.Id.ToString()[..8]} fue cerrado",
            _ => $"Actualización en tu ticket #{ticket.Id.ToString()[..8]}"
        };

        var detalleResolucion = $" Resolución: {resolucion}";
        var mensaje = nuevoEstado switch
        {
            "EN_REVISION" => $"Un operador tomó tu ticket sobre {ticket.ReferenciaTipo} y está analizando el caso.",
            "RESUELTO" => $"El reclamo sobre {ticket.ReferenciaTipo} fue resuelto.{detalleResolucion}",
            "CERRADO" => $"El ticket sobre {ticket.ReferenciaTipo} ha sido cerrado.",
            _ => $"El estado de tu reclamo cambió a {nuevoEstado}."
        };

        // La resolución completa sigue disponible en el detalle protegido del ticket.
        if (mensaje.Length > 1000)
            mensaje = "Tu reclamo fue resuelto. Consultá la resolución completa en el ticket.";

        await AgregarAvisoAsync(ticket, asunto, mensaje, ct);
    }

    private async Task AgregarAvisoAsync(Ticket ticket, string asunto, string mensaje, CancellationToken ct)
    {
        var rol = ticket.Rol == Roles.Cliente ? RolDestinatario.CLIENTE : RolDestinatario.CONDUCTOR;
        var contacto = await db.ContactosDestinatarios.AsNoTracking()
            .FirstOrDefaultAsync(c => c.UsuarioId == ticket.UsuarioId, ct);

        var refTipo = ticket.ReferenciaTipo switch
        {
            "VIAJE" => TipoReferencia.VIAJE,
            "RESERVA" => TipoReferencia.RESERVA,
            "PAGO" => TipoReferencia.PAGO,
            _ => (TipoReferencia?)null
        };

        var notificacion = fabricaNotificaciones.DeSoporte(
            ticket.UsuarioId,
            rol,
            asunto,
            mensaje,
            refTipo,
            ticket.ReferenciaId,
            contacto,
            reloj.GetUtcNow());

        db.Notificaciones.Add(notificacion);
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
