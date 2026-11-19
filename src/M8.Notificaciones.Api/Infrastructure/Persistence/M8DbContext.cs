using Microsoft.EntityFrameworkCore;
using M8.Notificaciones.Api.Features.Notificaciones;
using M8.Notificaciones.Api.Features.Notificaciones.Entities;
using M8.Notificaciones.Api.Features.Eventos.Entities;
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

    // Notificaciones (TP2): avisos, sus intentos, contactos registrados y bandeja de entrada de eventos.
    public DbSet<Notificacion> Notificaciones => Set<Notificacion>();
    public DbSet<IntentoNotificacion> IntentosNotificacion => Set<IntentoNotificacion>();
    public DbSet<ContactoDestinatario> ContactosDestinatarios => Set<ContactoDestinatario>();
    public DbSet<EventoRecibido> EventosRecibidos => Set<EventoRecibido>();

    public DbSet<Comprobante> Comprobantes => Set<Comprobante>();
    public DbSet<Ticket> Tickets => Set<Ticket>();

    // Viajes (CQRS + Event Sourcing): escritura = EventosViaje; lectura = ViajesHistorialVista.
    public DbSet<EventoAlmacenado> EventosViaje => Set<EventoAlmacenado>();
    public DbSet<ViajeHistorialItem> ViajesHistorialVista => Set<ViajeHistorialItem>();
    public DbSet<ProyeccionCheckpoint> ProyeccionCheckpoints => Set<ProyeccionCheckpoint>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Notificaciones (TP2): la configuración vive en la propia feature.
        NotificacionesModelo.Configurar(modelBuilder);

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
