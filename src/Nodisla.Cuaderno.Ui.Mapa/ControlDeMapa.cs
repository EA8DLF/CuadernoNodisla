using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Tiling;
using Mapsui.UI.Wpf;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Ui.Mapa;

/// <summary>
/// El mapa del cuaderno.
/// </summary>
/// <remarks>
/// Este control es la unica puerta al motor de mapas. Ni la ventana principal ni los modelos
/// de vista saben que por debajo hay Mapsui: le pasan marcas, trayectos y una hora, y el mapa
/// se las apana. Esa frontera no es un capricho: los controles de mapa son la dependencia que
/// mas se rompe entre versiones, y mantenerla aqui permite cambiar de motor sin tocar una
/// sola linea de la interfaz.
///
/// El mapa tiene que funcionar sin conexion. Los mosaicos del fondo se descargan de un
/// servidor; cuando no hay red, no se cuelga ni se queda esperando: se queda el fondo liso y
/// encima siguen viendose los contactos, los trayectos y el paso gris, que es lo que de
/// verdad importa. La atribucion del servidor de mosaicos se ensena siempre que se usan.
/// </remarks>
public class ControlDeMapa : UserControl, IDisposable
{
    /// <summary>Marcas que se pintan sobre el mapa.</summary>
    public static readonly DependencyProperty MarcasProperty = DependencyProperty.Register(
        nameof(Marcas),
        typeof(IReadOnlyList<MarcaDelMapa>),
        typeof(ControlDeMapa),
        new PropertyMetadata(null, AlCambiarLasMarcas));

    /// <summary>Trayectos de circulo maximo que se dibujan.</summary>
    public static readonly DependencyProperty TrayectosProperty = DependencyProperty.Register(
        nameof(Trayectos),
        typeof(IReadOnlyList<TrayectoDelMapa>),
        typeof(ControlDeMapa),
        new PropertyMetadata(null, AlCambiarLosTrayectos));

    /// <summary>Donde esta la estacion propia, o nulo si todavia no se sabe.</summary>
    public static readonly DependencyProperty EstacionPropiaProperty = DependencyProperty.Register(
        nameof(EstacionPropia),
        typeof(Coordenada?),
        typeof(ControlDeMapa),
        new PropertyMetadata(null, AlCambiarLaEstacion));

    /// <summary>Indicativo propio, para escribirlo al lado de la marca.</summary>
    public static readonly DependencyProperty IndicativoPropioProperty = DependencyProperty.Register(
        nameof(IndicativoPropio),
        typeof(string),
        typeof(ControlDeMapa),
        new PropertyMetadata(string.Empty, AlCambiarLaEstacion));

    /// <summary>Se pinta la sombra de la noche y la linea del paso gris.</summary>
    public static readonly DependencyProperty MostrarPasoGrisProperty = DependencyProperty.Register(
        nameof(MostrarPasoGris),
        typeof(bool),
        typeof(ControlDeMapa),
        new PropertyMetadata(true, AlCambiarElPasoGris));

    /// <summary>Hora para la que se dibuja el paso gris.</summary>
    public static readonly DependencyProperty InstanteDelPasoGrisProperty = DependencyProperty.Register(
        nameof(InstanteDelPasoGris),
        typeof(DateTimeOffset),
        typeof(ControlDeMapa),
        new PropertyMetadata(DateTimeOffset.UtcNow, AlCambiarElPasoGris));

    /// <summary>Se descargan los mosaicos del mapa de fondo.</summary>
    public static readonly DependencyProperty MostrarFondoProperty = DependencyProperty.Register(
        nameof(MostrarFondo),
        typeof(bool),
        typeof(ControlDeMapa),
        new PropertyMetadata(true, AlCambiarElFondo));

    /// <summary>El mapa se pinta con los colores del tema oscuro.</summary>
    public static readonly DependencyProperty TemaOscuroProperty = DependencyProperty.Register(
        nameof(TemaOscuro),
        typeof(bool),
        typeof(ControlDeMapa),
        new PropertyMetadata(false, AlCambiarElTema));

