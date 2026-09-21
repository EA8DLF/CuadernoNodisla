namespace Nodisla.Cuaderno.Dominio.Valores;

/// <summary>Banda de radioaficionado segun la enumeracion <c>Band</c> de ADIF 3.1.5.</summary>
public readonly record struct Banda
{
    private Banda(string nombre) => Nombre = nombre;

    /// <summary>Nombre ADIF de la banda en minusculas, por ejemplo <c>20m</c>.</summary>
    public string Nombre { get; }

    public static Banda Vacia => default;

    public bool EsVacia => string.IsNullOrEmpty(Nombre);

    /// <summary>Limites ADIF de cada banda, en MHz. El orden es el de la tabla del estandar.</summary>
    private static readonly (string Nombre, decimal Inferior, decimal Superior)[] Limites =
    [
        ("2190m",       0.1357m,      0.1378m),
        ("630m",        0.472m,       0.479m),
        ("560m",        0.501m,       0.504m),
        ("160m",        1.8m,         2.0m),
        ("80m",         3.5m,         4.0m),
        ("60m",         5.06m,        5.45m),
        ("40m",         7.0m,         7.3m),
        ("30m",        10.1m,        10.15m),
        ("20m",        14.0m,        14.35m),
        ("17m",        18.068m,      18.168m),
        ("15m",        21.0m,        21.45m),
        ("12m",        24.89m,       24.99m),
        ("10m",        28.0m,        29.7m),
        ("8m",         40.0m,        45.0m),
        ("6m",         50.0m,        54.0m),
        ("5m",         54.000001m,   69.9m),
        ("4m",         70.0m,        71.0m),
        ("2m",        144.0m,       148.0m),
        ("1.25m",     222.0m,       225.0m),
        ("70cm",      420.0m,       450.0m),
        ("33cm",      902.0m,       928.0m),
        ("23cm",     1240.0m,      1300.0m),
        ("13cm",     2300.0m,      2450.0m),
        ("9cm",      3300.0m,      3500.0m),
        ("6cm",      5650.0m,      5925.0m),
        ("3cm",     10000.0m,     10500.0m),
        ("1.25cm",  24000.0m,     24250.0m),
        ("6mm",     47000.0m,     47200.0m),
        ("4mm",     75500.0m,     81000.0m),
        ("2.5mm",  119980.0m,    120020.0m),
        ("2mm",    134000.0m,    149000.0m),
        ("1mm",    241000.0m,    250000.0m),
        ("submm",  300000.0m,   7500000.0m),
    ];

    /// <summary>Todas las bandas ADIF, de la mas baja a la mas alta.</summary>
    public static IReadOnlyList<Banda> Todas { get; } =
        Limites.Select(l => new Banda(l.Nombre)).ToArray();

    public static bool TryParse(string? texto, out Banda banda)
    {
        banda = Vacia;
        if (string.IsNullOrWhiteSpace(texto)) return false;
        var v = texto.Trim().ToLowerInvariant();
        foreach (var l in Limites)
        {
            if (l.Nombre == v) { banda = new Banda(l.Nombre); return true; }
        }
        return false;
    }

    public static Banda Parse(string? texto) =>
        TryParse(texto, out var b) ? b : throw new FormatException($"Banda no reconocida: {texto}");

    /// <summary>Deduce la banda a partir de la frecuencia. Devuelve <see cref="Vacia"/> si cae fuera.</summary>
    public static Banda DesdeFrecuencia(Frecuencia frecuencia)
    {
        if (frecuencia.EsCero) return Vacia;
        var mhz = frecuencia.Megahercios;
        foreach (var l in Limites)
        {
            if (mhz >= l.Inferior && mhz <= l.Superior) return new Banda(l.Nombre);
        }
        return Vacia;
    }

    /// <summary>Limites de la banda en MHz.</summary>
    public (decimal Inferior, decimal Superior) Limite
    {
        get
        {
            if (EsVacia) throw new InvalidOperationException("Banda vacia.");
            foreach (var l in Limites)
            {
                if (l.Nombre == Nombre) return (l.Inferior, l.Superior);
            }
            throw new InvalidOperationException($"Banda desconocida: {Nombre}");
        }
    }

    /// <summary>Indica si la frecuencia cae dentro de esta banda.</summary>
    public bool Contiene(Frecuencia frecuencia)
    {
        if (EsVacia || frecuencia.EsCero) return false;
        var (inf, sup) = Limite;
        return frecuencia.Megahercios >= inf && frecuencia.Megahercios <= sup;
    }

    public override string ToString() => Nombre;

    public static implicit operator string(Banda b) => b.Nombre;
}
