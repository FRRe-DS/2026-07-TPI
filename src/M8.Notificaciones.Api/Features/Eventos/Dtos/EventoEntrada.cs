using M8.Notificaciones.Api.Features.Notificaciones.Dominio;

namespace M8.Notificaciones.Api.Features.Eventos.Dtos;

/// <summary>
/// Cuerpo de POST /api/v1/eventos y de los mensajes de la cola (esquema
/// EventoEntrada del contrato). Es el MISMO formato por los dos caminos: el
/// contrato dice que con la mensajería "los mismos eventos viajan por la cola y
/// este endpoint se mantiene".
///
/// Las propiedades son anulables a propósito: la validación de reglas la hace
/// <see cref="ValidadorEvento"/> en un único lugar, y así responde igual por HTTP
/// que por RabbitMQ. Un enum con un valor que no existe en el contrato falla al
/// deserializar (400 por HTTP, cola de errores por RabbitMQ).
/// </summary>
public sealed record EventoEntrada(
    Guid? EventoId,
    TipoEvento? Tipo,
    ModuloOrigen? Origen,
    DateTimeOffset? OcurridoEn,
    Guid? ViajeId,
    Guid? ReservaId,
    Guid? PagoId,
    IReadOnlyList<DestinatarioEntrada>? Destinatarios,
    DetalleEventoEntrada? Detalle);

/// <summary>A quién avisar. El contacto lo informa quien emite el evento.</summary>
public sealed record DestinatarioEntrada(
    Guid? UsuarioId,
    RolDestinatario? Rol,
    string? Email,
    string? Telefono,
    IReadOnlyList<Canal>? CanalesPreferidos);

/// <summary>
/// Datos para redactar el aviso. "conductorId" NO está en el contrato: es la
/// extensión que usa el historial de viajes (ver docs/DECISIONES.md, 2.2).
/// </summary>
public sealed record DetalleEventoEntrada(
    string? Origen,
    string? Destino,
    DateTimeOffset? FechaProgramada,
    string? ZonaHoraria,
    TipoVehiculo? TipoVehiculo,
    string? Motivo,
    ImporteEntrada? Importe,
    Guid? ConductorId);

public sealed record ImporteEntrada(decimal? Monto, string? Moneda);

/// <summary>Respuesta 202 (esquema EventoRecibido del contrato).</summary>
public sealed record EventoRecibidoResponse(Guid EventoId, DateTimeOffset RecibidoEn);
