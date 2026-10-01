using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Nodisla.Cuaderno.Ui.Conversores;

/// <summary>
/// Dice si un numero esta en una lista escrita como «0,1,6,8».
/// </summary>
/// <remarks>
/// <para>
/// Es lo que enciende las entradas de la barra de navegacion: cada boton lleva en su
/// <c>Tag</c> las paginas que le tocan, y se marca como elegido si la pagina que se ve esta
/// entre ellas. Asi un solo estilo vale para todos los botones.
/// </para>
/// <para>
/// Como conversor multiple recibe (numero, lista). Como conversor simple recibe el numero y
/// la lista va en el parametro. Si el destino es <see cref="Visibility"/>, devuelve
/// <c>Visible</c> o <c>Collapsed</c>; si no, un booleano.
/// </para>
/// </remarks>
public sealed class EstaEnLaLista : IValueConverter, IMultiValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Responder(value, parameter, targetType);

    /// <inheritdoc />
    public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture) =>
        Responder(values.Length > 0 ? values[0] : null, values.Length > 1 ? values[1] : null, targetType);

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;

    /// <inheritdoc />
    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        [];

    /// <summary>La cuenta de verdad.</summary>
    /// <param name="numero">El numero que se busca.</param>
    /// <param name="lista">La lista, separada por comas.</param>
    /// <returns>Cierto si esta.</returns>
    public static bool Esta(object? numero, object? lista)
    {
        if (numero is not int n || lista?.ToString() is not { Length: > 0 } texto) return false;

        foreach (var trozo in texto.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (int.TryParse(trozo, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) && v == n) return true;
        }

        return false;
    }

    private static object Responder(object? numero, object? lista, Type destino)
    {
        var esta = Esta(numero, lista);
        return destino == typeof(Visibility) ? (esta ? Visibility.Visible : Visibility.Collapsed) : esta;
    }
}
