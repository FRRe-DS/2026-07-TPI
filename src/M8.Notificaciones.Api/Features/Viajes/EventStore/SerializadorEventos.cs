using System.Text.Json;
using M8.Notificaciones.Api.Features.Viajes.Dominio;

namespace M8.Notificaciones.Api.Features.Viajes.EventStore;

/// <summary>
/// Traduce eventos de dominio a (Tipo, Payload JSON) y al revés.
///
/// Por qué un switch explícito y no reflexión ni una librería: cada tipo de
/// evento que el sistema sabe leer queda escrito acá, a la vista. Es también
/// el lugar donde, si un evento cambiara de forma (versionado), se agregaría
/// un "upcaster": leer el JSON viejo y convertirlo al evento nuevo, sin tocar
/// nunca las filas ya guardadas.
/// </summary>
public static class SerializadorEventos
{
    // camelCase para que el payload se lea igual que el resto de la API.
    private static readonly JsonSerializerOptions Opciones = new(JsonSerializerDefaults.Web);

    public static string NombreDe(EventoDeViaje evento) => evento switch
    {
        ViajeSolicitado => nameof(ViajeSolicitado),
        ConductorAsignado => nameof(ConductorAsignado),
        ViajeFinalizado => nameof(ViajeFinalizado),
        _ => throw new InvalidOperationException($"Evento sin nombre registrado: {evento.GetType().Name}")
    };

    public static string Serializar(EventoDeViaje evento) =>
        JsonSerializer.Serialize(evento, evento.GetType(), Opciones);

    public static EventoDeViaje Deserializar(EventoAlmacenado fila) => fila.Tipo switch
    {
        nameof(ViajeSolicitado) => Leer<ViajeSolicitado>(fila),
        nameof(ConductorAsignado) => Leer<ConductorAsignado>(fila),
        nameof(ViajeFinalizado) => Leer<ViajeFinalizado>(fila),
        _ => throw new InvalidOperationException($"Tipo de evento desconocido en el store: {fila.Tipo} (Seq {fila.Seq})")
    };

    private static T Leer<T>(EventoAlmacenado fila) where T : EventoDeViaje =>
        JsonSerializer.Deserialize<T>(fila.Payload, Opciones)
        ?? throw new InvalidOperationException($"Payload vacío en el evento Seq {fila.Seq}");
}
