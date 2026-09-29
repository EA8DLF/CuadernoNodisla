using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Concursos.Catalogo;

/// <summary>Los cinco modos que entiende Cabrillo.</summary>
/// <remarks>
/// La especificacion de Cabrillo solo admite <c>CW</c>, <c>PH</c>, <c>FM</c>, <c>RY</c> y
/// <c>DG</c>. Los ciento y pico modos de ADIF hay que reducirlos a estos cinco, y conviene
/// que esa reduccion este en un solo sitio: la usan tanto el exportador como las reglas del
/// concurso, y si cada uno la hiciera por su cuenta acabarian discrepando.
/// </remarks>
public enum ModoCabrillo
{
    /// <summary>Telegrafia.</summary>
    Cw,
    /// <summary>Fonia: banda lateral y amplitud modulada.</summary>
    Ph,
    /// <summary>Frecuencia modulada.</summary>
    Fm,
    /// <summary>Teletipo.</summary>
    Ry,
    /// <summary>Cualquier otro modo digital.</summary>
    Dg,
}

/// <summary>Reduce un modo de ADIF a la clase con la que trabajan los concursos.</summary>
public static class ClaseDeModo
{
    /// <summary>Clase de un modo del cuaderno.</summary>
    /// <param name="modo">Modo tal y como se guardo el contacto.</param>
    /// <returns>El modo equivalente de Cabrillo.</returns>
    public static ModoCabrillo De(Modo modo) => De(modo.Principal);

    /// <summary>Clase del nombre de un modo.</summary>
    /// <param name="principal">Nombre del modo principal, por ejemplo <c>SSB</c>.</param>
    /// <returns>El modo equivalente de Cabrillo.</returns>
    public static ModoCabrillo De(string? principal) => (principal ?? string.Empty).ToUpperInvariant() switch
    {
        "CW" => ModoCabrillo.Cw,
        "SSB" or "USB" or "LSB" or "AM" or "PH" or "PHONE" or "DIGITALVOICE" or "SSTV" => ModoCabrillo.Ph,
        "FM" => ModoCabrillo.Fm,
        "RTTY" or "RY" => ModoCabrillo.Ry,
        // Todo lo demas —FT8, PSK, MFSK, JS8, olivia…— es digital a ojos del organizador.
        _ => ModoCabrillo.Dg,
    };

    /// <summary>Abreviatura que espera Cabrillo en la linea <c>QSO:</c>.</summary>
    /// <param name="clase">Clase del modo.</param>
    public static string Abreviatura(ModoCabrillo clase) => clase switch
    {
        ModoCabrillo.Cw => "CW",
        ModoCabrillo.Ph => "PH",
        ModoCabrillo.Fm => "FM",
        ModoCabrillo.Ry => "RY",
        _ => "DG",
    };

    /// <summary>
    /// El modo que declaran las bases admite un contacto de esta clase.
    /// </summary>
    /// <remarks>
    /// La frecuencia modulada cuenta como fonia en los concursos que hablan de <c>SSB</c>:
    /// las bases dicen «fonia», y un contacto en FM en seis metros es fonia. Solo se separan
    /// en el Cabrillo, porque ahi la especificacion las distingue.
    /// </remarks>
    /// <param name="modoDeLasBases">Modo declarado en las reglas: <c>CW</c>, <c>SSB</c>, <c>RTTY</c>…</param>
    /// <param name="clase">Clase del contacto.</param>
    public static bool Equivale(string? modoDeLasBases, ModoCabrillo clase) =>
        (modoDeLasBases ?? string.Empty).ToUpperInvariant() switch
        {
            "MIXED" or "ALL" or "" => true,
            "CW" => clase == ModoCabrillo.Cw,
            "SSB" or "PH" or "PHONE" => clase is ModoCabrillo.Ph or ModoCabrillo.Fm,
            "FM" => clase == ModoCabrillo.Fm,
            "RTTY" or "RY" => clase == ModoCabrillo.Ry,
            "DIGI" or "DG" or "DIGITAL" => clase is ModoCabrillo.Dg or ModoCabrillo.Ry,
            _ => false,
        };
}