    /// <summary>Marcas que se pintan como mucho; de ahi para arriba se agrupan.</summary>
    public static readonly DependencyProperty MaximoDeMarcasProperty = DependencyProperty.Register(
        nameof(MaximoDeMarcas),
        typeof(int),
        typeof(ControlDeMapa),
        new PropertyMetadata(1200, AlCambiarLasMarcas));

    /// <summary>Como de tupido se dibuja todo.</summary>
    public static readonly DependencyProperty DetalleProperty = DependencyProperty.Register(
        nameof(Detalle),
        typeof(DetalleDelMapa),
        typeof(ControlDeMapa),
        new PropertyMetadata(DetalleDelMapa.Normal, AlCambiarElTema));

    private readonly MapControl _mapa = new();
    private readonly TextBlock _atribucion = new();
    private readonly TextBlock _aviso = new();

    /// <summary>
    /// La sombra de la noche, atenuada como capa.
    /// </summary>
    /// <remarks>
    /// La transparencia se pone AQUI, en la capa, y no en el color del relleno. Con el color
    /// translucido el motor de mapas componia la mancha de manera que el mapa de debajo
    /// desaparecia: quedaban dos bandas macizas que tapaban continentes, mosaicos y contactos,
    /// y el mapa solo asomaba por la franja de dia. Atenuando la capa entera, el velo deja ver
    /// lo que hay debajo, que es lo que tiene que hacer una linea gris.
    /// </remarks>
    private readonly MemoryLayer _capaDeLaNoche = new("Noche") { Opacity = 0.3 };

    private readonly MemoryLayer _capaDelPasoGris = new("Paso gris");
    private readonly MemoryLayer _capaDeTrayectos = new("Trayectos");
    private readonly MemoryLayer _capaDeMarcas = new("Contactos y spots");
    private readonly MemoryLayer _capaDeLaEstacion = new("Estación propia");

    private ILayer? _capaDeMosaicos;
    /// <summary>Calculo en curso de cada capa, para poder cancelar solo el suyo.</summary>
    private readonly Dictionary<MemoryLayer, CancellationTokenSource> _dibujosEnCurso = [];

    /// <summary>Cuantos recalculos se han pedido y cuantos han llegado a colgarse.</summary>
    private int _recalculosPedidos;
    private int _recalculosColgados;
    private int _recalculosCancelados;
    private int _recalculosRotos;

    /// <summary>
    /// Espera antes de rehacer las marcas, para juntar las rafagas del cluster.
    /// </summary>
    /// <remarks>
    /// Cada anuncio que llega cambia la lista de marcas, y con el cuaderno entero encima cada
    /// recalculo reparte veinte mil puntos en casillas. En una rafaga eso son decenas de
    /// repartos identicos seguidos. Esperando a que la rafaga pare se hace uno solo, y que el
    /// mapa ensene los spots con medio segundo de retraso no se lo nota nadie.
    /// </remarks>
    private readonly System.Windows.Threading.DispatcherTimer _esperaDeMarcas = new()
    {
        Interval = TimeSpan.FromMilliseconds(400),
    };

    /// <summary>Latido de diagnostico: cuenta lo que hay en cada capa cada pocos segundos.</summary>
    private readonly System.Windows.Threading.DispatcherTimer _latido = new()
    {
        Interval = TimeSpan.FromSeconds(15),
    };
    private bool _liberado;
    private bool _encuadrado;

    /// <summary>Escalon de zoom con el que se agruparon las marcas por ultima vez.</summary>
    private int _escalonDeZoom = int.MinValue;

    /// <summary>Ultima marca sobre la que estuvo el raton, para no rehacer la ayuda a cada pixel.</summary>
    private MarcaDelMapa? _marcaSenalada;

