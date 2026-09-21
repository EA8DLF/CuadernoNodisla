using System.Globalization;
using System.Windows.Controls;
using System.Windows.Data;

namespace Nodisla.Cuaderno.Ui.Conversores;

/// <summary>
/// Convierte el tamano de letra en un ancho. Asi los campos y las columnas se miden en letras
/// y no en pixeles: al subir la escala al 200 % todo crece a la vez y nada se solapa.
/// </summary>
public sealed class AnchoEnLetras : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var letra = value is double d ? d : 14.0;
        var letras = LeerFactor(parameter);
        return Math.Round(letra * letras, 0);
    }

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    /// <summary>Lee el numero de letras que ocupa el elemento.</summary>
    internal static double LeerFactor(object? parameter) =>
        parameter is string texto
        && double.TryParse(texto, NumberStyles.Float, CultureInfo.InvariantCulture, out var f)
            ? f
            : 10.0;
}

/// <summary>
/// Reparte el alto de la ventana entre la zona de entrada y el cuaderno.
/// </summary>
/// <remarks>
/// El cuaderno tiene preferencia: primero se le reserva lo que necesita para su cabecera, sus
/// filtros, la paginacion y cinco contactos, y lo que sobra es el tope de la zona superior, que
/// se desplaza si no cabe. Todo se mide en letras, no en pixeles, para que la cuenta siga
/// valiendo con la escala al 200 %.
/// </remarks>
public sealed class AlturaDeLaZonaSuperior : IMultiValueConverter
{
    /// <summary>Letras de alto que necesita el cuaderno para verse con cinco contactos.</summary>
    private const double LetrasDelCuaderno = 25.0;

    /// <summary>Letras de alto que ocupa la barra de estado.</summary>
    private const double LetrasDeLaBarraDeEstado = 4.0;

    /// <summary>Lo minimo que se le deja a la zona superior aunque la ventana sea diminuta.</summary>
    private const double LetrasMinimasArriba = 7.0;

    /// <inheritdoc />
    public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Length < 2 || values[0] is not double alto || values[1] is not double letra)
        {
            return double.PositiveInfinity;
        }

        if (double.IsNaN(alto) || alto <= 0 || letra <= 0) return double.PositiveInfinity;

        var reservado = letra * (LetrasDelCuaderno + LetrasDeLaBarraDeEstado);
        var disponible = alto - reservado;

        return Math.Max(letra * LetrasMinimasArriba, Math.Min(disponible, alto * 0.55));
    }

    /// <inheritdoc />
    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Lo mismo que <see cref="AnchoEnLetras"/>, pero para el ancho de una columna.</summary>
public sealed class AnchoDeColumnaEnLetras : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var letra = value is double d ? d : 14.0;
        return new DataGridLength(Math.Round(letra * AnchoEnLetras.LeerFactor(parameter), 0));
    }

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
