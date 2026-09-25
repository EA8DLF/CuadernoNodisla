using System.Globalization;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Concursos.Cabrillo;

/// <summary>
/// Escribe la frecuencia como la quiere Cabrillo.
/// </summary>
/// <remarks>
/// La especificacion admite dos cosas distintas en la misma columna: en HF, la frecuencia de
/// verdad en kilohercios; de 50 MHz para arriba, un <b>designador de banda</b> fijo —<c>50</c>,
/// <c>144</c>, <c>432</c>, <c>1.2G</c>…— y no la frecuencia. Escribir <c>144300</c> donde toca
/// <c>144</c> es un error clasico que hace que el robot no reconozca la banda.
/// </remarks>
public static class FrecuenciaCabrillo
{
    private static readonly (decimal Desde, decimal Hasta, string Designador)[] Designadores =
    [
        (50m, 54m, "50"),
        (70m, 71m, "70"),
        (144m, 148m, "144"),
        (222m, 225m, "222"),
        (420m, 450m, "432"),
        (902m, 928m, "902"),
        (1240m, 1300m, "1.2G"),
        (2300m, 2450m, "2.3G"),
        (3300m, 3500m, "3.4G"),
        (5650m, 5925m, "5.7G"),
        (10000m, 10500m, "10G"),
        (24000m, 24250m, "24G"),
        (47000m, 47200m, "47G"),
        (75500m, 81000m, "75G"),
        (119980m, 120020m, "122G"),
        (134000m, 149000m, "134G"),
        (241000m, 250000m, "241G"),
    ];

    /// <summary>Convierte una frecuencia, con la banda como red de seguridad.</summary>
    /// <param name="frecuencia">Frecuencia del contacto; puede venir a cero.</param>
    /// <param name="banda">
    /// Banda del contacto. Se usa cuando no hay frecuencia, que pasa a menudo en los
    /// contactos importados de otros programas.
    /// </param>
    /// <returns>Lo que va en la columna de frecuencia, o cadena vacia si no se sabe.</returns>
    public static string De(Frecuencia frecuencia, Banda banda)
    {
        var mhz = frecuencia.Megahercios;
        if (mhz <= 0m && !banda.EsVacia) mhz = banda.Limite.Inferior;
        if (mhz <= 0m) return string.Empty;

        if (mhz >= 300000m) return "LIGHT";
        foreach (var (desde, hasta, designador) in Designadores)
        {
            if (mhz >= desde && mhz <= hasta) return designador;
        }
        // De 50 MHz para arriba solo valen los designadores. Si la frecuencia cae fuera de
        // todos ellos, no hay nada honesto que escribir: se devuelve vacio y el exportador
        // avisa, en vez de inventarse una banda.
        if (mhz >= 50m) return string.Empty;

        // Por debajo de 50 MHz va la frecuencia de verdad, en kilohercios enteros.
        return Math.Round(mhz * 1000m, MidpointRounding.AwayFromZero)
            .ToString("0", CultureInfo.InvariantCulture);
    }
}
