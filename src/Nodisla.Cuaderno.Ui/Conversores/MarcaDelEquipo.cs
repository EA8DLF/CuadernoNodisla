using System.Globalization;
using System.Windows.Data;
using Nodisla.Cuaderno.Idiomas;

namespace Nodisla.Cuaderno.Ui.Conversores;

/// <summary>Pone « · del equipo» detras del nombre de un dispositivo que es el codec de la radio.</summary>
public sealed class MarcaDelEquipo : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Textos.T("Cabina.Fonia.DelEquipo") : string.Empty;

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
