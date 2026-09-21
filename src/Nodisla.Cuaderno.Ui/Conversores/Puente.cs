using System.Windows;

namespace Nodisla.Cuaderno.Ui.Conversores;

/// <summary>
/// Puente para llegar al modelo de vista desde las columnas de la rejilla. Las columnas de un
/// <c>DataGrid</c> no forman parte del arbol visual y no heredan el contexto de datos: sin este
/// rodeo no hay manera de que una columna se oculte segun una casilla del panel de columnas.
/// </summary>
public sealed class Puente : Freezable
{
    /// <summary>Propiedad de dependencia que guarda el objeto al que se quiere llegar.</summary>
    public static readonly DependencyProperty DatosProperty =
        DependencyProperty.Register(nameof(Datos), typeof(object), typeof(Puente), new UIPropertyMetadata(null));

    /// <summary>Objeto al que apuntan las columnas, normalmente el modelo de vista de la ventana.</summary>
    public object? Datos
    {
        get => GetValue(DatosProperty);
        set => SetValue(DatosProperty, value);
    }

    /// <inheritdoc />
    protected override Freezable CreateInstanceCore() => new Puente();
}
