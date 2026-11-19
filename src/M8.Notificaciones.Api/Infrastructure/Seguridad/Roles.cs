namespace M8.Notificaciones.Api.Infrastructure.Seguridad;

/// <summary>Roles del contrato de M8. Cada operación declara cuáles admite.</summary>
public static class Roles
{
    public const string Cliente = "CLIENTE";
    public const string Conductor = "CONDUCTOR";
    public const string Operador = "OPERADOR";
    public const string Servicio = "SERVICIO";

    public static readonly IReadOnlyList<string> Todos = new[] { Cliente, Conductor, Operador, Servicio };

    // Combinaciones usadas en [Authorize(Roles = ...)]: ASP.NET las separa por coma.
    public const string OperadorOServicio = Operador + "," + Servicio;
}
