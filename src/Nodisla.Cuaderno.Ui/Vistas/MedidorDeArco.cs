using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Nodisla.Cuaderno.Ui.Vistas;

/// <summary>
/// El medidor de arco del visor: la escala S arriba, la de potencia debajo y la aguja roja.
/// </summary>
/// <remarks>
/// Como en el equipo (docs\capturas\referencia-ft710-frontal.jpg): «S 1 3 5 7 9» en blanco
/// hasta el nueve, y de ahí «+20 +40 +60 dB» con el arco en rojo; debajo, la escala de
/// potencia. La graduación es la de <c>NivelDeSenal</c> del equipo: S9 a dos tercios del arco.
/// En recepción la aguja marca la señal; transmitiendo, la potencia sobre su escala.
/// Se dibuja en un lienzo fijo de 150 × 58 y se escala a lo que le den.
/// </remarks>
public sealed class MedidorDeArco : FrameworkElement
{
    /// <summary>Señal, de 0 (S0) a 1 (+60 dB); S9 en 0,66.</summary>
    public static readonly DependencyProperty NivelDeSenalProperty = DependencyProperty.Register(
        nameof(NivelDeSenal), typeof(double), typeof(MedidorDeArco),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Potencia, de 0 a 1 (100 W).</summary>
    public static readonly DependencyProperty NivelDePotenciaProperty = DependencyProperty.Register(
        nameof(NivelDePotencia), typeof(double), typeof(MedidorDeArco),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Transmitiendo: la aguja marca la potencia.</summary>
    public static readonly DependencyProperty TransmitiendoProperty = DependencyProperty.Register(
        nameof(Transmitiendo), typeof(bool), typeof(MedidorDeArco),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    private const double Ancho = 150;
    private const double Alto = 58;
    private const double Cx = 75;
    private const double Cy = 118;
    private const double MedioArco = 38;
    private const double S9 = 0.66;

    private static readonly Brush Blanco = Congelado(Color.FromRgb(0xF2, 0xF6, 0xFA));
    private static readonly Brush Rojo = Congelado(Color.FromRgb(0xE2, 0x35, 0x2B));
    private static readonly Brush Gris = Congelado(Color.FromRgb(0xB4, 0xBE, 0xC8));
    private static readonly Brush Azul = Congelado(Color.FromRgb(0x4F, 0xA8, 0xFF));
    private static readonly Typeface Letra = new(new FontFamily("Arial"), FontStyles.Normal, FontWeights.Bold, FontStretches.Condensed);

    /// <summary>Señal, de 0 (S0) a 1 (+60 dB); S9 en 0,66.</summary>
    public double NivelDeSenal
    {
        get => (double)GetValue(NivelDeSenalProperty);
        set => SetValue(NivelDeSenalProperty, value);
    }

    /// <summary>Potencia, de 0 a 1 (100 W).</summary>
    public double NivelDePotencia
    {
        get => (double)GetValue(NivelDePotenciaProperty);
        set => SetValue(NivelDePotenciaProperty, value);
    }

    /// <summary>Transmitiendo: la aguja marca la potencia.</summary>
    public bool Transmitiendo
    {
        get => (bool)GetValue(TransmitiendoProperty);
        set => SetValue(TransmitiendoProperty, value);
    }

    private static SolidColorBrush Congelado(Color color)
    {
        var brocha = new SolidColorBrush(color);
        brocha.Freeze();
        return brocha;
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize) => new(Ancho, Alto);

    /// <inheritdoc />
    protected override void OnRender(DrawingContext dc)
    {
        var escala = Math.Min(ActualWidth / Ancho, ActualHeight / Alto);
        if (escala <= 0 || double.IsNaN(escala)) return;

        dc.PushTransform(new ScaleTransform(escala, escala));
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, Ancho, Alto)));

        var ppd = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        // ── Escala S: rótulos por fuera del arco ──
        const double rS = 100;
        Arco(dc, rS, 0, S9, new Pen(Blanco, 1.4));
        Arco(dc, rS, S9, 1, new Pen(Rojo, 2.6));
        (string Texto, double F)[] marcasS =
        [
            ("1", S9 / 9), ("3", S9 * 3 / 9), ("5", S9 * 5 / 9), ("7", S9 * 7 / 9), ("9", S9),
            ("+20", S9 + ((1 - S9) / 3)), ("+40", S9 + ((1 - S9) * 2 / 3)), ("+60", 1.0),
        ];
        foreach (var (texto, f) in marcasS)
        {
            var rojo = f > S9 + 0.001;
            Raya(dc, f, rS - 4, rS + 1, new Pen(rojo ? Rojo : Blanco, 1.2));
            Rotulo(dc, texto, f, rS + 9, rojo ? Rojo : Blanco, rojo ? 10.5 : 12, ppd);
        }

        Texto(dc, "S", new Point(0, 12), Blanco, 15, ppd);
        Texto(dc, "dB", new Point(136, 0), Azul, 10, ppd);

        // ── Escala de potencia, por dentro ──
        const double rP = 80;
        Arco(dc, rP, 0, 1, new Pen(Gris, 1));
        if (NivelDePotencia > 0.001)
        {
            Arco(dc, rP - 2.5, 0, Math.Clamp(NivelDePotencia, 0, 1), new Pen(Azul, 4));
        }

        (string Texto, double F)[] marcasP = [("0", 0), ("25", 0.25), ("50", 0.5), ("75", 0.75), ("100", 1)];
        foreach (var (texto, f) in marcasP)
        {
            Raya(dc, f, rP, rP + 3, new Pen(Gris, 1));
            Rotulo(dc, texto, f, rP + 10, Gris, 9.5, ppd);
        }

        Texto(dc, "PO", new Point(0, 44), Gris, 9.5, ppd);

        // ── La aguja: la señal en recepción, la potencia transmitiendo ──
        var nivel = Transmitiendo ? NivelDePotencia : NivelDeSenal;
        var angulo = Angulo(Math.Clamp(double.IsNaN(nivel) ? 0 : nivel, 0, 1));
        dc.DrawLine(new Pen(Rojo, 1.8), Punto(angulo, 56), Punto(angulo, rS + 3));

        dc.Pop();
        dc.Pop();
    }

