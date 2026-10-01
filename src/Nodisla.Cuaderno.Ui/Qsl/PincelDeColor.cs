using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Nodisla.Cuaderno.Ui.Qsl;

/// <summary>
/// Pasa un color escrito (<c>#RRGGBB</c>, <c>#AARRGGBB</c> o un nombre) a pincel para la
/// muestra del editor. Un color que no se entiende sale transparente en vez de romper el enlace.
/// </summary>
public sealed class PincelDeColor : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var pincel = new SolidColorBrush(DibujanteDeQsl.AColor(value as string, Colors.Transparent));
        pincel.Freeze();
        return pincel;
    }

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
