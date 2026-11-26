using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;
using M8.Notificaciones.Api.Features.Notificaciones.Dominio;

namespace M8.Notificaciones.Api.Features.Notificaciones.Envio;

/// <summary>Lo que el proveedor necesita para entregar un aviso.</summary>
public sealed record MensajeSaliente(Guid NotificacionId, Guid DestinatarioId, Canal Canal, string? Contacto, string Asunto, string Mensaje);

/// <summary>Resultado de un intento. El error nunca incluye datos personales (va a la base y a la API).</summary>
public sealed record ResultadoEnvio(bool Exito, string? Error = null, bool Simulado = false)
{
    public static readonly ResultadoEnvio Ok = new(true);
    public static ResultadoEnvio Fallo(string error, bool simulado = false) => new(false, error, simulado);
}

/// <summary>Un canal de salida (servicio externo de mensajería, real o simulado).</summary>
public interface IProveedorCanal
{
    Canal Canal { get; }
    Task<ResultadoEnvio> EnviarAsync(MensajeSaliente mensaje, CancellationToken ct);
}

/// <summary>
/// Fallas simuladas para poder mostrar reintentos y FALLIDA sin depender de un
/// proveedor real: si el contacto contiene el marcador (por defecto "falla",
/// por ejemplo cliente@falla.test) o al azar con la probabilidad configurada.
/// </summary>
public static class SimulacionFallas
{
    public static string? Evaluar(MensajeSaliente m, OpcionesSimulacion opciones)
    {
        if (!string.IsNullOrEmpty(opciones.MarcadorFallo) && m.Contacto?.Contains(opciones.MarcadorFallo, StringComparison.OrdinalIgnoreCase) == true)
        {
            return "Destinatario rechazado por el proveedor (simulado).";
        }
        if (opciones.ProbabilidadFallo > 0 && Random.Shared.NextDouble() < opciones.ProbabilidadFallo)
        {
            return "Proveedor no disponible (falla aleatoria simulada).";
        }
        return null;
    }
}

/// <summary>Proveedor simulado: no envía nada, registra el envío en el log (sin el contacto).</summary>
public sealed class ProveedorSimulado : IProveedorCanal
{
    private readonly OpcionesSimulacion _simulacion;
    private readonly ILogger<ProveedorSimulado> _logger;

    public ProveedorSimulado(Canal canal, OpcionesSimulacion simulacion, ILogger<ProveedorSimulado> logger)
    {
        Canal = canal;
        _simulacion = simulacion;
        _logger = logger;
    }

    public Canal Canal { get; }

    public Task<ResultadoEnvio> EnviarAsync(MensajeSaliente m, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var falla = SimulacionFallas.Evaluar(m, _simulacion);
        if (falla is not null)
        {
            return Task.FromResult(ResultadoEnvio.Fallo(falla));
        }
        _logger.LogInformation("[{Canal} simulado] Notificación {NotificacionId} aceptada en simulación", Canal, m.NotificacionId);
        return Task.FromResult(ResultadoEnvio.Ok);
    }
}

/// <summary>
/// EMAIL por SMTP. En docker compose apunta a Mailpit, una bandeja de prueba
/// con interfaz web (http://localhost:8025) donde se ven los correos que manda M8.
/// </summary>
public sealed class ProveedorEmailSmtp : IProveedorCanal
{
    private readonly OpcionesNotificaciones _opciones;

    public ProveedorEmailSmtp(IOptions<OpcionesNotificaciones> opciones) => _opciones = opciones.Value;

    public Canal Canal => Canal.EMAIL;

    public async Task<ResultadoEnvio> EnviarAsync(MensajeSaliente m, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(m.Contacto))
        {
            return ResultadoEnvio.Fallo("Sin email registrado.");
        }
        var falla = SimulacionFallas.Evaluar(m, _opciones.Simulacion);
        if (falla is not null)
        {
            return ResultadoEnvio.Fallo(falla, simulado: true);
        }

        var smtp = _opciones.Smtp;
        using var cliente = new SmtpClient(smtp.Host, smtp.Puerto) { EnableSsl = smtp.UsarSsl };
        if (!string.IsNullOrEmpty(smtp.Usuario))
        {
            cliente.Credentials = new NetworkCredential(smtp.Usuario, smtp.Password);
        }
        using var correo = new MailMessage(smtp.Remitente, m.Contacto, m.Asunto, m.Mensaje);
        correo.Headers.Add("X-M8-Notificacion-Id", m.NotificacionId.ToString());

        try
        {
            await cliente.SendMailAsync(correo, ct);
            return ResultadoEnvio.Ok;
        }
        catch (SmtpException ex)
        {
            // Solo el código SMTP: el mensaje de la excepción puede incluir la dirección.
            return ResultadoEnvio.Fallo($"El servidor de correo rechazó el envío (SMTP {(int)ex.StatusCode}).");
        }
        catch (Exception ex) when (ex is System.Net.Sockets.SocketException or IOException or InvalidOperationException)
        {
            return ResultadoEnvio.Fallo("No se pudo conectar con el servidor de correo.");
        }
    }
}
