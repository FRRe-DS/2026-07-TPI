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

    [Fact]
    public async Task Enviar_DevuelveLaNotificacionEnviadaYLaRutaParaConsultarla()
    {
        using var db = TestDb.Crear();
        var controller = new NotificacionesController(db, NullLogger<NotificacionesController>.Instance);
        var request = new EnviarNotificacionRequest(
            "cliente@ejemplo.com", CanalNotificacion.Push, "Tu conductor llegó", "Te espera en la puerta");

        var resultado = await controller.Enviar(request, CancellationToken.None);

        var creado = Assert.IsType<CreatedAtActionResult>(resultado.Result);
        var respuesta = Assert.IsType<NotificacionDetalleResponse>(creado.Value);
        Assert.Equal(nameof(NotificacionesController.ObtenerPorId), creado.ActionName);
        Assert.Equal(respuesta.Id, creado.RouteValues!["id"]);
        Assert.NotEqual(Guid.Empty, respuesta.Id);
        Assert.Equal("cliente@ejemplo.com", respuesta.Destinatario);
        Assert.Equal(CanalNotificacion.Push, respuesta.Canal);
        Assert.Equal("Tu conductor llegó", respuesta.Asunto);
        Assert.Equal(EstadoNotificacion.Enviado, respuesta.Estado);
        Assert.Equal(0, respuesta.IntentosReenvio);
        Assert.NotNull(respuesta.FechaEnvio);
        Assert.InRange(respuesta.FechaCreacion, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow);
    }

    [Fact]
    public async Task Enviar_SinReferencia_GuardaLaReferenciaComoNull()
    {
        using var db = TestDb.Crear();
        var controller = new NotificacionesController(db, NullLogger<NotificacionesController>.Instance);
        var request = new EnviarNotificacionRequest(
            "conductor@ejemplo.com", CanalNotificacion.Sms, "Aviso", "Mensaje sin viaje asociado");

        await controller.Enviar(request, CancellationToken.None);

        var guardada = await db.Notificaciones.SingleAsync();
        Assert.Null(request.ReferenciaId);
        Assert.Null(guardada.ReferenciaId);
        Assert.Equal("Mensaje sin viaje asociado", guardada.Mensaje);
        Assert.Null(guardada.ErrorUltimoIntento);
    }

    [Theory]
    [InlineData(CanalNotificacion.Email)]
    [InlineData(CanalNotificacion.Push)]
    [InlineData(CanalNotificacion.Sms)]
    public async Task Enviar_GuardaElCanalIndicado(CanalNotificacion canal)
    {
        using var db = TestDb.Crear();
        var controller = new NotificacionesController(db, NullLogger<NotificacionesController>.Instance);

        await controller.Enviar(new EnviarNotificacionRequest("destino", canal, "Asunto", "Mensaje"), CancellationToken.None);

        Assert.Equal(canal, (await db.Notificaciones.SingleAsync()).Canal);
    }

    [Fact]
    public async Task Enviar_ConLaOperacionCancelada_NoGuardaNada()
    {
        using var db = TestDb.Crear();
        var controller = new NotificacionesController(db, NullLogger<NotificacionesController>.Instance);
        var request = new EnviarNotificacionRequest("cliente@ejemplo.com", CanalNotificacion.Email, "Asunto", "Mensaje");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => controller.Enviar(request, new CancellationToken(canceled: true)));

        Assert.Empty(db.Notificaciones);
    }

    [Fact]
    public async Task ObtenerPorId_ConNotificacionFallida_DevuelveSusIntentosYSinFechaDeEnvio()
    {
        using var db = TestDb.Crear();
        var notificacion = new Notificacion
        {
            Destinatario = "cliente@ejemplo.com",
            Canal = CanalNotificacion.Email,
            Asunto = "Comprobante de pago",
            Mensaje = "Adjuntamos tu comprobante",
            Estado = EstadoNotificacion.Fallido,
            IntentosReenvio = 3,
            ErrorUltimoIntento = "Buzón lleno"
        };
        db.Notificaciones.Add(notificacion);
        await db.SaveChangesAsync();
        var controller = new NotificacionesController(db, NullLogger<NotificacionesController>.Instance);

        var resultado = await controller.ObtenerPorId(notificacion.Id, CancellationToken.None);

        var respuesta = Assert.IsType<NotificacionDetalleResponse>(Assert.IsType<OkObjectResult>(resultado.Result).Value);
        Assert.Equal(EstadoNotificacion.Fallido, respuesta.Estado);
        Assert.Equal(3, respuesta.IntentosReenvio);
        Assert.Null(respuesta.FechaEnvio);
        Assert.Equal(notificacion.FechaCreacion, respuesta.FechaCreacion);
        Assert.Equal("Comprobante de pago", respuesta.Asunto);
    }

    [Fact]
    public async Task ObtenerPorId_ConNotificacionInexistente_NoDevuelveDatosDeOtraNotificacion()
    {
        using var db = TestDb.Crear();
        db.Notificaciones.Add(new Notificacion { Destinatario = "otro@ejemplo.com", Asunto = "Otra" });
        await db.SaveChangesAsync();
        var controller = new NotificacionesController(db, NullLogger<NotificacionesController>.Instance);
        var idInexistente = Guid.NewGuid();

        var resultado = await controller.ObtenerPorId(idInexistente, CancellationToken.None);

        var noEncontrado = Assert.IsType<NotFoundObjectResult>(resultado.Result);
        Assert.IsNotType<NotificacionDetalleResponse>(noEncontrado.Value);
        Assert.Contains(idInexistente.ToString(), noEncontrado.Value!.ToString());
    }

    [Fact]
    public async Task ObtenerPorId_ConLaOperacionCancelada_LanzaOperationCanceled()
    {
        using var db = TestDb.Crear();
        var controller = new NotificacionesController(db, NullLogger<NotificacionesController>.Instance);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => controller.ObtenerPorId(Guid.NewGuid(), new CancellationToken(canceled: true)));
    }

    [Fact]
    public void Notificacion_Nueva_TieneValoresPorDefecto()
    {
        var notificacion = new Notificacion();

        Assert.NotEqual(Guid.Empty, notificacion.Id);
        Assert.Equal(string.Empty, notificacion.Destinatario);
        Assert.Equal(string.Empty, notificacion.Asunto);
        Assert.Equal(string.Empty, notificacion.Mensaje);
        Assert.Equal(EstadoNotificacion.Pendiente, notificacion.Estado);
        Assert.Equal(0, notificacion.IntentosReenvio);
        Assert.InRange(notificacion.FechaCreacion, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow);
        Assert.Null(notificacion.FechaEnvio);
        Assert.Null(notificacion.ReferenciaId);
        Assert.Null(notificacion.ErrorUltimoIntento);
    }

    [Fact]
    public void Notificacion_NuevasInstancias_TienenIdsDistintos()
    {
        Assert.NotEqual(new Notificacion().Id, new Notificacion().Id);
    }
}
