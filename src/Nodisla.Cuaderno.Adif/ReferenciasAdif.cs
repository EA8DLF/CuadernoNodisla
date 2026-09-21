using Nodisla.Cuaderno.Dominio.Entidades;

namespace Nodisla.Cuaderno.Adif;

/// <summary>
/// Traduccion entre los campos de referencia de ADIF y la tabla de referencias del modelo.
/// </summary>
/// <remarks>
/// Cada programa de activaciones tiene su propio campo (<c>IOTA</c>, <c>POTA_REF</c>,
/// <c>SOTA_REF</c>…) y ademas existe el par generico <c>SIG</c> / <c>SIG_INFO</c> para los que
/// no tienen campo propio. Los dos caminos acaban en la misma tabla.
/// </remarks>
internal static class ReferenciasAdif
{
    /// <summary>Campos ADIF de un programa de activaciones con campo propio.</summary>
    /// <param name="Tipo">Programa del modelo.</param>
    /// <param name="Corresponsal">Campo del lado del corresponsal.</param>
    /// <param name="Propia">Campo del lado de mi estacion.</param>
    internal sealed record Descriptor(TipoDeReferencia Tipo, string Corresponsal, string Propia);

    /// <summary>Programas con campo propio en ADIF, en el orden en que se exportan.</summary>
    public static IReadOnlyList<Descriptor> Descriptores { get; } =
    [
        new(TipoDeReferencia.Iota, "IOTA", "MY_IOTA"),
        new(TipoDeReferencia.Sota, "SOTA_REF", "MY_SOTA_REF"),
        new(TipoDeReferencia.Pota, "POTA_REF", "MY_POTA_REF"),
        new(TipoDeReferencia.Wwff, "WWFF_REF", "MY_WWFF_REF"),
    ];

    /// <summary>Indice por nombre de campo: dice el programa y el lado al que pertenece.</summary>
    public static IReadOnlyDictionary<string, (TipoDeReferencia Tipo, LadoDeReferencia Lado)> PorCampo { get; } =
        ConstruirIndice();

    private static Dictionary<string, (TipoDeReferencia, LadoDeReferencia)> ConstruirIndice()
    {
        var indice = new Dictionary<string, (TipoDeReferencia, LadoDeReferencia)>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in Descriptores)
        {
            indice[d.Corresponsal] = (d.Tipo, LadoDeReferencia.Corresponsal);
            indice[d.Propia] = (d.Tipo, LadoDeReferencia.Propia);
        }
        return indice;
    }

    /// <summary>Traduce el codigo de programa que usa Log4OM en su JSON.</summary>
    public static TipoDeReferencia? TipoDesdeCodigo(string? codigo) => (codigo ?? string.Empty).Trim().ToUpperInvariant() switch
    {
        "IOTA" => TipoDeReferencia.Iota,
        "SOTA" => TipoDeReferencia.Sota,
        "POTA" => TipoDeReferencia.Pota,
        "WWFF" => TipoDeReferencia.Wwff,
        "WCA" => TipoDeReferencia.Wca,
        "DME" => TipoDeReferencia.Dme,
        _ => null,
    };

    /// <summary>Anade una referencia si no estaba ya, para no duplicar lo que llega por dos vias.</summary>
    public static void Anadir(
        Qso qso,
        TipoDeReferencia tipo,
        string? nombrePrograma,
        string codigo,
        LadoDeReferencia lado)
    {
        if (string.IsNullOrWhiteSpace(codigo) && string.IsNullOrWhiteSpace(nombrePrograma)) return;

        foreach (var r in qso.Referencias)
        {
            if (r.Tipo == tipo
                && r.Lado == lado
                && string.Equals(r.Codigo, codigo, StringComparison.OrdinalIgnoreCase)
                && string.Equals(r.NombrePrograma ?? string.Empty, nombrePrograma ?? string.Empty, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        qso.Referencias.Add(new QsoReferencia
        {
            Tipo = tipo,
            NombrePrograma = string.IsNullOrWhiteSpace(nombrePrograma) ? null : nombrePrograma.Trim(),
            Codigo = codigo.Trim(),
            Lado = lado,
        });
    }
}
