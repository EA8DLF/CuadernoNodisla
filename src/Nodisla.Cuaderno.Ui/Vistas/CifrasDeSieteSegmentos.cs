using System.Windows;
using System.Windows.Media;

namespace Nodisla.Cuaderno.Ui.Vistas;

/// <summary>
/// Una cifra como la del visor del equipo: siete segmentos dibujados, inclinados, sin fuente.
/// </summary>
/// <remarks>
/// La frecuencia del FT-710 sale en cifras de segmentos gruesas y en cursiva. No hay fuente
/// libre que se parezca y que venga con Windows, así que se DIBUJA: cada cifra son sus
/// segmentos, cada punto de millar un cuadradito estrecho. Entiende cifras, punto, coma,
/// guion y espacio; lo demás deja el hueco de una cifra.
/// </remarks>
public sealed class CifrasDeSieteSegmentos : FrameworkElement
{
    /// <summary>Texto que se pinta.</summary>
    public static readonly DependencyProperty TextoProperty = DependencyProperty.Register(
        nameof(Texto), typeof(string), typeof(CifrasDeSieteSegmentos),
        new FrameworkPropertyMetadata(string.Empty,
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Color de los segmentos encendidos.</summary>
    public static readonly DependencyProperty RellenoProperty = DependencyProperty.Register(
        nameof(Relleno), typeof(Brush), typeof(CifrasDeSieteSegmentos),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Alto de una cifra, en puntos.</summary>
    public static readonly DependencyProperty AltoDeCifraProperty = DependencyProperty.Register(
        nameof(AltoDeCifra), typeof(double), typeof(CifrasDeSieteSegmentos),
        new FrameworkPropertyMetadata(24.0,
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    // a b c d e f g, de arriba y en el sentido de las agujas; g es el del medio.
    private static readonly byte[] Mascaras =
    [
        0b0111111, 0b0000110, 0b1011011, 0b1001111, 0b1100110,
        0b1101101, 0b1111101, 0b0000111, 0b1111111, 0b1101111,
    ];

    private const double Inclinacion = 0.12;

    /// <summary>Texto que se pinta.</summary>
    public string Texto
    {
        get => (string)GetValue(TextoProperty);
        set => SetValue(TextoProperty, value);
    }

    /// <summary>Color de los segmentos encendidos.</summary>
    public Brush Relleno
    {
        get => (Brush)GetValue(RellenoProperty);
        set => SetValue(RellenoProperty, value);
    }

    /// <summary>Alto de una cifra, en puntos.</summary>
    public double AltoDeCifra
    {
        get => (double)GetValue(AltoDeCifraProperty);
        set => SetValue(AltoDeCifraProperty, value);
    }

    private static bool EsPunto(char c) => c is '.' or ',';

    // El uno es estrecho, como en el visor del equipo: sin esto deja un hueco a su izquierda.
    private double AnchoDe(char c) => AltoDeCifra * (EsPunto(c) ? 0.24 : c == '1' ? 0.36 : 0.58);

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var ancho = (Texto ?? string.Empty).Sum(AnchoDe) + (AltoDeCifra * Inclinacion);
        return new Size(ancho, AltoDeCifra);
    }

    /// <inheritdoc />
    protected override void OnRender(DrawingContext dc)
    {
        var h = AltoDeCifra;
        var x = 0.0;
        var geometria = new StreamGeometry();

        using (var g = geometria.Open())
        {
            foreach (var c in Texto ?? string.Empty)
            {
                var w = AnchoDe(c);
                if (EsPunto(c))
                {
                    var t = h * 0.14;
                    var cx = x + ((w - t) / 2);
                    Poligono(g, h, (cx, h - t), (cx + t, h - t), (cx + t, h), (cx, h));
                }
                else
                {
                    var mascara = c switch
                    {
                        >= '0' and <= '9' => Mascaras[c - '0'],
                        '-' or '—' or '–' => (byte)0b1000000,
                        _ => (byte)0,
                    };
                    var normal = h * 0.58;
                    Cifra(g, x + w - normal, normal - (h * 0.1), h, mascara);
                }

                x += w;
            }
        }

        geometria.Freeze();
        dc.DrawGeometry(Relleno, null, geometria);
    }

    private static void Cifra(StreamGeometryContext g, double x0, double w, double h, byte mascara)
    {
        var t = h * 0.14;
        var hueco = t * 0.16;
        var izq = x0 + (t / 2);
        var der = x0 + w - (t / 2);
        var arriba = t / 2;
        var medio = h / 2;
        var abajo = h - (t / 2);

        if ((mascara & 1) != 0) Horizontal(g, h, izq + hueco, der - hueco, arriba, t);
        if ((mascara & 2) != 0) Vertical(g, h, der, arriba + hueco, medio - hueco, t);
        if ((mascara & 4) != 0) Vertical(g, h, der, medio + hueco, abajo - hueco, t);
        if ((mascara & 8) != 0) Horizontal(g, h, izq + hueco, der - hueco, abajo, t);
        if ((mascara & 16) != 0) Vertical(g, h, izq, medio + hueco, abajo - hueco, t);
        if ((mascara & 32) != 0) Vertical(g, h, izq, arriba + hueco, medio - hueco, t);
        if ((mascara & 64) != 0) Horizontal(g, h, izq + hueco, der - hueco, medio, t);
    }

    private static void Horizontal(StreamGeometryContext g, double h, double xa, double xb, double y, double t) =>
        Poligono(g, h,
            (xa, y), (xa + (t / 2), y - (t / 2)), (xb - (t / 2), y - (t / 2)),
            (xb, y), (xb - (t / 2), y + (t / 2)), (xa + (t / 2), y + (t / 2)));

    private static void Vertical(StreamGeometryContext g, double h, double x, double ya, double yb, double t) =>
        Poligono(g, h,
            (x, ya), (x + (t / 2), ya + (t / 2)), (x + (t / 2), yb - (t / 2)),
            (x, yb), (x - (t / 2), yb - (t / 2)), (x - (t / 2), ya + (t / 2)));

    // La cursiva: cuanto más arriba, más a la derecha.
    private static void Poligono(StreamGeometryContext g, double h, params (double X, double Y)[] puntos)
    {
        static Point Inclinar(double h, (double X, double Y) p) => new(p.X + ((h - p.Y) * Inclinacion), p.Y);

        g.BeginFigure(Inclinar(h, puntos[0]), isFilled: true, isClosed: true);
        for (var i = 1; i < puntos.Length; i++)
        {
            g.LineTo(Inclinar(h, puntos[i]), isStroked: false, isSmoothJoin: false);
        }
    }
}
