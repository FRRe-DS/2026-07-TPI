using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using M8.Notificaciones.Api.Features.Notificaciones.Dominio;
using M8.Notificaciones.Api.Features.Notificaciones.Entities;
using M8.Notificaciones.Api.Infrastructure.Observability;
using M8.Notificaciones.Api.Infrastructure.Persistence;
using M8.Notificaciones.Api.Infrastructure.Tiempo;

namespace M8.Notificaciones.Api.Features.Notificaciones.Servicios;

/// <summary>
/// Lo que Notificaciones le ofrece a Documentos para el reenvío de comprobantes
/// (RF-8.5, POST /api/v1/comprobantes/{id}/reenvios).
///
/// Documentos valida el comprobante (existe, es del cliente, está GENERADO) y la
/// Idempotency-Key; Notificaciones arma el aviso:
///  - al contacto REGISTRADO del cliente, nunca a uno de la solicitud;
///  - con Origen = REENVIO, que sigue la misma política de intentos que las demás
///    (lo envía el despachador en segundo plano);
///  - auditado (quién lo pidió, cuándo y con qué X-Request-Id, RNF-19).
/// Si el cliente no tiene contacto para ese canal, nace FALLIDA (como dice el contrato).
/// La respuesta del endpoint es el esquema Notificacion: NotificacionResponse.De(resultado).
/// </summary>
public sealed class ReenviosNotificaciones
{
    private readonly M8DbContext _db;
    private readonly FabricaNotificaciones _fabrica;
    private readonly TimeProvider _reloj;
    private readonly ILogger<ReenviosNotificaciones> _logger;

    public ReenviosNotificaciones(M8DbContext db, IOptions<OpcionesNotificaciones> opciones, TimeProvider reloj,
        ILogger<ReenviosNotificaciones> logger)
    {
        _db = db;
        _fabrica = new FabricaNotificaciones(opciones.Value);
        _reloj = reloj;
        _logger = logger;
    }

    public async Task<Notificacion> RegistrarAsync(Guid clienteId, Canal canal, string asunto, string mensaje,
        TipoReferencia? refTipo, Guid? refId, string actor, string? requestId, CancellationToken ct)
    {
        var contacto = await _db.ContactosDestinatarios.AsNoTracking().FirstOrDefaultAsync(c => c.UsuarioId == clienteId, ct);
        var ahora = _reloj.Ahora();
        var notificacion = _fabrica.Reenvio(clienteId, canal, asunto, mensaje, refTipo, refId, contacto, ahora);

        var fallidaAlCrearse = Telemetria.MarcarPrimeraFinalizacion(notificacion);
        _db.Notificaciones.Add(notificacion);
        _db.AuditoriaNotificaciones.Add(new AuditoriaNotificacion
        {
            NotificacionId = notificacion.Id,
            Accion = AccionAuditoria.REENVIO,
            Actor = actor,
            RequestId = requestId,
            RealizadaEn = ahora
        });
        await _db.SaveChangesAsync(ct);
        if (fallidaAlCrearse) Telemetria.NotificacionFinalizada(enviada: false, reintentada: false);

        _logger.LogInformation("Reenvío {NotificacionId} registrado por {Actor} ({Estado})", notificacion.Id, actor, notificacion.Estado);
        return notificacion;
    }
}