    private static double Angulo(double fraccion) => (-MedioArco + (fraccion * 2 * MedioArco)) * Math.PI / 180;

    private static Point Punto(double angulo, double radio) =>
        new(Cx + (radio * Math.Sin(angulo)), Cy - (radio * Math.Cos(angulo)));

    private static void Arco(DrawingContext dc, double radio, double desde, double hasta, Pen pluma)
    {
        var a = Angulo(desde);
        var b = Angulo(hasta);
        var figura = new PathFigure { StartPoint = Punto(a, radio), IsClosed = false };
        figura.Segments.Add(new ArcSegment(Punto(b, radio), new Size(radio, radio), 0, false, SweepDirection.Clockwise, true));
        dc.DrawGeometry(null, pluma, new PathGeometry([figura]));
    }

    private static void Raya(DrawingContext dc, double f, double r1, double r2, Pen pluma)
    {
        var a = Angulo(f);
        dc.DrawLine(pluma, Punto(a, r1), Punto(a, r2));
    }

    private static void Rotulo(DrawingContext dc, string texto, double f, double radio, Brush color, double tam, double ppd)
    {
        var ft = new FormattedText(texto, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Letra, tam, color, ppd);
        var p = Punto(Angulo(f), radio);
        dc.DrawText(ft, new Point(p.X - (ft.Width / 2), p.Y - (ft.Height / 2)));
    }

    private static void Texto(DrawingContext dc, string texto, Point donde, Brush color, double tam, double ppd) =>
        dc.DrawText(new FormattedText(texto, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Letra, tam, color, ppd), donde);
}
