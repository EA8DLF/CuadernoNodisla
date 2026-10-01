using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Nodisla.Cuaderno.Ui.VistaModelos;

namespace Nodisla.Cuaderno.Ui.Vistas;

/// <summary>
/// La cabina de operacion: el frontal del equipo a todo el ancho, y debajo el contacto nuevo
/// a la izquierda con el cluster a la derecha.
/// </summary>
/// <remarks>
/// El reparto del alto lo manda lo de abajo, como en Digital: primero se garantiza a contacto +
/// cluster <see cref="AltoMinimoDeAbajo"/> y el frontal (con la fonia al lado, que escala con
/// el) se queda con lo que sobre, hasta <see cref="AltoMaximoDelFrontal"/>. Por debajo de
/// <see cref="AltoMinimoDelFrontal"/> se pliega solo, sin tocar la preferencia guardada del
/// operador: cuando vuelve a haber sitio, vuelve.
/// </remarks>
public partial class PanelOperar : UserControl
{
    /// <summary>Alto minimo de contacto + cluster: el formulario entero y unas 8 filas de cluster.</summary>
    public const double AltoMinimoDeAbajo = 420;

    /// <summary>Alto por debajo del cual el frontal no se lee y se pliega solo.</summary>
    public const double AltoMinimoDelFrontal = 150;

    /// <summary>Tope de alto del frontal cuando sobra sitio.</summary>
    public const double AltoMaximoDelFrontal = 370;

    /// <summary>Margenes, rellenos y bordes de las tarjetas alrededor del frontal.</summary>
    private const double Rellenos = 24;

    /// <summary>El operador pidio ver el frontal aunque no quepa; dura hasta que vuelva a caber.</summary>
    private bool _forzado;

    private INotifyPropertyChanged? _modelo;

    /// <summary>Monta la cabina.</summary>
    public PanelOperar()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_modelo is not null) _modelo.PropertyChanged -= AlCambiarElModelo;
            _modelo = DataContext as INotifyPropertyChanged;
            if (_modelo is not null) _modelo.PropertyChanged += AlCambiarElModelo;
            Repartir();
        };
    }

    /// <summary>Trae el foco al indicativo, que es donde se empieza a teclear.</summary>
    public void EnfocarIndicativo() => Entrada.EnfocarIndicativo();

    /// <summary>
    /// Alto que le toca al frontal: 0 si no cabe legible (se pliega solo), o lo que sobra
    /// tras dar a lo de abajo su minimo, hasta el tope.
    /// </summary>
    public static double AltoDelFrontal(double altoDelPanel, double altoDeLoDeArriba)
    {
        var sobra = altoDelPanel - altoDeLoDeArriba - Rellenos - AltoMinimoDeAbajo;
        return sobra < AltoMinimoDelFrontal ? 0 : Math.Min(sobra, AltoMaximoDelFrontal);
    }

    private void AlCambiarElModelo(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(VistaModeloPrincipal.FrontalDibujadoVisible) or nameof(VistaModeloPrincipal.Fonia))
            Repartir();
    }

    private void AlCambiarElReparto(object sender, SizeChangedEventArgs e) => Repartir();

    private void AlForzarElFrontal(object sender, RoutedEventArgs e)
    {
        _forzado = true;
        Repartir();
    }

    private void Repartir()
    {
        if (Raiz.ActualHeight <= 0) return;
        var arriba = TarjetaDeLaBarra.ActualHeight + TarjetaDeLaBarra.Margin.Bottom
                     + (TarjetaDeLosMandos.IsVisible ? TarjetaDeLosMandos.ActualHeight + TarjetaDeLosMandos.Margin.Bottom : 0);
        var alto = AltoDelFrontal(Raiz.ActualHeight, arriba);
        if (alto > 0) _forzado = false;

        var quiereFrontal = DataContext is VistaModeloPrincipal { FrontalDibujadoVisible: true };
        var plegadoSolo = quiereFrontal && alto <= 0 && !_forzado;

        HuecoDelFrontal.Visibility = plegadoSolo ? Visibility.Collapsed : Visibility.Visible;
        LineaDePlegadoAutomatico.Visibility = plegadoSolo ? Visibility.Visible : Visibility.Collapsed;
        CajaDelFrontal.MaxHeight = alto > 0 ? alto : AltoMinimoDelFrontal;
        CajaDelFrontal.Repartir();

        // Con el frontal plegado solo, la fonia vuelve a su franja bajo la barra (el estilo la
        // esconde porque cree que va al lado del frontal).
        var hayFonia = DataContext is VistaModeloPrincipal { Fonia: not null };
        if (plegadoSolo && hayFonia) FranjaDeFonia.Visibility = Visibility.Visible;
        else FranjaDeFonia.ClearValue(VisibilityProperty);
    }
}
