using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Nodisla.Cuaderno.Idiomas;
using Nodisla.Cuaderno.Ui.Qsl;

namespace Nodisla.Cuaderno.Ui.Vistas.Controles;

/// <summary>
/// Selector visual de color: botón con la muestra y el nombre, y un desplegable con la paleta,
/// los colores del diseño, los recientes, la opacidad, un selector libre de tono, saturación y
/// brillo y, plegado en «Avanzado», el código.
/// </summary>
/// <remarks>
/// <see cref="Color"/> es el TEXTO guardado en la plantilla (<c>#RRGGBB</c>, <c>#AARRGGBB</c> o un
/// nombre en inglés); el selector lo lee tal cual y solo lo reescribe cuando se elige otro color,
/// con <see cref="PaletaDeColores.Formatear"/>. Así las plantillas de antes cargan igual.
/// </remarks>
public partial class SelectorDeColor : UserControl
{
    /// <summary>El color, como texto guardado. Enlace de ida y vuelta por omisión.</summary>
    public static readonly DependencyProperty ColorProperty = DependencyProperty.Register(
        nameof(Color),
        typeof(string),
        typeof(SelectorDeColor),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, AlCambiarElColor)
        {
            DefaultUpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
        });

    /// <summary>Si el campo admite transparencia: enseña la barra de opacidad.</summary>
    public static readonly DependencyProperty AdmiteOpacidadProperty = DependencyProperty.Register(
        nameof(AdmiteOpacidad), typeof(bool), typeof(SelectorDeColor), new PropertyMetadata(false, AlCambiarUnaOpcion));

    /// <summary>Si el campo puede quedarse sin color (el recuadro detrás del texto).</summary>
    public static readonly DependencyProperty AdmiteVacioProperty = DependencyProperty.Register(
        nameof(AdmiteVacio), typeof(bool), typeof(SelectorDeColor), new PropertyMetadata(false, AlCambiarUnaOpcion));

    /// <summary>Lo que se lee cuando no hay color, y el botón para quitarlo. Vacío: «Sin color» en el idioma del programa.</summary>
    public static readonly DependencyProperty TextoSiVacioProperty = DependencyProperty.Register(
        nameof(TextoSiVacio), typeof(string), typeof(SelectorDeColor), new PropertyMetadata(string.Empty, AlCambiarUnaOpcion));

    /// <summary>Qué color es («Color del texto»): para el lector de pantalla y la cabecera. Vacío: «Color».</summary>
    public static readonly DependencyProperty TituloProperty = DependencyProperty.Register(
        nameof(Titulo), typeof(string), typeof(SelectorDeColor), new PropertyMetadata(string.Empty, AlCambiarUnaOpcion));

    /// <summary>
    /// De dónde salen los «Colores del diseño». Se hereda: basta ponerlo en el panel del editor
    /// (<c>controles:SelectorDeColor.OrigenDeColores="{Binding}"</c>) y lo ven todos sus selectores.
    /// </summary>
    public static readonly DependencyProperty OrigenDeColoresProperty = DependencyProperty.RegisterAttached(
        "OrigenDeColores", typeof(IColoresDelDiseno), typeof(SelectorDeColor), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits));

    /// <summary>
    /// El color elegido, heredado dentro del desplegable: las muestras lo miran para poner su
    /// marca. Es heredado (no un enlace por nombre) para que el desplegable funcione igual
    /// dentro del Popup que pintado aparte para las capturas.
    /// </summary>
    public static readonly DependencyProperty ColorActualProperty = DependencyProperty.RegisterAttached(
        "ColorActual", typeof(string), typeof(SelectorDeColor), new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.Inherits));

    private const double PasoFino = 0.02;
    private const double PasoGrueso = 0.1;

    private double _tono;
    private double _saturacion;
    private double _brillo;
    private byte _alfa = 255;
    private bool _escribiendo;
    private string _alAbrir = string.Empty;
    private ColoresRecientes? _recientes;

    /// <summary>Crea el selector.</summary>
    public SelectorDeColor()
    {
        InitializeComponent();
        listaDeRecientes.ItemsSource = Recientes.Muestras;
        Refrescar();
        Textos.AlCambiar(this, static s => s.Refrescar());
    }

    /// <summary>El color, como texto guardado en la plantilla.</summary>
    public string Color
    {
        get => (string)GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    /// <summary>Si se puede elegir opacidad.</summary>
    public bool AdmiteOpacidad
    {
        get => (bool)GetValue(AdmiteOpacidadProperty);
        set => SetValue(AdmiteOpacidadProperty, value);
    }

    /// <summary>Si se puede dejar sin color.</summary>
    public bool AdmiteVacio
    {
        get => (bool)GetValue(AdmiteVacioProperty);
        set => SetValue(AdmiteVacioProperty, value);
    }

    /// <summary>El texto de «sin color».</summary>
    public string TextoSiVacio
    {
        get => (string)GetValue(TextoSiVacioProperty) is { Length: > 0 } texto ? texto : Textos.T("Qsl.Color.SinColor");
        set => SetValue(TextoSiVacioProperty, value);
    }

    /// <summary>Qué color es.</summary>
    public string Titulo
    {
        get => (string)GetValue(TituloProperty) is { Length: > 0 } titulo ? titulo : Textos.T("Qsl.Editor.Color");
        set => SetValue(TituloProperty, value);
    }

    /// <summary>Los últimos usados; por omisión, los de todo el programa.</summary>
    public ColoresRecientes Recientes
    {
        get => _recientes ?? ColoresRecientes.Compartidos;
        set
        {
            _recientes = value;
            listaDeRecientes.ItemsSource = Recientes.Muestras;
            Refrescar();
        }
    }

    /// <summary>El desplegable (para las capturas de la ayuda, que lo pintan fuera de la pantalla).</summary>
    public Popup Desplegable => desplegable;

    /// <summary>Lo de dentro del desplegable.</summary>
    public FrameworkElement ContenidoDelDesplegable => contenido;

    /// <summary>Los colores del diseño de la última vez que se preparó el desplegable.</summary>
    public IReadOnlyList<MuestraDeColor> ColoresDelDiseno { get; private set; } = [];

    /// <summary>El color leído, si lo hay.</summary>
    public Color? ColorLeido => PaletaDeColores.TryLeer(Color, out var c) ? c : null;

    /// <summary>Lee el origen de los colores del diseño.</summary>
    /// <param name="elemento">El elemento.</param>
    /// <returns>El origen.</returns>
    public static IColoresDelDiseno? GetOrigenDeColores(DependencyObject elemento) =>
        (IColoresDelDiseno?)(elemento ?? throw new ArgumentNullException(nameof(elemento))).GetValue(OrigenDeColoresProperty);

    /// <summary>Pone el origen de los colores del diseño.</summary>
    /// <param name="elemento">El elemento.</param>
    /// <param name="valor">El origen.</param>
    public static void SetOrigenDeColores(DependencyObject elemento, IColoresDelDiseno? valor) =>
        (elemento ?? throw new ArgumentNullException(nameof(elemento))).SetValue(OrigenDeColoresProperty, valor);

    /// <summary>Lee el color elegido heredado.</summary>
    /// <param name="elemento">El elemento.</param>
    /// <returns>El color, como texto.</returns>
    public static string GetColorActual(DependencyObject elemento) =>
        (string)(elemento ?? throw new ArgumentNullException(nameof(elemento))).GetValue(ColorActualProperty);

    /// <summary>Pone el color elegido heredado.</summary>
    /// <param name="elemento">El elemento.</param>
    /// <param name="valor">El color.</param>
    public static void SetColorActual(DependencyObject elemento, string valor) =>
        (elemento ?? throw new ArgumentNullException(nameof(elemento))).SetValue(ColorActualProperty, valor);

    /// <summary>Despliega u oculta «Color exacto».</summary>
    public bool ColorExactoDesplegado
    {
        get => plegarExacto.IsChecked == true;
        set => plegarExacto.IsChecked = value;
    }

    /// <summary>Despliega u oculta «Avanzado» (el código).</summary>
    public bool AvanzadoDesplegado
    {
        get => plegarAvanzado.IsChecked == true;
        set => plegarAvanzado.IsChecked = value;
    }

    /// <summary>Abre el desplegable.</summary>
    public void Abrir() => desplegable.IsOpen = true;

    /// <summary>
    /// Deja el desplegable al día (colores del diseño, recientes, marcas) sin abrirlo: lo hace
    /// al abrirse y lo usan las capturas.
    /// </summary>
    public void PrepararDesplegable()
    {
        ColoresDelDiseno = PaletaDeColores.DelDiseno(GetOrigenDeColores(this)?.ColoresEnUso());
        listaDelDiseno.ItemsSource = ColoresDelDiseno;
        Refrescar();
    }

    /// <summary>Elige una muestra: la de la paleta conserva la opacidad que hubiera.</summary>
    /// <param name="muestra">La muestra.</param>
    public void Elegir(MuestraDeColor muestra)
    {
        ArgumentNullException.ThrowIfNull(muestra);
        var color = muestra.Color;
        if (AdmiteOpacidad && PaletaDeColores.Basica.Contains(muestra) && ColorLeido is { A: > 0 } actual) color.A = actual.A;
        Elegir(color);
    }

    /// <summary>Elige un color: lo escribe en el formato guardado y lo apunta en los recientes.</summary>
    /// <param name="color">El color.</param>
    public void Elegir(Color color)
    {
        if (!AdmiteOpacidad) color.A = 255;
        Escribir(color, sincronizar: true);
        Recientes.Anotar(color);
    }

    /// <summary>Deja el campo sin color (si lo admite).</summary>
    public void Vaciar()
    {
        if (!AdmiteVacio) return;
        _escribiendo = true;
        try
        {
            Color = string.Empty;
        }
        finally
        {
            _escribiendo = false;
        }

        Refrescar();
    }

    private static void AlCambiarElColor(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var selector = (SelectorDeColor)d;
        if (!selector._escribiendo && selector.ColorLeido is { } color) selector.Sincronizar(color);
        selector.Refrescar();
    }

    private static void AlCambiarUnaOpcion(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((SelectorDeColor)d).Refrescar();

    private static Brush Pincel(Color color)
    {
        var pincel = new SolidColorBrush(color);
        pincel.Freeze();
        return pincel;
    }

    private static Button? BotonDe(ItemsControl lista, object muestra)
    {
        if (lista.ItemContainerGenerator.ContainerFromItem(muestra) is not DependencyObject contenedor) return null;
        return Buscar<Button>(contenedor);
    }

    private static T? Buscar<T>(DependencyObject raiz)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(raiz); i++)
        {
            var hijo = VisualTreeHelper.GetChild(raiz, i);
            if (hijo is T encontrado) return encontrado;
            if (Buscar<T>(hijo) is { } dentro) return dentro;
        }

        return null;
    }

    private void Sincronizar(Color color)
    {
        var (tono, saturacion, brillo) = PaletaDeColores.ATsb(color);

        // En grises y negros el tono no existe: se conserva el que hubiera para no saltar al rojo.
        if (saturacion > 0 && brillo > 0) _tono = tono;
        if (brillo > 0) _saturacion = saturacion;
        _brillo = brillo;
        _alfa = color.A;
    }

    private void Escribir(Color color, bool sincronizar)
    {
        if (sincronizar) Sincronizar(color);
        _escribiendo = true;
        try
        {
            Color = PaletaDeColores.Formatear(color);
        }
        finally
        {
            _escribiendo = false;
        }

        Refrescar();
    }

    private void EscribirDesdeTsb() =>
        Escribir(PaletaDeColores.DeTsb(_tono, _saturacion, _brillo, AdmiteOpacidad ? _alfa : (byte)255), sincronizar: false);

    private void Refrescar()
    {
        var leido = ColorLeido;
        var nombre = leido is { } c ? PaletaDeColores.Nombrar(c) : (AdmiteVacio ? TextoSiVacio : Textos.T("Qsl.Color.SinColor"));
        var pincel = leido is { } conColor ? Pincel(conColor) : null;

        SetColorActual(contenido, Color ?? string.Empty);
        muestraDelBoton.Background = pincel;
        muestraGrande.Background = pincel;
        rayaDelBoton.Visibility = leido is null ? Visibility.Visible : Visibility.Collapsed;
        rayaGrande.Visibility = rayaDelBoton.Visibility;
        nombreDelBoton.Text = nombre;
        nombreGrande.Text = nombre;
        tituloDelDesplegable.Text = Titulo;
        AutomationProperties.SetName(boton, Textos.F("Qsl.Color.BotonNombre", Titulo, nombre));
        AutomationProperties.SetHelpText(boton, Textos.T("Qsl.Color.AbrePaleta"));
        boton.ToolTip = Textos.F("Qsl.Color.Boton", Titulo, nombre);

        botonVacio.Visibility = AdmiteVacio ? Visibility.Visible : Visibility.Collapsed;
        botonVacio.Content = TextoSiVacio;
        AutomationProperties.SetName(botonVacio, TextoSiVacio);
        bloqueDelDiseno.Visibility = ColoresDelDiseno.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        bloqueDeRecientes.Visibility = Recientes.Muestras.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        bloqueDeOpacidad.Visibility = AdmiteOpacidad ? Visibility.Visible : Visibility.Collapsed;

        if (!codigo.IsKeyboardFocused)
        {
            codigo.Text = leido is { } paraCodigo ? PaletaDeColores.Formatear(paraCodigo) : string.Empty;
            errorDelCodigo.Visibility = Visibility.Collapsed;
        }

        ColocarMarcas();
    }

    private void ColocarMarcas()
    {
        var opaco = PaletaDeColores.DeTsb(_tono, 1, 1);
        fondoDelTono.Background = Pincel(opaco);

        var actual = PaletaDeColores.DeTsb(_tono, _saturacion, _brillo);
        degradadoDeOpacidad.Background = new LinearGradientBrush(
            System.Windows.Media.Color.FromArgb(0, actual.R, actual.G, actual.B), actual, 0);
        textoDeOpacidad.Text = Textos.F("Qsl.Color.Porcentaje", Math.Round(_alfa * 100.0 / 255));
        AutomationProperties.SetItemStatus(barraDeOpacidad, textoDeOpacidad.Text);

        var ancho = superficieSv.ActualWidth;
        var alto = superficieSv.ActualHeight;
        var x = _saturacion * ancho;
        var y = (1 - _brillo) * alto;
        Canvas.SetLeft(marcaSv, x - (marcaSv.Width / 2));
        Canvas.SetTop(marcaSv, y - (marcaSv.Height / 2));
        Canvas.SetLeft(marcaSvSombra, x - (marcaSvSombra.Width / 2));
        Canvas.SetTop(marcaSvSombra, y - (marcaSvSombra.Height / 2));
        Canvas.SetLeft(marcaDeTono, (_tono / 360 * barraDeTono.ActualWidth) - (marcaDeTono.Width / 2));
        Canvas.SetLeft(marcaDeOpacidad, (_alfa / 255.0 * barraDeOpacidad.ActualWidth) - (marcaDeOpacidad.Width / 2));
    }

    private void Cerrar(bool devolverElFoco)
    {
        // El foco solo vuelve al botón si el desplegable estaba abierto (no se roba a nadie).
        var estabaAbierto = desplegable.IsOpen;
        desplegable.IsOpen = false;
        if (devolverElFoco && estabaAbierto) boton.Focus();
    }

    private void AlAbrir(object? sender, EventArgs e)
    {
        _alAbrir = Color ?? string.Empty;
        boton.IsHitTestVisible = false;
        PrepararDesplegable();

        // El foco entra en la muestra del color actual (o la primera): con el teclado se sigue de ahí.
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            var leido = ColorLeido;
            var actual = leido is { } c
                ? (object?)ColoresDelDiseno.FirstOrDefault(m => m.Color == c) ?? PaletaDeColores.Basica.FirstOrDefault(m => m.Color == c)
                : null;
            var lista = actual is not null && ColoresDelDiseno.Contains(actual) ? listaDelDiseno : listaBasica;
            var destino = (actual is not null ? BotonDe(lista, actual) : null) ?? BotonDe(listaBasica, PaletaDeColores.Basica[0]);
            destino?.Focus();
        });
    }

    private void AlCerrar(object? sender, EventArgs e)
    {
        boton.IsHitTestVisible = true;
        if (!string.Equals(Color, _alAbrir, StringComparison.Ordinal) && ColorLeido is { } c) Recientes.Anotar(c);
        Refrescar();
    }

    private void AlTeclearEnElBoton(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.F4 or Key.Down || (e.Key == Key.System && e.SystemKey == Key.Down))
        {
            Abrir();
            e.Handled = true;
        }
    }

    private void AlTeclearEnElDesplegable(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        Cerrar(devolverElFoco: true);
        e.Handled = true;
    }

    private void AlPulsarMuestra(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not MuestraDeColor muestra) return;
        Elegir(muestra);
        Cerrar(devolverElFoco: true);
    }

    private void AlPulsarVacio(object sender, RoutedEventArgs e)
    {
        Vaciar();
        Cerrar(devolverElFoco: true);
    }

    private void AlPlegarODesplegar(object sender, RoutedEventArgs e)
    {
        bloqueExacto.Visibility = plegarExacto.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        bloqueAvanzado.Visibility = plegarAvanzado.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, ColocarMarcas);
    }

    private void AlCambiarDeTamano(object sender, SizeChangedEventArgs e) => ColocarMarcas();

    private void AlCambiarElFoco(object sender, KeyboardFocusChangedEventArgs e)
    {
        var borde = sender == superficieSv ? bordeDelCuadro : sender == barraDeTono ? bordeDelTono : degradadoDeOpacidad;
        var conFoco = ((UIElement)sender).IsKeyboardFocused;
        if (conFoco)
        {
            borde.SetResourceReference(Border.BorderBrushProperty, "Acento");
        }
        else
        {
            borde.SetResourceReference(Border.BorderBrushProperty, "Borde");
        }

        borde.BorderThickness = new Thickness(conFoco ? 2 : 1);
    }

    private void AlPulsarSuperficie(object sender, MouseButtonEventArgs e)
    {
        var superficie = (FrameworkElement)sender;
        superficie.Focus();
        superficie.CaptureMouse();
        AplicarPosicion(superficie, e.GetPosition(superficie));
        e.Handled = true;
    }

    private void AlMoverSobreSuperficie(object sender, MouseEventArgs e)
    {
        var superficie = (FrameworkElement)sender;
        if (!superficie.IsMouseCaptured) return;
        AplicarPosicion(superficie, e.GetPosition(superficie));
    }

    private void AlSoltarSuperficie(object sender, MouseButtonEventArgs e)
    {
        var superficie = (FrameworkElement)sender;
        if (!superficie.IsMouseCaptured) return;
        superficie.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void AplicarPosicion(FrameworkElement superficie, Point punto)
    {
        var x = superficie.ActualWidth > 0 ? Math.Clamp(punto.X / superficie.ActualWidth, 0, 1) : 0;
        var y = superficie.ActualHeight > 0 ? Math.Clamp(punto.Y / superficie.ActualHeight, 0, 1) : 0;
        if (superficie == superficieSv)
        {
            _saturacion = x;
            _brillo = 1 - y;
        }
        else if (superficie == barraDeTono)
        {
            _tono = Math.Min(x * 360, 359.9);
        }
        else
        {
            _alfa = (byte)Math.Round(x * 255);
        }

        EscribirDesdeTsb();
    }

    private void AlTeclearEnSuperficie(object sender, KeyEventArgs e)
    {
        var paso = (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? PasoGrueso : PasoFino;
        var (dx, dy) = e.Key switch
        {
            Key.Left => (-paso, 0d),
            Key.Right => (paso, 0d),
            Key.Up => (0d, paso),
            Key.Down => (0d, -paso),
            _ => (0d, 0d),
        };
        if (dx == 0 && dy == 0) return;

        if (sender == superficieSv)
        {
            _saturacion = Math.Clamp(_saturacion + dx, 0, 1);
            _brillo = Math.Clamp(_brillo + dy, 0, 1);
        }
        else if (sender == barraDeTono)
        {
            _tono = Math.Clamp(_tono + ((dx + dy) * 180), 0, 359.9);
        }
        else
        {
            _alfa = (byte)Math.Clamp(Math.Round(_alfa + ((dx + dy) * 255)), 0, 255);
        }

        EscribirDesdeTsb();
        e.Handled = true;
    }

    private void AlTeclearElCodigo(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        AplicarCodigo();
        e.Handled = true;
    }

    private void AlSalirDelCodigo(object sender, KeyboardFocusChangedEventArgs e) => AplicarCodigo();

    private void AplicarCodigo()
    {
        var texto = codigo.Text.Trim();
        if (texto.Length == 0 && AdmiteVacio)
        {
            Vaciar();
            return;
        }

        if (!PaletaDeColores.TryLeer(texto, out var color))
        {
            errorDelCodigo.Visibility = Visibility.Visible;
            return;
        }

        errorDelCodigo.Visibility = Visibility.Collapsed;
        if (!AdmiteOpacidad) color.A = 255;
        if (ColorLeido == color) return;
        Escribir(color, sincronizar: true);
    }
}

/// <summary>Visible si la muestra es el color elegido ahora (la marca ✓).</summary>
public sealed class EsElColorActual : IMultiValueConverter
{
    /// <inheritdoc />
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture) =>
        values is [MuestraDeColor muestra, string texto, ..] && PaletaDeColores.TryLeer(texto, out var color) && color == muestra.Color
            ? Visibility.Visible
            : Visibility.Hidden;

    /// <inheritdoc />
    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
