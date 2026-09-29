using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Nodisla.Cuaderno.Ui.Vistas.Frontales;

/// <summary>
/// El medidor de barra de las pantallas ICOM y del LCD del FT-891: la escala S arriba
/// (1 3 5 7 9 +20 +40 +60) y la barra debajo; transmitiendo, la potencia (0…100 %).
/// </summary>
/// <remarks>
/// Los mismos niveles que <see cref="MedidorDeArco"/>: señal de 0 (S0) a 1 (+60 dB), S9 en 0,66;
/// potencia de 0 a 1. Se dibuja entero en <see cref="OnRender"/>: no hay enlaces que se rompan.
/// </remarks>
public sealed class MedidorDeBarra : FrameworkElement
{
    /// <summary>Señal, de 0 (S0) a 1 (+60 dB); S9 en 0,66.</summary>
    public static readonly DependencyProperty NivelDeSenalProperty = DependencyProperty.Register(
        nameof(NivelDeSenal), typeof(double), typeof(MedidorDeBarra),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Potencia, de 0 a 1.</summary>
    public static readonly DependencyProperty NivelDePotenciaProperty = DependencyProperty.Register(
        nameof(NivelDePotencia), typeof(double), typeof(MedidorDeBarra),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Transmitiendo: la barra marca la potencia.</summary>
    public static readonly DependencyProperty TransmitiendoProperty = DependencyProperty.Register(
        nameof(Transmitiendo), typeof(bool), typeof(MedidorDeBarra),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Color de la escala y de la barra.</summary>
    public static readonly DependencyProperty TintaProperty = DependencyProperty.Register(
        nameof(Tinta), typeof(Brush), typeof(MedidorDeBarra),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Color de la barra por encima de S9 (y en transmisión).</summary>
    public static readonly DependencyProperty TintaFuerteProperty = DependencyProperty.Register(
        nameof(TintaFuerte), typeof(Brush), typeof(MedidorDeBarra),
        new FrameworkPropertyMetadata(Brushes.OrangeRed, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly (double Donde, string Texto)[] MarcasS =
    [
        (1 / 9d * 0.66, "1"), (3 / 9d * 0.66, "3"), (5 / 9d * 0.66, "5"), (7 / 9d * 0.66, "7"),
        (0.66, "9"), (0.66 + (0.34 / 3), "+20"), (0.66 + (0.68 / 3), "+40"), (1, "+60"),
    ];

    private static readonly (double Donde, string Texto)[] MarcasPo =
    [
        (0, "0"), (0.25, "25"), (0.5, "50"), (0.75, "75"), (1, "100%"),
    ];

    /// <summary>Monta el medidor.</summary>
    public MedidorDeBarra() => SnapsToDevicePixels = true;

    /// <summary>Señal, de 0 (S0) a 1 (+60 dB); S9 en 0,66.</summary>
    public double NivelDeSenal
    {
        get => (double)GetValue(NivelDeSenalProperty);
        set => SetValue(NivelDeSenalProperty, value);
    }

    /// <summary>Potencia, de 0 a 1.</summary>
    public double NivelDePotencia
    {
        get => (double)GetValue(NivelDePotenciaProperty);
        set => SetValue(NivelDePotenciaProperty, value);
    }

    /// <summary>Transmitiendo: la barra marca la potencia.</summary>
    public bool Transmitiendo
    {
        get => (bool)GetValue(TransmitiendoProperty);
        set => SetValue(TransmitiendoProperty, value);
    }

    /// <summary>Color de la escala y de la barra.</summary>
    public Brush Tinta
    {
        get => (Brush)GetValue(TintaProperty);
        set => SetValue(TintaProperty, value);
    }

    /// <summary>Color de la barra por encima de S9.</summary>
    public Brush TintaFuerte
    {
        get => (Brush)GetValue(TintaFuerteProperty);
        set => SetValue(TintaFuerteProperty, value);
    }

    /// <inheritdoc />
    protected override void OnRender(DrawingContext dc)
    {
        ArgumentNullException.ThrowIfNull(dc);
        var ancho = ActualWidth;
        var alto = ActualHeight;
        if (ancho < 20 || alto < 8) return;

        var margen = 14d;
        var util = ancho - (2 * margen);
        var letra = Math.Max(6, alto * 0.36);
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var tipo = new Typeface(new FontFamily("Arial"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        var pluma = new Pen(Tinta, 1);
        pluma.Freeze();

        var marcas = Transmitiendo ? MarcasPo : MarcasS;
        var rotulo = new FormattedText(Transmitiendo ? "Po" : "S", CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, tipo, letra, Tinta, dpi);
        dc.DrawText(rotulo, new Point(0, 0));

        var lineaDeEscala = letra + 2;
        dc.DrawLine(pluma, new Point(margen, lineaDeEscala), new Point(margen + util, lineaDeEscala));
        foreach (var (donde, texto) in marcas)
        {
            var x = margen + (donde * util);
            dc.DrawLine(pluma, new Point(x, lineaDeEscala - 3), new Point(x, lineaDeEscala));
            var t = new FormattedText(texto, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, tipo,
                letra, !Transmitiendo && donde > 0.67 ? TintaFuerte : Tinta, dpi);
            dc.DrawText(t, new Point(Math.Min(ancho - t.Width, Math.Max(0, x - (t.Width / 2))), 0));
        }

        var nivel = Math.Clamp(Transmitiendo ? NivelDePotencia : NivelDeSenal, 0, 1);
        var arriba = lineaDeEscala + 3;
        var altoDeBarra = Math.Max(3, alto - arriba - 1);
        var s9 = Transmitiendo ? 1 : 0.66;

        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(0x30, 0x80, 0x80, 0x80)), null,
            new Rect(margen, arriba, util, altoDeBarra));
        dc.DrawRectangle(Transmitiendo ? TintaFuerte : Tinta, null,
            new Rect(margen, arriba, Math.Min(nivel, s9) * util, altoDeBarra));
        if (nivel > s9)
        {
            dc.DrawRectangle(TintaFuerte, null, new Rect(margen + (s9 * util), arriba, (nivel - s9) * util, altoDeBarra));
        }
    }
}
