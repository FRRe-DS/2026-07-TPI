using M8.Notificaciones.Api.Features.Viajes.Dominio;
using M8.Notificaciones.Api.Features.Viajes.EventStore;

namespace M8.Notificaciones.Tests.Viajes;

public class SerializadorEventosTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 11, 13, 0, 0, TimeSpan.Zero);

    public static IEnumerable<object[]> Eventos()
    {
        yield return new object[] { new ViajeSolicitado(Guid.NewGuid(), T0) };
        yield return new object[] { new ConductorAsignado(Guid.NewGuid(), Guid.NewGuid(), T0) };
        yield return new object[] { new ViajeFinalizado(Guid.NewGuid(), T0) };
    }

    [Theory]
    [MemberData(nameof(Eventos))]
    public void Serializar_y_deserializar_devuelve_el_mismo_evento(EventoDeViaje evento)
    {
        var fila = new EventoAlmacenado
        {
            Tipo = SerializadorEventos.NombreDe(evento),
            Payload = SerializadorEventos.Serializar(evento)
        };

        Assert.Equal(evento, SerializadorEventos.Deserializar(fila));
        Assert.Contains("\"viajeId\"", fila.Payload); // camelCase, como el resto de la API
    }

    [Fact]
    public void Un_tipo_desconocido_en_el_store_falla_con_un_mensaje_claro()
    {
        var fila = new EventoAlmacenado { Seq = 7, Tipo = "ViajeTeletransportado", Payload = "{}" };

        var error = Assert.Throws<InvalidOperationException>(() => SerializadorEventos.Deserializar(fila));
        Assert.Contains("ViajeTeletransportado", error.Message);
    }

    [Fact]
    public void Un_payload_null_falla_con_un_mensaje_claro()
    {
        var fila = new EventoAlmacenado { Seq = 8, Tipo = nameof(ViajeSolicitado), Payload = "null" };

        Assert.Throws<InvalidOperationException>(() => SerializadorEventos.Deserializar(fila));
    }

    private sealed record EventoSinNombre(Guid ViajeId, DateTimeOffset OcurridoEn) : EventoDeViaje(ViajeId, OcurridoEn);

    [Fact]
    public void Un_evento_sin_nombre_registrado_no_se_puede_guardar()
    {
        Assert.Throws<InvalidOperationException>(() => SerializadorEventos.NombreDe(new EventoSinNombre(Guid.NewGuid(), T0)));
    }

    [Fact]
    public void Normalizar_fechas_pasa_a_UTC_y_trunca_a_microsegundos()
    {
        // 10:00:00.1234567 -03:00 → 13:00:00.123456 Z (PostgreSQL guarda microsegundos).
        var conTicks = new DateTimeOffset(2026, 11, 11, 10, 0, 0, TimeSpan.FromHours(-3)).AddTicks(1_234_567);

        var normalizada = FechasUtc.Normalizar(conTicks);

        Assert.Equal(TimeSpan.Zero, normalizada.Offset);
        Assert.Equal(new DateTimeOffset(2026, 11, 11, 13, 0, 0, TimeSpan.Zero).AddTicks(1_234_560), normalizada);
    }
}
