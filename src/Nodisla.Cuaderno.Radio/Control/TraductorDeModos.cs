using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Radio.Control;

/// <summary>
/// Traduce entre los modos que entiende el equipo (los nombres de Hamlib y de OmniRig) y los
/// modos ADIF del cuaderno.
/// </summary>
/// <remarks>
/// La traduccion no es simetrica ni puede serlo: el equipo sabe si esta en banda lateral o en
/// un modo de datos, pero no sabe si lo que se transmite por ahi es FT8, JS8 o RTTY. Por eso
/// los modos de paquete (<c>PKTUSB</c> y compañia) se traducen al modo digital que el operador
/// declare en <see cref="ModoAdifParaDatos"/>.
/// </remarks>
public sealed class TraductorDeModos
{
    /// <summary>Traductor con los ajustes de partida: los datos se apuntan como FT8.</summary>
    public static TraductorDeModos PorOmision { get; } = new();

    /// <summary>Modo ADIF con el que se apuntan los modos de datos del equipo.</summary>
    public Modo ModoAdifParaDatos { get; init; } = Modo.Parse("FT8");

    /// <summary>Nombre que se manda al equipo para operar en datos. Por omision, <c>PKTUSB</c>.</summary>
    public string ModoDeDatosDelEquipo { get; init; } = "PKTUSB";

    /// <summary>Pasa el nombre de modo del equipo al modo ADIF del cuaderno.</summary>
    /// <param name="modoDelEquipo">Nombre tal y como lo da el equipo, por ejemplo <c>USB</c>.</param>
    /// <returns>El modo ADIF equivalente, o <see cref="Modo.Vacio"/> si no se reconoce.</returns>
    public Modo DesdeElEquipo(string? modoDelEquipo)
    {
        if (string.IsNullOrWhiteSpace(modoDelEquipo))
        {
            return Modo.Vacio;
        }

        var nombre = modoDelEquipo.Trim().ToUpperInvariant();

        return nombre switch
        {
            "USB" or "ECSSUSB" => Modo.Parse("SSB", "USB"),
            "LSB" or "ECSSLSB" => Modo.Parse("SSB", "LSB"),
            "DSB" => Modo.Parse("SSB"),
            "CW" or "CWR" or "CWN" => Modo.Parse("CW"),
            "RTTY" or "RTTYR" => Modo.Parse("RTTY"),
            "AM" or "AMN" or "AMS" or "SAM" or "SAL" or "SAH" => Modo.Parse("AM"),
            "FM" or "FMN" or "WFM" or "FMW" => Modo.Parse("FM"),
            "FAX" => Modo.Parse("FAX"),
            "PSK" or "PSKR" => Modo.Parse("PSK"),
            "DSTAR" => Modo.Parse("DIGITALVOICE", "DSTAR"),
            "C4FM" => Modo.Parse("DIGITALVOICE", "C4FM"),
            "DMR" => Modo.Parse("DIGITALVOICE", "DMR"),
            "P25" or "DPMR" or "NXDNVN" or "NXDN_N" or "DCR" => Modo.Parse("DIGITALVOICE"),
            "PKTUSB" or "PKTLSB" or "PKTFM" or "PKTAM" or "DIGU" or "DIGL" or "DATA-U" or "DATA-L"
                or "DATA" or "DIG" => ModoAdifParaDatos,
            _ => Modo.Crudo(nombre),
        };
    }

    /// <summary>
    /// Pasa el modo ADIF al nombre que espera el equipo.
    /// </summary>
    /// <param name="modo">Modo del cuaderno.</param>
    /// <param name="frecuencia">
    /// Frecuencia, para decidir la banda lateral cuando el modo es <c>SSB</c> sin submodo:
    /// por debajo de 10 MHz se usa LSB y por encima USB, que es la costumbre.
    /// </param>
    /// <returns>El nombre para el equipo, o nulo si el modo no se sabe traducir.</returns>
    public string? AlEquipo(Modo modo, Frecuencia frecuencia)
    {
        if (modo.EsVacio)
        {
            return null;
        }

        var submodo = modo.Submodo?.ToUpperInvariant();
        if (submodo is "USB" or "LSB")
        {
            return submodo;
        }

        return modo.Principal.ToUpperInvariant() switch
        {
            "SSB" => frecuencia.Megahercios >= 10m ? "USB" : "LSB",
            "CW" => "CW",
            "RTTY" => "RTTY",
            "AM" => "AM",
            "FM" => "FM",
            "FAX" => "FAX",
            "DIGITALVOICE" => submodo switch
            {
                "DSTAR" => "DSTAR",
                "C4FM" => "C4FM",
                "DMR" => "DMR",
                _ => "FM",
            },
            _ => ModoDeDatosDelEquipo,
        };
    }
}
