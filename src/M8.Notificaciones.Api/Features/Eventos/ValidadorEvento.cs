using System.Net.Mail;
using System.Text.RegularExpressions;
using M8.Notificaciones.Api.Features.Eventos.Dtos;
using M8.Notificaciones.Api.Features.Notificaciones.Dominio;

namespace M8.Notificaciones.Api.Features.Eventos;

/// <summary>
/// Reglas del esquema EventoEntrada del contrato que el deserializador no
/// controla solo: obligatorios, largos, formatos y qué identificador exige cada tipo.
/// Devuelve la lista de problemas (vacía si el evento es válido).
/// </summary>
public static partial class ValidadorEvento
{
    public const int MaxDestinatarios = 5;

    public static IReadOnlyList<string> Validar(EventoEntrada? e)
    {
        var errores = new List<string>();
        if (e is null)
        {
            errores.Add("Falta el cuerpo del evento.");
            return errores;
        }

        if (e.EventoId is null || e.EventoId == Guid.Empty) errores.Add("Falta 'eventoId' (UUID que genera quien emite el evento).");
        if (e.Tipo is null) errores.Add("Falta 'tipo'.");
        if (e.Origen is null) errores.Add("Falta 'origen' (M5, M6, M7 o M9).");
        if (e.OcurridoEn is null) errores.Add("Falta 'ocurridoEn' (fecha y hora ISO 8601).");

        if (Vacio(e.ViajeId) && Vacio(e.ReservaId) && Vacio(e.PagoId))
            errores.Add("El evento debe traer al menos uno de 'viajeId', 'reservaId' o 'pagoId'.");

        if (e.Tipo is { } tipo)
        {
            if (EsDeViaje(tipo) && Vacio(e.ViajeId))
                errores.Add($"{tipo} requiere 'viajeId'.");
            if (EsDeReserva(tipo) && Vacio(e.ReservaId))
                errores.Add($"{tipo} requiere 'reservaId'.");
            if (EsDePago(tipo) && (Vacio(e.PagoId) || Vacio(e.ViajeId)))
                errores.Add($"{tipo} requiere 'pagoId' y 'viajeId'.");
        }

        // "destinatarios" es obligatorio en el contrato, pero lo aceptamos ausente
        // (decisión ya tomada por el grupo para el historial de viajes, DECISIONES 2.1):
        // sin destinatarios el evento se registra y no genera avisos.
        if (e.Destinatarios is { } destinatarios)
        {
            if (destinatarios.Count == 0 || destinatarios.Count > MaxDestinatarios)
                errores.Add($"'destinatarios' debe tener entre 1 y {MaxDestinatarios} elementos.");

            for (var i = 0; i < destinatarios.Count; i++)
            {
                ValidarDestinatario(destinatarios[i], $"destinatarios[{i}]", errores);
            }
        }

        if (e.Detalle is { } d)
        {
            Largo(d.Origen, 255, "detalle.origen", errores);
            Largo(d.Destino, 255, "detalle.destino", errores);
            Largo(d.Motivo, 500, "detalle.motivo", errores);
            if (d.ZonaHoraria is not null && !TryZonaHoraria(d.ZonaHoraria, out _))
                errores.Add("'detalle.zonaHoraria' no es una zona horaria IANA válida.");
            if (d.Importe is { } imp)
            {
                if (imp.Monto is null || imp.Monto < 0) errores.Add("'detalle.importe.monto' es obligatorio y no puede ser negativo.");
                if (imp.Moneda is null || !Moneda().IsMatch(imp.Moneda)) errores.Add("'detalle.importe.moneda' debe ser un código ISO 4217 (por ejemplo ARS).");
            }
        }

        return errores;
    }

    private static void ValidarDestinatario(DestinatarioEntrada? d, string ruta, List<string> errores)
    {
        if (d is null)
        {
            errores.Add($"'{ruta}' no puede ser null.");
            return;
        }
        if (Vacio(d.UsuarioId)) errores.Add($"Falta '{ruta}.usuarioId'.");
        if (d.Rol is null) errores.Add($"Falta '{ruta}.rol' (CLIENTE o CONDUCTOR).");
        if (d.Email is not null && !EsEmail(d.Email)) errores.Add($"'{ruta}.email' no tiene formato de email.");
        if (d.Telefono is not null && !TelefonoE164().IsMatch(d.Telefono)) errores.Add($"'{ruta}.telefono' debe estar en formato E.164 (por ejemplo +5493624000000).");
        if (d.CanalesPreferidos is { } canales && canales.Distinct().Count() != canales.Count)
            errores.Add($"'{ruta}.canalesPreferidos' no puede repetir canales.");
    }

    public static bool EsDeViaje(TipoEvento t) => t.ToString().StartsWith("VIAJE_", StringComparison.Ordinal);
    public static bool EsDeReserva(TipoEvento t) => t.ToString().StartsWith("RESERVA_", StringComparison.Ordinal);
    public static bool EsDePago(TipoEvento t) => t is TipoEvento.PAGO_CAPTURADO or TipoEvento.REINTEGRO_REGISTRADO;

    public static bool TryZonaHoraria(string id, out TimeZoneInfo? zona)
    {
        try
        {
            zona = TimeZoneInfo.FindSystemTimeZoneById(id);
            return true;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            zona = null;
            return false;
        }
    }

    private static bool Vacio(Guid? id) => id is null || id == Guid.Empty;

    private static void Largo(string? valor, int maximo, string campo, List<string> errores)
    {
        if (valor is not null && valor.Length > maximo) errores.Add($"'{campo}' supera los {maximo} caracteres.");
    }

    private static bool EsEmail(string valor) =>
        valor.Length <= 254 && MailAddress.TryCreate(valor, out var direccion) && direccion.Address == valor;

    [GeneratedRegex(@"^\+[1-9][0-9]{6,14}$")]
    private static partial Regex TelefonoE164();

    [GeneratedRegex("^[A-Z]{3}$")]
    private static partial Regex Moneda();
}
