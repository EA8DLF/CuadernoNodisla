using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Nodisla.Cuaderno.Idiomas;
using Nodisla.Cuaderno.Ui.Recursos;
using Nodisla.Cuaderno.Ui.VistaModelos;

namespace Nodisla.Cuaderno.Ui.Vistas;

/// <summary>El analizador de espectro de la propia radio, dentro del visor del frontal.</summary>
/// <remarks>
/// <para>
/// Arranca la lectura al mostrarse y la para al ocultarse. Las imagenes las pinta
/// <see cref="VistaModeloAnalizador"/>; aqui se coloca la marca del VFO, los rotulos de los
/// spots (en carriles, <see cref="CarrilesDeSpots"/>), las cifras de los picos y la linea que
/// sigue al raton, y se atienden el clic y la rueda.
/// </para>
/// <para>
/// Rendimiento: los rotulos y las cifras son piezas que se reutilizan; solo se recolocan cuando
/// cambian los spots o lo que se ve (al resintonizar), no en cada una de las once pasadas por
/// segundo.
/// </para>
/// </remarks>
public partial class AnalizadorDelEquipo : UserControl
{
    /// <summary>Alto de un carril de rotulos, en puntos.</summary>
    public const double AltoDeCarril = 15;

    /// <summary>Lo que mide de ancho una letra del rotulo (Consolas de 12 puntos: 0,55 em).</summary>
    public const double AnchoDeLetra = 6.6;

    /// <summary>Lo que mide un rotulo, en puntos.</summary>
    /// <param name="texto">Lo que dice.</param>
    /// <returns>Ancho.</returns>
    public static double AnchoDeRotulo(string texto) => ((texto?.Length ?? 0) * AnchoDeLetra) + 6;

    private static readonly Brush ColorEntidadNueva = Congelar(Color.FromRgb(0xFF, 0x4F, 0xD8));
    private static readonly Brush ColorHuecoNuevo = Congelar(Color.FromRgb(0x4F, 0xE0, 0x7A));
    private static readonly Brush ColorTrabajado = Congelar(Color.FromRgb(0xC9, 0xD3, 0xDD));
    private static readonly Brush ColorDeEscucha = Congelar(Color.FromRgb(0x8C, 0x97, 0xA3));
    private static readonly Brush FondoDeRotulo = Congelar(Color.FromArgb(0xD8, 0x05, 0x07, 0x0A));
    private static readonly Brush ColorDePico = Congelar(Color.FromRgb(0xFF, 0xC2, 0x33));
    private static readonly FontFamily LetraDeRotulo = new("Consolas");

    private readonly List<RotuloDeSpot> _rotulos = [];
    private readonly List<TextBlock> _cifrasDePicos = [];
    private VistaModeloAnalizador? _modelo;

    /// <summary>Monta el control.</summary>
    public AnalizadorDelEquipo()
    {
        InitializeComponent();
        // Se engancha cuando esta A LA VISTA y tiene modelo, sea cual sea el orden en que
        // lleguen las dos cosas. Antes se miraba en Loaded, y en la pestaña Digital el modelo
        // llegaba (DataContextChanged) antes de Loaded y se descartaba: al cambiar de Operar a
        // Digital el analizador se paraba y no volvia (28-09-2026). Con IsVisible, ademas, se
        // deja de leer con el frontal plegado y se vuelve a leer al desplegarlo.
        IsVisibleChanged += (_, _) => Reenganchar();
        DataContextChanged += (_, _) => Reenganchar();
        Loaded += (_, _) => Reenganchar();
        Unloaded += (_, _) => Enganchar(null);
        Lienzo.SizeChanged += (_, _) => Recolocar();
        VistaDeCascada.SizeChanged += (_, _) => Recolocar();

        ZonaDelEspectro.MouseLeftButtonUp += AlHacerClic;
        ZonaDelEspectro.MouseWheel += AlGirarLaRueda;
        ZonaDelEspectro.MouseMove += (_, e) => SeguirAlRaton(e.GetPosition(Lienzo).X);
        ZonaDelEspectro.MouseLeave += (_, _) => CapaDelRaton.Visibility = Visibility.Collapsed;
    }

    /// <summary>Rotulos de spots colocados ahora mismo (para pruebas).</summary>
    public IEnumerable<(FilaDeSpot Fila, double Izquierda, int Carril)> RotulosVisibles =>
        _rotulos.Where(r => r.Caja.Visibility == Visibility.Visible && r.Fila is not null)
            .Select(r => (r.Fila!, Canvas.GetLeft(r.Caja), r.Carril));

