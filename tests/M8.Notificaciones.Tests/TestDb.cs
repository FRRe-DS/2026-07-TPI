using Microsoft.EntityFrameworkCore;
using M8.Notificaciones.Api.Infrastructure.Persistence;

namespace M8.Notificaciones.Tests;

/// <summary>
/// Crea un M8DbContext sobre una base en memoria, distinta para cada test,
/// para que las pruebas no dependan de PostgreSQL ni se afecten entre sí.
/// </summary>
internal static class TestDb
{
    public static M8DbContext Crear()
    {
        var options = new DbContextOptionsBuilder<M8DbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new M8DbContext(options);
    }
}
