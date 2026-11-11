using Microsoft.EntityFrameworkCore;
using M8.Notificaciones.Api.Infrastructure.Persistence;

namespace M8.Notificaciones.Api.Features.Viajes.EventStore;

/// <summary>
/// Dos registros quisieron ocupar la misma posición (ViajeId, Version) o el
/// mismo EventoId: otro comando ganó la carrera. Se traduce a 409; quien llama
/// puede reintentar (si era el mismo evento, el reintento cae en la rama
/// idempotente y recibe la respuesta original).
/// </summary>
public sealed class ConflictoDeConcurrenciaException : Exception
{
    public ConflictoDeConcurrenciaException(string mensaje, Exception? causa = null) : base(mensaje, causa) { }
}

/// <summary>
/// Acceso a la tabla EventosViaje. Solo tiene operaciones de LEER y AGREGAR:
/// no hay Actualizar ni Borrar, porque los eventos son inmutables.
/// </summary>
public sealed class EventStoreViajes
{
    private readonly M8DbContext _db;

    public EventStoreViajes(M8DbContext db)
    {
        _db = db;
    }

    /// <summary>El flujo de un viaje, en el orden en que se registró (Version 1, 2, 3...).</summary>
    public Task<List<EventoAlmacenado>> LeerFlujoAsync(Guid viajeId, CancellationToken ct = default) =>
        _db.EventosViaje
            .AsNoTracking()
            .Where(e => e.ViajeId == viajeId)
            .OrderBy(e => e.Version)
            .ToListAsync(ct);

    public Task<EventoAlmacenado?> BuscarPorEventoIdAsync(Guid eventoId, CancellationToken ct = default) =>
        _db.EventosViaje.AsNoTracking().FirstOrDefaultAsync(e => e.EventoId == eventoId, ct);

    /// <summary>
    /// Agrega un evento al final del flujo.
    ///
    /// Hay DOS defensas contra la concurrencia, y las dos son a propósito:
    ///  1. Chequeo previo en código: ¿ya existe esa (ViajeId, Version)? Es lo que
    ///     se puede probar con EF InMemory, que no hace cumplir índices únicos.
    ///  2. El índice único de PostgreSQL: es la defensa REAL. Entre el chequeo
    ///     previo y el INSERT otro comando puede colarse; en ese caso la base
    ///     rechaza el segundo INSERT y acá lo convertimos en un conflicto.
    /// </summary>
    public async Task AgregarAsync(EventoAlmacenado evento, CancellationToken ct = default)
    {
        var posicionOcupada = await _db.EventosViaje.AnyAsync(
            e => e.ViajeId == evento.ViajeId && e.Version == evento.Version, ct);
        if (posicionOcupada)
        {
            throw new ConflictoDeConcurrenciaException(
                $"Otro comando ya registró la versión {evento.Version} del viaje {evento.ViajeId}. Reintentá la operación.");
        }

        _db.EventosViaje.Add(evento);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            // Lo sacamos del change tracker para que el contexto no quede "sucio".
            _db.Entry(evento).State = EntityState.Detached;
            throw new ConflictoDeConcurrenciaException(
                $"La base rechazó el evento {evento.EventoId} (versión {evento.Version} del viaje {evento.ViajeId}): otro comando ganó la carrera. Reintentá la operación.",
                ex);
        }
    }
}
