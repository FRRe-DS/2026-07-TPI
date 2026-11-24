using M8.Notificaciones.Api.Features.Viajes.Dominio;
using M8.Notificaciones.Api.Features.Viajes.Dtos;
using M8.Notificaciones.Api.Features.Viajes.EventStore;

namespace M8.Notificaciones.Api.Features.Viajes.Comandos;

/// <summary>
/// Handler del comando "registrar un hito de viaje" (lado de ESCRITURA de CQRS).
///
/// Es una clase común que el controlador llama directo. No usamos MediatR ni
/// otra librería: el flujo completo se lee de arriba a abajo en ManejarAsync.
///
/// Pasos:
///  1. Validar el cuerpo y traducir el tipo del contrato al evento de dominio.
///  2. Idempotencia: si el eventoId ya existe, devolver la respuesta original
///     (mismo cuerpo) o 422 (otro cuerpo).
///  3. Rehidratar el viaje leyendo su flujo de eventos.
///  4. Pedirle al agregado que decida (puede lanzar ReglaDeNegocioException → 409).
///  5. Agregar el evento nuevo al store con Version = anterior + 1
///     (si otro comando ganó la carrera → 409).
///
/// Lo que NO hace: no toca la vista de lectura. Eso lo hace después, de forma
/// asíncrona, el proyector. Por eso respondemos 202 (Accepted) y no 200.
/// </summary>
public sealed class RegistrarEventoViajeHandler
{
    // Tipos del contrato de M8 que esta feature sabe registrar.
    public const string ViajeSolicitadoContrato = "VIAJE_SOLICITADO";
    public const string ViajeAsignadoContrato = "VIAJE_ASIGNADO";
    public const string ViajeFinalizadoContrato = "VIAJE_FINALIZADO";

    public static readonly IReadOnlyList<string> TiposSoportados =
        new[] { ViajeSolicitadoContrato, ViajeAsignadoContrato, ViajeFinalizadoContrato };

    // Valores del enum ModuloOrigen del contrato.
    private static readonly string[] OrigenesValidos = { "M5", "M6", "M7", "M9" };

    private readonly EventStoreViajes _store;
    private readonly TimeProvider _reloj;

    public RegistrarEventoViajeHandler(EventStoreViajes store, TimeProvider reloj)
    {
        _store = store;
        _reloj = reloj;
    }

    public async Task<EventoRegistradoResponse> ManejarAsync(EventoEntranteRequest request, CancellationToken ct = default)
    {
        // 1. Validación: todo junto acá, en un solo lugar.
        var comando = Validar(request);

        // 2. Idempotencia por eventoId (regla del contrato).
        var existente = await _store.BuscarPorEventoIdAsync(comando.EventoId, ct);
        if (existente is not null)
        {
            if (TieneElMismoContenido(existente, comando))
            {
                // Reintento del emisor (por ejemplo, se le cortó la conexión): devolvemos
                // la MISMA respuesta y no duplicamos el evento.
                return CrearRespuesta(existente);
            }
            throw new EventoIdReutilizadoException(comando.EventoId);
        }

        // 3. Rehidratar: el estado actual sale de reproducir el flujo, no de una tabla.
        var flujo = await _store.LeerFlujoAsync(comando.ViajeId, ct);
        var viaje = Viaje.Rehidratar(flujo.Select(SerializadorEventos.Deserializar));

        // 4. Decidir: el agregado valida la transición y devuelve el evento nuevo.
        EventoDeViaje nuevo = comando.Tipo switch
        {
            ViajeSolicitadoContrato => viaje.Solicitar(comando.ViajeId, comando.OcurridoEn),
            ViajeAsignadoContrato => viaje.AsignarConductor(comando.ConductorId!.Value, comando.OcurridoEn),
            _ => viaje.Finalizar(comando.OcurridoEn) // ViajeFinalizadoContrato (ya validado en el paso 1)
        };

        // 5. Agregar (append) al final del flujo. viaje.Version ya incluye el evento nuevo.
        var fila = new EventoAlmacenado
        {
            EventoId = comando.EventoId,
            ViajeId = comando.ViajeId,
            Version = viaje.Version,
            Tipo = SerializadorEventos.NombreDe(nuevo),
            Origen = comando.Origen,
            Payload = SerializadorEventos.Serializar(nuevo),
            OcurridoEn = comando.OcurridoEn,
            RegistradoEn = FechasUtc.Normalizar(_reloj.GetUtcNow())
        };
        await _store.AgregarAsync(fila, ct);

        return CrearRespuesta(fila);
    }

