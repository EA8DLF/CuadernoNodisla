using System.ComponentModel;
using System.Windows.Media;
using Nodisla.Cuaderno.Idiomas;
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

    /// <summary>Alineaciones, con su nombre en el idioma del programa.</summary>
    public static IReadOnlyList<OpcionDelEditor> OpcionesDeAlineacion { get; } = Opciones(Alineaciones, "Qsl.Alineacion");

    /// <summary>Ajustes del fondo, con su nombre.</summary>
    public static IReadOnlyList<OpcionDelEditor> OpcionesDeAjuste { get; } = Opciones(AjustesDeFondo, "Qsl.Ajuste");

    /// <summary>Papeles, con su nombre.</summary>
    public static IReadOnlyList<OpcionDelEditor> OpcionesDePapel { get; } = Opciones(Papeles, "Qsl.Papel");

    /// <summary>Estilos de orla, con su nombre.</summary>
    public static IReadOnlyList<OpcionDelEditor> OpcionesDeOrla { get; } = Opciones(EstilosDeMarco, "Qsl.Orla");

    /// <summary>Usos de imagen, con su nombre.</summary>
    public static IReadOnlyList<OpcionDelEditor> OpcionesDeUso { get; } = Opciones(UsosDeImagen, "Qsl.UsoDeImagen");

    private static IReadOnlyList<OpcionDelEditor> Opciones<T>(IEnumerable<T> valores, string apartado)
        where T : struct, Enum =>
        valores.Select(v => new OpcionDelEditor(v, $"{apartado}.{v}")).ToList();
}

/// <summary>
/// Un valor de una lista del editor con su nombre en el idioma del programa. Las listas enlazan
/// <c>SelectedValue</c> a <see cref="Valor"/> y enseñan <see cref="Nombre"/>, que cambia solo al
/// cambiar de idioma.
/// </summary>
public sealed class OpcionDelEditor : INotifyPropertyChanged
{
    private readonly string _clave;

    /// <summary>Crea la opción.</summary>
    /// <param name="valor">El valor (un enumerado).</param>
    /// <param name="clave">Clave del texto.</param>
    public OpcionDelEditor(object valor, string clave)
    {
        Valor = valor;
        _clave = clave;
        Textos.AlCambiar(this, static o => o.PropertyChanged?.Invoke(o, new PropertyChangedEventArgs(nameof(Nombre))));
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>El valor.</summary>
    public object Valor { get; }

    /// <summary>Como se lee.</summary>
    public string Nombre => Textos.T(_clave);

    /// <inheritdoc />
    public override string ToString() => Nombre;
}
