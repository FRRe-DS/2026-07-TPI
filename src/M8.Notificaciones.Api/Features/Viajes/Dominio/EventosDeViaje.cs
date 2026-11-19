namespace M8.Notificaciones.Api.Features.Viajes.Dominio;

// ---------------------------------------------------------------------------
// Eventos de dominio del viaje (Event Sourcing).
//
// Un evento es un HECHO QUE YA PASÓ: por eso los nombres están en participio
// pasado (Solicitado, Asignado, Finalizado) y expresan la intención del negocio,
// no un cambio técnico de columnas (no hay un "EstadoCambiado"). Microsoft
// recomienda justamente eso: eventos que cuentan QUÉ pasó y POR QUÉ.
//
// Son `record` inmutables: una vez creados no se pueden modificar. Si mañana
// algo cambia, se agrega un evento nuevo (por ejemplo, un ViajeCancelado),
// nunca se edita uno viejo.
//
// Privacidad (RNF-19): los eventos solo llevan IDENTIFICADORES (viajeId,
// conductorId), nunca email, teléfono ni nombres. Como el registro de eventos
// es append-only y no se borra, un dato personal guardado acá no se podría
// suprimir después (derecho de supresión). Los datos personales viven en el
// módulo dueño (M1) y se consultan por id cuando hacen falta.
// ---------------------------------------------------------------------------

/// <summary>Base común: todo evento pertenece a un viaje y ocurrió en un momento (UTC).</summary>
public abstract record EventoDeViaje(Guid ViajeId, DateTimeOffset OcurridoEn);

/// <summary>Un cliente pidió un viaje (lo informa M5). Siempre es el primer evento del flujo.</summary>
public sealed record ViajeSolicitado(Guid ViajeId, DateTimeOffset OcurridoEn)
    : EventoDeViaje(ViajeId, OcurridoEn);

/// <summary>Se asignó un conductor al viaje (lo informa M5). En el contrato: VIAJE_ASIGNADO.</summary>
public sealed record ConductorAsignado(Guid ViajeId, Guid ConductorId, DateTimeOffset OcurridoEn)
    : EventoDeViaje(ViajeId, OcurridoEn);

/// <summary>El viaje terminó (lo informa M6).</summary>
public sealed record ViajeFinalizado(Guid ViajeId, DateTimeOffset OcurridoEn)
    : EventoDeViaje(ViajeId, OcurridoEn);

/// <summary>
/// Estados posibles. Van en mayúsculas porque así los expone el contrato de M8
/// y así salen en el JSON, sin tablas de traducción.
/// </summary>
public enum EstadoViaje
{
    SOLICITADO,
    ASIGNADO,
    FINALIZADO
}
