using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using M8.Notificaciones.Api.Features.Soporte.Controllers;
using M8.Notificaciones.Api.Features.Soporte.Dtos;
using M8.Notificaciones.Api.Features.Soporte.Entities;

namespace M8.Notificaciones.Tests;

public class SoporteControllerTests
{
    [Fact]
    public async Task CrearTicket_GuardaUnTicketAbiertoEnLaBase()
    {
        using var db = TestDb.Crear();
        var controller = new SoporteController(db, NullLogger<SoporteController>.Instance);
        var request = new CrearTicketRequest(
            "usuario-1", "viaje-123", TipoIncidencia.Viaje, "El conductor no llegó", "Esperé 20 minutos");

        var resultado = await controller.CrearTicket(request, CancellationToken.None);

        var creado = Assert.IsType<CreatedAtActionResult>(resultado.Result);
        var respuesta = Assert.IsType<TicketResponse>(creado.Value);
        Assert.Equal(EstadoTicket.Abierto, respuesta.Estado);
        var guardado = await db.Tickets.SingleAsync();
        Assert.Equal(respuesta.Id, guardado.Id);
        Assert.Equal("usuario-1", guardado.UsuarioId);
        Assert.Equal("viaje-123", guardado.ViajeId);
        Assert.Equal(TipoIncidencia.Viaje, guardado.Tipo);
        Assert.Equal(EstadoTicket.Abierto, guardado.Estado);
        Assert.Null(guardado.FechaCierre);
    }

    [Fact]
    public async Task ObtenerTicketPorId_ConTicketExistente_DevuelveOkConSusDatos()
    {
        using var db = TestDb.Crear();
        var ticket = new Ticket
        {
            UsuarioId = "usuario-2",
            Tipo = TipoIncidencia.Pago,
            Titulo = "Cobro duplicado",
            Descripcion = "Me cobraron dos veces"
        };
        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();
        var controller = new SoporteController(db, NullLogger<SoporteController>.Instance);

        var resultado = await controller.ObtenerTicketPorId(ticket.Id, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(resultado.Result);
        var respuesta = Assert.IsType<TicketResponse>(ok.Value);
        Assert.Equal(ticket.Id, respuesta.Id);
        Assert.Equal("Cobro duplicado", respuesta.Titulo);
        Assert.Equal(TipoIncidencia.Pago, respuesta.Tipo);
    }

    [Fact]
    public async Task ObtenerTicketPorId_ConTicketInexistente_DevuelveNotFound()
    {
        using var db = TestDb.Crear();
        var controller = new SoporteController(db, NullLogger<SoporteController>.Instance);

        var resultado = await controller.ObtenerTicketPorId(Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(resultado.Result);
    }
}