    /// <summary>Monta el mapa con sus capas y lo deja mirando al mundo entero.</summary>
    public ControlDeMapa()
    {
        var lienzo = new Grid();

        _atribucion.Margin = new Thickness(0, 0, 8, 6);
        _atribucion.HorizontalAlignment = HorizontalAlignment.Right;
        _atribucion.VerticalAlignment = VerticalAlignment.Bottom;
        _atribucion.IsHitTestVisible = false;
        _atribucion.Text = "Mosaicos © colaboradores de OpenStreetMap";
        _atribucion.Opacity = 0.75;

        _aviso.Margin = new Thickness(8, 8, 8, 8);
        _aviso.HorizontalAlignment = HorizontalAlignment.Center;
        _aviso.VerticalAlignment = VerticalAlignment.Top;
        _aviso.IsHitTestVisible = false;
        _aviso.TextWrapping = TextWrapping.Wrap;
        _aviso.Visibility = Visibility.Collapsed;

        lienzo.Children.Add(_mapa);
        lienzo.Children.Add(_atribucion);
        lienzo.Children.Add(_aviso);
        Content = lienzo;

        _mapa.Map.Layers.Add(_capaDeLaNoche);
        _mapa.Map.Layers.Add(_capaDelPasoGris);
        _mapa.Map.Layers.Add(_capaDeTrayectos);
        _mapa.Map.Layers.Add(_capaDeMarcas);
        _mapa.Map.Layers.Add(_capaDeLaEstacion);
        _mapa.MapTapped += AlTocarElMapa;
        _mapa.MapPointerMoved += AlPasarElRaton;
        _mapa.Map.Navigator.ViewportChanged += AlCambiarElEncuadre;

        AplicarTema();
        RehacerElFondo();

        _esperaDeMarcas.Tick += (_, _) =>
        {
            _esperaDeMarcas.Stop();
            RehacerLasMarcas();
        };

        _latido.Tick += (_, _) => Contar();
        _latido.Start();

        // Encuadrar antes de que el control tenga tamano no hace nada: el motor de mapas
        // necesita saber cuantos puntos de pantalla tiene para calcular la escala. Por eso se
        // espera a la primera medida de verdad y se encuadra entonces, una sola vez.
        SizeChanged += AlCambiarDeTamano;
    }

    /// <summary>Salta cuando el operador toca una marca del mapa.</summary>
    public event EventHandler<MarcaDelMapa>? MarcaElegida;

    /// <summary>Marcas que se pintan sobre el mapa.</summary>
    public IReadOnlyList<MarcaDelMapa>? Marcas
    {
        get => (IReadOnlyList<MarcaDelMapa>?)GetValue(MarcasProperty);
        set => SetValue(MarcasProperty, value);
    }

    /// <summary>Trayectos de circulo maximo que se dibujan.</summary>
    public IReadOnlyList<TrayectoDelMapa>? Trayectos
    {
        get => (IReadOnlyList<TrayectoDelMapa>?)GetValue(TrayectosProperty);
        set => SetValue(TrayectosProperty, value);
    }

    /// <summary>Donde esta la estacion propia.</summary>
    public Coordenada? EstacionPropia
    {
        get => (Coordenada?)GetValue(EstacionPropiaProperty);
        set => SetValue(EstacionPropiaProperty, value);
    }

    /// <summary>Indicativo propio, para escribirlo al lado de la marca.</summary>
    public string IndicativoPropio
    {
        get => (string)GetValue(IndicativoPropioProperty);
        set => SetValue(IndicativoPropioProperty, value);
    }

    /// <summary>Se pinta la sombra de la noche y la linea del paso gris.</summary>
    public bool MostrarPasoGris
    {
        get => (bool)GetValue(MostrarPasoGrisProperty);
        set => SetValue(MostrarPasoGrisProperty, value);
    }

    /// <summary>Hora para la que se dibuja el paso gris.</summary>
    public DateTimeOffset InstanteDelPasoGris
    {
        get => (DateTimeOffset)GetValue(InstanteDelPasoGrisProperty);
        set => SetValue(InstanteDelPasoGrisProperty, value);
    }

    /// <summary>Se descargan los mosaicos del mapa de fondo.</summary>
    public bool MostrarFondo
    {
        get => (bool)GetValue(MostrarFondoProperty);
        set => SetValue(MostrarFondoProperty, value);
    }

