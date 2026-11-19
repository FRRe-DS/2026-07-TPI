namespace M8.Notificaciones.Api.Infrastructure.Mensajeria;

/// <summary>
/// Sección "RabbitMQ". Host, puerto y credenciales vienen del .env (RNF-06).
/// La topología está documentada en docs/CATALOGO_EVENTOS.md.
/// </summary>
public sealed class OpcionesRabbitMq
{
    public const string Seccion = "RabbitMQ";

    /// <summary>Si es false no se conecta (por ejemplo, en los tests unitarios).</summary>
    public bool Habilitado { get; set; } = true;

    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string User { get; set; } = "guest";
    public string Password { get; set; } = "guest";
    public string VirtualHost { get; set; } = "/";

    /// <summary>Exchange (topic) común donde M5, M6, M7 y M9 publican sus hitos.</summary>
    public string Exchange { get; set; } = "movilidad.eventos";

    /// <summary>Cola propia de M8.</summary>
    public string Cola { get; set; } = "m8.eventos";

    /// <summary>Exchange y cola donde terminan los mensajes que no se pueden procesar.</summary>
    public string ExchangeErrores { get; set; } = "m8.eventos.dlx";
    public string ColaErrores { get; set; } = "m8.eventos.dlq";

    /// <summary>Claves de enrutamiento que escucha M8. Null = viaje.#, reserva.#, pago.#, reintegro.#</summary>
    public string[]? ClavesEnrutamiento { get; set; }

    /// <summary>Cuántos mensajes sin confirmar puede tener M8 a la vez.</summary>
    public ushort Prefetch { get; set; } = 10;

    /// <summary>Reentregas máximas de un mensaje que falla por un error transitorio antes de ir a la cola de errores.</summary>
    public int LimiteEntregas { get; set; } = 5;

    public string[] Claves() => ClavesEnrutamiento is { Length: > 0 } c ? c : new[] { "viaje.#", "reserva.#", "pago.#", "reintegro.#" };
}
