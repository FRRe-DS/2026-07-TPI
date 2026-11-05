namespace M8.Notificaciones.Api.Features.Documentos.Entities;

public class Comprobante
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ViajeId { get; set; }
    public Guid? PagoId {get ; set; }
    public Guid ClienteId {get; set;}
    public string Motivo {get; set;} = string.Empty;
    public string Estado {get; set;} = "PENDIENTE";
    public decimal MontoTotal { get; set;}
    public string Moneda { get; set; } = "ARS";
    public DateTime FechaEmision { get; set; } = DateTime.UtcNow;
    public string? RutaArchivoPdf { get; set; }
    public bool EnviadoPorEmail { get; set; } = false;
}