    private static SolidColorBrush Congelar(Color color)
    {
        var pincel = new SolidColorBrush(color);
        pincel.Freeze();
        return pincel;
    }

    /// <summary>El color de un spot, segun lo que aporta: entidad nueva, banda o modo nuevos, o ya trabajado.</summary>
    /// <param name="fila">El spot.</param>
    /// <returns>El pincel.</returns>
    public static Brush ColorDelSpot(FilaDeSpot fila)
    {
        ArgumentNullException.ThrowIfNull(fila);
        if (fila.EsEntidadNueva) return ColorEntidadNueva;
        if (fila.EsHuecoNuevo) return ColorHuecoNuevo;
        return fila.EsDeEscuchaAutomatica ? ColorDeEscucha : ColorTrabajado;
    }

    private void Reenganchar() =>
        Enganchar(IsVisible ? DataContext as VistaModeloAnalizador : null);

    private void Enganchar(VistaModeloAnalizador? modelo)
    {
        if (ReferenceEquals(modelo, _modelo)) return;
        if (_modelo is not null)
        {
            _modelo.PropertyChanged -= AlCambiarElModelo;
            _modelo.Parar();
        }

        _modelo = modelo;
        if (_modelo is not null)
        {
            _modelo.PropertyChanged += AlCambiarElModelo;
            _modelo.Arrancar();
        }

        MostrarLaVista();
        MostrarElAviso();
        ColocarLosSpots();
        ColocarLosPicos();
    }

