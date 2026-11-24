using System.Text.Json;

namespace M8.Notificaciones.Api.Features.Viajes.Dtos;

// ============================ Escritura (comando) ============================

/// <summary>
/// Cuerpo de POST /api/v1/eventos, compatible con EventoEntrada del contrato de M8.
///
/// Todas las propiedades son anulables a propósito: la validación la hace el
/// handler en un único lugar (y así se prueba igual desde los tests que desde HTTP).
///
/// Diferencias con el contrato (ver docs/DECISIONES.md):
///  - "destinatarios" es opcional en esta feature y NO se lee ni se guarda:
///    trae email y teléfono, y los eventos no pueden tener datos personales.
///    Como no está declarada acá, el deserializador simplemente la ignora.
///  - "detalle.conductorId" es una extensión nuestra: el contrato no dice qué
///    conductor se asignó, y sin ese dato el evento ConductorAsignado no tiene sentido.
/// </summary>
public sealed record EventoEntranteRequest(
    Guid? EventoId,
    string? Tipo,
    string? Origen,
    DateTimeOffset? OcurridoEn,
    Guid? ViajeId,
    DetalleEventoRequest? Detalle);

/// <summary>Solo leemos conductorId; el resto de DetalleEvento (origen, destino, importe...) se ignora.</summary>
public sealed record DetalleEventoRequest(Guid? ConductorId);

/// <summary>
/// Respuesta 202. Incluye "recibidoEn" (lo exige EventoRecibido del contrato) y
/// los datos propios de Event Sourcing: en qué posición del flujo quedó (version).
/// </summary>
public sealed record EventoRegistradoResponse(
    Guid EventoId,
    Guid ViajeId,
    int Version,
    DateTimeOffset RecibidoEn,
    DateTimeOffset RegistradoEn);

// ===================== Lectura del lado Event Sourcing =====================

/// <summary>Estado reconstruido reproduciendo los eventos (GET .../estado).</summary>
public sealed record EstadoViajeResponse(
    Guid ViajeId,
    string Estado,
    Guid? ConductorId,
    int Version,
    int EventosReproducidos,
    HitosViajeResponse Hitos);

public sealed record HitosViajeResponse(
    DateTimeOffset? SolicitadoEn,
    DateTimeOffset? AsignadoEn,
    DateTimeOffset? FinalizadoEn);

/// <summary>El flujo crudo del event store (GET .../eventos).</summary>
public sealed record FlujoEventosResponse(Guid ViajeId, int Cantidad, IReadOnlyList<EventoDelFlujoResponse> Eventos);

public sealed record EventoDelFlujoResponse(
    long Seq,
    Guid EventoId,
    int Version,
    string Tipo,
    string Origen,
    DateTimeOffset OcurridoEn,
    DateTimeOffset RegistradoEn,
    JsonElement Payload);

// ========================= Lectura del lado CQRS =========================

/// <summary>Historial leído SOLO de la vista proyectada (GET .../historial).</summary>
public sealed record HistorialViajeResponse(
    Guid ViajeId,
    IReadOnlyList<ItemHistorialResponse> Items,
    ResumenHistorialResponse Resumen,
    DateTimeOffset ConsultadoEn);

public sealed record ItemHistorialResponse(
    int Version,
    string Evento,
    string Descripcion,
    DateTimeOffset OcurridoEn,
    DateTimeOffset RegistradoEn,
    DateTimeOffset ProyectadoEn,
    long RetrasoMs);

/// <summary>Estado y conductor según la vista. Ambos null si todavía no se proyectó nada.</summary>
public sealed record ResumenHistorialResponse(string? Estado, Guid? ConductorId);

// ========================= Laboratorio (diagnóstico) =========================

public sealed record EstadoProyeccionResponse(
    int EventosEnStore,
    int EventosProyectados,
    int Pendientes,
    int RetrasoConfiguradoMs,
    DateTimeOffset? UltimaProyeccionEn);

public sealed record ReconstruccionResponse(int EventosReproducidos, DateTimeOffset ReconstruidoEn);
