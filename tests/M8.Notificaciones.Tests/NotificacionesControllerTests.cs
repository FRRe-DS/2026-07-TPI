using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using M8.Notificaciones.Api.Features.Notificaciones.Controllers;
using M8.Notificaciones.Api.Features.Notificaciones.Dtos;
using M8.Notificaciones.Api.Features.Notificaciones.Entities;

namespace M8.Notificaciones.Tests;

public class NotificacionesControllerTests
{
    [Fact]
    public async Task Enviar_GuardaLaNotificacionEnLaBase()
    {
        using var db = TestDb.Crear();
        var controller = new NotificacionesController(db, NullLogger<NotificacionesController>.Instance);
        var request = new EnviarNotificacionRequest(
            "cliente@ejemplo.com", CanalNotificacion.Email, "Tu viaje finalizó", "Gracias por viajar", "viaje-123");

        var resultado = await controller.Enviar(request, CancellationToken.None);

        var creado = Assert.IsType<CreatedAtActionResult>(resultado.Result);
        var respuesta = Assert.IsType<NotificacionDetalleResponse>(creado.Value);
        var guardada = await db.Notificaciones.SingleAsync();
        Assert.Equal(respuesta.Id, guardada.Id);
        Assert.Equal("cliente@ejemplo.com", guardada.Destinatario);
        Assert.Equal(CanalNotificacion.Email, guardada.Canal);
        Assert.Equal("Tu viaje finalizó", guardada.Asunto);
        Assert.Equal("viaje-123", guardada.ReferenciaId);
    }

    [Fact]
    public async Task ObtenerPorId_ConNotificacionExistente_DevuelveOkConSusDatos()
    {
        using var db = TestDb.Crear();
        var notificacion = new Notificacion
        {
            Destinatario = "conductor@ejemplo.com",
            Canal = CanalNotificacion.Sms,
            Asunto = "Nuevo viaje asignado",
            Mensaje = "Tenés un viaje asignado"
        };
        db.Notificaciones.Add(notificacion);
        await db.SaveChangesAsync();
        var controller = new NotificacionesController(db, NullLogger<NotificacionesController>.Instance);

        var resultado = await controller.ObtenerPorId(notificacion.Id, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(resultado.Result);
        var respuesta = Assert.IsType<NotificacionDetalleResponse>(ok.Value);
        Assert.Equal(notificacion.Id, respuesta.Id);
        Assert.Equal("conductor@ejemplo.com", respuesta.Destinatario);
        Assert.Equal(CanalNotificacion.Sms, respuesta.Canal);
        Assert.Equal(EstadoNotificacion.Pendiente, respuesta.Estado);
    }

    [Fact]
    public async Task ObtenerPorId_ConNotificacionInexistente_DevuelveNotFound()
    {
        using var db = TestDb.Crear();
        var controller = new NotificacionesController(db, NullLogger<NotificacionesController>.Instance);

        var resultado = await controller.ObtenerPorId(Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(resultado.Result);
    }
}
