using System.Windows.Media;
using Nodisla.Cuaderno.Impresion.Diplomas;
using Nodisla.Cuaderno.Impresion.Qsl;

namespace Nodisla.Cuaderno.Ui.Qsl;

/// <summary>
/// Las listas que ofrecen los editores de QSL y de diplomas: tipografias instaladas,
/// alineaciones, ajustes de fondo… Estaticas para que la plantilla del campo de texto, que es la
/// misma en los dos editores, no dependa de cual sea su pantalla.
/// </summary>
public static class CatalogosDelEditor
{
    private static readonly Lazy<IReadOnlyList<string>> FuentesDelSistema = new(() =>
        Fonts.SystemFontFamilies.Select(f => f.Source).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList());

    /// <summary>Tipografias instaladas.</summary>
    public static IReadOnlyList<string> Fuentes => FuentesDelSistema.Value;

    /// <summary>Alineaciones de un texto respecto a su ancla.</summary>
    public static IReadOnlyList<AlineacionDeCampo> Alineaciones { get; } = Enum.GetValues<AlineacionDeCampo>();

    /// <summary>Como se coloca una imagen de fondo.</summary>
    public static IReadOnlyList<AjusteDeFondo> AjustesDeFondo { get; } = Enum.GetValues<AjusteDeFondo>();

    /// <summary>Tamaños de papel del diploma.</summary>
    public static IReadOnlyList<PapelDeDiploma> Papeles { get; } = Enum.GetValues<PapelDeDiploma>();

    /// <summary>Estilos de orla.</summary>
    public static IReadOnlyList<EstiloDeMarco> EstilosDeMarco { get; } = Enum.GetValues<EstiloDeMarco>();

    /// <summary>Usos de una imagen del diploma.</summary>
    public static IReadOnlyList<UsoDeImagen> UsosDeImagen { get; } = Enum.GetValues<UsoDeImagen>();
}
