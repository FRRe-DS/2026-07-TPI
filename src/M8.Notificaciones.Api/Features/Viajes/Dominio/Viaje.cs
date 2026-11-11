namespace M8.Notificaciones.Api.Features.Viajes.Dominio;

/// <summary>
/// Agregado Viaje: el corazón de Event Sourcing.
///
/// No se guarda en ninguna tabla. Su estado se RECONSTRUYE cada vez,
/// reproduciendo en orden los eventos del flujo (rehidratación). Por eso no
/// tiene dependencias de EF Core ni de ASP.NET: es C# puro y se prueba con
/// "dado (eventos previos) / cuando (comando) / entonces (evento o error)".
///
/// Tiene dos tipos de métodos, y la diferencia es la clave para explicarlo:
///  - Comandos (Solicitar, AsignarConductor, Finalizar): VALIDAN las reglas y,
///    si se cumplen, producen un evento nuevo. Pueden fallar.
///  - Aplicar(evento): cambia el estado en memoria. NO valida nada, porque un
///    evento es un hecho que ya ocurrió y quedó guardado: no se puede "rechazar
///    el pasado". Si validara, un cambio de reglas futuro rompería la lectura
///    de eventos viejos.
/// </summary>
public sealed class Viaje
{
    public Guid Id { get; private set; }

    /// <summary>null mientras no exista ningún evento (el viaje "no existe").</summary>
    public EstadoViaje? Estado { get; private set; }

    public Guid? ConductorId { get; private set; }

    /// <summary>
    /// Cantidad de eventos aplicados. El próximo evento se guarda con Version + 1,
    /// y el índice único (ViajeId, Version) de la base detecta si dos comandos
    /// compitieron por el mismo número (concurrencia optimista).
    /// </summary>
    public int Version { get; private set; }

    public DateTimeOffset? SolicitadoEn { get; private set; }
    public DateTimeOffset? AsignadoEn { get; private set; }
    public DateTimeOffset? FinalizadoEn { get; private set; }

    public bool Existe => Version > 0;

    /// <summary>Reconstruye el viaje reproduciendo los eventos en el orden en que ocurrieron.</summary>
    public static Viaje Rehidratar(IEnumerable<EventoDeViaje> eventos)
    {
        var viaje = new Viaje();
        foreach (var evento in eventos)
        {
            viaje.Aplicar(evento);
        }
        return viaje;
    }

    // ----------------------------- Comandos -----------------------------

    /// <summary>Regla: ViajeSolicitado solo puede ser el primer evento del flujo.</summary>
    public ViajeSolicitado Solicitar(Guid viajeId, DateTimeOffset ocurridoEn)
    {
        if (Existe)
        {
            throw new ReglaDeNegocioException(
                "El viaje ya fue solicitado",
                $"El viaje {viajeId} ya tiene {Version} evento(s); VIAJE_SOLICITADO solo puede ser el primero.");
        }

        var evento = new ViajeSolicitado(viajeId, ocurridoEn);
        Aplicar(evento);
        return evento;
    }

    /// <summary>Regla: solo se asigna conductor a un viaje SOLICITADO (no dos veces, no después de finalizar).</summary>
    public ConductorAsignado AsignarConductor(Guid conductorId, DateTimeOffset ocurridoEn)
    {
        ExigirEstado(EstadoViaje.SOLICITADO, "asignar un conductor");

        var evento = new ConductorAsignado(Id, conductorId, ocurridoEn);
        Aplicar(evento);
        return evento;
    }

    /// <summary>Regla: solo se finaliza un viaje ASIGNADO (no se puede finalizar sin conductor).</summary>
    public ViajeFinalizado Finalizar(DateTimeOffset ocurridoEn)
    {
        ExigirEstado(EstadoViaje.ASIGNADO, "finalizar el viaje");

        var evento = new ViajeFinalizado(Id, ocurridoEn);
        Aplicar(evento);
        return evento;
    }

    private void ExigirEstado(EstadoViaje requerido, string accion)
    {
        if (!Existe)
        {
            throw new ReglaDeNegocioException(
                "Transición de viaje inválida",
                $"No se puede {accion}: el viaje no existe (todavía no se recibió VIAJE_SOLICITADO).");
        }

        if (Estado != requerido)
        {
            throw new ReglaDeNegocioException(
                "Transición de viaje inválida",
                $"No se puede {accion}: el viaje {Id} está {Estado} y se requiere {requerido}.");
        }
    }

    // ------------------- Aplicar: un método por tipo de evento -------------------

    /// <summary>Despacha al Aplicar específico según el tipo del evento.</summary>
    public void Aplicar(EventoDeViaje evento)
    {
        switch (evento)
        {
            case ViajeSolicitado e: Aplicar(e); break;
            case ConductorAsignado e: Aplicar(e); break;
            case ViajeFinalizado e: Aplicar(e); break;
            default:
                // Si aparece un tipo nuevo y nadie escribió su Aplicar, preferimos
                // fallar fuerte antes que reconstruir un estado incorrecto.
                throw new InvalidOperationException($"Evento de viaje desconocido: {evento.GetType().Name}");
        }
    }

    private void Aplicar(ViajeSolicitado e)
    {
        Id = e.ViajeId;
        Estado = EstadoViaje.SOLICITADO;
        SolicitadoEn = e.OcurridoEn;
        Version++;
    }

    private void Aplicar(ConductorAsignado e)
    {
        Estado = EstadoViaje.ASIGNADO;
        ConductorId = e.ConductorId;
        AsignadoEn = e.OcurridoEn;
        Version++;
    }

    private void Aplicar(ViajeFinalizado e)
    {
        Estado = EstadoViaje.FINALIZADO;
        FinalizadoEn = e.OcurridoEn;
        Version++;
    }
}
