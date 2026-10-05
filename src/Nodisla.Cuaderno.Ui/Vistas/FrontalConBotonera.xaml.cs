using System.Windows;
using System.Windows.Controls;

namespace Nodisla.Cuaderno.Ui.Vistas;

/// <summary>
/// El frontal dibujado con la botonera HAM a la izquierda y los canales CB a la derecha.
/// </summary>
/// <remarks>
/// El reparto a lo ancho: primero el frontal a su alto (el que le dan con <c>MaxHeight</c>), y
/// al lado los dos paneles. Si no caben los dos, van los dos al lado izquierdo, uno cada vez,
/// con una pestaña para pasar del uno al otro. Nunca se encoge el frontal por ellos.
/// </remarks>
public partial class FrontalConBotonera : UserControl
{
    /// <summary>Lo que va a la derecha del todo (en Operar, la fonía por el PC).</summary>
    public static readonly DependencyProperty LateralProperty = DependencyProperty.Register(
        nameof(Lateral), typeof(object), typeof(FrontalConBotonera), new PropertyMetadata(null, (d, _) => ((FrontalConBotonera)d).Repartir()));

    /// <summary>Alto maximo del frontal.</summary>
    public static readonly DependencyProperty AltoMaximoDelFrontalProperty = DependencyProperty.Register(
        nameof(AltoMaximoDelFrontal), typeof(double), typeof(FrontalConBotonera),
        new PropertyMetadata(370d, (d, e) => ((FrontalConBotonera)d).Frontal.MaxHeight = (double)e.NewValue));

    /// <summary>
    /// Se ensena la botonera de canales CB al lado del frontal. Por omision, si: en Operar hace
    /// falta para operar CB desde la cabina. En Digital y en CW no tiene sentido (no se opera CB
    /// en modos digitales ni en telegrafia) y, sobre todo, le quitaba ancho de verdad al panel
    /// lateral (en Operar, la fonia): «Repartir» media su ancho aunque estuviera oculta a ojos
    /// del operador.
    /// </summary>
    public static readonly DependencyProperty MostrarCbProperty = DependencyProperty.Register(
        nameof(MostrarCb), typeof(bool), typeof(FrontalConBotonera),
        new PropertyMetadata(true, (d, _) => ((FrontalConBotonera)d).AlCambiarMostrarCb()));

    /// <summary>Proporcion ancho/alto del dibujo del frontal (lienzo de 1040 × 335).</summary>
    public const double ProporcionDelFrontal = 1040d / 335d;

    private bool _verCbEnVezDeHam;

    /// <summary>Monta el frontal con su botonera.</summary>
    public FrontalConBotonera()
    {
        InitializeComponent();
        Frontal.MaxHeight = AltoMaximoDelFrontal;
        PonerElFrontal(null);
        DataContextChanged += AlCambiarElContexto;
    }

    private string? _frontalPuesto;
    private System.ComponentModel.INotifyPropertyChanged? _contextoEscuchado;

