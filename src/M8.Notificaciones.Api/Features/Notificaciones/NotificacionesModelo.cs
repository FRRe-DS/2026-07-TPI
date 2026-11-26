using Microsoft.EntityFrameworkCore;
using M8.Notificaciones.Api.Features.Eventos.Entities;
using M8.Notificaciones.Api.Features.Notificaciones.Entities;

namespace M8.Notificaciones.Api.Features.Notificaciones;

/// <summary>
/// Configuración EF Core de las tablas de Notificaciones y de la bandeja de
/// entrada de eventos. Igual que ViajesModelo: vive en la feature para que el
/// cambio en M8DbContext sea una línea.
/// </summary>
public static class NotificacionesModelo
{
    public static void Configurar(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Notificacion>(e =>
        {
            e.ToTable("Notificaciones");
            e.HasKey(n => n.Id);
            e.Property(n => n.TipoEvento).HasConversion<string>().HasMaxLength(40);
            e.Property(n => n.Origen).HasConversion<string>().HasMaxLength(20);
            e.Property(n => n.Rol).HasConversion<string>().HasMaxLength(20);
            e.Property(n => n.Canal).HasConversion<string>().HasMaxLength(10);
            e.Property(n => n.Estado).HasConversion<string>().HasMaxLength(20);
            e.Property(n => n.ReferenciaTipo).HasConversion<string>().HasMaxLength(20);
            e.Property(n => n.Contacto).HasMaxLength(254);
            e.Property(n => n.Asunto).IsRequired().HasMaxLength(150);
            e.Property(n => n.Mensaje).IsRequired().HasMaxLength(1000);
            e.Property(n => n.UltimoError).HasMaxLength(500);

            // Concurrencia optimista: UPDATE ... WHERE "Version" = @original.
            e.Property(n => n.Version).IsConcurrencyToken();

            // Idempotencia a nivel base: un evento no puede generar dos avisos
            // para el mismo destinatario y rol (las manuales tienen EventoId null).
            e.HasIndex(n => new { n.EventoId, n.DestinatarioId, n.Rol }).IsUnique();

            // Lo que busca el despachador en cada pasada.
            e.HasIndex(n => new { n.Estado, n.ProximoIntentoEn });
            // Filtros del listado.
            e.HasIndex(n => n.DestinatarioId);
            e.HasIndex(n => n.ReferenciaId);
            e.HasIndex(n => n.CreadoEn);

            e.HasMany(n => n.HistorialIntentos)
                .WithOne()
                .HasForeignKey(i => i.NotificacionId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<IntentoNotificacion>(e =>
        {
            e.ToTable("NotificacionIntentos");
            e.HasKey(i => i.Id);
            e.Property(i => i.Id).ValueGeneratedOnAdd();
            e.Property(i => i.Canal).HasConversion<string>().HasMaxLength(10);
            e.Property(i => i.Resultado).HasConversion<string>().HasMaxLength(10);
            e.Property(i => i.Error).HasMaxLength(500);
            e.HasIndex(i => new { i.NotificacionId, i.Numero }).IsUnique();
        });

        modelBuilder.Entity<ContactoDestinatario>(e =>
        {
            e.ToTable("ContactosDestinatarios");
            e.HasKey(c => c.UsuarioId);
            e.Property(c => c.Email).HasMaxLength(254);
            e.Property(c => c.Telefono).HasMaxLength(16);
        });

        modelBuilder.Entity<EventoRecibido>(e =>
        {
            e.ToTable("EventosRecibidos");
            e.HasKey(x => x.EventoId);
            e.Property(x => x.Tipo).IsRequired().HasMaxLength(40);
            e.Property(x => x.Origen).IsRequired().HasMaxLength(5);
            e.Property(x => x.HashCuerpo).IsRequired().HasMaxLength(64);
            e.Property(x => x.Via).IsRequired().HasMaxLength(10);
            e.Property(x => x.EmitidoPor).HasMaxLength(100);
        });

        // TP3 (RNF-19): trazabilidad de operaciones manuales. Solo inserciones.
        modelBuilder.Entity<AuditoriaNotificacion>(e =>
        {
            e.ToTable("NotificacionAuditoria");
            e.HasKey(a => a.Id);
            e.Property(a => a.Id).ValueGeneratedOnAdd();
            e.Property(a => a.Accion).HasConversion<string>().HasMaxLength(20);
            e.Property(a => a.Actor).IsRequired().HasMaxLength(100);
            e.Property(a => a.RequestId).HasMaxLength(128);
            e.HasIndex(a => a.NotificacionId);
            e.HasOne<Notificacion>()
                .WithMany()
                .HasForeignKey(a => a.NotificacionId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
