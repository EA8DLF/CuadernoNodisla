using System.Globalization;

namespace Nodisla.Cuaderno.Dominio.Valores;

/// <summary>Forma en que se expresa un informe de senal.</summary>
public enum FormaDeInforme
{
    /// <summary>Sin informe.</summary>
    Ninguno,
    /// <summary>Legibilidad y senal, tipico de fonia: <c>59</c>.</summary>
    Rs,
    /// <summary>Legibilidad, senal y tono, tipico de telegrafia: <c>599</c>.</summary>
    Rst,
    /// <summary>Relacion senal-ruido en decibelios, tipico de FT8 y similares: <c>-15</c>.</summary>
    Decibelios,
    /// <summary>Cualquier otra cosa que venga en el campo, conservada tal cual.</summary>
    Libre,
}

/// <summary>Informe de senal enviado o recibido (campos <c>RST_SENT</c> y <c>RST_RCVD</c>).</summary>
public readonly record struct Informe
{
    private Informe(string texto, FormaDeInforme forma)
    {
        Texto = texto;
        Forma = forma;
    }

    /// <summary>Texto tal y como se guarda en ADIF.</summary>
    public string Texto { get; }

    public FormaDeInforme Forma { get; }

    public static Informe Ninguno => default;

    public bool EsVacio => string.IsNullOrEmpty(Texto);

    /// <summary>Informe por omision para el modo dado: 59, 599 o el SNR que corresponda.</summary>
    public static Informe PorOmisionPara(Modo modo)
    {
        if (modo.EsVacio) return Ninguno;
        if (modo.UsaInformeEnDecibelios) return Ninguno; // el SNR real lo aporta el decodificador
        return modo.Principal is "CW" or "RTTY" or "PSK" or "TOR" or "THOR" or "THRB" or "HELL"
            ? new Informe("599", FormaDeInforme.Rst)
            : new Informe("59", FormaDeInforme.Rs);
    }

    public static Informe Parse(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return Ninguno;
        var v = texto.Trim();

        if (v.Length is 2 or 3 && v.All(char.IsAsciiDigit))
        {
            var legibilidad = v[0] - '0';
            var senal = v[1] - '0';
            var tonoOk = v.Length == 2 || (v[2] - '0') is >= 1 and <= 9;
            if (legibilidad is >= 1 and <= 5 && senal is >= 1 and <= 9 && tonoOk)
            {
                return new Informe(v, v.Length == 2 ? FormaDeInforme.Rs : FormaDeInforme.Rst);
            }
        }

        if (int.TryParse(v, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var db)
            && db is >= -40 and <= 60 && (v[0] is '-' or '+'))
        {
            return new Informe(v, FormaDeInforme.Decibelios);
        }

        return new Informe(v, FormaDeInforme.Libre);
    }

    /// <summary>Informe en decibelios, tal y como lo reportan FT8 y modos similares.</summary>
    public static Informe DesdeDecibelios(int db) =>
        new(db.ToString("+00;-00;+00", CultureInfo.InvariantCulture), FormaDeInforme.Decibelios);

    /// <summary>Valor en decibelios si la forma es SNR; nulo en cualquier otro caso.</summary>
    public int? Decibelios => Forma == FormaDeInforme.Decibelios
        ? int.Parse(Texto, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture)
        : null;

    public override string ToString() => Texto;

    public static implicit operator string(Informe i) => i.Texto;
}
