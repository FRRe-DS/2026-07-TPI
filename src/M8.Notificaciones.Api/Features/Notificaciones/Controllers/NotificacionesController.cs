using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using M8.Notificaciones.Api.Features.Notificaciones.Dominio;
using M8.Notificaciones.Api.Features.Notificaciones.Dtos;
using M8.Notificaciones.Api.Features.Notificaciones.Servicios;
using M8.Notificaciones.Api.Infrastructure.Http;
using M8.Notificaciones.Api.Infrastructure.Idempotencia;
using M8.Notificaciones.Api.Infrastructure.Observability;
using M8.Notificaciones.Api.Infrastructure.Persistence;
using M8.Notificaciones.Api.Infrastructure.Seguridad;
using M8.Notificaciones.Api.Infrastructure.Tiempo;

namespace M8.Notificaciones.Api.Features.Notificaciones.Controllers;

/// <summary>
/// Notificaciones y seguimiento de su entrega (RF-8.1, RF-8.2, RF-8.6).
/// Rutas, roles, códigos y esquemas según el contrato de M8.
/// </summary>
[ApiController]
[Route("api/v1/notificaciones")]
[Tags("Notificaciones")]
// Sin [Produces("application/json")]: pisaría el application/problem+json de los errores.
public sealed class NotificacionesController : ControllerBase
{
    private const string Base = "/api/v1/notificaciones";

    private readonly M8DbContext _db;
    private readonly EjecucionIdempotente _idempotencia;
    private readonly FabricaNotificaciones _fabrica;
    private readonly TimeProvider _reloj;
    private readonly ILogger<NotificacionesController> _logger;

    public NotificacionesController(M8DbContext db, EjecucionIdempotente idempotencia, IOptions<OpcionesNotificaciones> opciones,
        TimeProvider reloj, ILogger<NotificacionesController> logger)
    {
        _db = db;
        _idempotencia = idempotencia;
        _fabrica = new FabricaNotificaciones(opciones.Value);
        _reloj = reloj;
        _logger = logger;
    }

