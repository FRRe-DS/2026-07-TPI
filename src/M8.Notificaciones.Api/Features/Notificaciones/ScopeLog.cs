using M8.Notificaciones.Api.Features.Eventos.Dtos;
using M8.Notificaciones.Api.Features.Notificaciones.Entities;

namespace M8.Notificaciones.Api.Features.Notificaciones;

/// <summary>
/// Datos de correlación para los scopes de logging (RNF-14): permiten filtrar
/// los logs por evento, notificación, viaje, reserva o pago. Solo identificadores:
/// nunca contactos ni textos de los avisos (RNF-19).
/// </summary>
public static class ScopeLog
{
    public static Dictionary<string, object> DeEvento(EventoEntrada e)
    {
        var scope = new Dictionary<string, object>();
        if (e.EventoId is { } eventoId) scope["EventoId"] = eventoId;
        if (e.ViajeId is { } viajeId) scope["ViajeId"] = viajeId;
        if (e.ReservaId is { } reservaId) scope["ReservaId"] = reservaId;
        if (e.PagoId is { } pagoId) scope["PagoId"] = pagoId;
        return scope;
    }

    /// <summary>"ViajeId", "ReservaId" o "PagoId" según la referencia del aviso.</summary>
    public static Dictionary<string, object> DeNotificacion(Notificacion n)
    {
        var scope = new Dictionary<string, object> { ["NotificacionId"] = n.Id };
        if (n.EventoId is { } eventoId) scope["EventoId"] = eventoId;
        if (n.ReferenciaTipo is { } tipo && n.ReferenciaId is { } id)
        {
            var clave = tipo switch
            {
                Dominio.TipoReferencia.VIAJE => "ViajeId",
                Dominio.TipoReferencia.RESERVA => "ReservaId",
                _ => "PagoId"
            };
            scope[clave] = id;
        }
        return scope;
    }
}
