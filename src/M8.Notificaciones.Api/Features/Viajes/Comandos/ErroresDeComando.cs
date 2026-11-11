namespace M8.Notificaciones.Api.Features.Viajes.Comandos;

// Errores que puede devolver el comando, además de los del dominio
// (ReglaDeNegocioException → 409) y de concurrencia (ConflictoDeConcurrenciaException → 409).
// El controlador traduce cada uno a su código HTTP con application/problem+json.

/// <summary>Falta un campo obligatorio o tiene un valor inválido → 400.</summary>
public sealed class SolicitudInvalidaException : Exception
{
    public SolicitudInvalidaException(string mensaje) : base(mensaje) { }
}

/// <summary>Se reusó un eventoId con un cuerpo distinto al original → 422 (regla de idempotencia del contrato).</summary>
public sealed class EventoIdReutilizadoException : Exception
{
    public EventoIdReutilizadoException(Guid eventoId)
        : base($"El eventoId {eventoId} ya se registró con otro contenido. Un eventoId identifica un único evento: usá uno nuevo.") { }
}

/// <summary>
/// Tipo válido en el contrato pero que esta feature todavía no modela
/// (por ejemplo, RESERVA_CREADA o VIAJE_CANCELADO) → 422.
/// </summary>
public sealed class EventoNoSoportadoException : Exception
{
    public EventoNoSoportadoException(string tipo)
        : base($"El tipo '{tipo}' no forma parte del historial de viajes. Soportados: {string.Join(", ", RegistrarEventoViajeHandler.TiposSoportados)}.") { }
}
