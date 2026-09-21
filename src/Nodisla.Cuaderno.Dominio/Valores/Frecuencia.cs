using System.Globalization;

namespace Nodisla.Cuaderno.Dominio.Valores;

/// <summary>
/// Frecuencia expresada en megahercios, que es la unidad del campo <c>FREQ</c> de ADIF.
/// Se guarda en MHz y no en hercios para no arrastrar errores de redondeo al exportar.
/// </summary>
public readonly record struct Frecuencia : IComparable<Frecuencia>
{
    private Frecuencia(decimal megahercios) => Megahercios = megahercios;

    public decimal Megahercios { get; }

    public static Frecuencia Cero => default;

    public bool EsCero => Megahercios == 0m;

    public long Hercios => (long)Math.Round(Megahercios * 1_000_000m, MidpointRounding.AwayFromZero);

    public decimal Kilohercios => Megahercios * 1000m;

    public static Frecuencia DesdeMegahercios(decimal mhz) => mhz < 0
        ? throw new ArgumentOutOfRangeException(nameof(mhz), "La frecuencia no puede ser negativa.")
        : new Frecuencia(decimal.Round(mhz, 6));

    public static Frecuencia DesdeHercios(long hz) => DesdeMegahercios(hz / 1_000_000m);

    public static Frecuencia DesdeKilohercios(decimal khz) => DesdeMegahercios(khz / 1000m);

    /// <summary>Lee una frecuencia en MHz en formato ADIF (punto decimal, cultura invariante).</summary>
    public static bool TryParseAdif(string? texto, out Frecuencia frecuencia)
    {
        frecuencia = Cero;
        if (string.IsNullOrWhiteSpace(texto)) return false;
        if (!decimal.TryParse(texto.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var mhz))
            return false;
        if (mhz < 0) return false;
        frecuencia = DesdeMegahercios(mhz);
        return true;
    }

    /// <summary>Escribe la frecuencia tal y como la espera ADIF: MHz con punto decimal.</summary>
    public string AAdif() => Megahercios.ToString("0.######", CultureInfo.InvariantCulture);

    public int CompareTo(Frecuencia otra) => Megahercios.CompareTo(otra.Megahercios);

    public override string ToString() => $"{AAdif()} MHz";

    public static bool operator <(Frecuencia a, Frecuencia b) => a.Megahercios < b.Megahercios;
    public static bool operator >(Frecuencia a, Frecuencia b) => a.Megahercios > b.Megahercios;
    public static bool operator <=(Frecuencia a, Frecuencia b) => a.Megahercios <= b.Megahercios;
    public static bool operator >=(Frecuencia a, Frecuencia b) => a.Megahercios >= b.Megahercios;
}
