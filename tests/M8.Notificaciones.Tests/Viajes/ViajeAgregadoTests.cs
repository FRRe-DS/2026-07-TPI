using M8.Notificaciones.Api.Features.Viajes.Dominio;

namespace M8.Notificaciones.Tests.Viajes;

/// <summary>
/// Tests del agregado con el formato "dado / cuando / entonces":
///  - DADO estos eventos previos (la historia del viaje),
///  - CUANDO llega este comando,
///  - ENTONCES se produce este evento (o este error).
/// No hay base de datos ni HTTP: el agregado es C# puro.
/// </summary>
public class ViajeAgregadoTests
{
    private static readonly Guid ViajeId = Guid.NewGuid();
    private static readonly Guid ConductorId = Guid.NewGuid();
    private static readonly DateTimeOffset T0 = new(2026, 11, 11, 13, 0, 0, TimeSpan.Zero);

    private static Viaje Dado(params EventoDeViaje[] eventos) => Viaje.Rehidratar(eventos);

    [Fact]
    public void Dado_un_flujo_vacio_Cuando_se_solicita_Entonces_genera_ViajeSolicitado()
    {
        var viaje = Dado();

        var evento = viaje.Solicitar(ViajeId, T0);

        Assert.Equal(new ViajeSolicitado(ViajeId, T0), evento);
        Assert.Equal(EstadoViaje.SOLICITADO, viaje.Estado);
        Assert.Equal(1, viaje.Version);
    }

    [Fact]
    public void Dado_un_flujo_vacio_Cuando_se_asigna_conductor_Entonces_falla()
    {
        var viaje = Dado();

        var error = Assert.Throws<ReglaDeNegocioException>(() => viaje.AsignarConductor(ConductorId, T0));

        Assert.Contains("no existe", error.Message);
        Assert.Equal("Transición de viaje inválida", error.Titulo);
    }

    [Fact]
    public void Dado_un_viaje_solicitado_Cuando_se_finaliza_sin_asignar_Entonces_falla()
    {
        var viaje = Dado(new ViajeSolicitado(ViajeId, T0));

        var error = Assert.Throws<ReglaDeNegocioException>(() => viaje.Finalizar(T0.AddMinutes(10)));

        Assert.Contains("está SOLICITADO y se requiere ASIGNADO", error.Message);
        Assert.Equal(1, viaje.Version); // el intento fallido no cambia nada
    }

    [Fact]
    public void Dado_un_viaje_asignado_Cuando_se_asigna_otra_vez_Entonces_falla()
    {
        var viaje = Dado(
            new ViajeSolicitado(ViajeId, T0),
            new ConductorAsignado(ViajeId, ConductorId, T0.AddMinutes(1)));

        Assert.Throws<ReglaDeNegocioException>(() => viaje.AsignarConductor(Guid.NewGuid(), T0.AddMinutes(2)));
        Assert.Equal(ConductorId, viaje.ConductorId);
    }

    [Fact]
    public void Dado_un_viaje_solicitado_Cuando_se_solicita_otra_vez_Entonces_falla()
    {
        var viaje = Dado(new ViajeSolicitado(ViajeId, T0));

        var error = Assert.Throws<ReglaDeNegocioException>(() => viaje.Solicitar(ViajeId, T0));

        Assert.Equal("El viaje ya fue solicitado", error.Titulo);
    }

    [Fact]
    public void Dado_un_flujo_vacio_Cuando_se_finaliza_Entonces_falla()
    {
        Assert.Throws<ReglaDeNegocioException>(() => Dado().Finalizar(T0));
    }

    [Fact]
    public void Dado_un_viaje_solicitado_Cuando_se_asigna_Entonces_genera_ConductorAsignado()
    {
        var viaje = Dado(new ViajeSolicitado(ViajeId, T0));

        var evento = viaje.AsignarConductor(ConductorId, T0.AddMinutes(1));

        Assert.Equal(new ConductorAsignado(ViajeId, ConductorId, T0.AddMinutes(1)), evento);
        Assert.Equal(EstadoViaje.ASIGNADO, viaje.Estado);
        Assert.Equal(2, viaje.Version);
    }

    [Fact]
    public void Dado_un_viaje_asignado_Cuando_se_finaliza_Entonces_genera_ViajeFinalizado()
    {
        var viaje = Dado(
            new ViajeSolicitado(ViajeId, T0),
            new ConductorAsignado(ViajeId, ConductorId, T0.AddMinutes(1)));

        var evento = viaje.Finalizar(T0.AddMinutes(30));

        Assert.Equal(new ViajeFinalizado(ViajeId, T0.AddMinutes(30)), evento);
        Assert.Equal(EstadoViaje.FINALIZADO, viaje.Estado);
    }

    [Fact]
    public void Dado_un_viaje_finalizado_Cuando_se_asigna_Entonces_falla()
    {
        var viaje = Dado(
            new ViajeSolicitado(ViajeId, T0),
            new ConductorAsignado(ViajeId, ConductorId, T0.AddMinutes(1)),
            new ViajeFinalizado(ViajeId, T0.AddMinutes(30)));

        Assert.Throws<ReglaDeNegocioException>(() => viaje.AsignarConductor(Guid.NewGuid(), T0.AddHours(1)));
    }

    [Fact]
    public void Reproducir_los_tres_eventos_reconstruye_el_estado_completo()
    {
        var viaje = Viaje.Rehidratar(new EventoDeViaje[]
        {
            new ViajeSolicitado(ViajeId, T0),
            new ConductorAsignado(ViajeId, ConductorId, T0.AddMinutes(1)),
            new ViajeFinalizado(ViajeId, T0.AddMinutes(30))
        });

        Assert.True(viaje.Existe);
        Assert.Equal(ViajeId, viaje.Id);
        Assert.Equal(EstadoViaje.FINALIZADO, viaje.Estado);
        Assert.Equal(ConductorId, viaje.ConductorId);
        Assert.Equal(3, viaje.Version);
        Assert.Equal(T0, viaje.SolicitadoEn);
        Assert.Equal(T0.AddMinutes(1), viaje.AsignadoEn);
        Assert.Equal(T0.AddMinutes(30), viaje.FinalizadoEn);
    }

    [Fact]
    public void Reproducir_una_lista_vacia_no_crea_ningun_viaje()
    {
        var viaje = Viaje.Rehidratar(Array.Empty<EventoDeViaje>());

        Assert.False(viaje.Existe);
        Assert.Null(viaje.Estado);
        Assert.Equal(0, viaje.Version);
    }

    private sealed record EventoInventado(Guid ViajeId, DateTimeOffset OcurridoEn) : EventoDeViaje(ViajeId, OcurridoEn);

    [Fact]
    public void Aplicar_un_tipo_de_evento_desconocido_falla_en_vez_de_inventar_un_estado()
    {
        var viaje = new Viaje();

        Assert.Throws<InvalidOperationException>(() => viaje.Aplicar(new EventoInventado(ViajeId, T0)));
    }
}
