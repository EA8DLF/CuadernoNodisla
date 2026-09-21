namespace Nodisla.Cuaderno.Dominio.Dxcc;

/// <summary>De donde procede una regla de la tabla de paises.</summary>
internal enum OrigenRegla
{
    /// <summary>Excepcion nominal: la tabla nombra el indicativo completo. Manda sobre todo lo demas.</summary>
    Excepcion,

    /// <summary>Prefijo vigente de <c>cty.dat</c>, sin ventana temporal.</summary>
    Prefijo,

    /// <summary>Ventana historica tomada del fichero de paises de Log4OM.</summary>
    Historica,
}

/// <summary>
/// Una linea de la tabla de paises: que entidad, zonas y coordenada corresponden a una
/// clave (prefijo o indicativo completo) y entre que fechas.
/// </summary>
internal sealed class ReglaDxcc
{
    /// <summary>Prefijo o indicativo completo que dispara la regla.</summary>
    public required string Clave { get; init; }

    /// <summary>Numero de entidad DXCC al que apunta la regla.</summary>
    public required int Dxcc { get; init; }

    /// <summary>Zona CQ propia del prefijo. Nulo si vale la de la entidad.</summary>
    public int? ZonaCq { get; init; }

    /// <summary>Zona ITU propia del prefijo. Nulo si vale la de la entidad.</summary>
    public int? ZonaItu { get; init; }

    /// <summary>Continente propio del prefijo. Nulo si vale el de la entidad.</summary>
    public string? Continente { get; init; }

    /// <summary>Latitud propia del prefijo. Nulo si vale la de la entidad.</summary>
    public double? Latitud { get; init; }

    /// <summary>Longitud propia del prefijo. Nulo si vale la de la entidad.</summary>
    public double? Longitud { get; init; }

    /// <summary>Primer dia en que la regla es aplicable. Nulo si no tiene principio conocido.</summary>
    public DateOnly? Desde { get; init; }

    /// <summary>Ultimo dia en que la regla es aplicable. Nulo si sigue vigente.</summary>
    public DateOnly? Hasta { get; init; }

    /// <summary>De donde sale la regla.</summary>
    public required OrigenRegla Origen { get; init; }

    /// <summary>La ventana esta cerrada por la derecha: describe una asignacion ya terminada.</summary>
    public bool EsHistoricaCerrada => Origen == OrigenRegla.Historica && Hasta is not null;

    /// <summary>
    /// Orden de preferencia cuando varias reglas comparten clave. Primero las ventanas
    /// historicas cerradas, porque describen una epoca concreta; despues el prefijo
    /// vigente de <c>cty.dat</c>, que trae las zonas afinadas; por ultimo, las ventanas
    /// abiertas del fichero historico, que solo repiten lo que ya se sabe.
    /// </summary>
    public int Preferencia => Origen switch
    {
        OrigenRegla.Excepcion => 0,
        OrigenRegla.Historica when Hasta is not null => 1,
        OrigenRegla.Prefijo => 2,
        _ => 3,
    };

    /// <summary>Indica si la regla se aplica en la fecha dada.</summary>
    public bool EsValidaEn(DateOnly fecha) =>
        (Desde is null || fecha >= Desde) && (Hasta is null || fecha <= Hasta);
}