    private void AlCambiarElModelo(object? remitente, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(VistaModeloAnalizador.PosicionDelVfo):
                ColocarLaMarca();
                break;
            case nameof(VistaModeloAnalizador.Recibiendo):
                MostrarElAviso();
                break;
            case nameof(VistaModeloAnalizador.EnTresD) or nameof(VistaModeloAnalizador.Multiple)
                or nameof(VistaModeloAnalizador.HayAudio):
                MostrarLaVista();
                break;
            case nameof(VistaModeloAnalizador.SpotsALaVista) or nameof(VistaModeloAnalizador.SpotsEncima)
                or nameof(VistaModeloAnalizador.CarrilesDeSpots):
                ColocarLosSpots();
                break;
            case nameof(VistaModeloAnalizador.Picos):
                ColocarLosPicos();
                break;
            case nameof(VistaModeloAnalizador.ClicParaSintonizar):
                ZonaDelEspectro.Cursor = _modelo is { ClicParaSintonizar: true } ? Cursors.Cross : null;
                break;
        }
    }

    /// <summary>3DSS: la vista en perspectiva en todo el hueco; si no, traza y cascada.</summary>
    private void MostrarLaVista()
    {
        var tresD = _modelo is { EnTresD: true };
        VistaTresD.Visibility = tresD ? Visibility.Visible : Visibility.Collapsed;
        VistaDeCascada.Visibility = tresD ? Visibility.Collapsed : Visibility.Visible;

        // MULTI: el analizador se queda con la mitad de arriba y abajo van los dos paneles.
        var multiple = _modelo is { Multiple: true };
        FilaMultiple.Height = multiple ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        VistaMultiple.Visibility = multiple ? Visibility.Visible : Visibility.Collapsed;
        AvisoDeAudio.Visibility = _modelo is { HayAudio: true } ? Visibility.Collapsed : Visibility.Visible;
        ZonaDelEspectro.Cursor = _modelo is { ClicParaSintonizar: true } ? Cursors.Cross : null;
        Recolocar();
    }

    private void MostrarElAviso() =>
        Aviso.Visibility = _modelo is { Recibiendo: true } ? Visibility.Collapsed : Visibility.Visible;

    private void Recolocar()
    {
        ColocarLaMarca();
        ColocarLosSpots();
        ColocarLosPicos();
    }

    /// <summary>Alto de la zona de la traza (en 3DSS, todo el hueco del analizador).</summary>
    private double AltoDeLaTraza()
    {
        var alto = VistaDeCascada.ActualHeight > 0 ? VistaDeCascada.ActualHeight : VistaTresD.ActualHeight;
        return _modelo is { EnTresD: true } ? alto : alto / 2;
    }

    private void ColocarLaMarca()
    {
        var ancho = Lienzo.ActualWidth;
        var x = (_modelo?.PosicionDelVfo ?? 0.5) * ancho;
        Canvas.SetLeft(TrianguloDelVfo, x - 3.5);
        Canvas.SetTop(TrianguloDelVfo, 0);
        Canvas.SetLeft(LineaDelVfo, Math.Round(x) - 0.5);
        Canvas.SetTop(LineaDelVfo, 5);
        // En 3DSS la linea baja hasta la pasada de delante; en cascada, solo por la traza.
        LineaDelVfo.Height = Math.Max(0, AltoDeLaTraza() - 5);
    }

    /// <summary>
    /// Los spots en carriles: el rotulo con el indicativo, del color de su novedad, y una raya
    /// fina que baja hasta la traza justo en su frecuencia.
    /// </summary>
    private void ColocarLosSpots()
    {
        var ancho = Lienzo.ActualWidth;
        var altoTraza = AltoDeLaTraza();
        var usados = 0;

        if (_modelo is { SpotsEncima: true, Escala.Valida: true } modelo && ancho > 0 && altoTraza > AltoDeCarril)
        {
            var spots = modelo.SpotsALaVista;
            var carriles = Math.Max(1, Math.Min(modelo.CarrilesDeSpots, (int)(altoTraza * 0.7 / AltoDeCarril)));
            var paraColocar = spots
                .Select(f => new SpotParaColocar(
                    f.Spot.Frecuencia.Hercios,
                    AnchoDeRotulo(f.Indicativo),
                    f.EsEntidadNueva ? 2 : f.EsHuecoNuevo ? 1 : 0))
                .ToList();
            var colocados = CarrilesDeSpots.Colocar(paraColocar, modelo.Escala, ancho, carriles, out _);

            foreach (var c in colocados)
            {
                var fila = spots[c.Indice];
                var rotulo = Rotulo(usados++);
                var color = ColorDelSpot(fila);
                rotulo.Poner(fila, c.Carril, color);
                var arriba = 1 + (c.Carril * AltoDeCarril);
                Canvas.SetLeft(rotulo.Caja, c.Izquierda);
                Canvas.SetTop(rotulo.Caja, arriba);
                rotulo.Caja.Width = AnchoDeRotulo(fila.Indicativo);
                Canvas.SetLeft(rotulo.Raya, Math.Round(c.X) - 0.5);
                Canvas.SetTop(rotulo.Raya, arriba + AltoDeCarril - 1);
                rotulo.Raya.Height = Math.Max(0, altoTraza - arriba - AltoDeCarril + 1);
            }
        }

        for (var i = usados; i < _rotulos.Count; i++) _rotulos[i].Esconder();
    }

    private RotuloDeSpot Rotulo(int i)
    {
        if (i < _rotulos.Count) return _rotulos[i];
        var nuevo = new RotuloDeSpot(this);
        CapaDeSpots.Children.Add(nuevo.Raya);
        CapaDeSpots.Children.Add(nuevo.Caja);
        _rotulos.Add(nuevo);
        return nuevo;
    }

    /// <summary>Las cifras de los picos: «+23» encima de cada marca ámbar.</summary>
    private void ColocarLosPicos()
    {
        var picos = _modelo?.Picos ?? [];
        var ancho = Lienzo.ActualWidth;
        var altoTraza = AltoDeLaTraza();
        var tresD = _modelo is { EnTresD: true };
        for (var i = 0; i < picos.Count && !tresD; i++)
        {
            if (i >= _cifrasDePicos.Count)
            {
                var nueva = new TextBlock
                {
                    Foreground = ColorDePico,
                    FontFamily = LetraDeRotulo,
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    TextTrimming = TextTrimming.None,
                };
                _cifrasDePicos.Add(nueva);
                CapaDePicos.Children.Add(nueva);
            }

            var cifra = _cifrasDePicos[i];
            cifra.Text = string.Create(CultureInfo.InvariantCulture, $"+{picos[i].SobreElSuelo:0}");
            cifra.Visibility = Visibility.Visible;
            Canvas.SetLeft(cifra, Math.Clamp((picos[i].Posicion * ancho) - 8, 0, Math.Max(0, ancho - 24)));
            Canvas.SetTop(cifra, Math.Max(0, (picos[i].Arriba * altoTraza) - 13));
        }

        for (var i = tresD ? 0 : picos.Count; i < _cifrasDePicos.Count; i++) _cifrasDePicos[i].Visibility = Visibility.Collapsed;
    }

    /// <summary>La linea fina y la frecuencia a la que iria un clic, siguiendo al raton.</summary>
    private void SeguirAlRaton(double x)
    {
        if (_modelo is not { ClicParaSintonizar: true, Sintonia: not null, Escala.Valida: true } modelo || Lienzo.ActualWidth <= 0)
        {
            CapaDelRaton.Visibility = Visibility.Collapsed;
            return;
        }

        var ancho = Lienzo.ActualWidth;
        var hz = modelo.FrecuenciaDelClic(x / ancho, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
        var alto = AltoDeLaTraza();
        CapaDelRaton.Visibility = Visibility.Visible;
        Canvas.SetLeft(LineaDelRaton, Math.Round(x) - 0.5);
        Canvas.SetTop(LineaDelRaton, 0);
        LineaDelRaton.Height = Math.Max(0, alto);
        FrecuenciaDelRaton.Text = string.Create(CultureInfo.InvariantCulture, $"{hz / 1_000_000.0:0.000##}");

        // La cifra al lado de la linea, en la parte baja de la traza; se da la vuelta en el borde.
        CajaDelRaton.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var w = CajaDelRaton.DesiredSize.Width;
        Canvas.SetLeft(CajaDelRaton, x + w + 6 > ancho ? x - w - 4 : x + 4);
        Canvas.SetTop(CajaDelRaton, Math.Max(0, alto - 17));
    }

    private async void AlHacerClic(object remitente, MouseButtonEventArgs e)
    {
        if (_modelo is not { ClicParaSintonizar: true, Sintonia: not null } modelo || Lienzo.ActualWidth <= 0) return;
        e.Handled = true;
        var fino = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        await modelo.ClicAsync(e.GetPosition(Lienzo).X / Lienzo.ActualWidth, fino).ConfigureAwait(true);
    }

    private async void AlGirarLaRueda(object remitente, MouseWheelEventArgs e)
    {
        if (_modelo is not { ClicParaSintonizar: true, Sintonia: not null } modelo || e.Delta == 0) return;

        // Se queda con la rueda: girarla encima del analizador mueve el VFO, no la pagina.
        e.Handled = true;
        var muescas = e.Delta / 120;
        if (muescas == 0) muescas = Math.Sign(e.Delta);
        await modelo.RuedaAsync(muescas, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)).ConfigureAwait(true);
        SeguirAlRaton(e.GetPosition(Lienzo).X);
    }

    /// <summary>Un rotulo de spot con su raya: se crea una vez y se reutiliza.</summary>
    private sealed class RotuloDeSpot
    {
        private readonly TextBlock _texto;

        public RotuloDeSpot(AnalizadorDelEquipo dueno)
        {
            _texto = new TextBlock
            {
                FontFamily = LetraDeRotulo,
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                TextTrimming = TextTrimming.None,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Caja = new Border
            {
                Background = FondoDeRotulo,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(2),
                Height = AltoDeCarril - 1,
                Child = _texto,
                Cursor = Cursors.Hand,
            };
            ToolTipService.SetInitialShowDelay(Caja, 250);
            Caja.MouseLeftButtonUp += async (_, e) =>
            {
                e.Handled = true;
                if (Fila is { } fila && dueno._modelo is { } modelo) await modelo.ElegirSpotAsync(fila).ConfigureAwait(true);
            };
            Raya = new Rectangle { Width = 1, Opacity = 0.7, IsHitTestVisible = false };
        }

        public Border Caja { get; }

        public Rectangle Raya { get; }

        public FilaDeSpot? Fila { get; private set; }

        public int Carril { get; private set; }

        public void Poner(FilaDeSpot fila, int carril, Brush color)
        {
            Carril = carril;
            if (!ReferenceEquals(fila, Fila))
            {
                Fila = fila;
                _texto.Text = fila.Indicativo;
                Caja.ToolTip = $"{fila.Detalle}{Environment.NewLine}{Textos.T("Cabina.Analizador.SpotClic")}";
                AutomationProperties.SetName(Caja, fila.Detalle);
            }

            _texto.Foreground = color;
            Caja.BorderBrush = color;
            Raya.Fill = color;
            Caja.Visibility = Visibility.Visible;
            Raya.Visibility = Visibility.Visible;
        }

        public void Esconder()
        {
            Caja.Visibility = Visibility.Collapsed;
            Raya.Visibility = Visibility.Collapsed;
        }
    }
}
