using System.Windows;
using System.Windows.Media;

namespace Nodisla.Cuaderno.Ui.Vistas;

/// <summary>
/// La miniatura de banda del visor: la «caja» a la derecha de ATT/IPO/DNF/AGC.
/// </summary>
/// <remarks>
/// En el equipo es un trapecio con la forma de la banda y una marca donde está el dial
/// (docs\capturas\referencia-ft710-frontal.jpg). Aquí, lo mismo: la línea de base, la banda
/// levantada entre sus bordes y la marca en <see cref="Posicion"/>. No se inventa señal dentro:
/// solo la forma de la banda y dónde cae el dial. Posición negativa (fuera de banda o sin
/// frecuencia): la banda sin marca.
/// </remarks>
public sealed class MiniaturaDeBanda : FrameworkElement
{
    /// <summary>Dónde cae el dial en la banda, de 0 a 1. Negativo: sin marca.</summary>
    public static readonly DependencyProperty PosicionProperty = DependencyProperty.Register(
        nameof(Posicion), typeof(double), typeof(MiniaturaDeBanda),
        new FrameworkPropertyMetadata(-1.0, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly Brush Fondo = Congelado(Color.FromRgb(0x0B, 0x0E, 0x12));
    private static readonly Brush Relleno = Congelado(Color.FromArgb(0x55, 0x6C, 0xC3, 0xFF));
    private static readonly Pen Marco = new Pen(Congelado(Color.FromRgb(0x3A, 0x44, 0x4F)), 1).Congelada();
    private static readonly Pen Linea = new Pen(Congelado(Color.FromRgb(0xE6, 0xEC, 0xF2)), 1.2).Congelada();
    private static readonly Pen Marca = new Pen(Congelado(Color.FromRgb(0xE2, 0x35, 0x2B)), 2).Congelada();

    /// <summary>Dónde cae el dial en la banda, de 0 a 1. Negativo: sin marca.</summary>
    public double Posicion
    {
        get => (double)GetValue(PosicionProperty);
        set => SetValue(PosicionProperty, value);
    }

    private static SolidColorBrush Congelado(Color color)
    {
        var brocha = new SolidColorBrush(color);
        brocha.Freeze();
        return brocha;
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize) => new(60, 20);

    /// <inheritdoc />
    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w < 10 || h < 8) return;

        dc.DrawRectangle(Fondo, Marco, new Rect(0.5, 0.5, w - 1, h - 1));

        // La banda: un trapecio entre el 25 % y el 75 % del ancho, como en el equipo.
        var baseY = h - 4;
        var altoY = 5.0;
        var x0 = w * 0.25;
        var x1 = w * 0.75;
        var inclinacion = Math.Min(6, w * 0.03);

        var trapecio = new StreamGeometry();
        using (var g = trapecio.Open())
        {
            g.BeginFigure(new Point(4, baseY), false, false);
            g.LineTo(new Point(x0, baseY), true, false);
            g.LineTo(new Point(x0 + inclinacion, altoY), true, false);
            g.LineTo(new Point(x1 - inclinacion, altoY), true, false);
            g.LineTo(new Point(x1, baseY), true, false);
            g.LineTo(new Point(w - 4, baseY), true, false);
        }

        trapecio.Freeze();

        var interior = new RectangleGeometry(new Rect(new Point(x0 + inclinacion, altoY), new Point(x1 - inclinacion, baseY)));
        dc.DrawGeometry(Relleno, null, interior);
        dc.DrawGeometry(null, Linea, trapecio);

        var p = Posicion;
        if (p >= 0 && !double.IsNaN(p))
        {
            var x = x0 + inclinacion + (Math.Clamp(p, 0, 1) * (x1 - x0 - (2 * inclinacion)));
            dc.DrawLine(Marca, new Point(x, 2), new Point(x, baseY));
        }
    }
}

/// <summary>Plumas congeladas en una línea.</summary>
internal static class PlumaCongelada
{
    /// <summary>Congela la pluma y la devuelve.</summary>
    public static Pen Congelada(this Pen pluma)
    {
        pluma.Freeze();
        return pluma;
    }
}
