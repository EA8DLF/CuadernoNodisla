namespace Nodisla.Cuaderno.Ui.VistaModelos;

/// <summary>Los planes de canales de la banda ciudadana (27 MHz) que ofrece la botonera.</summary>
public enum PlanCb
{
    /// <summary>
    /// «11m»: la banda de 11 metros sin plan propio. Usa la tabla de los 40 canales (la misma que
    /// CEPT y USA): los canales son el sitio donde se escucha en 11 m, y asi la tecla no inventa
    /// frecuencias. Se deja aparte para que el operador vea que esta en 11 m y no en CB legal.
    /// </summary>
    Once,

    /// <summary>Reino Unido, plan 27/81: 40 canales de 27,60125 a 27,99125 MHz, cada 10 kHz.</summary>
    Uk,

    /// <summary>CEPT (ECC/DEC/(11)03): la tabla de los 40 canales, 26,965 a 27,405 MHz.</summary>
    Cept,

    /// <summary>Polonia: la tabla CEPT 5 kHz por debajo (canal 1 = 26,960 MHz).</summary>
    Pl,

    /// <summary>EE. UU. (FCC 47 CFR 95.963): la misma tabla de los 40 canales que CEPT.</summary>
    Usa,
}

/// <summary>
/// Frecuencia de cada canal de la banda ciudadana segun el plan.
/// </summary>
/// <remarks>
/// <para>
/// Tabla de los 40 canales: FCC, 47 CFR § 95.963 «CB Rules: channel frequencies»
/// (ecfr.gov), que es la misma que adopta la CEPT en ECC/DEC/(11)03. Tiene los huecos de 20 kHz
/// entre 3-4, 7-8, 11-12, 15-16 y 19-20 (los canales «alfa» de radiocontrol) y el 23-24-25
/// desordenado: 23 = 27,255, 24 = 27,235, 25 = 27,245 MHz.
/// </para>
/// <para>
/// Reino Unido: Ofcom IR 2027 (UK Interface Requirement, «CB 27/81»): canal 1 = 27,60125 MHz y
/// paso de 10 kHz hasta el 40 = 27,99125 MHz. Polonia: la tabla CEPT desplazada −5 kHz (el
/// «cero» polaco, 26,960 MHz el canal 1).
/// </para>
/// </remarks>
public static class CanalesCb
{
    /// <summary>Numero de canales de cada plan.</summary>
    public const int Canales = 40;

    /// <summary>Los 40 canales en hercios, tabla FCC 95.963 / CEPT.</summary>
    private static readonly long[] Tabla =
    [
        26_965_000, 26_975_000, 26_985_000, 27_005_000, 27_015_000, 27_025_000, 27_035_000, 27_055_000,
        27_065_000, 27_075_000, 27_085_000, 27_105_000, 27_115_000, 27_125_000, 27_135_000, 27_155_000,
        27_165_000, 27_175_000, 27_185_000, 27_205_000, 27_215_000, 27_225_000, 27_255_000, 27_235_000,
        27_245_000, 27_265_000, 27_275_000, 27_285_000, 27_295_000, 27_305_000, 27_315_000, 27_325_000,
        27_335_000, 27_345_000, 27_355_000, 27_365_000, 27_375_000, 27_385_000, 27_395_000, 27_405_000,
    ];

    /// <summary>Los planes en el orden de la botonera.</summary>
    public static IReadOnlyList<PlanCb> Planes { get; } = [PlanCb.Once, PlanCb.Uk, PlanCb.Cept, PlanCb.Pl, PlanCb.Usa];

    /// <summary>Rotulo corto del plan para su tecla.</summary>
    /// <param name="plan">Plan.</param>
    /// <returns>«11m», «UK», «CEPT», «PL» o «USA».</returns>
    public static string Rotulo(PlanCb plan) => plan switch
    {
        PlanCb.Once => "11m",
        PlanCb.Uk => "UK",
        PlanCb.Cept => "CEPT",
        PlanCb.Pl => "PL",
        _ => "USA",
    };

    /// <summary>Frecuencia de un canal en hercios.</summary>
    /// <param name="plan">Plan de canales.</param>
    /// <param name="canal">Canal, de 1 a 40.</param>
    /// <returns>La frecuencia en hercios.</returns>
    public static long Hercios(PlanCb plan, int canal)
    {
        if (canal is < 1 or > Canales)
        {
            throw new ArgumentOutOfRangeException(nameof(canal), canal, "Los canales van del 1 al 40.");
        }

        return plan switch
        {
            PlanCb.Uk => 27_601_250 + ((canal - 1) * 10_000L),
            PlanCb.Pl => Tabla[canal - 1] - 5_000,
            _ => Tabla[canal - 1],
        };
    }

    /// <summary>El canal del plan en el que cae una frecuencia, o nulo.</summary>
    /// <param name="plan">Plan de canales.</param>
    /// <param name="hercios">Frecuencia del dial.</param>
    /// <returns>El canal si el dial esta a menos de 500 Hz de el.</returns>
    public static int? CanalEn(PlanCb plan, long hercios)
    {
        for (var canal = 1; canal <= Canales; canal++)
        {
            if (Math.Abs(Hercios(plan, canal) - hercios) <= 500) return canal;
        }

        return null;
    }
}
