using M8.Notificaciones.Api.Features.Eventos;
using M8.Notificaciones.Api.Features.Eventos.Dtos;
using M8.Notificaciones.Api.Features.Notificaciones.Dominio;
using M8.Notificaciones.Api.Features.Notificaciones.Entities;

namespace M8.Notificaciones.Api.Features.Notificaciones.Servicios;

/// <summary>
/// Arma las entidades Notificacion. No guarda nada ni envía nada: es lógica pura
/// y se prueba sin base. El envío lo hace después el despachador (asíncrono).
/// </summary>
public sealed class FabricaNotificaciones
{
    private readonly OpcionesNotificaciones _opciones;

    public FabricaNotificaciones(OpcionesNotificaciones opciones) => _opciones = opciones;

    /// <summary>
    /// Una notificación por destinatario del evento (RF-8.1 / RF-8.2). Si el evento
    /// no trae email/teléfono se usa el contacto registrado de ese usuario, si existe.
    /// </summary>
    public List<Notificacion> DesdeEvento(EventoEntrada e, IReadOnlyDictionary<Guid, ContactoDestinatario> contactos, DateTimeOffset ahora)
    {
        var tipo = e.Tipo!.Value;
        var (refTipo, refId) = Referencia(e);
        var resultado = new List<Notificacion>();

        // Un mismo usuario/rol repetido en el evento recibe un solo aviso.
        foreach (var d in (e.Destinatarios ?? Array.Empty<DestinatarioEntrada>())
                     .DistinctBy(d => (d.UsuarioId, d.Rol)))
        {
            var usuarioId = d.UsuarioId!.Value;
            contactos.TryGetValue(usuarioId, out var registrado);
            var eleccion = SelectorCanal.Elegir(
                d.CanalesPreferidos, _opciones.CanalPorDefecto,
                d.Email ?? registrado?.Email,
                d.Telefono ?? registrado?.Telefono);

            var (asunto, mensaje) = PlantillasMensajes.Redactar(tipo, d.Rol!.Value, e.Detalle);
            resultado.Add(Nueva(OrigenNotificacion.EVENTO, usuarioId, d.Rol.Value, eleccion, asunto, mensaje, refTipo, refId, ahora,
                e.EventoId, tipo));
        }
        return resultado;
    }

    /// <summary>Notificación manual de un OPERADOR (POST /api/v1/notificaciones).</summary>
    public Notificacion Manual(Guid destinatarioId, RolDestinatario rol, Canal canal, string asunto, string mensaje,
        TipoReferencia? refTipo, Guid? refId, ContactoDestinatario? registrado, DateTimeOffset ahora)
    {
        var eleccion = SelectorCanal.ParaCanal(canal, registrado?.Email, registrado?.Telefono);
        return Nueva(OrigenNotificacion.MANUAL, destinatarioId, rol, eleccion, asunto, mensaje, refTipo, refId, ahora, null, null);
    }

    private Notificacion Nueva(OrigenNotificacion origen, Guid destinatarioId, RolDestinatario rol, SelectorCanal.Eleccion eleccion,
        string asunto, string mensaje, TipoReferencia? refTipo, Guid? refId, DateTimeOffset ahora, Guid? eventoId, TipoEvento? tipo)
    {
        var n = new Notificacion
        {
            Id = Guid.NewGuid(),
            EventoId = eventoId,
            TipoEvento = tipo,
            Origen = origen,
            DestinatarioId = destinatarioId,
            Rol = rol,
            Canal = eleccion.Canal,
            Contacto = eleccion.Contacto,
            Asunto = asunto,
            Mensaje = mensaje,
            ReferenciaTipo = refTipo,
            ReferenciaId = refId,
            Estado = EstadoNotificacion.PENDIENTE,
            Intentos = 0,
            MaxIntentos = _opciones.Envio.MaxIntentos,
            ProximoIntentoEn = ahora,
            CreadoEn = ahora
        };

        if (!eleccion.Utilizable)
        {
            // Sin contacto para ningún canal posible: no hay nada que intentar (contrato).
            n.Estado = EstadoNotificacion.FALLIDA;
            n.ProximoIntentoEn = null;
            n.UltimoError = $"Sin contacto registrado para el canal {eleccion.Canal}.";
        }
        return n;
    }

    /// <summary>Viaje, reserva o pago al que se refiere el aviso.</summary>
    public static (TipoReferencia?, Guid?) Referencia(EventoEntrada e)
    {
        var tipo = e.Tipo!.Value;
        if (ValidadorEvento.EsDePago(tipo)) return (TipoReferencia.PAGO, e.PagoId);
        if (ValidadorEvento.EsDeReserva(tipo)) return (TipoReferencia.RESERVA, e.ReservaId);
        return (TipoReferencia.VIAJE, e.ViajeId);
    }
}
