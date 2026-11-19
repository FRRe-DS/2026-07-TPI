using Microsoft.EntityFrameworkCore;
using M8.Notificaciones.Api.Features.Viajes.Consultas;
using M8.Notificaciones.Api.Features.Viajes.Proyecciones;

namespace M8.Notificaciones.Tests.Viajes;

/// <summary>
/// La proyección recibe el "ahora" como parámetro: así probamos el retraso
/// moviendo un reloj de mentira, sin esperar segundos reales.
/// </summary>
public class ProyeccionHistorialViajesTests
{
    [Fact]
    public async Task No_proyecta_eventos_mas_nuevos_que_el_retraso_configurado()
    {
        using var esc = new EscenarioViajes(retrasoMs: 3000);
        await esc.RegistrarViajeCompleto(); // registrados en Inicio

        // 2,9 s después: todavía no venció el retraso de 3 s.
        var procesados = await esc.Proyeccion.ProyectarPendientesAsync(EscenarioViajes.Inicio.AddMilliseconds(2900));

        Assert.Equal(0, procesados);
        Assert.Empty(await esc.Db.ViajesHistorialVista.ToListAsync());
    }

    [Fact]
    public async Task Pasado_el_retraso_proyecta_los_eventos_y_mide_el_retraso()
    {
        using var esc = new EscenarioViajes(retrasoMs: 3000);
        var viajeId = await esc.RegistrarViajeCompleto();
        var ahora = EscenarioViajes.Inicio.AddMilliseconds(3000);

        var procesados = await esc.Proyeccion.ProyectarPendientesAsync(ahora);

        Assert.Equal(3, procesados);
        esc.Reloj.Ahora = ahora;
        var historial = await new ConsultasHistorial(esc.Db, esc.Reloj).ObtenerAsync(viajeId);
        Assert.Equal(new[] { 1, 2, 3 }, historial.Items.Select(i => i.Version));
        Assert.All(historial.Items, i => Assert.Equal(3000, i.RetrasoMs));
        Assert.Equal("FINALIZADO", historial.Resumen.Estado);
    }

    [Fact]
    public async Task Proyecta_solo_los_eventos_viejos_y_deja_los_nuevos_para_despues()
    {
        using var esc = new EscenarioViajes(retrasoMs: 3000);
        var viajeId = Guid.NewGuid();
        await esc.Registrar(Solicitudes.Solicitado(viajeId));          // t = 0 s
        esc.Reloj.Avanzar(TimeSpan.FromSeconds(2));
        await esc.Registrar(Solicitudes.Asignado(viajeId, Guid.NewGuid())); // t = 2 s

        // En t = 3,5 s el primero ya venció (3,5 s) y el segundo no (1,5 s).
        var procesados = await esc.Proyeccion.ProyectarPendientesAsync(EscenarioViajes.Inicio.AddMilliseconds(3500));
        Assert.Equal(1, procesados);
        var diag = await esc.Proyeccion.ObtenerDiagnosticoAsync();
        Assert.Equal((2, 1, 1), (diag.EventosEnStore, diag.EventosProyectados, diag.Pendientes));

        // En t = 5 s también vence el segundo.
        procesados = await esc.Proyeccion.ProyectarPendientesAsync(EscenarioViajes.Inicio.AddSeconds(5));
        Assert.Equal(1, procesados);
        diag = await esc.Proyeccion.ObtenerDiagnosticoAsync();
        Assert.Equal(0, diag.Pendientes);
        Assert.Equal(EscenarioViajes.Inicio.AddSeconds(5), diag.UltimaProyeccionEn);
        Assert.Equal(3000, diag.RetrasoConfiguradoMs);
    }