    /// <summary>Comando ya validado: sin nulos y con fechas en UTC.</summary>
    private sealed record ComandoRegistrar(
        Guid EventoId, string Tipo, string Origen, DateTimeOffset OcurridoEn, Guid ViajeId, Guid? ConductorId);

    private static ComandoRegistrar Validar(EventoEntranteRequest r)
    {
        if (r.EventoId is null || r.EventoId == Guid.Empty)
            throw new SolicitudInvalidaException("Falta 'eventoId' (UUID que genera quien emite el evento).");
        if (string.IsNullOrWhiteSpace(r.Tipo))
            throw new SolicitudInvalidaException("Falta 'tipo'.");
        if (string.IsNullOrWhiteSpace(r.Origen) || !OrigenesValidos.Contains(r.Origen))
            throw new SolicitudInvalidaException($"'origen' debe ser uno de: {string.Join(", ", OrigenesValidos)}.");
        if (r.OcurridoEn is null)
            throw new SolicitudInvalidaException("Falta 'ocurridoEn' (fecha y hora ISO 8601).");

        // El tipo es válido en el contrato pero quizás no es un hito que modelamos.
        if (!TiposSoportados.Contains(r.Tipo))
            throw new EventoNoSoportadoException(r.Tipo);

        if (r.ViajeId is null || r.ViajeId == Guid.Empty)
            throw new SolicitudInvalidaException("Los eventos VIAJE_* requieren 'viajeId'.");

        var conductorId = r.Detalle?.ConductorId;
        if (r.Tipo == ViajeAsignadoContrato && (conductorId is null || conductorId == Guid.Empty))
            throw new SolicitudInvalidaException("VIAJE_ASIGNADO requiere 'detalle.conductorId'.");

        return new ComandoRegistrar(
            r.EventoId.Value,
            r.Tipo,
            r.Origen,
            FechasUtc.Normalizar(r.OcurridoEn.Value),
            r.ViajeId.Value,
            r.Tipo == ViajeAsignadoContrato ? conductorId : null);
    }

    /// <summary>
    /// ¿El evento guardado tiene el mismo contenido que el que llega?
    /// Se comparan los campos que forman el evento (no "destinatarios", que no
    /// se guarda por privacidad).
    /// </summary>
    private static bool TieneElMismoContenido(EventoAlmacenado guardado, ComandoRegistrar comando)
    {
        var eventoGuardado = SerializadorEventos.Deserializar(guardado);
        var conductorGuardado = (eventoGuardado as ConductorAsignado)?.ConductorId;
        var tipoEsperado = comando.Tipo switch
        {
            ViajeSolicitadoContrato => nameof(ViajeSolicitado),
            ViajeAsignadoContrato => nameof(ConductorAsignado),
            _ => nameof(ViajeFinalizado)
        };

        return guardado.Tipo == tipoEsperado
            && guardado.ViajeId == comando.ViajeId
            && guardado.Origen == comando.Origen
            && guardado.OcurridoEn == comando.OcurridoEn
            && conductorGuardado == comando.ConductorId;
    }

    private static EventoRegistradoResponse CrearRespuesta(EventoAlmacenado fila) =>
        new(fila.EventoId, fila.ViajeId, fila.Version, RecibidoEn: fila.RegistradoEn, RegistradoEn: fila.RegistradoEn);
}
