using System.ComponentModel;
using Nodisla.Cuaderno.Idiomas;

namespace Nodisla.Cuaderno.Ui.Idiomas;

/// <summary>
/// Los textos del programa como algo a lo que se puede enlazar: <c>{Binding [Comun.Cerrar], Source=...}</c>.
/// </summary>
/// <remarks>
/// Es el truco del cambio de idioma en caliente: cada texto de la pantalla es un enlace al
/// indizador de este objeto, y al cambiar de idioma se avisa de que ha cambiado «Item[]», con lo
/// que WPF vuelve a leer todos a la vez. No hay que reabrir ninguna ventana.
/// </remarks>
public sealed class FuenteDeTextos : INotifyPropertyChanged
{
    private static readonly PropertyChangedEventArgs CambioDelIndizador = new("Item[]");

    private FuenteDeTextos()
    {
        Textos.IdiomaCambiado += (_, _) => PropertyChanged?.Invoke(this, CambioDelIndizador);
    }

    /// <summary>La única fuente: todos los textos de todas las ventanas se enlazan aquí.</summary>
    public static FuenteDeTextos Instancia { get; } = new();

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>El texto de una clave en el idioma en uso.</summary>
    /// <param name="clave">Clave con su apartado delante.</param>
    /// <returns>El texto.</returns>
    public string this[string clave] => Textos.T(clave);
}