    [Fact]
    public async Task Volver_a_proyectar_no_duplica_filas()
    {
        using var esc = new EscenarioViajes(retrasoMs: 0);
        await esc.RegistrarViajeCompleto();
        var ahora = EscenarioViajes.Inicio.AddSeconds(1);

        await esc.Proyeccion.ProyectarPendientesAsync(ahora);
        var segundaPasada = await esc.Proyeccion.ProyectarPendientesAsync(ahora);

        Assert.Equal(0, segundaPasada); // el checkpoint ya pasó esos eventos
        Assert.Equal(3, await esc.Db.ViajesHistorialVista.CountAsync());
    }

    [Fact]
    public async Task Si_el_checkpoint_se_pierde_la_proyeccion_es_idempotente_y_saltea_filas_existentes()
    {
        using var esc = new EscenarioViajes(retrasoMs: 0);
        await esc.RegistrarViajeCompleto();
        var ahora = EscenarioViajes.Inicio.AddSeconds(1);
        await esc.Proyeccion.ProyectarPendientesAsync(ahora);

        // Simulamos un corte: las filas quedaron guardadas pero el checkpoint volvió a 0.
        var checkpoint = await esc.Db.ProyeccionCheckpoints.SingleAsync();
        checkpoint.UltimaSeq = 0;
        await esc.Db.SaveChangesAsync();

        var reprocesados = await esc.Proyeccion.ProyectarPendientesAsync(ahora);

        Assert.Equal(3, reprocesados);                              // los volvió a recorrer...
        Assert.Equal(3, await esc.Db.ViajesHistorialVista.CountAsync()); // ...sin duplicar
    }

    [Fact]
    public async Task Reconstruir_la_vista_da_el_mismo_resultado_que_la_proyeccion_incremental()
    {
        using var esc = new EscenarioViajes(retrasoMs: 3000);
        await esc.RegistrarViajeCompleto();
        esc.Reloj.Avanzar(TimeSpan.FromSeconds(1));
        await esc.RegistrarViajeCompleto();

        // Incremental: en dos pasadas, a medida que vence el retraso.
        await esc.Proyeccion.ProyectarPendientesAsync(EscenarioViajes.Inicio.AddSeconds(3));
        await esc.Proyeccion.ProyectarPendientesAsync(EscenarioViajes.Inicio.AddSeconds(4));
        var incremental = await LeerVista(esc);

        var reproducidos = await esc.Proyeccion.ReconstruirAsync(EscenarioViajes.Inicio.AddMinutes(5));
        var reconstruida = await LeerVista(esc);

        Assert.Equal(6, reproducidos);
        Assert.Equal(incremental, reconstruida);
        Assert.Equal(0, (await esc.Proyeccion.ObtenerDiagnosticoAsync()).Pendientes);
    }

    [Fact]
    public async Task Reconstruir_ignora_el_retraso_y_proyecta_tambien_los_eventos_recientes()
    {
        using var esc = new EscenarioViajes(retrasoMs: 60_000);
        await esc.RegistrarViajeCompleto();

        var reproducidos = await esc.Proyeccion.ReconstruirAsync(EscenarioViajes.Inicio);

        Assert.Equal(3, reproducidos);
    }

    [Fact]
    public async Task Diagnostico_sin_eventos_ni_checkpoint_devuelve_ceros()
    {
        using var esc = new EscenarioViajes();

        var diag = await esc.Proyeccion.ObtenerDiagnosticoAsync();

        Assert.Equal((0, 0, 0), (diag.EventosEnStore, diag.EventosProyectados, diag.Pendientes));
        Assert.Null(diag.UltimaProyeccionEn);
    }

    /// <summary>Contenido "de negocio" de la vista (sin ProyectadoEn, que es metadato de la proyección).</summary>
    private static async Task<List<string>> LeerVista(EscenarioViajes esc) =>
        (await esc.Db.ViajesHistorialVista.AsNoTracking().ToListAsync())
            .OrderBy(v => v.ViajeId).ThenBy(v => v.Version)
            .Select(v => $"{v.ViajeId}|{v.Version}|{v.Evento}|{v.Descripcion}|{v.EstadoResultante}|{v.ConductorId}|{v.OcurridoEn:O}|{v.RegistradoEn:O}")
            .ToList();
}
