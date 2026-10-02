using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Nodisla.Cuaderno.Modos.Cw;

namespace Nodisla.Cuaderno.Ui.Vistas;

/// <summary>
/// El mini espectro del panel de telegrafía: el audio de 200 a 1300 Hz, con la señal principal y
/// las del skimmer marcadas. Un clic fija el tono ahí.
/// </summary>
public sealed class EspectroCw : FrameworkElement
{
    /// <summary>La foto del decodificador que se pinta.</summary>
    public static readonly DependencyProperty EstadoProperty = DependencyProperty.Register(
        nameof(Estado), typeof(EstadoCw), typeof(EspectroCw),
        new FrameworkPropertyMetadata(EstadoCw.Vacio, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Lo que se hace con el tono pulsado (recibe los hercios como <see cref="double"/>).</summary>
    public static readonly DependencyProperty AlPulsarProperty = DependencyProperty.Register(
        nameof(AlPulsar), typeof(ICommand), typeof(EspectroCw));

    /// <summary>Por encima de esto (dB sobre el ruido) el trazo llega arriba.</summary>
    public const double TopeDb = 30;

    /// <summary>Monta el control.</summary>
    public EspectroCw()
    {
        Cursor = Cursors.Cross;
        Focusable = false;
        SnapsToDevicePixels = true;
    }

    /// <summary>La foto del decodificador.</summary>
    public EstadoCw Estado
    {
        get => (EstadoCw)GetValue(EstadoProperty);
        set => SetValue(EstadoProperty, value);
    }

    /// <summary>La orden que recibe el tono pulsado.</summary>
    public ICommand? AlPulsar
    {
        get => (ICommand?)GetValue(AlPulsarProperty);
        set => SetValue(AlPulsarProperty, value);
    }

    /// <summary>De dónde a dónde se pinta (Hz).</summary>
    public (double Desde, double Hasta) Margen
    {
        get
        {
            var e = Estado;
            if (e.EspectroDb.Length < 2) return (200, 1300);
            return (e.PrimeraHz, e.PrimeraHz + ((e.EspectroDb.Length - 1) * e.HzPorCasilla));
        }
    }

    /// <summary>Hercios del punto <paramref name="x"/> del control.</summary>
    public double HerciosEn(double x)
    {
        var (desde, hasta) = Margen;
        var ancho = Math.Max(1, ActualWidth);
        return desde + (Math.Clamp(x, 0, ancho) / ancho * (hasta - desde));
    }

    /// <summary>Pulsa en <paramref name="x"/>, como un clic (para las pruebas y la accesibilidad).</summary>
    public void PulsarEn(double x)
    {
        var hz = HerciosEn(x);
        if (AlPulsar?.CanExecute(hz) == true) AlPulsar.Execute(hz);
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 300 : availableSize.Width, 56);

    /// <inheritdoc />
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseLeftButtonUp(e);
        PulsarEn(e.GetPosition(this).X);
        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnRender(DrawingContext dc)
    {
        ArgumentNullException.ThrowIfNull(dc);
        var ancho = ActualWidth;
        var alto = ActualHeight;
        if (ancho < 2 || alto < 2) return;

        var fondo = Pincel("FondoCampo", Brushes.Black);
        var trazo = Pincel("Acento", Brushes.DodgerBlue);
        var tenue = Pincel("TextoTenue", Brushes.Gray);
        var marca = Pincel("CorrectoTexto", Brushes.LimeGreen);
        var otra = Pincel("AvisoTexto", Brushes.Orange);
        dc.DrawRectangle(fondo, new Pen(Pincel("BordeSuave", Brushes.DimGray), 1), new Rect(0, 0, ancho, alto));

        var e = Estado;
        var (desde, hasta) = Margen;
        double X(double hz) => (hz - desde) / Math.Max(1, hasta - desde) * ancho;

        // Rejilla cada 100 Hz, con rótulo cada 200.
        var lapiz = new Pen(tenue, 0.5) { DashStyle = DashStyles.Dot };
        var tipo = new Typeface("Segoe UI");
        var ppp = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        for (var hz = Math.Ceiling(desde / 100) * 100; hz <= hasta; hz += 100)
        {
            var x = X(hz);
            dc.DrawLine(lapiz, new Point(x, 0), new Point(x, alto));
            if ((int)hz % 200 == 0)
            {
                var texto = new FormattedText(hz.ToString("0", CultureInfo.CurrentCulture), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, tipo, 9, tenue, ppp);
                dc.DrawText(texto, new Point(x + 2, alto - texto.Height));
            }
        }

        // El espectro.
        if (e.EspectroDb.Length >= 2)
        {
            var geometria = new StreamGeometry();
            using (var g = geometria.Open())
            {
                for (var i = 0; i < e.EspectroDb.Length; i++)
                {
                    var hz = e.PrimeraHz + (i * e.HzPorCasilla);
                    var y = alto - (Math.Clamp(e.EspectroDb[i] + 3, 0, TopeDb) / TopeDb * (alto - 2)) - 1;
                    if (i == 0) g.BeginFigure(new Point(X(hz), y), false, false);
                    else g.LineTo(new Point(X(hz), y), true, false);
                }
            }

            geometria.Freeze();
            dc.DrawGeometry(null, new Pen(trazo, 1.2), geometria);
        }

        // Las señales: la principal en verde y gruesa, las demás en ámbar.
        foreach (var c in e.Canales)
        {
            var x = X(c.TonoHz);
            if (x < 0 || x > ancho) continue;
            dc.DrawLine(new Pen(c.EsPrincipal ? marca : otra, c.EsPrincipal ? 2 : 1), new Point(x, 0), new Point(x, alto));
        }

        if (e.TonoFijoHz is { } fijo)
        {
            var x = X(fijo);
            var triangulo = new StreamGeometry();
            using (var g = triangulo.Open())
            {
                g.BeginFigure(new Point(x - 5, 0), true, true);
                g.LineTo(new Point(x + 5, 0), true, false);
                g.LineTo(new Point(x, 7), true, false);
            }

            triangulo.Freeze();
            dc.DrawGeometry(marca, null, triangulo);
        }
    }

    private Brush Pincel(string clave, Brush deReserva) => TryFindResource(clave) as Brush ?? deReserva;
}
