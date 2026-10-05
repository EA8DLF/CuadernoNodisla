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
/// cluster <see cref="AltoMinimoDeAbajo"/> y el frontal se queda con lo que sobre, hasta
/// <see cref="AltoMaximoDelFrontal"/>. Por debajo de <see cref="AltoMinimoDelFrontal"/> se pliega
/// solo, sin tocar la preferencia guardada del operador: cuando vuelve a haber sitio, vuelve.
/// </remarks>
/// <remarks>
/// La fonia en columna (junto al frontal, <c>FrontalConBotonera.Lateral</c> en este XAML) vive
/// dentro de un Viewbox que solo encoge. Medida sin avisos activos, esa columna (PTT, EN
/// RECEPCION, dos deslizadores, «Escuchar por el PC», «Dispositivos...») pide de forma natural
/// unos 330 puntos de alto: dandole el mismo alto que al dibujo del frontal (que puede bajar
/// hasta <see cref="AltoMinimoDelFrontal"/>, 150) el Viewbox la encogia a menos de la mitad —el
/// boton del PTT y los deslizadores quedaban practicamente impulsables, aunque el frontal en si
/// siguiera leyendose bien—. <see cref="Repartir"/> le da a esa columna un suelo propio
/// (<see cref="AltoMinimoDeFoniaEnColumna"/>), por encima del que le toque al dibujo del frontal
/// cuando haga falta: el frontal no crece ni un punto por esto (sigue en su <c>alto</c>, via
/// <c>CajaDelFrontal.AltoMaximoDelFrontal</c>), solo el hueco lateral que ocupa la fonia a su
/// lado. Que esa columna sea un poco mas alta que el dibujo, ocasionalmente, no pliega nada ni
/// le quita su minimo a contacto+cluster salvo en el caso mas extremo (frontal justo en su suelo
/// de 150): ahi la fonia toma los puntos de mas y el formulario/cluster, que tienen scroll
/// propio, se quedan un poco mas apretados en vez de dejar el PTT inservible.
/// </remarks>
public partial class PanelOperar : UserControl
{
    /// <summary>Alto minimo de contacto + cluster: el formulario entero y unas 8 filas de cluster.</summary>
    public const double AltoMinimoDeAbajo = 420;

    /// <summary>Alto por debajo del cual el frontal no se lee y se pliega solo.</summary>
    public const double AltoMinimoDelFrontal = 150;

    /// <summary>
    /// Suelo propio del alto que recibe la columna de fonia (lateral del frontal): unos 330
    /// puntos le bastan sin avisos activos, y por debajo de eso el Viewbox que la escala la deja
    /// con el PTT y los deslizadores dificiles de pulsar. Se le da este alto aunque al dibujo del
    /// frontal le toque menos (hasta <see cref="AltoMinimoDelFrontal"/>): el frontal no crece por
    /// esto, solo el hueco lateral a su lado.
    /// </summary>
    public const double AltoMinimoDeFoniaEnColumna = 300;

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
                     + (TarjetaDeLosMandos.IsVisible ? TarjetaDeLosMandos.ActualHeight + TarjetaDeLosMandos.Margin.Bottom : 0)
                     + (TarjetaCw.IsVisible ? TarjetaCw.ActualHeight + TarjetaCw.Margin.Bottom : 0);
        var alto = AltoDelFrontal(Raiz.ActualHeight, arriba);
        if (alto > 0) _forzado = false;

        var quiereFrontal = DataContext is VistaModeloPrincipal { FrontalDibujadoVisible: true };
        var plegadoSolo = quiereFrontal && alto <= 0 && !_forzado;

        HuecoDelFrontal.Visibility = plegadoSolo ? Visibility.Collapsed : Visibility.Visible;
        LineaDePlegadoAutomatico.Visibility = plegadoSolo ? Visibility.Visible : Visibility.Collapsed;

        var altoDelDibujo = alto > 0 ? alto : AltoMinimoDelFrontal;
        var hayFonia = DataContext is VistaModeloPrincipal { Fonia: not null };

        // El dibujo del frontal se queda exactamente en «altoDelDibujo» (nunca crece por la
        // fonia); el hueco entero (que es tambien el alto disponible para la columna de fonia a
        // su lado) puede ser un poco mas alto que eso cuando hace falta, para que esa columna no
        // quede diminuta.
        CajaDelFrontal.AltoMaximoDelFrontal = altoDelDibujo;
        CajaDelFrontal.MaxHeight = hayFonia ? Math.Max(altoDelDibujo, AltoMinimoDeFoniaEnColumna) : altoDelDibujo;
        CajaDelFrontal.Repartir();

        // Con el frontal plegado solo, la fonia vuelve a su franja bajo la barra (el estilo la
        // esconde porque cree que va al lado del frontal).
        if (plegadoSolo && hayFonia) FranjaDeFonia.Visibility = Visibility.Visible;
        else FranjaDeFonia.ClearValue(VisibilityProperty);
    }
}
