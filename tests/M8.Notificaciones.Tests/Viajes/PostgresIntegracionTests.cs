using Microsoft.EntityFrameworkCore;
using Npgsql;
using M8.Notificaciones.Api.Features.Viajes.Comandos;
using M8.Notificaciones.Api.Features.Viajes.Dominio;
using M8.Notificaciones.Api.Features.Viajes.EventStore;
using M8.Notificaciones.Api.Infrastructure.Persistence;

namespace M8.Notificaciones.Tests.Viajes;

/// <summary>
/// Se ejecuta solo si está definida la variable M8_TEST_POSTGRES con una cadena
/// de conexión (por ejemplo, la del Postgres del docker-compose). Si no, el
/// test aparece como "omitido" y la CI sigue en verde.
/// </summary>
public sealed class PostgresFactAttribute : FactAttribute
{
    public const string Variable = "M8_TEST_POSTGRES";

    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Variable)))
        {
            Skip = $"Definí {Variable} con una cadena de conexión a PostgreSQL para correr esta prueba.";
        }
    }
}

/// <summary>
/// Lo que EF InMemory NO puede probar: que la migración se aplica, que los
/// índices únicos de PostgreSQL frenan de verdad la concurrencia y que las
/// fechas viajan bien a timestamptz. Cada test usa una base temporal propia.
/// </summary>
public class PostgresIntegracionTests
{
    private static M8DbContext CrearContexto(string cadena) =>
        new(new DbContextOptionsBuilder<M8DbContext>().UseNpgsql(cadena).Options);

    private static string CadenaConBaseTemporal()
    {
        var builder = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable(PostgresFactAttribute.Variable))
        {
            Database = "m8_test_" + Guid.NewGuid().ToString("N")[..12]
        };
        return builder.ConnectionString;
    }

    [PostgresFact]
    public async Task Con_comandos_simultaneos_sobre_el_mismo_viaje_gana_uno_solo()
    {
        var cadena = CadenaConBaseTemporal();
        await using (var db = CrearContexto(cadena))
        {
            await db.Database.MigrateAsync(); // también prueba que la migración nueva aplica
        }

        try
        {
            var viajeId = Guid.NewGuid();
            var reloj = new RelojFijo(DateTimeOffset.UtcNow);
            await using (var db = CrearContexto(cadena))
            {
                await new RegistrarEventoViajeHandler(new EventStoreViajes(db), reloj)
                    .ManejarAsync(Solicitudes.Solicitado(viajeId));
            }

            // 8 módulos "asignan conductor" al mismo tiempo, cada uno con su propio DbContext.
            var intentos = Enumerable.Range(0, 8).Select(async _ =>
            {
                await using var db = CrearContexto(cadena);
                var handler = new RegistrarEventoViajeHandler(new EventStoreViajes(db), reloj);
                try
                {
                    await handler.ManejarAsync(Solicitudes.Asignado(viajeId, Guid.NewGuid()));
                    return "aceptado";
                }
                catch (ConflictoDeConcurrenciaException) { return "409-concurrencia"; }
                catch (ReglaDeNegocioException) { return "409-regla"; }
            });
            var resultados = await Task.WhenAll(intentos);

            // Exactamente uno gana; los demás reciben 409 (por el índice único o porque ya ven ASIGNADO).
            Assert.Single(resultados, r => r == "aceptado");
            await using (var db = CrearContexto(cadena))
            {
                var flujo = await new EventStoreViajes(db).LeerFlujoAsync(viajeId);
                Assert.Equal(new[] { 1, 2 }, flujo.Select(e => e.Version));
                Assert.Equal(TimeSpan.Zero, flujo[0].OcurridoEn.Offset);
            }
        }
        finally
        {
            await using var db = CrearContexto(cadena);
            await db.Database.EnsureDeletedAsync();
        }
    }

    [PostgresFact]
    public async Task El_indice_unico_de_EventoId_rechaza_un_duplicado_aunque_se_saltee_el_chequeo()
    {
        var cadena = CadenaConBaseTemporal();
        await using (var db = CrearContexto(cadena))
        {
            await db.Database.MigrateAsync();
        }

        try
        {
            var eventoId = Guid.NewGuid();
            EventoAlmacenado Fila(int version) => new()
            {
                EventoId = eventoId, ViajeId = Guid.NewGuid(), Version = version, Tipo = "ViajeSolicitado",
                Origen = "M5", Payload = "{}", OcurridoEn = DateTimeOffset.UtcNow, RegistradoEn = DateTimeOffset.UtcNow
            };

            await using (var db = CrearContexto(cadena))
            {
                await new EventStoreViajes(db).AgregarAsync(Fila(1));
            }

            // Otro viaje, mismo EventoId: el chequeo previo no lo mira, la base sí.
            await using (var db = CrearContexto(cadena))
            {
                var error = await Assert.ThrowsAsync<ConflictoDeConcurrenciaException>(
                    () => new EventStoreViajes(db).AgregarAsync(Fila(1)));
                Assert.IsType<DbUpdateException>(error.InnerException);
            }
        }
        finally
        {
            await using var db = CrearContexto(cadena);
            await db.Database.EnsureDeletedAsync();
        }
    }
}
