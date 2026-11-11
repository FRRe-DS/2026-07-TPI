using Microsoft.EntityFrameworkCore;
using M8.Notificaciones.Api.Features.Notificaciones.Entities;
using M8.Notificaciones.Api.Features.Documentos.Entities;
using M8.Notificaciones.Api.Features.Soporte.Entities;
using M8.Notificaciones.Api.Features.Viajes;
using M8.Notificaciones.Api.Features.Viajes.EventStore;
using M8.Notificaciones.Api.Features.Viajes.Proyecciones;

namespace M8.Notificaciones.Api.Infrastructure.Persistence;

public class M8DbContext : DbContext
{
    public M8DbContext(DbContextOptions<M8DbContext> options) : base(options)
    {
    }

    public DbSet<Notificacion> Notificaciones => Set<Notificacion>();
    public DbSet<Comprobante> Comprobantes => Set<Comprobante>();
    public DbSet<Ticket> Tickets => Set<Ticket>();

    // Viajes (CQRS + Event Sourcing): escritura = EventosViaje; lectura = ViajesHistorialVista.
    public DbSet<EventoAlmacenado> EventosViaje => Set<EventoAlmacenado>();
    public DbSet<ViajeHistorialItem> ViajesHistorialVista => Set<ViajeHistorialItem>();
    public DbSet<ProyeccionCheckpoint> ProyeccionCheckpoints => Set<ProyeccionCheckpoint>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Notificaciones
        modelBuilder.Entity<Notificacion>(entity =>
        {
            entity.HasKey(n => n.Id);
            entity.Property(n => n.Destinatario).IsRequired().HasMaxLength(250);
            entity.Property(n => n.Asunto).IsRequired().HasMaxLength(250);
            entity.Property(n => n.Canal).HasConversion<string>();
            entity.Property(n => n.Estado).HasConversion<string>();
            entity.HasIndex(n => n.Destinatario);
            entity.HasIndex(n => n.Estado);
        });

        // Comprobantes
        modelBuilder.Entity<Comprobante>(entity =>
        {
            entity.HasKey(c => c.Id);
            entity.Property(c => c.ViajeId).IsRequired().HasMaxLength(100);
            entity.Property(c => c.ClienteId).IsRequired().HasMaxLength(100);
            entity.Property(c => c.MontoTotal).HasPrecision(18, 2);
            entity.Property(c => c.Moneda).HasMaxLength(10);
            entity.HasIndex(c => c.ViajeId);
        });

        // Tickets
        modelBuilder.Entity<Ticket>(entity =>
        {
            entity.HasKey(t => t.Id);
            entity.Property(t => t.UsuarioId).IsRequired().HasMaxLength(100);
            entity.Property(t => t.Titulo).IsRequired().HasMaxLength(250);
            entity.Property(t => t.Tipo).HasConversion<string>();
            entity.Property(t => t.Estado).HasConversion<string>();
            entity.HasIndex(t => t.UsuarioId);
            entity.HasIndex(t => t.ViajeId);
            entity.HasIndex(t => t.Estado);
        });

        // Viajes (CQRS + Event Sourcing): la configuración vive en la propia feature.
        ViajesModelo.Configurar(modelBuilder);
    }
}