    private void AlCambiarElContexto(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_contextoEscuchado is not null) _contextoEscuchado.PropertyChanged -= AlCambiarElEquipo;
        _contextoEscuchado = e.NewValue as System.ComponentModel.INotifyPropertyChanged;
        if (_contextoEscuchado is not null) _contextoEscuchado.PropertyChanged += AlCambiarElEquipo;
        PonerElFrontal((e.NewValue as VistaModelos.VistaModeloEquipo)?.NombreDelFrontal);
    }

    private void AlCambiarElEquipo(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null or nameof(VistaModelos.VistaModeloEquipo.NombreDelFrontal)
            && sender is VistaModelos.VistaModeloEquipo equipo)
        {
            Dispatcher.BeginInvoke(() => PonerElFrontal(equipo.NombreDelFrontal));
        }
    }

    /// <summary>
    /// Pone el dibujo del modelo (ver <see cref="SelectorDeFrontal"/>). Solo se rehace si cambia
    /// la clase: el del FT-710 es pesado y no se tira por cada aviso.
    /// </summary>
    private void PonerElFrontal(string? nombre)
    {
        var tipo = SelectorDeFrontal.Resolver(nombre);
        if (_frontalPuesto == tipo.FullName && Frontal.Content is not null) return;
        _frontalPuesto = tipo.FullName;
        Frontal.Content = SelectorDeFrontal.Crear(nombre);
    }

    /// <summary>Lo que va a la derecha del todo.</summary>
    public object? Lateral
    {
        get => GetValue(LateralProperty);
        set => SetValue(LateralProperty, value);
    }

    /// <summary>Alto maximo del frontal cuando sobra sitio.</summary>
    public double AltoMaximoDelFrontal
    {
        get => (double)GetValue(AltoMaximoDelFrontalProperty);
        set => SetValue(AltoMaximoDelFrontalProperty, value);
    }

    /// <summary>Se ensena la botonera de canales CB. Por omision, si (como siempre hasta ahora).</summary>
    public bool MostrarCb
    {
        get => (bool)GetValue(MostrarCbProperty);
        set => SetValue(MostrarCbProperty, value);
    }

    private void AlCambiarMostrarCb()
    {
        LadoCb.Visibility = MostrarCb ? Visibility.Visible : Visibility.Collapsed;
        Repartir();
    }

    /// <summary>Los dos paneles no caben y comparten el lado izquierdo.</summary>
    public bool Estrecho { get; private set; }

    /// <summary>
    /// Decide si caben los dos paneles a los lados. Lo llama quien cambia el alto
    /// (<c>MaxHeight</c>) y cada cambio de tamaño.
    /// </summary>
    /// <remarks>
    /// <c>MaxHeight</c> (el alto de TODO este control) puede venir un poco mas alto que
    /// <see cref="AltoMaximoDelFrontal"/> (el que le toca solo al dibujo): quien lo pone
    /// (<c>PanelOperar</c>) le da ese extra al hueco lateral -la fonia en columna- para que no
    /// quede diminuta, sin que el dibujo del frontal crezca por ello. HAM, CB y el lateral miden
    /// con ese alto real (<c>altoAmbiente</c>): es el que de verdad van a recibir al repartirse
    /// el ancho. El dibujo, en cambio, solo cuenta con el suyo (<c>altoDelDibujo</c>) para lo que
    /// aporta a <c>necesario</c>.
    /// </remarks>
    public void Repartir()
    {
        if (ActualWidth <= 0) return;

        var altoAmbiente = double.IsInfinity(MaxHeight) ? AltoMaximoDelFrontal : MaxHeight;
        var altoDelDibujo = Math.Min(altoAmbiente, AltoMaximoDelFrontal);
        var medida = new Size(double.PositiveInfinity, altoAmbiente);

        LadoHam.Measure(medida);
        LadoCb.Measure(medida);
        HuecoLateral.Measure(medida);
        var lateral = Lateral is null ? 0 : HuecoLateral.DesiredSize.Width;
        var anchoCb = MostrarCb ? LadoCb.DesiredSize.Width : 0;

        var necesario = (altoDelDibujo * ProporcionDelFrontal) + LadoHam.DesiredSize.Width + anchoCb + lateral;
        Estrecho = necesario > ActualWidth;

        Ham.MostrarCambio = MostrarCb && Estrecho;
        Cb.MostrarCambio = Estrecho;
        DockPanel.SetDock(LadoCb, Estrecho ? Dock.Left : Dock.Right);
        LadoCb.Margin = Estrecho ? new Thickness(0, 0, 8, 0) : new Thickness(8, 0, 0, 0);
        LadoHam.Visibility = MostrarCb && Estrecho && _verCbEnVezDeHam ? Visibility.Collapsed : Visibility.Visible;
        LadoCb.Visibility = !MostrarCb ? Visibility.Collapsed : Estrecho && !_verCbEnVezDeHam ? Visibility.Collapsed : Visibility.Visible;
    }

    private void AlCambiarDeTamano(object sender, SizeChangedEventArgs e) => Repartir();

    private void AlCambiarDeLado(object? sender, EventArgs e)
    {
        _verCbEnVezDeHam = !_verCbEnVezDeHam;
        Repartir();
    }
}
