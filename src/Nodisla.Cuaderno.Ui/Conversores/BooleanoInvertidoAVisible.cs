using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Nodisla.Cuaderno.Ui.Conversores;

/// <summary>
/// Igual que <see cref="System.Windows.Controls.BooleanToVisibilityConverter"/>, pero al reves:
/// <c>false</c> se ve y <c>true</c> se esconde. Sirve para alternar dos bloques —«hay ronda
/// abierta» / «no la hay»— sin duplicar la propiedad en el modelo de vista.
/// </summary>
public sealed class BooleanoInvertidoAVisible : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
