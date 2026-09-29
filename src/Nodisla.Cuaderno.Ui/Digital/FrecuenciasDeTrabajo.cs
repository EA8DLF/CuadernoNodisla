using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Ui.Digital;

/// <summary>Una frecuencia de trabajo de un modo: donde se queda la gente.</summary>
/// <remarks>Es una clase con propiedades de lectura y escritura porque se edita en una tabla y se guarda en JSON.</remarks>
public sealed class FrecuenciaDeTrabajo
{
    /// <summary>Modo al que corresponde.</summary>
    public ModoDelModem Modo { get; set; }

    /// <summary>Frecuencia del dial, en megahercios.</summary>
    public decimal Megahercios { get; set; }

    /// <summary>Nota libre: region, uso, lo que sea.</summary>
    public string Nota { get; set; } = string.Empty;

    /// <summary>La banda, sacada de la frecuencia.</summary>
    public string Banda => Dominio.Valores.Banda.DesdeFrecuencia(Frecuencia.DesdeMegahercios(Megahercios)).Nombre;

    /// <summary>El modo en letras, para la tabla.</summary>
    public string ModoTexto => DescripcionDelModo.Nombre(Modo);

    /// <summary>La frecuencia como valor del dominio.</summary>
    public Frecuencia Frecuencia => Frecuencia.DesdeMegahercios(Megahercios);
}

