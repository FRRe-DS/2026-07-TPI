using System.Globalization;
using M8.Notificaciones.Api.Features.Eventos;
using M8.Notificaciones.Api.Features.Eventos.Dtos;
using M8.Notificaciones.Api.Features.Notificaciones.Dominio;

namespace M8.Notificaciones.Api.Features.Notificaciones.Servicios;

/// <summary>
/// Redacta asunto y mensaje para cada hito (RF-8.1 viajes, RF-8.2 reservas,
/// pagos) y para cada rol. Usa solo lo que trae el evento en "detalle".
/// </summary>
public static class PlantillasMensajes
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-AR");

    public static (string Asunto, string Mensaje) Redactar(TipoEvento tipo, RolDestinatario rol, DetalleEventoEntrada? d)
    {
        var tramo = Tramo(d);
        var fecha = FechaProgramada(d);
        var motivo = string.IsNullOrWhiteSpace(d?.Motivo) ? "" : $" Motivo: {d!.Motivo}.";
        var importe = Importe(d);
        var esCliente = rol == RolDestinatario.CLIENTE;

        (string, string) r = tipo switch
        {
            TipoEvento.VIAJE_SOLICITADO => esCliente
                ? ("Recibimos tu solicitud de viaje", $"Estamos buscando un conductor para tu viaje{tramo}.")
                : ("Nueva solicitud de viaje", $"Hay una nueva solicitud de viaje{tramo}."),
            TipoEvento.VIAJE_ASIGNADO => esCliente
                ? ("Tu viaje tiene conductor", $"Un conductor aceptó tu viaje{tramo} y está en camino.")
                : ("Viaje asignado", $"Se te asignó un viaje{tramo}. Dirigite al punto de retiro."),
            TipoEvento.VIAJE_ARRIBO_CONDUCTOR => esCliente
                ? ("Tu conductor llegó", "Tu conductor está en el punto de retiro. Tené a mano el QR para iniciar el viaje.")
                : ("Arribo registrado", "Registramos tu llegada al punto de retiro."),
            TipoEvento.VIAJE_INICIADO => ("Viaje iniciado", $"El viaje{tramo} comenzó. ¡Buen viaje!"),
            TipoEvento.VIAJE_CANCELADO => ("Viaje cancelado", $"El viaje{tramo} fue cancelado.{motivo}"),
            TipoEvento.VIAJE_FINALIZADO => esCliente
                ? ("Tu viaje finalizó", $"Llegaste a destino{tramo}. Gracias por viajar con nosotros.{importe}")
                : ("Viaje finalizado", $"Finalizaste el viaje{tramo}.{importe}"),

            TipoEvento.RESERVA_CREADA => ("Reserva registrada", $"Registramos tu reserva{tramo}{fecha}."),
            TipoEvento.RESERVA_MODIFICADA => ("Reserva modificada", $"Tu reserva se modificó{tramo}{fecha}."),
            TipoEvento.RESERVA_CONFIRMADA => ("Reserva confirmada", $"Tu reserva quedó confirmada{tramo}{fecha}."),
            TipoEvento.RESERVA_RECORDATORIO => ("Recordatorio de reserva", $"Te recordamos tu viaje reservado{tramo}{fecha}."),
            TipoEvento.RESERVA_ACTIVADA => ("Tu reserva está en marcha", $"Llegó el momento de tu reserva{tramo}: estamos buscando un conductor."),
            TipoEvento.RESERVA_CANCELADA => ("Reserva cancelada", $"Tu reserva{tramo}{fecha} fue cancelada.{motivo}"),
            TipoEvento.RESERVA_VENCIDA => ("No pudimos cumplir tu reserva", $"Tu reserva{tramo}{fecha} no pudo realizarse.{motivo}"),

            TipoEvento.PAGO_CAPTURADO => ("Pago registrado", $"Registramos el pago de tu viaje.{importe} Tu comprobante va a estar disponible en breve."),
            TipoEvento.REINTEGRO_REGISTRADO => ("Reintegro registrado", $"Registramos un reintegro a tu favor.{importe}"),
            _ => ("Novedad de tu viaje", "Hay una novedad sobre tu viaje.")
        };
        return (Recortar(r.Item1, 150), Recortar(r.Item2, 1000));
    }

    private static string Tramo(DetalleEventoEntrada? d)
    {
        if (string.IsNullOrWhiteSpace(d?.Origen) || string.IsNullOrWhiteSpace(d.Destino)) return "";
        return $" de {d.Origen} a {d.Destino}";
    }

    private static string FechaProgramada(DetalleEventoEntrada? d)
    {
        if (d?.FechaProgramada is not { } fecha) return "";
        var local = fecha;
        if (d.ZonaHoraria is not null && ValidadorEvento.TryZonaHoraria(d.ZonaHoraria, out var zona) && zona is not null)
        {
            local = TimeZoneInfo.ConvertTime(fecha, zona);
        }
        return $" para el {local.ToString("dd/MM/yyyy 'a las' HH:mm", Es)}";
    }

    private static string Importe(DetalleEventoEntrada? d)
    {
        if (d?.Importe is not { Monto: { } monto, Moneda: { } moneda }) return "";
        return $" Importe: {moneda} {monto.ToString("N2", Es)}.";
    }

    private static string Recortar(string texto, int maximo) => texto.Length <= maximo ? texto : texto[..maximo];
}