    /// <summary>El mapa se pinta con los colores del tema oscuro.</summary>
    public bool TemaOscuro
    {
        get => (bool)GetValue(TemaOscuroProperty);
        set => SetValue(TemaOscuroProperty, value);
    }

    /// <summary>Marcas que se pintan como mucho antes de empezar a agrupar.</summary>
    public int MaximoDeMarcas
    {
        get => (int)GetValue(MaximoDeMarcasProperty);
        set => SetValue(MaximoDeMarcasProperty, value);
    }

    /// <summary>Como de tupido se dibuja todo.</summary>
    public DetalleDelMapa Detalle
    {
        get => (DetalleDelMapa)GetValue(DetalleProperty);
        set => SetValue(DetalleProperty, value);
    }

    /// <summary>Borde del mundo en la proyeccion del mapa, en metros.</summary>
    private const double BordeDelMundo = 20_037_508.34;

    /// <summary>
    /// Encuadra el mundo entero.
    /// </summary>
    /// <remarks>
    /// La escala se calcula aqui y no se le pide al motor de mapas que la deduzca de un
    /// rectangulo: encuadrar por rectangulo necesita que el motor ya sepa de que tamano es el
    /// control, y eso no se cumple mientras la ventana se esta montando. Con la cuenta hecha a
    /// mano —cuantos metros de mundo caben en cada punto de pantalla— el encuadre sale bien a
    /// la primera, tenga el mapa el tamano que tenga.
    /// </remarks>
    public void VerElMundo()
    {
        if (_liberado) return;

        var ancho = _mapa.ActualWidth > 1 ? _mapa.ActualWidth : ActualWidth;
        var alto = _mapa.ActualHeight > 1 ? _mapa.ActualHeight : ActualHeight;
        if (ancho < 1 || alto < 1) return;

        // La mayor de las dos escalas es la que hace que el mundo quepa entero.
        var resolucion = Math.Max(2 * BordeDelMundo / ancho, 2 * BordeDelMundo / alto);
        _mapa.Map.Navigator.CenterOnAndZoomTo(new MPoint(0, 0), resolucion);
    }

    /// <summary>Lleva el mapa a un punto concreto.</summary>
    /// <param name="donde">Punto al que se va.</param>
    /// <param name="metrosPorPunto">Cuanto terreno cabe en cada punto de pantalla.</param>
    public void Centrar(Coordenada donde, double metrosPorPunto = 4_000)
    {
        if (_liberado) return;

        var punto = Proyeccion.A(donde);
        _mapa.Map.Navigator.CenterOnAndZoomTo(new MPoint(punto.X, punto.Y), metrosPorPunto);
    }

