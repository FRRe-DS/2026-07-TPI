using M8.Notificaciones.Api.Features.Eventos.Dtos;
using M8.Notificaciones.Api.Features.Notificaciones.Dominio;

namespace M8.Notificaciones.Tests.Notificaciones.Apoyo;

/// <summary>Eventos de ejemplo con la forma del contrato.</summary>
internal static class Eventos
{
    public static readonly DateTimeOffset Momento = new(2026, 11, 18, 15, 0, 0, TimeSpan.Zero);

    public static DestinatarioEntrada Cliente(Guid? id = null, string? email = "cliente@ejemplo.com", string? telefono = null,
        params Canal[] canales) =>
        new(id ?? Guid.NewGuid(), RolDestinatario.CLIENTE, email, telefono, canales.Length == 0 ? null : canales);

    public static DestinatarioEntrada Conductor(Guid? id = null, string? email = null, string? telefono = "+5493624000000",
        params Canal[] canales) =>
        new(id ?? Guid.NewGuid(), RolDestinatario.CONDUCTOR, email, telefono, canales.Length == 0 ? null : canales);

    public static EventoEntrada Viaje(TipoEvento tipo = TipoEvento.VIAJE_ASIGNADO, Guid? eventoId = null, Guid? viajeId = null,
        params DestinatarioEntrada[] destinatarios) =>
        new(eventoId ?? Guid.NewGuid(), tipo, tipo is TipoEvento.VIAJE_SOLICITADO or TipoEvento.VIAJE_ASIGNADO ? ModuloOrigen.M5 : ModuloOrigen.M6,
            Momento, viajeId ?? Guid.NewGuid(), null, null,
            destinatarios.Length == 0 ? new[] { Cliente() } : destinatarios,
            new DetalleEventoEntrada("Av. Sarmiento 100", "Plaza 25 de Mayo", null, null, TipoVehiculo.AUTO, null, null, null));

    public static EventoEntrada Reserva(TipoEvento tipo = TipoEvento.RESERVA_CONFIRMADA, params DestinatarioEntrada[] destinatarios) =>
        new(Guid.NewGuid(), tipo, ModuloOrigen.M9, Momento, null, Guid.NewGuid(), null,
            destinatarios.Length == 0 ? new[] { Cliente() } : destinatarios,
            new DetalleEventoEntrada("Casa", "Aeropuerto", new DateTimeOffset(2026, 12, 1, 9, 30, 0, TimeSpan.Zero),
                "America/Argentina/Buenos_Aires", TipoVehiculo.AUTO, null, null, null));

    public static EventoEntrada Pago(params DestinatarioEntrada[] destinatarios) =>
        new(Guid.NewGuid(), TipoEvento.PAGO_CAPTURADO, ModuloOrigen.M7, Momento, Guid.NewGuid(), null, Guid.NewGuid(),
            destinatarios.Length == 0 ? new[] { Cliente() } : destinatarios,
            new DetalleEventoEntrada(null, null, null, null, null, null, new ImporteEntrada(1534.5m, "ARS"), null));
}
