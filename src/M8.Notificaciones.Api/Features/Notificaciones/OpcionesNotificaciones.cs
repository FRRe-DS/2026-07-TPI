using M8.Notificaciones.Api.Features.Notificaciones.Dominio;

namespace M8.Notificaciones.Api.Features.Notificaciones;

/// <summary>Sección "Notificaciones" de la configuración. Todo con valores por defecto razonables.</summary>
public sealed class OpcionesNotificaciones
{
    public const string Seccion = "Notificaciones";

    /// <summary>Canal que se usa cuando el destinatario no indica canales preferidos.</summary>
    public Canal CanalPorDefecto { get; set; } = Canal.EMAIL;

    public OpcionesEnvio Envio { get; set; } = new();
    public OpcionesSmtp Smtp { get; set; } = new();
    public OpcionesSimulacion Simulacion { get; set; } = new();
}

/// <summary>Política de entrega (contrato: "como máximo 3 intentos automáticos con espera creciente").</summary>
public sealed class OpcionesEnvio
{
    /// <summary>Si es false, el despachador en segundo plano no arranca (los tests lo controlan a mano).</summary>
    public bool Habilitado { get; set; } = true;

    public int MaxIntentos { get; set; } = 3;

    /// <summary>
    /// Espera antes del intento 2, 3, ... (creciente). Si hay menos valores que
    /// intentos, se repite el último. Null = [5, 30].
    /// </summary>
    public int[]? EsperasSegundos { get; set; }

    /// <summary>Tiempo máximo de cada intento contra el proveedor (RNF-13: nunca esperar indefinidamente).</summary>
    public int TimeoutSegundos { get; set; } = 10;

    public int IntervaloSondeoMs { get; set; } = 1000;
    public int TamanoLote { get; set; } = 20;

    public TimeSpan EsperaAntesDelIntento(int numeroIntento)
    {
        var esperas = EsperasSegundos is { Length: > 0 } e ? e : new[] { 5, 30 };
        var indice = Math.Clamp(numeroIntento - 2, 0, esperas.Length - 1);
        return TimeSpan.FromSeconds(esperas[indice]);
    }

    /// <summary>Cuánto dura el "lease" de una notificación mientras se envía.</summary>
    public TimeSpan DuracionBloqueo => TimeSpan.FromSeconds(TimeoutSegundos + 30);
}

/// <summary>Servidor SMTP para el canal EMAIL. En el compose apunta a Mailpit (bandeja de prueba).</summary>
public sealed class OpcionesSmtp
{
    public bool Habilitado { get; set; }
    public string Host { get; set; } = "localhost";
    public int Puerto { get; set; } = 1025;
    public bool UsarSsl { get; set; }
    public string? Usuario { get; set; }
    public string? Password { get; set; }
    public string Remitente { get; set; } = "no-responder@m8.movilidad.local";
}

/// <summary>
/// Proveedores simulados (SMS, PUSH y EMAIL si no hay SMTP). La consigna admite
/// servicios externos "reales o simulados según la etapa".
/// </summary>
public sealed class OpcionesSimulacion
{
    /// <summary>Si el contacto contiene este texto, el envío falla (para mostrar reintentos y FALLIDA). Ej.: cliente@falla.test</summary>
    public string? MarcadorFallo { get; set; } = "falla";

    /// <summary>Probabilidad (0 a 1) de que un envío simulado falle al azar.</summary>
    public double ProbabilidadFallo { get; set; }
}