/// <summary>
/// La tabla de frecuencias de trabajo por banda y modo, con las de WSJT-X 2.7 de fabrica.
/// </summary>
/// <remarks>
/// Son las de la lista por omision de WSJT-X (IARU region 1 donde hay diferencia). La tabla es
/// editable: lo que no este aqui, o este mal para su region, lo cambia el operador y se guarda.
/// </remarks>
public static class FrecuenciasDeTrabajo
{
    /// <summary>Las de fabrica.</summary>
    public static List<FrecuenciaDeTrabajo> DeFabrica() =>
    [
        // FT8
        F(ModoDelModem.Ft8, 1.840m), F(ModoDelModem.Ft8, 3.573m), F(ModoDelModem.Ft8, 5.357m), F(ModoDelModem.Ft8, 7.074m),
        F(ModoDelModem.Ft8, 10.136m), F(ModoDelModem.Ft8, 14.074m), F(ModoDelModem.Ft8, 18.100m), F(ModoDelModem.Ft8, 21.074m),
        F(ModoDelModem.Ft8, 24.915m), F(ModoDelModem.Ft8, 28.074m), F(ModoDelModem.Ft8, 50.313m), F(ModoDelModem.Ft8, 50.323m, "DX"),
        F(ModoDelModem.Ft8, 70.154m), F(ModoDelModem.Ft8, 144.174m), F(ModoDelModem.Ft8, 222.080m), F(ModoDelModem.Ft8, 432.174m),
        F(ModoDelModem.Ft8, 1296.174m),

        // FT4
        F(ModoDelModem.Ft4, 3.575m), F(ModoDelModem.Ft4, 7.0475m), F(ModoDelModem.Ft4, 10.140m), F(ModoDelModem.Ft4, 14.080m),
        F(ModoDelModem.Ft4, 18.104m), F(ModoDelModem.Ft4, 21.140m), F(ModoDelModem.Ft4, 24.919m), F(ModoDelModem.Ft4, 28.180m),
        F(ModoDelModem.Ft4, 50.318m), F(ModoDelModem.Ft4, 144.170m),

        // WSPR (dial USB; la señal va 1500 Hz por encima)
        F(ModoDelModem.Wspr, 0.136m), F(ModoDelModem.Wspr, 0.4742m), F(ModoDelModem.Wspr, 1.8366m), F(ModoDelModem.Wspr, 3.5686m),
        F(ModoDelModem.Wspr, 5.2872m), F(ModoDelModem.Wspr, 5.3647m), F(ModoDelModem.Wspr, 7.0386m), F(ModoDelModem.Wspr, 10.1387m),
        F(ModoDelModem.Wspr, 14.0956m), F(ModoDelModem.Wspr, 18.1046m), F(ModoDelModem.Wspr, 21.0946m), F(ModoDelModem.Wspr, 24.9246m),
        F(ModoDelModem.Wspr, 28.1246m), F(ModoDelModem.Wspr, 50.293m), F(ModoDelModem.Wspr, 70.091m), F(ModoDelModem.Wspr, 144.489m),
        F(ModoDelModem.Wspr, 432.300m), F(ModoDelModem.Wspr, 1296.500m),

        // JT65
        F(ModoDelModem.Jt65, 1.838m), F(ModoDelModem.Jt65, 3.570m), F(ModoDelModem.Jt65, 5.357m), F(ModoDelModem.Jt65, 7.076m),
        F(ModoDelModem.Jt65, 10.138m), F(ModoDelModem.Jt65, 14.076m), F(ModoDelModem.Jt65, 18.102m), F(ModoDelModem.Jt65, 21.076m),
        F(ModoDelModem.Jt65, 24.917m), F(ModoDelModem.Jt65, 28.076m), F(ModoDelModem.Jt65, 50.310m), F(ModoDelModem.Jt65, 70.100m),
        F(ModoDelModem.Jt65, 144.120m), F(ModoDelModem.Jt65, 432.065m), F(ModoDelModem.Jt65, 1296.065m),

        // JT9
        F(ModoDelModem.Jt9, 1.839m), F(ModoDelModem.Jt9, 3.572m), F(ModoDelModem.Jt9, 7.078m), F(ModoDelModem.Jt9, 10.140m),
        F(ModoDelModem.Jt9, 14.078m), F(ModoDelModem.Jt9, 18.104m), F(ModoDelModem.Jt9, 21.078m), F(ModoDelModem.Jt9, 24.919m),
        F(ModoDelModem.Jt9, 28.078m), F(ModoDelModem.Jt9, 50.312m), F(ModoDelModem.Jt9, 144.120m),

        // MSK144 (dispersion meteorica)
        F(ModoDelModem.Msk144, 50.260m, "R2/R3"), F(ModoDelModem.Msk144, 50.280m, "R1"), F(ModoDelModem.Msk144, 70.174m),
        F(ModoDelModem.Msk144, 144.150m, "R1"), F(ModoDelModem.Msk144, 144.360m, "R2"), F(ModoDelModem.Msk144, 222.065m),

        // Q65
        F(ModoDelModem.Q65, 50.275m), F(ModoDelModem.Q65, 144.116m, "EME"),

        // FST4 y FST4W: bandas bajas
        F(ModoDelModem.Fst4, 0.136m), F(ModoDelModem.Fst4, 0.4742m), F(ModoDelModem.Fst4, 1.839m),
        F(ModoDelModem.Fst4w, 0.136m), F(ModoDelModem.Fst4w, 0.4742m),
    ];

    /// <summary>
    /// La frecuencia del modo mas cercana al dial actual, dentro de la misma banda; si no hay,
    /// la primera del modo.
    /// </summary>
    public static FrecuenciaDeTrabajo? Para(IEnumerable<FrecuenciaDeTrabajo> tabla, ModoDelModem modo, Frecuencia dial)
    {
        ArgumentNullException.ThrowIfNull(tabla);

        var delModo = tabla.Where(f => f.Modo == modo).ToList();
        if (delModo.Count == 0) return null;

        if (!dial.EsCero)
        {
            var banda = Banda.DesdeFrecuencia(dial);
            var enLaBanda = delModo.Where(f => !banda.EsVacia && f.Banda == banda.Nombre)
                .OrderBy(f => Math.Abs(f.Megahercios - dial.Megahercios))
                .FirstOrDefault();
            if (enLaBanda is not null) return enLaBanda;
        }

        return delModo[0];
    }

    private static FrecuenciaDeTrabajo F(ModoDelModem modo, decimal mhz, string nota = "") =>
        new() { Modo = modo, Megahercios = mhz, Nota = nota };
}
