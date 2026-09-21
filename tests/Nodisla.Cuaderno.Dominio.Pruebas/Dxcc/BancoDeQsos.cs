using System.Globalization;

namespace Nodisla.Cuaderno.Dominio.Pruebas.Dxcc;

/// <summary>Un QSO real de EA8DLF tal y como lo resolvio Log4OM.</summary>
public sealed record QsoDeReferencia(
    string Indicativo, DateOnly Fecha, int Dxcc, int ZonaCq, int ZonaItu, string Continente, string Pais);

/// <summary>
/// Banco de pruebas de oro: 1.836 contactos reales ya resueltos por Log4OM 2.40.
/// Sirve para medir contra que se compara el resolutor propio.
/// </summary>
public static class BancoDeQsos
{
    private static readonly Lazy<IReadOnlyList<QsoDeReferencia>> Perezoso = new(Cargar);

    /// <summary>Los contactos del banco, en el orden del fichero.</summary>
    public static IReadOnlyList<QsoDeReferencia> Todos => Perezoso.Value;

    private static IReadOnlyList<QsoDeReferencia> Cargar()
    {
        var ensamblado = typeof(BancoDeQsos).Assembly;
        var nombre = ensamblado.GetManifestResourceNames()
            .Single(n => n.EndsWith("qsos-log4om.tsv", StringComparison.Ordinal));
        using var flujo = ensamblado.GetManifestResourceStream(nombre)!;
        using var lector = new StreamReader(flujo);

        var lista = new List<QsoDeReferencia>(2000);
        while (lector.ReadLine() is { } linea)
        {
            if (linea.Length == 0 || linea[0] == '#') continue;
            var c = linea.Split('\t');
            if (c.Length < 7) continue;
            lista.Add(new QsoDeReferencia(
                c[0],
                DateOnly.ParseExact(c[1], "yyyy-MM-dd", CultureInfo.InvariantCulture),
                int.Parse(c[2], CultureInfo.InvariantCulture),
                int.Parse(c[3], CultureInfo.InvariantCulture),
                int.Parse(c[4], CultureInfo.InvariantCulture),
                c[5],
                c[6]));
        }
        return lista;
    }
}