    /// <summary>Listar notificaciones, más recientes primero (listarNotificaciones).</summary>
    [HttpGet]
    [Authorize(Roles = Roles.OperadorOServicio)]
    [ProducesResponseType(typeof(Pagina<NotificacionResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Listar(
        [FromQuery] Guid? destinatarioId,
        [FromQuery] EstadoNotificacion? estado,
        [FromQuery] Canal? canal,
        [FromQuery] TipoEvento? tipoEvento,
        [FromQuery] Guid? referenciaId,
        [FromQuery] int page = 0,
        [FromQuery] int size = 20,
        CancellationToken ct = default)
    {
        if (PaginacionInvalida(page, size, out var error)) return error!;

        var consulta = _db.Notificaciones.AsNoTracking();
        if (destinatarioId is { } d) consulta = consulta.Where(n => n.DestinatarioId == d);
        if (estado is { } e) consulta = consulta.Where(n => n.Estado == e);
        if (canal is { } c) consulta = consulta.Where(n => n.Canal == c);
        if (tipoEvento is { } t) consulta = consulta.Where(n => n.TipoEvento == t);
        if (referenciaId is { } r) consulta = consulta.Where(n => n.ReferenciaId == r);

        var total = await consulta.CountAsync(ct);
        var items = await consulta
            .OrderByDescending(n => n.CreadoEn).ThenBy(n => n.Id)
            .Skip(page * size).Take(size)
            .ToListAsync(ct);

        return Ok(Pagina<NotificacionResponse>.Crear(items.Select(NotificacionResponse.De).ToList(), page, size, total));
    }

    /// <summary>Enviar una notificación manual de un OPERADOR (enviarNotificacion). El envío es asíncrono.</summary>
    [HttpPost]
    [Authorize(Roles = Roles.Operador)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(NotificacionResponse), StatusCodes.Status202Accepted)]
    public Task<IActionResult> Enviar(
        [FromHeader(Name = EjecucionIdempotente.Encabezado)] string? idempotencyKey,
        [FromBody] NotificacionNuevaRequest solicitud,
        CancellationToken ct)
    {
        return _idempotencia.EjecutarAsync($"notificaciones.crear:{User.Actor()}", idempotencyKey, solicitud, Base, async () =>
        {
            var errores = Validar(solicitud);
            if (errores.Count > 0)
            {
                return ProblemasHttp.Generico(StatusCodes.Status400BadRequest, string.Join(" ", errores), Base);
            }

            var contacto = await _db.ContactosDestinatarios.AsNoTracking()
                .FirstOrDefaultAsync(x => x.UsuarioId == solicitud.DestinatarioId, ct);
            var notificacion = _fabrica.Manual(
                solicitud.DestinatarioId!.Value, solicitud.Rol!.Value, solicitud.Canal!.Value,
                solicitud.Asunto!, solicitud.Mensaje!, solicitud.Referencia?.Tipo, solicitud.Referencia?.Id,
                contacto, _reloj.Ahora());

            var fallidaAlCrearse = Telemetria.MarcarPrimeraFinalizacion(notificacion);
            _db.Notificaciones.Add(notificacion);
            await _db.SaveChangesAsync(ct);
            if (fallidaAlCrearse) Telemetria.NotificacionFinalizada(enviada: false, reintentada: false);
            _logger.LogInformation("Notificación manual {NotificacionId} registrada por {Actor} ({Estado})",
                notificacion.Id, User.Actor(), notificacion.Estado);

            Response.Headers.Location = $"{Base}/{notificacion.Id}";
            return StatusCode(StatusCodes.Status202Accepted, NotificacionResponse.De(notificacion));
        }, ct);
    }

    /// <summary>Consultar el estado de una notificación (obtenerNotificacion).</summary>
    [HttpGet("{notificacionId}")]
    [Authorize(Roles = Roles.OperadorOServicio)]
    [ProducesResponseType(typeof(NotificacionResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Obtener(Guid notificacionId, CancellationToken ct)
    {
        var n = await _db.Notificaciones.AsNoTracking().FirstOrDefaultAsync(x => x.Id == notificacionId, ct);
        return n is null ? NoEncontrada(notificacionId) : Ok(NotificacionResponse.De(n));
    }

    /// <summary>Historial de intentos de entrega, en orden cronológico (listarIntentos).</summary>
    [HttpGet("{notificacionId}/intentos")]
    [Authorize(Roles = Roles.OperadorOServicio)]
    [ProducesResponseType(typeof(Pagina<IntentoResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListarIntentos(Guid notificacionId, [FromQuery] int page = 0, [FromQuery] int size = 20,
        CancellationToken ct = default)
    {
        if (PaginacionInvalida(page, size, out var error)) return error!;
        if (!await _db.Notificaciones.AnyAsync(x => x.Id == notificacionId, ct)) return NoEncontrada(notificacionId);

        var consulta = _db.IntentosNotificacion.AsNoTracking().Where(i => i.NotificacionId == notificacionId);
        var total = await consulta.CountAsync(ct);
        var items = await consulta.OrderBy(i => i.Numero).Skip(page * size).Take(size).ToListAsync(ct);
        return Ok(Pagina<IntentoResponse>.Crear(items.Select(IntentoResponse.De).ToList(), page, size, total));
    }

    /// <summary>
    /// Reintentar una notificación FALLIDA (reintentarNotificacion). Agrega un
    /// intento nuevo y no borra los anteriores. En otro estado, 409.
    /// </summary>
    [HttpPost("{notificacionId}/reintentos")]
    [Authorize(Roles = Roles.Operador)]
    [ProducesResponseType(typeof(NotificacionResponse), StatusCodes.Status202Accepted)]
    public Task<IActionResult> Reintentar(
        Guid notificacionId,
        [FromHeader(Name = EjecucionIdempotente.Encabezado)] string? idempotencyKey,
        CancellationToken ct)
    {
        var instancia = $"{Base}/{notificacionId}/reintentos";
        return _idempotencia.EjecutarAsync($"notificaciones.reintentar:{User.Actor()}", idempotencyKey, new { notificacionId }, instancia, async () =>
        {
            var n = await _db.Notificaciones.FirstOrDefaultAsync(x => x.Id == notificacionId, ct);
            if (n is null) return NoEncontrada(notificacionId);
            if (n.Estado != EstadoNotificacion.FALLIDA)
            {
                return ProblemasHttp.Dominio(StatusCodes.Status409Conflict, "/errores/notificacion-no-reintentable",
                    "La notificación no se puede reintentar",
                    $"Solo se reintenta una notificación FALLIDA; esta está {n.Estado}.", instancia);
            }

            n.Estado = EstadoNotificacion.PENDIENTE;
            n.MaxIntentos = n.Intentos + 1; // exactamente un intento más
            n.ProximoIntentoEn = _reloj.Ahora();
            n.BloqueadaHasta = null;
            n.Version++;
            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Otro OPERADOR la reintentó al mismo tiempo (con otra clave): gana uno solo.
                return ProblemasHttp.Dominio(StatusCodes.Status409Conflict, "/errores/notificacion-no-reintentable",
                    "La notificación no se puede reintentar", "La notificación cambió mientras se procesaba el reintento.", instancia);
            }

            _logger.LogInformation("Notificación {NotificacionId} reintentada por {Actor}", n.Id, User.Actor());
            return StatusCode(StatusCodes.Status202Accepted, NotificacionResponse.De(n));
        }, ct);
    }

    private ObjectResult NoEncontrada(Guid id) =>
        ProblemasHttp.Generico(StatusCodes.Status404NotFound, $"No existe la notificación {id}.", $"{Base}/{id}");

    private bool PaginacionInvalida(int page, int size, out IActionResult? error)
    {
        error = null;
        if (page >= 0 && size is >= 1 and <= 100) return false;
        error = ProblemasHttp.Generico(StatusCodes.Status400BadRequest,
            "'page' debe ser 0 o mayor y 'size' estar entre 1 y 100.", Request.Path);
        return true;
    }

    private static List<string> Validar(NotificacionNuevaRequest s)
    {
        var errores = new List<string>();
        if (s.DestinatarioId is null || s.DestinatarioId == Guid.Empty) errores.Add("Falta 'destinatarioId'.");
        if (s.Rol is null) errores.Add("Falta 'rol' (CLIENTE o CONDUCTOR).");
        if (s.Canal is null) errores.Add("Falta 'canal' (EMAIL, SMS o PUSH).");
        if (string.IsNullOrWhiteSpace(s.Asunto) || s.Asunto.Length > 150) errores.Add("'asunto' es obligatorio y tiene hasta 150 caracteres.");
        if (string.IsNullOrWhiteSpace(s.Mensaje) || s.Mensaje.Length > 1000) errores.Add("'mensaje' es obligatorio y tiene hasta 1000 caracteres.");
        if (s.Referencia is { } r && (r.Tipo is null || r.Id is null || r.Id == Guid.Empty))
            errores.Add("'referencia' necesita 'tipo' (VIAJE, RESERVA o PAGO) e 'id'.");
        return errores;
    }
}
