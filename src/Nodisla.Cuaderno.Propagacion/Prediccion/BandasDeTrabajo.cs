using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Propagacion.Prediccion;

/// <summary>
/// Bandas que se predicen y la frecuencia con la que se calcula cada una.
/// </summary>
/// <remarks>
/// <para>
/// No se usa el centro del tramo ADIF sino la frecuencia donde de verdad se trabaja el DX en
/// la Region 1: en 10 metros el centro ADIF caeria en 28,85 MHz, que es FM, y en 6 metros en
/// 52 MHz, donde no hay nadie.
/// </para>
/// <para>
/// De 6 metros se da solo lo que hace la capa F2. La capa E esporadica, que es lo que abre esa
/// banda la mayor parte de las veces, no esta en el modelo y no se puede predecir con el flujo
/// solar.
/// </para>
/// </remarks>
public static class BandasDeTrabajo
{
    /// <summary>Bandas de HF que se predicen, de la mas baja a la mas alta.</summary>
    public static IReadOnlyList<(Banda Banda, double FrecuenciaMhz)> Bandas { get; } = Construir();

    private static IReadOnlyList<(Banda, double)> Construir()
    {
        (string Nombre, double Frecuencia)[] tabla =
        [
            ("160m", 1.840),
            ("80m", 3.650),
            ("60m", 5.357),
            ("40m", 7.100),
            ("30m", 10.125),
            ("20m", 14.150),
            ("17m", 18.110),
            ("15m", 21.200),
            ("12m", 24.940),
            ("10m", 28.400),
            ("6m", 50.150),
        ];

        var lista = new List<(Banda, double)>(tabla.Length);
        foreach (var (nombre, frecuencia) in tabla)
        {
            if (Banda.TryParse(nombre, out var banda))
            {
                lista.Add((banda, frecuencia));
            }
        }

        return lista;
    }
}