    /// <summary>Suelta el motor de mapas y sus descargas pendientes.</summary>
    public void Dispose()
    {
        Dispose(liberando: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Suelta el motor de mapas y sus descargas pendientes.</summary>
    /// <param name="liberando">Cierto cuando lo llama <see cref="Dispose()"/>.</param>
    protected virtual void Dispose(bool liberando)
    {
        if (_liberado || !liberando) return;
        _liberado = true;

        _latido.Stop();
        _esperaDeMarcas.Stop();
        _mapa.MapTapped -= AlTocarElMapa;
        SizeChanged -= AlCambiarDeTamano;
        foreach (var testigo in _dibujosEnCurso.Values)
        {
            testigo.Cancel();
            testigo.Dispose();
        }

        _dibujosEnCurso.Clear();
        _mapa.Dispose();
    }

    private static void AlCambiarLasMarcas(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((ControlDeMapa)d).PedirLasMarcas();

    private static void AlCambiarLosTrayectos(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((ControlDeMapa)d).RehacerLosTrayectos();

    private static void AlCambiarLaEstacion(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((ControlDeMapa)d).RehacerLaEstacion();

    private static void AlCambiarElPasoGris(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((ControlDeMapa)d).RehacerElPasoGris();

    private static void AlCambiarElFondo(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((ControlDeMapa)d).RehacerElFondo();

    private static void AlCambiarElTema(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (ControlDeMapa)d;
        control.AplicarTema();
        control.RehacerloTodo();
    }

    /// <summary>Paleta que toca segun el tema.</summary>
    private PaletaDelMapa Paleta => PaletaDelMapa.Para(TemaOscuro);

    /// <summary>
    /// Tamano de letra heredado de la ventana. El mapa escribe con el mismo cuerpo que el
    /// resto del programa, asi que al 200 % de escala sus etiquetas crecen tambien.
    /// </summary>
    private double LetraDeLaVentana => FontSize > 0 ? FontSize : 14.0;

    private void AplicarTema()
    {
        var paleta = Paleta;
        _mapa.Map.BackColor = paleta.Fondo;

        var texto = Color.FromArgb(
            (byte)paleta.Texto.A,
            (byte)paleta.Texto.R,
            (byte)paleta.Texto.G,
            (byte)paleta.Texto.B);

        _atribucion.Foreground = new SolidColorBrush(texto);
        _aviso.Foreground = new SolidColorBrush(texto);
        _mapa.RefreshGraphics();
    }

    private void RehacerloTodo()
    {
        RehacerElPasoGris();
        RehacerLosTrayectos();
        RehacerLasMarcas();
        RehacerLaEstacion();
    }

    /// <summary>
    /// Monta o quita la capa de mosaicos.
    /// </summary>
    /// <remarks>
    /// Crear la capa no descarga nada: los mosaicos se piden despues, cada uno por su cuenta y
    /// en segundo plano. Si no hay red, las peticiones fallan calladas y el mapa se queda con
    /// el fondo liso. Lo unico que se vigila aqui es que montar la capa no reviente, porque
    /// eso si dejaria la ventana sin mapa.
    /// </remarks>
    private void RehacerElFondo()
    {
        if (_capaDeMosaicos is { } vieja)
        {
            _mapa.Map.Layers.Remove(vieja);
            (vieja as IDisposable)?.Dispose();
            _capaDeMosaicos = null;
        }

        if (!MostrarFondo)
        {
            _atribucion.Visibility = Visibility.Collapsed;
            MostrarAviso(null);
            return;
        }

        try
        {
            var capa = OpenStreetMap.CreateTileLayer("CuadernoNodisla/0.1 (+https://nodisla.org)");

            // El motor de mapas pinta su propia atribucion, en ingles y encima de la nuestra.
            // Se apaga la suya y se deja la de este control, que esta en espanol y dice lo
            // mismo: la atribucion se sigue viendo, que es lo que pide OpenStreetMap.
            capa.Attribution.Enabled = false;

            _capaDeMosaicos = capa;
            _mapa.Map.Layers.Insert(0, capa);
            _atribucion.Visibility = Visibility.Visible;
            MostrarAviso(null);
        }
        catch (Exception)
        {
            // Sin mosaicos el mapa sigue sirviendo: los contactos, los trayectos y el paso
            // gris se pintan igual sobre el fondo liso.
            _atribucion.Visibility = Visibility.Collapsed;
            MostrarAviso("Sin mapa de fondo: no se han podido cargar los mosaicos. Los contactos y el paso gris se siguen viendo.");
        }
    }

    private void RehacerElPasoGris()
    {
        if (_liberado) return;

        if (!MostrarPasoGris)
        {
            Colgar(_capaDeLaNoche, []);
            Colgar(_capaDelPasoGris, []);
            return;
        }

        var instante = InstanteDelPasoGris;
        var paleta = Paleta;
        var detalle = Detalle;

        EnSegundoPlano(_capaDeLaNoche, () => CapasDelMapa.SombraDeLaNoche(instante, paleta, detalle));
        EnSegundoPlano(_capaDelPasoGris, () => CapasDelMapa.LineasDelPasoGris(instante, paleta, detalle));
    }

    private void RehacerLosTrayectos()
    {
        if (_liberado) return;

        var trayectos = Trayectos ?? [];
        var paleta = Paleta;
        var detalle = Detalle;

        EnSegundoPlano(_capaDeTrayectos, () => CapasDelMapa.Trayectos(trayectos, paleta, detalle));
    }

    /// <summary>Puntos de pantalla que se dejan entre una burbuja y la siguiente.</summary>
    /// <remarks>
    /// Una burbuja con su numero dentro mide unos treinta puntos. Agrupando con casillas de
    /// cuarenta y cuatro, dos burbujas vecinas no se tocan ni a vista de mundo, que era el
    /// problema: veinte encima unas de otras sobre Europa.
    /// </remarks>
    private const double PuntosEntreBurbujas = 44.0;

    /// <summary>Vuelta al mundo por el ecuador, en metros de Mercator.</summary>
    private const double VueltaAlMundoEnMetros = 40075016.686;

    /// <summary>
    /// Lado de la casilla de agrupacion, en grados, para el zoom que hay ahora.
    /// </summary>
    /// <remarks>
    /// La resolucion del motor de mapas son metros de Mercator por punto de pantalla. Pasar de
    /// ahi a grados de longitud es una regla de tres con la vuelta al mundo. Devuelve cero si
    /// todavia no hay encuadre, y entonces se agrupa como antes, por el tope de marcas.
    /// </remarks>
    private double LadoDeCasillaEnGrados()
    {
        var resolucion = _mapa.Map.Navigator.Viewport.Resolution;
        if (double.IsNaN(resolucion) || resolucion <= 0) return 0;

        return Math.Clamp(PuntosEntreBurbujas * resolucion * 360.0 / VueltaAlMundoEnMetros, 0.05, 90.0);
    }

    /// <summary>Pide rehacer las marcas cuando pare la rafaga de cambios.</summary>
    private void PedirLasMarcas()
    {
        if (_liberado) return;

        _esperaDeMarcas.Stop();
        _esperaDeMarcas.Start();
    }

    private void RehacerLasMarcas()
    {
        if (_liberado) return;

        var marcas = Marcas ?? [];
        var paleta = Paleta;
        var maximo = MaximoDeMarcas;
        var letra = LetraDeLaVentana;
        var lado = LadoDeCasillaEnGrados();

        _escalonDeZoom = EscalonDeZoom();

        EnSegundoPlano(_capaDeMarcas, () => CapasDelMapa.Marcas(marcas, paleta, maximo, letra, lado));
    }

    /// <summary>
    /// Escalon de zoom, para no rehacer las marcas con cada rueda del raton.
    /// </summary>
    /// <remarks>
    /// Se agrupa de nuevo cuando el zoom cambia al doble o a la mitad, no antes: rehacer
    /// veinte mil contactos en cada paso intermedio dejaria el mapa a tirones sin que el
    /// reparto de burbujas cambiara nada que se note.
    /// </remarks>
    private int EscalonDeZoom()
    {
        var resolucion = _mapa.Map.Navigator.Viewport.Resolution;
        return double.IsNaN(resolucion) || resolucion <= 0
            ? int.MinValue
            : (int)Math.Round(Math.Log2(resolucion));
    }

    private void AlCambiarElEncuadre(object? origen, EventArgs args)
    {
        if (_liberado) return;

        var escalon = EscalonDeZoom();
        if (escalon == int.MinValue || escalon == _escalonDeZoom) return;

        _ = Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Background,
            new Action(PedirLasMarcas));
    }

    private void RehacerLaEstacion()
    {
        if (_liberado) return;

        if (EstacionPropia is not { } donde)
        {
            Colgar(_capaDeLaEstacion, []);
            return;
        }

        Colgar(
            _capaDeLaEstacion,
            CapasDelMapa.EstacionPropia(donde, IndicativoPropio ?? string.Empty, Paleta, LetraDeLaVentana));
    }

    /// <summary>
    /// Calcula las figuras fuera del hilo de la interfaz y las cuelga cuando estan listas.
    /// </summary>
    /// <remarks>
    /// Agrupar veinte mil contactos y trazar noventa y seis puntos por curva cuesta decimas de
    /// segundo. Hacerlo en el hilo de la ventana se notaria como un tiron cada vez que se mueve
    /// el mapa o pasa un minuto del reloj, asi que se hace aparte. Cada peticion nueva cancela
    /// la anterior: lo que importa es lo ultimo que pidio el operador, no lo que pidio antes.
    /// </remarks>
    /// <summary>
    /// Calcula unas figuras fuera del hilo de la ventana y las cuelga de su capa.
    /// </summary>
    /// <param name="capa">Capa que recibe el resultado.</param>
    /// <param name="calcular">Lo que hay que calcular, que puede tardar decimas de segundo.</param>
    /// <remarks>
    /// <b>Cada capa lleva su propio testigo de cancelacion.</b> Antes habia uno solo para todo
    /// el control, asi que rehacer una capa cancelaba el calculo de la anterior: pedir a la vez
    /// la noche y las lineas del paso gris —o las marcas y los trayectos— dejaba fuera a la
    /// primera, y la capa se quedaba vacia sin que nadie se enterara. Lo que si tiene que
    /// cancelarse es el calculo VIEJO DE LA MISMA CAPA, que ya no sirve para nada.
    /// </remarks>
    private void EnSegundoPlano(MemoryLayer capa, Func<IReadOnlyList<IFeature>> calcular)
    {
        if (_dibujosEnCurso.TryGetValue(capa, out var anterior))
        {
            anterior.Cancel();
            anterior.Dispose();
        }

        var testigo = new CancellationTokenSource();
        _dibujosEnCurso[capa] = testigo;
        System.Threading.Interlocked.Increment(ref _recalculosPedidos);

        var ct = testigo.Token;
        _ = Task.Run(
            () =>
            {
                IReadOnlyList<IFeature> figuras;
                try
                {
                    figuras = calcular();
                }
                catch (Exception ex)
                {
                    // Un dato imposible no puede dejar la ventana sin mapa: se deja la capa
                    // como estaba y se sigue. Pero SE ANOTA: tragarse el fallo en silencio fue
                    // lo que dejo el mapa en negro sin una sola linea en el registro.
                    System.Threading.Interlocked.Increment(ref _recalculosRotos);
                    Serilog.Log.Error(ex, "No se ha podido calcular la capa {Capa} del mapa.", capa.Name);
                    return;
                }

                if (ct.IsCancellationRequested)
                {
                    System.Threading.Interlocked.Increment(ref _recalculosCancelados);
                    return;
                }
                _ = Dispatcher.InvokeAsync(() =>
                {
                    if (ct.IsCancellationRequested || _liberado)
                    {
                        System.Threading.Interlocked.Increment(ref _recalculosCancelados);
                        return;
                    }

                    System.Threading.Interlocked.Increment(ref _recalculosColgados);
                    Colgar(capa, figuras);
                });
            },
            ct);
    }

    private void Colgar(MemoryLayer capa, IReadOnlyList<IFeature> figuras)
    {
        capa.Features = figuras;
        capa.DataHasChanged();
        _mapa.RefreshGraphics();
    }

    /// <summary>
    /// Deja en el registro que hay en cada capa. Diagnostico del mapa en negro.
    /// </summary>
    private void Contar()
    {
        // El latido solo cuesta algo si alguien lo va a leer. En marcha normal el registro va
        // a nivel de informacion y esto no hace nada.
        if (_liberado || !Serilog.Log.IsEnabled(Serilog.Events.LogEventLevel.Debug)) return;

        var capas = string.Join(
            " · ",
            _mapa.Map.Layers.Select(c => $"{c.Name}={(c is MemoryLayer m ? (m.Features?.Count() ?? -1) : -2)}"));

        Serilog.Log.Debug(
            "MAPA capas: {Capas} | resolucion={Resolucion} | vista={VistaAncho}x{VistaAlto}"
            + " centro=({CentroX},{CentroY}) | tamano={Ancho}x{Alto} | "
            + "recalculos pedidos={Pedidos} colgados={Colgados} cancelados={Cancelados} rotos={Rotos} | "
            + "marcas entrantes={Marcas} | memoria={MemoriaMb} MB | trabajo={TrabajoMb} MB",
            capas,
            _mapa.Map.Navigator.Viewport.Resolution,
            _mapa.Map.Navigator.Viewport.Width,
            _mapa.Map.Navigator.Viewport.Height,
            Math.Round(_mapa.Map.Navigator.Viewport.CenterX),
            Math.Round(_mapa.Map.Navigator.Viewport.CenterY),
            ActualWidth,
            ActualHeight,
            _recalculosPedidos,
            _recalculosColgados,
            _recalculosCancelados,
            _recalculosRotos,
            Marcas?.Count ?? -1,
            GC.GetTotalMemory(false) / (1024 * 1024),
            System.Diagnostics.Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024));
    }

    private void MostrarAviso(string? texto)
    {
        _aviso.Text = texto ?? string.Empty;
        _aviso.Visibility = texto is { Length: > 0 } ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Encuadra la primera vez y revisa el encuadre cada vez que cambia el tamano.
    /// </summary>
    /// <remarks>
    /// La revision hace falta porque el panel vive en una pestana: si la ventana se maximiza
    /// mientras se esta mirando otra, el motor de mapas se entera del tamano nuevo cuando ya
    /// no cuadra con el encuadre que tenia. Se le vuelve a dar el mismo centro y la misma
    /// escala —no se mueve lo que el operador estuviera mirando— y con eso recalcula la vista
    /// con el tamano bueno. Si lo que tiene no sirve para nada, se vuelve al mundo entero.
    /// </remarks>
    private void AlCambiarDeTamano(object origen, SizeChangedEventArgs args)
    {
        if (_liberado) return;
        if (args.NewSize.Width < 1 || args.NewSize.Height < 1) return;

        if (!_encuadrado)
        {
            _encuadrado = true;
            _ = Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Loaded,
                new Action(VerElMundo));
            return;
        }

        _ = Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Loaded,
            new Action(RevisarElEncuadre));
    }

    /// <summary>Devuelve la vista a un estado utilizable sin mover lo que se estaba mirando.</summary>
    private void RevisarElEncuadre()
    {
        if (_liberado) return;

        var vista = _mapa.Map.Navigator.Viewport;

        if (double.IsNaN(vista.Resolution) || vista.Resolution <= 0
            || double.IsNaN(vista.CenterX) || double.IsNaN(vista.CenterY))
        {
            VerElMundo();
            return;
        }

        _mapa.Map.Navigator.CenterOnAndZoomTo(new MPoint(vista.CenterX, vista.CenterY), vista.Resolution);
        _mapa.RefreshGraphics();
    }

    /// <summary>
    /// Ensena de quien es la marca que hay bajo el raton.
    /// </summary>
    /// <remarks>
    /// Con el cuaderno entero encima no caben los indicativos escritos —se pisarian unos a
    /// otros hasta tapar el mapa—, asi que el rotulo se guarda para cuando hay sitio y el
    /// resto del tiempo se dice aqui, que no ocupa nada hasta que hace falta.
    /// </remarks>
    private void AlPasarElRaton(object? origen, MapEventArgs args)
    {
        if (_liberado) return;

        var info = args.GetMapInfo([_capaDeLaEstacion, _capaDeMarcas]);
        var marca = info.Feature?[CapasDelMapa.ClaveDeLaMarca] as MarcaDelMapa;

        if (ReferenceEquals(marca, _marcaSenalada)) return;
        _marcaSenalada = marca;

        if (marca is null)
        {
            _mapa.ToolTip = null;
            return;
        }

        var cuantos = info.Feature?[CapasDelMapa.ClaveDeCuantos] as int? ?? 1;

        _mapa.ToolTip = cuantos > 1
            ? $"{marca.Etiqueta} y {cuantos - 1} más en esta zona"
            : marca.Etiqueta;
    }

    private void AlTocarElMapa(object? origen, MapEventArgs args)
    {
        if (_liberado || MarcaElegida is null) return;

        var info = args.GetMapInfo([_capaDeLaEstacion, _capaDeMarcas]);
        if (info.Feature?[CapasDelMapa.ClaveDeLaMarca] is MarcaDelMapa marca)
        {
            MarcaElegida.Invoke(this, marca);
        }
    }
}
