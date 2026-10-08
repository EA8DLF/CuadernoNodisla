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

/// <summary>
/// Ensena un elemento solo cuando el texto que lleva tiene algo que decir.
/// </summary>
/// <remarks>
/// Es la unica manera de que un aviso ocupe cero cuando no hay aviso: con un panel siempre
/// visible, el bloque deja un hueco muerto que en escalas grandes se lleva una fila entera
/// del cuaderno.
/// </remarks>
public sealed class NoEsVacio : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string texto && texto.Length > 0
            ? System.Windows.Visibility.Visible
            : System.Windows.Visibility.Collapsed;

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Lo contrario de <see cref="NoEsVacio"/>: ensena el elemento solo mientras NO hay dato.
/// </summary>
/// <remarks>
/// Es lo que hace falta para las invitaciones —«elija un diploma para ver su detalle»—, que
/// tienen que desaparecer en cuanto hay algo de verdad que ensenar en su sitio.
/// </remarks>
public sealed class EsVacio : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string texto && texto.Length > 0
            ? System.Windows.Visibility.Collapsed
            : System.Windows.Visibility.Visible;

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Convierte una fraccion de cero a uno en un ancho, para pintar barras de medidor.
/// </summary>
/// <remarks>
/// El canal de la barra tiene un ancho fijo dentro del frontal dibujado, que se escala entero
/// con el resto del dibujo. Por eso el ancho se calcula sobre ese tamano y no sobre el ancho
/// real en pantalla: si se midiera en pantalla, la barra daria un salto cada vez que la
/// ventana cambia de tamano.
/// </remarks>
public sealed class FraccionDeAncho : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var fraccion = value is double d && !double.IsNaN(d) ? Math.Clamp(d, 0, 1) : 0;
        var canal = AnchoEnLetras.LeerFactor(parameter);
        return Math.Round(canal * fraccion, 1);
    }

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Da alto a una fila de la rejilla solo cuando su contenido esta a la vista.
/// </summary>
/// <remarks>
/// Una fila proporcional —de las que llevan asterisco— <b>sigue reservando su sitio aunque su
/// contenido este plegado</b>: el bloque desaparece y deja un hueco del mismo tamano. Por eso
/// el alto de la fila tiene que seguir al mismo interruptor que la visibilidad del bloque.
/// El parametro dice cuantas partes se lleva la fila cuando si esta a la vista.
/// </remarks>
public sealed class AltoDeFila : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool visible && visible
            ? new System.Windows.GridLength(AnchoEnLetras.LeerFactor(parameter), System.Windows.GridUnitType.Star)
            : new System.Windows.GridLength(0);

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Convierte una fraccion de cero a uno en el angulo de la aguja de un medidor de arco.
/// </summary>
/// <remarks>
/// La aguja de un medidor analogico barre un arco corto, no media vuelta: el del equipo va de
/// unos cuarenta y cinco grados a la izquierda a otros tantos a la derecha. El parametro dice
/// cuantos grados mide medio arco.
/// </remarks>
public sealed class AnguloDeAguja : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var fraccion = value is double d && !double.IsNaN(d) ? Math.Clamp(d, 0, 1) : 0;
        var medioArco = AnchoEnLetras.LeerFactor(parameter);
        return Math.Round((fraccion * 2 * medioArco) - medioArco, 2);
    }

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Ensena un elemento cuando la condicion es FALSA.
/// </summary>
/// <remarks>
/// Hace falta uno propio: <see cref="Negacion"/> devuelve un booleano, y enlazar un booleano
/// a <c>Visibility</c> no convierte nada —WPF se queda con el valor de partida y el bloque
/// sale SIEMPRE visible—. Es un fallo silencioso y feo: el aviso de «esta cifra no es firme»
/// aparecia tambien sobre las cifras que si lo eran.
/// </remarks>
public sealed class VisibleSiEsFalso : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool cierto && cierto
            ? System.Windows.Visibility.Collapsed
            : System.Windows.Visibility.Visible;

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Invierte un booleano, para las visibilidades que van al reves.</summary>
public sealed class Negacion : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool cierto && !cierto;

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool cierto && !cierto;
}

/// <summary>
/// Convierte una fraccion de cero a uno en una columna de rejilla a estrellas, para repartir
/// el relleno de un medidor sin que el color de cada zona cambie de sitio al crecer o encoger.
/// </summary>
/// <remarks>
/// Un LinearGradientBrush normal reparte sus colores sobre el propio ancho del relleno: si el
/// relleno mide poco, el verde-ambar-rojo entero se aprieta en esa franja estrecha y el medidor
/// miente -parece a punto de saturar con el nivel bajisimo-. Aqui las zonas de color se quedan
/// pintadas a todo el ancho de la pista, siempre en el mismo sitio, y lo unico que crece con el
/// nivel es una mascara que tapa lo que aun no se ha alcanzado: dos columnas a estrellas, el
/// nivel y su resto, y con el parametro «Inverso» se pide la segunda.
/// </remarks>
public sealed class ProporcionAEstrellas : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var nivel = value is double d && !double.IsNaN(d) ? Math.Clamp(d, 0, 1) : 0;
        if (string.Equals(parameter as string, "Inverso", StringComparison.OrdinalIgnoreCase)) nivel = 1 - nivel;

        // Una estrella en cero desaparece de la rejilla: con un minimo practico la columna sigue
        // existiendo -y midiendose- aunque el medidor este a tope o completamente vacio.
        return new System.Windows.GridLength(Math.Max(nivel, 0.0001), System.Windows.GridUnitType.Star);
    }

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
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
