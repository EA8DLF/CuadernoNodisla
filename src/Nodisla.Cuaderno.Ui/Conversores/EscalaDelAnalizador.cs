using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Nodisla.Cuaderno.Aplicacion.CasosDeUso;

namespace Nodisla.Cuaderno.Ui.Conversores;

/// <summary>
/// Una marca de la escala bajo el analizador del visor, como la del equipo:
/// <c>-80k -40k 21.073.300 +40k +80k</c>.
/// </summary>
/// <remarks>
/// <para>
/// Valores: el ancho del analizador en hercios (0 si no hay espectro de la radio) y, detrás,
/// los mismos seis de <see cref="EtiquetaDelEspectro"/>. El parámetro es la posición de la
/// marca, de 0 (izquierda) a 1 (derecha). Un octavo valor opcional es donde empieza la
/// escala en FIX (0 si no está en FIX).
/// </para>
/// <para>
/// Con ancho: la marca del centro es la frecuencia del dial con puntos de millar y las demás
/// su distancia al centro («-40k»). Sin ancho, lo que hay dentro es la cascada del audio, y
/// la marca dice la frecuencia de verdad de ese punto, que es lo honrado.
/// </para>
/// <para>
/// En FIX la escala no sigue al dial: cada marca es la frecuencia de ese punto, como en la radio.
/// </para>
/// </remarks>
public sealed class EscalaDelAnalizador : IMultiValueConverter
{
    private static readonly EtiquetaDelEspectro DelAudio = new();

    /// <inheritdoc />
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values is not { Length: 7 or 8 }) return string.Empty;

        var fraccion = double.TryParse(parameter as string, NumberStyles.Float, CultureInfo.InvariantCulture, out var f)
            ? Math.Clamp(f, 0, 1)
            : 0.5;

        var ancho = values[0] is double d && !double.IsNaN(d) ? d : 0;
        if (ancho <= 0)
        {
            return DelAudio.Convert(values[1..7], targetType, fraccion.ToString(CultureInfo.InvariantCulture), culture);
        }

        if (values is [.., double inicioFijo] && values.Length == 8 && inicioFijo > 0)
        {
            return (inicioFijo + (fraccion * ancho)).ToString("#,##0", culture);
        }

        if (Math.Abs(fraccion - 0.5) < 0.001)
        {
            var usaB = values[2] is false && values[3] is string b && TextoDeFrecuencia.Leer(b) is { EsCero: false };
            var dial = TextoDeFrecuencia.Leer((usaB ? values[3] : values[1]) as string ?? string.Empty);
            return dial.EsCero ? string.Empty : dial.Hercios.ToString("#,##0", culture);
        }

        var desplazamiento = (fraccion - 0.5) * ancho / 1000.0;
        return desplazamiento.ToString("+0.#;-0.#", CultureInfo.InvariantCulture) + "k";
    }

    /// <inheritdoc />
    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        [.. targetTypes.Select(_ => DependencyProperty.UnsetValue)];
}

/// <summary>
/// La línea de encima del analizador: la que ponga quien lo pinta (en el equipo,
/// <c>CENTER FAST1 SPAN 200kHz</c>) o, si no pone nada, la del audio.
/// </summary>
/// <remarks>Valores: el rótulo que llega de fuera y el ancho visible del audio en hercios.</remarks>
public sealed class RotuloDelAnalizador : IMultiValueConverter
{
    /// <inheritdoc />
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values is [string { Length: > 0 } rotulo, ..]) return rotulo;

        return values is [_, int or double, ..]
            ? $"AUDIO RX   SPAN {System.Convert.ToDouble(values[1], CultureInfo.InvariantCulture) / 1000.0:0.0}kHz"
            : "AUDIO RX";
    }

    /// <inheritdoc />
    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        [.. targetTypes.Select(_ => DependencyProperty.UnsetValue)];
}
