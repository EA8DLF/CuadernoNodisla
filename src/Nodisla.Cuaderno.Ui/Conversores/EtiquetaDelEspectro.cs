using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Nodisla.Cuaderno.Aplicacion.CasosDeUso;

namespace Nodisla.Cuaderno.Ui.Conversores;

/// <summary>
/// Frecuencia de radio que cae en un punto del eje del espectro del frontal.
/// </summary>
/// <remarks>
/// El espectro es el del audio de recepcion: de 0 Hz al ancho visible del modem. En banda lateral
/// superior (y en los digitales, que van en superior) cada hercio de audio es el dial mas ese
/// hercio; en inferior es el dial menos. Se usa el VFO que recibe.
/// Entrada: A.Frecuencia, A.Recibe, B.Frecuencia, A.Modo, B.Modo, ancho visible en Hz.
/// Parametro: fraccion del eje, de 0 a 1.
/// </remarks>
public sealed class EtiquetaDelEspectro : IMultiValueConverter
{
    /// <inheritdoc />
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values is not [string frecuenciaA, bool aRecibe, string frecuenciaB, string modoA, string modoB, var ancho]
            || ancho is not (int or double))
        {
            return string.Empty;
        }

        var anchoHz = System.Convert.ToDouble(ancho, CultureInfo.InvariantCulture);

        var usaB = !aRecibe && TextoDeFrecuencia.Leer(frecuenciaB) is { EsCero: false };
        var dial = TextoDeFrecuencia.Leer(usaB ? frecuenciaB : frecuenciaA);
        if (dial.EsCero) return string.Empty;

        var fraccion = double.TryParse(parameter as string, NumberStyles.Float, CultureInfo.InvariantCulture, out var f)
            ? Math.Clamp(f, 0, 1)
            : 0;

        var modo = usaB ? modoB : modoA;
        var signo = modo.Contains("LSB", StringComparison.OrdinalIgnoreCase) ? -1 : 1;
        var hercios = dial.Hercios + (signo * fraccion * anchoHz);

        return (hercios / 1000.0).ToString("N1", CultureInfo.CurrentCulture);
    }

    /// <inheritdoc />
    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        [.. targetTypes.Select(_ => DependencyProperty.UnsetValue)];
}
