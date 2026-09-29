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

    /// <summary>Los dos paneles no caben y comparten el lado izquierdo.</summary>
    public bool Estrecho { get; private set; }

    /// <summary>
    /// Decide si caben los dos paneles a los lados. Lo llama quien cambia el alto
    /// (<c>MaxHeight</c>) y cada cambio de tamaño.
    /// </summary>
    public void Repartir()
    {
        if (ActualWidth <= 0) return;

        var alto = Math.Min(double.IsInfinity(MaxHeight) ? AltoMaximoDelFrontal : MaxHeight, AltoMaximoDelFrontal);
        var medida = new Size(double.PositiveInfinity, alto);

        LadoHam.Measure(medida);
        LadoCb.Measure(medida);
        HuecoLateral.Measure(medida);
        var lateral = Lateral is null ? 0 : HuecoLateral.DesiredSize.Width;

        var necesario = (alto * ProporcionDelFrontal) + LadoHam.DesiredSize.Width + LadoCb.DesiredSize.Width + lateral;
        Estrecho = necesario > ActualWidth;

        Ham.MostrarCambio = Estrecho;
        Cb.MostrarCambio = Estrecho;
        DockPanel.SetDock(LadoCb, Estrecho ? Dock.Left : Dock.Right);
        LadoCb.Margin = Estrecho ? new Thickness(0, 0, 8, 0) : new Thickness(8, 0, 0, 0);
        LadoHam.Visibility = Estrecho && _verCbEnVezDeHam ? Visibility.Collapsed : Visibility.Visible;
        LadoCb.Visibility = Estrecho && !_verCbEnVezDeHam ? Visibility.Collapsed : Visibility.Visible;
    }

    private void AlCambiarDeTamano(object sender, SizeChangedEventArgs e) => Repartir();

    private void AlCambiarDeLado(object? sender, EventArgs e)
    {
        _verCbEnVezDeHam = !_verCbEnVezDeHam;
        Repartir();
    }
}
