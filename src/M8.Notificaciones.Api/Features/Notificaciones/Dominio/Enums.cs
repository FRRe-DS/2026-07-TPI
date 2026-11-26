using System.Text.Json.Serialization;

namespace M8.Notificaciones.Api.Features.Notificaciones.Dominio;

// Los nombres de los valores son EXACTAMENTE los del contrato de M8 (por eso van
// en mayúsculas). Se serializan como texto y no se aceptan números.

/// <summary>Convertidor de enums del contrato: texto, sin aceptar enteros.</summary>
public sealed class EnumContrato<T> : JsonStringEnumConverter<T> where T : struct, Enum
{
    public EnumContrato() : base(namingPolicy: null, allowIntegerValues: false) { }
}

[JsonConverter(typeof(EnumContrato<Canal>))]
public enum Canal { EMAIL, SMS, PUSH }

[JsonConverter(typeof(EnumContrato<RolDestinatario>))]
public enum RolDestinatario { CLIENTE, CONDUCTOR }

/// <summary>
/// PENDIENTE: todavía tiene intentos por hacer. ENVIADA: algún intento fue
/// exitoso. FALLIDA: se agotaron los intentos automáticos.
/// </summary>
[JsonConverter(typeof(EnumContrato<EstadoNotificacion>))]
public enum EstadoNotificacion { PENDIENTE, ENVIADA, FALLIDA }

[JsonConverter(typeof(EnumContrato<TipoReferencia>))]
public enum TipoReferencia { VIAJE, RESERVA, PAGO }

[JsonConverter(typeof(EnumContrato<ResultadoIntento>))]
public enum ResultadoIntento { EXITO, ERROR }

[JsonConverter(typeof(EnumContrato<ModuloOrigen>))]
public enum ModuloOrigen { M5, M6, M7, M9 }

[JsonConverter(typeof(EnumContrato<TipoVehiculo>))]
public enum TipoVehiculo { AUTO, MOTO }

/// <summary>Hitos que M8 sabe procesar (TipoEvento del contrato).</summary>
[JsonConverter(typeof(EnumContrato<TipoEvento>))]
public enum TipoEvento
{
    VIAJE_SOLICITADO,
    VIAJE_ASIGNADO,
    VIAJE_ARRIBO_CONDUCTOR,
    VIAJE_INICIADO,
    VIAJE_CANCELADO,
    VIAJE_FINALIZADO,
    RESERVA_CREADA,
    RESERVA_MODIFICADA,
    RESERVA_CONFIRMADA,
    RESERVA_RECORDATORIO,
    RESERVA_ACTIVADA,
    RESERVA_CANCELADA,
    RESERVA_VENCIDA,
    PAGO_CAPTURADO,
    REINTEGRO_REGISTRADO
}

/// <summary>De dónde salió la notificación (dato interno, no está en el contrato).</summary>
public enum OrigenNotificacion { EVENTO, MANUAL, REENVIO, SOPORTE }
