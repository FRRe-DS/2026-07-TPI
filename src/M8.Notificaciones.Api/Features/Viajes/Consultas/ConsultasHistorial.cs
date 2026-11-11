using Microsoft.EntityFrameworkCore;
using M8.Notificaciones.Api.Features.Viajes.Dtos;
using M8.Notificaciones.Api.Infrastructure.Persistence;

namespace M8.Notificaciones.Api.Features.Viajes.Consultas;

/// <summary>
/// Consulta del lado CQRS: lee SOLO la vista ViajesHistorialVista.
///
/// Regla de oro de esta clase: no usa EventosViaje ni el agregado. Es un
/// SELECT simple sobre una tabla ya preparada para responder, por eso es
/// barata y escalaría por separado del lado de escritura.
///
/// El precio: puede estar ATRASADA respecto de los eventos (consistencia
/// eventual). Si todavía no se proyectó nada, devolvemos 200 con la lista
/// vacía (y no 404): el viaje existe, solo que la vista aún no se enteró. Eso
/// es exactamente lo que la demo quiere mostrar.
/// </summary>
public sealed class ConsultasHistorial
{
    private readonly M8DbContext _db;
    private readonly TimeProvider _reloj;

    public ConsultasHistorial(M8DbContext db, TimeProvider reloj)
    {
        _db = db;
        _reloj = reloj;
    }

    public async Task<HistorialViajeResponse> ObtenerAsync(Guid viajeId, CancellationToken ct = default)
    {
        var filas = await _db.ViajesHistorialVista
            .AsNoTracking()
            .Where(v => v.ViajeId == viajeId)
            .OrderBy(v => v.Version)
            .ToListAsync(ct);

        var items = filas
            .Select(f => new ItemHistorialResponse(
                f.Version, f.Evento, f.Descripcion, f.OcurridoEn, f.RegistradoEn, f.ProyectadoEn,
                // Cuánto tardó el evento en volverse consultable: el retraso de CQRS, medido.
                RetrasoMs: (long)Math.Round((f.ProyectadoEn - f.RegistradoEn).TotalMilliseconds)))
            .ToList();

        // El resumen sale de la propia vista: estado de la última fila y último conductor conocido.
        var resumen = new ResumenHistorialResponse(
            filas.LastOrDefault()?.EstadoResultante,
            filas.LastOrDefault(f => f.ConductorId is not null)?.ConductorId);

        return new HistorialViajeResponse(viajeId, items, resumen, _reloj.GetUtcNow());
    }
}
