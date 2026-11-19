namespace M8.Notificaciones.Api.Features.Notificaciones.Entities;

/// <summary>
/// Último contacto conocido de un usuario, aprendido de los eventos.
///
/// Por qué existe: M8 no consulta a otros módulos (RNF-05); el contacto llega en
/// "destinatarios" de cada evento. Pero las notificaciones manuales del OPERADOR
/// y los reenvíos de comprobantes solo traen el usuarioId ("se envía al contacto
/// registrado, nunca a una dirección indicada en la solicitud"). Esta tabla es
/// ese "contacto registrado". Son datos personales: no se exponen ni se loguean.
/// </summary>
public class ContactoDestinatario
{
    public Guid UsuarioId { get; set; }
    public string? Email { get; set; }
    public string? Telefono { get; set; }
    public DateTimeOffset ActualizadoEn { get; set; }
}
