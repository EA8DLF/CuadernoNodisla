using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nodisla.Cuaderno.Aplicacion.CasosDeUso;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Entidades;

namespace Nodisla.Cuaderno.Ui.VistaModelos;

/// <summary>
/// La ventana principal entera: el formulario de entrada arriba, el cuaderno abajo y la barra
/// de estado con el perfil de estacion, la hora UTC y el numero de contactos.
/// </summary>
public sealed partial class VistaModeloPrincipal : ObservableObject
{
    private readonly IRepositorioEstacion _estaciones;
    private readonly BuscarEnCuaderno _buscar;
    private readonly IControlEquipo _control;
    private readonly Ajustes.EstadoDeLosPaneles _estadoDeLosPaneles;
    private readonly Ajustes.ArranqueDeOperacion _arranque;
    private readonly DispatcherTimer _reloj;

    /// <summary>Monta la ventana con todos sus paneles y arranca el reloj UTC.</summary>
    /// <param name="entrada">Formulario de entrada de contactos.</param>
    /// <param name="cuaderno">Rejilla del cuaderno.</param>
    /// <param name="equipo">Panel del equipo.</param>
    /// <param name="cluster">Panel del cluster de DX.</param>
    /// <param name="digital">Panel de modos digitales.</param>
    /// <param name="mapa">Panel del mapa.</param>
    /// <param name="estaciones">Perfiles de estacion.</param>
    /// <param name="buscar">Busqueda en el cuaderno.</param>
    /// <param name="control">Control del equipo, para llevarlo a la frecuencia de un spot.</param>
    /// <param name="estadoDeLosPaneles">Como dejo el operador los paneles la ultima vez.</param>
    public VistaModeloPrincipal(
        VistaModeloEntradaQso entrada,
        VistaModeloCuaderno cuaderno,
        VistaModeloEquipo equipo,
        VistaModeloCluster cluster,
        VistaModeloDigital digital,
        VistaModeloMapa mapa,
        IRepositorioEstacion estaciones,
        BuscarEnCuaderno buscar,
        IControlEquipo control,
        Ajustes.EstadoDeLosPaneles estadoDeLosPaneles,
        Ajustes.ArranqueDeOperacion arranque)
    {
        ArgumentNullException.ThrowIfNull(arranque);
        _arranque = arranque;
        ArgumentNullException.ThrowIfNull(entrada);
        ArgumentNullException.ThrowIfNull(cuaderno);
        ArgumentNullException.ThrowIfNull(equipo);
        ArgumentNullException.ThrowIfNull(cluster);
        ArgumentNullException.ThrowIfNull(digital);
        ArgumentNullException.ThrowIfNull(mapa);
        ArgumentNullException.ThrowIfNull(estadoDeLosPaneles);

        Entrada = entrada;
        Cuaderno = cuaderno;
        Equipo = equipo;
        Cluster = cluster;
        Digital = digital;
        Mapa = mapa;
        _estaciones = estaciones;
        _buscar = buscar;
        _control = control;
        _estadoDeLosPaneles = estadoDeLosPaneles;

        Entrada.CuadernoCambiado += async (_, _) => await RefrescarTodoAsync().ConfigureAwait(true);
        Cuaderno.SolicitaEditar += (_, qso) => Entrada.CargarParaEditar(qso);

        // El dial manda sobre el formulario mientras el equipo este conectado.
        Equipo.DialCambiado += (_, dial) => Entrada.SeguirAlDial(dial.Frecuencia, dial.Modo);
        Equipo.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(VistaModeloEquipo.Conectado))
            {
                Entrada.SiguiendoAlEquipo = Equipo.Conectado;
            }
        };

        Cluster.SpotElegido += async (_, fila) => await IrAlSpotAsync(fila).ConfigureAwait(true);
        Cluster.SpotsCambiaron += (_, _) =>
        {
            Mapa.PonerSpots(Cluster.Spots);

            // Los dos VFO ensenan quien esta anunciado en su propia frecuencia: es el dato
            // que decide si merece la pena llamar.
            Equipo.PonerSpots(Cluster.Spots);
        };
        Digital.CuadernoCambiado += async (_, _) => await RefrescarTodoAsync().ConfigureAwait(true);
        Mapa.MarcaElegida += (_, marca) => Entrada.Indicativo = marca.Etiqueta;

        RecuperarEstadoDeLosPaneles();

        _reloj = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1),
        };
        _reloj.Tick += (_, _) => Latir();
        Latir();
    }

    /// <summary>Formulario de entrada de contactos.</summary>
    public VistaModeloEntradaQso Entrada { get; }

    /// <summary>Rejilla del cuaderno.</summary>
    public VistaModeloCuaderno Cuaderno { get; }

    /// <summary>Panel del equipo.</summary>
    public VistaModeloEquipo Equipo { get; }

    /// <summary>Panel del cluster de DX.</summary>
    public VistaModeloCluster Cluster { get; }

    /// <summary>Panel de modos digitales.</summary>
    public VistaModeloDigital Digital { get; }

    /// <summary>Panel del mapa.</summary>
    public VistaModeloMapa Mapa { get; }

    /// <summary>Pestanas de la ventana, en el orden en que salen.</summary>
    public IReadOnlyList<string> Pestanas { get; } = ["Operar", "Cuaderno", "Mapa", "Diplomas", "Ajustes"];

    /// <summary>Perfiles de estacion disponibles.</summary>
    public ObservableCollection<Estacion> Estaciones { get; } = [];

    /// <summary>Escalas de letra que se ofrecen, en tanto por ciento.</summary>
    public IReadOnlyList<int> EscalasDeLetra { get; } = [100, 125, 150, 175, 200];

    /// <summary>Escalas ya como lista, para poder buscar la posicion de la actual.</summary>
    private List<int> Escalas => _escalas ??= [.. EscalasDeLetra];

    private List<int>? _escalas;

    [ObservableProperty]
    private string _horaUtc = string.Empty;

    [ObservableProperty]
    private string _fechaUtc = string.Empty;

    [ObservableProperty]
    private Estacion? _estacionActiva;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalDeQsosTexto))]
    private int _totalDeQsos;

    [ObservableProperty]
    private bool _temaOscuro;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PanelDeOperacionVisible))]
    [NotifyPropertyChangedFor(nameof(TextoDelPanelDeOperacion))]
    private bool _panelDeOperacionPedido = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PanelDeOperacionVisible))]
    [NotifyPropertyChangedFor(nameof(TextoDelPanelDeOperacion))]
    private bool _panelDeOperacionSinSitio;

    [ObservableProperty]
    private Ajustes.PanelDeOperacion _panelElegido = Ajustes.PanelDeOperacion.Mapa;

    [ObservableProperty]
    private double _anchoDelPanelEnLetras = 34;

    /// <summary>
    /// El equipo esta desplegado en la cabina.
    /// </summary>
    /// <remarks>
    /// El frontal ocupa una franja ancha, y hay ratos —un concurso en FT8, por ejemplo— en
    /// que lo que interesa es el cluster y los decodificados. Se pliega <b>a mano</b> y se
    /// recuerda; nunca se pliega solo. Plegarlo solo fue el error que dejo a Jose sin ver
    /// nunca la radio.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextoDelPliegueDelFrontal))]
    private bool _frontalDesplegado = true;

    /// <summary>
    /// La lista completa de mandos esta a la vista.
    /// </summary>
    /// <remarks>
    /// Es una vista <b>complementaria</b> del frontal, no un sustituto: se abre cuando se
    /// quiere y ensena todos los mandos que declara el equipo, incluidos los que el dibujo
    /// no tiene sitio para ensenar con su valor.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextoDeLaListaDeMandos))]
    private bool _listaDeMandosVisible;

    [ObservableProperty]
    private int _escalaDeLetra = 100;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EntradaVisible))]
    [NotifyPropertyChangedFor(nameof(CabeceraVisible))]
    [NotifyPropertyChangedFor(nameof(DatosAmpliadosVisibles))]
    [NotifyPropertyChangedFor(nameof(ContactosPreviosVisibles))]
    [NotifyPropertyChangedFor(nameof(TextoDelPliegue))]
    private bool _entradaPlegada;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DatosAmpliadosVisibles))]
    [NotifyPropertyChangedFor(nameof(TextoDeDatosAmpliados))]
    private bool _datosAmpliadosPedidos = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CabeceraVisible))]
    private bool _cabeceraSinSitio;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DatosAmpliadosVisibles))]
    [NotifyPropertyChangedFor(nameof(TextoDeDatosAmpliados))]
    private bool _datosAmpliadosSinSitio;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ContactosPreviosVisibles))]
    private bool _contactosPreviosSinSitio;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LeyendaVisible))]
    private bool _leyendaSinSitio;

    /// <summary>
    /// El aviso de «trabajado antes» se queda en una sola linea, con el texto completo en la
    /// sugerencia. Es el ultimo recurso antes de quitarle contactos al cuaderno: recortar un
    /// texto con puntos suspensivos no rompe nada; dejar el cuaderno en dos filas, si.
    /// </summary>
    [ObservableProperty]
    private bool _avisoCompacto;

    /// <summary>
    /// La lista de atajos de la barra de estado. Es lo primero que se pliega cuando falta alto:
    /// es una ayuda, no un dato, y con la letra grande se lleva dos lineas enteras.
    /// </summary>
    public bool LeyendaVisible => !LeyendaSinSitio;

    /// <summary>La zona de entrada esta desplegada.</summary>
    public bool EntradaVisible => !EntradaPlegada;

    /// <summary>
    /// La cabecera con el perfil, la escala y el reloj. Es lo primero que se sacrifica cuando
    /// no hay alto: su contenido esta repetido en la barra de estado.
    /// </summary>
    public bool CabeceraVisible => !EntradaPlegada && !CabeceraSinSitio;

    /// <summary>
    /// La segunda fila de la entrada: fecha, hora, nombre, QTH, localizador y comentario.
    /// Se puede plegar a mano con F7 y se pliega sola cuando el cuaderno se quedaria sin sitio.
    /// </summary>
    public bool DatosAmpliadosVisibles => !EntradaPlegada && DatosAmpliadosPedidos && !DatosAmpliadosSinSitio;

    /// <summary>La lista de contactos anteriores del aviso de «trabajado antes».</summary>
    public bool ContactosPreviosVisibles => !EntradaPlegada && !ContactosPreviosSinSitio;

    /// <summary>
    /// La columna de operacion esta a la vista.
    /// </summary>
    /// <remarks>
    /// Los paneles nuevos no pueden quitarle sitio al cuaderno ni a la entrada: se pliegan a
    /// mano con F10 y se pliegan solos cuando la ventana se estrecha tanto que el cuaderno se
    /// quedaria sin columnas que ensenar.
    /// </remarks>
    public bool PanelDeOperacionVisible => PanelDeOperacionPedido && !PanelDeOperacionSinSitio;

    /// <summary>Texto del boton que pliega y despliega la columna de operacion.</summary>
    public string TextoDelPanelDeOperacion =>
        PanelDeOperacionVisible ? "Ocultar operación (F10)" : "Mostrar operación (F10)";

    /// <summary>
    /// Se ensena el frontal del equipo dibujado.
    /// </summary>
    /// <remarks>
    /// <b>Siempre</b>, mientras el equipo declare sus mandos y el operador no lo haya plegado
    /// a mano. El dibujo es vectorial y vive dentro de un <c>Viewbox</c>: con la ventana
    /// estrecha o la letra al 200 % se <b>encoge</b>, no se recorta ni desaparece. Un frontal
    /// pequeno sigue siendo un frontal; ninguno no lo es.
    /// </remarks>
    public bool FrontalDibujadoVisible => Equipo.EsAvanzado && FrontalDesplegado;

    /// <summary>Texto del boton que pliega y despliega el frontal del equipo.</summary>
    public string TextoDelPliegueDelFrontal => FrontalDesplegado ? "Ocultar equipo" : "Mostrar equipo";

    /// <summary>Texto del boton que abre y cierra la lista completa de mandos.</summary>
    public string TextoDeLaListaDeMandos => ListaDeMandosVisible ? "Ocultar todos los mandos" : "Todos los mandos";

    /// <summary>Ancho de la columna de operacion en puntos, medido en letras.</summary>
    /// <remarks>
    /// Va en letras y no en pixeles para que al 200 % de escala la columna crezca con el
    /// texto: si se quedara en pixeles, a esa escala no cabrian ni los titulos.
    /// </remarks>
    public double AnchoDelPanel => Math.Round(TamanoDeLetra * AnchoDelPanelEnLetras, 0);

    /// <summary>Pestana que se esta viendo.</summary>
    [ObservableProperty]
    private int _indiceDeLaPestana;

    /// <summary>Texto del boton que pliega y despliega la zona de entrada.</summary>
    public string TextoDelPliegue => EntradaPlegada ? "Mostrar entrada (F6)" : "Ocultar entrada (F6)";

    /// <summary>Texto del boton que pliega y despliega la segunda fila de la entrada.</summary>
    public string TextoDeDatosAmpliados => DatosAmpliadosVisibles ? "Menos datos (F7)" : "Más datos (F7)";

    /// <summary>Tamano de letra base de la ventana, calculado a partir de la escala elegida.</summary>
    public double TamanoDeLetra => Math.Round(14.0 * EscalaDeLetra / 100.0, 1);

    /// <summary>
    /// Numero de contactos ya escrito en espanol.
    /// </summary>
    /// <remarks>
    /// Se formatea aqui y no con un <c>StringFormat</c> del XAML a proposito: los enlaces de
    /// WPF formatean con la cultura del elemento, que sale de las opciones de «Region» de
    /// Windows y puede traer separadores personalizados. Aqui manda la cultura que fija el
    /// programa, y asi siempre se lee «20.000».
    /// </remarks>
    public string TotalDeQsosTexto => TotalDeQsos.ToString("N0", CultureInfo.CurrentCulture);

    /// <summary>Nombre del perfil activo, para la barra de estado.</summary>
    public string PerfilActivo => EstacionActiva is { } e
        ? $"{e.NombrePerfil} · {e.StationCallsign.Valor}"
        : "Sin perfil de estación";

    /// <summary>Carga los perfiles de estacion, la primera pagina del cuaderno y el mapa.</summary>
    public async Task InicializarAsync()
    {
        var perfiles = await _estaciones.TodasAsync().ConfigureAwait(true);
        Estaciones.Clear();
        foreach (var e in perfiles) Estaciones.Add(e);

        EstacionActiva = await _estaciones.PredeterminadaAsync().ConfigureAwait(true) ?? Estaciones.FirstOrDefault();

        await RefrescarTodoAsync().ConfigureAwait(true);
        _reloj.Start();

        // El mapa carga aparte y sin bloquear: son decenas de miles de contactos y la ventana
        // tiene que poder usarse desde el primer segundo.
        _ = Mapa.CargarAsync();

        if (_arranque.ConectarSolo) _ = ArrancarLaOperacionAsync();
    }

    /// <summary>
    /// Conecta los tres puertos de operacion.
    /// </summary>
    /// <remarks>
    /// Solo se llama con los puertos simulados. Con una radio de verdad detras, quien pulsa
    /// «Conectar» es el operador: abrir un puerto serie y empezar a mandar ordenes CAT a un
    /// equipo que puede estar haciendo otra cosa no es decision del programa.
    /// </remarks>
    private async Task ArrancarLaOperacionAsync()
    {
        foreach (var conectar in new Func<Task>[]
                 {
                     () => Equipo.ConectarAsync(),
                     () => Cluster.ConectarAsync(),
                     () => Digital.ArrancarAsync(),
                 })
        {
            try
            {
                await conectar().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "No se ha podido arrancar uno de los paneles de operación.");
            }
        }
    }

    /// <summary>Pone a la vista la cabina de operacion.</summary>
    [RelayCommand]
    public void VerOperar() => IndiceDeLaPestana = 0;

    /// <summary>Pone a la vista el cuaderno.</summary>
    [RelayCommand]
    public void VerElCuaderno() => IndiceDeLaPestana = 1;

    /// <summary>Pone a la vista el mapa.</summary>
    [RelayCommand]
    public void VerElMapa() => IndiceDeLaPestana = 2;

    /// <summary>Guarda como han quedado los paneles. Lo llama la ventana al cerrarse.</summary>
    /// <param name="carpeta">Carpeta de datos del programa.</param>
    public void GuardarEstadoDeLosPaneles(string carpeta)
    {
        _estadoDeLosPaneles.TemaOscuro = TemaOscuro;
        _estadoDeLosPaneles.EscalaDeLetra = EscalaDeLetra;
        _estadoDeLosPaneles.Pestana = IndiceDeLaPestana;
        _estadoDeLosPaneles.EquipoDesplegado = FrontalDesplegado;
        _estadoDeLosPaneles.PanelVisible = PanelDeOperacionPedido;
        _estadoDeLosPaneles.PanelElegido = PanelElegido;
        _estadoDeLosPaneles.AnchoEnLetras = AnchoDelPanelEnLetras;
        _estadoDeLosPaneles.PasoGris = Mapa.MostrarPasoGris;
        _estadoDeLosPaneles.FondoDelMapa = Mapa.MostrarFondo;
        _estadoDeLosPaneles.ContactosEnElMapa = Mapa.MostrarContactos;
        _estadoDeLosPaneles.SpotsEnElMapa = Mapa.MostrarSpots;
        _estadoDeLosPaneles.Guardar(carpeta);
    }

    /// <summary>
    /// Lleva el equipo a la frecuencia del spot y prepara el formulario para trabajarlo.
    /// </summary>
    /// <param name="fila">Spot elegido.</param>
    public async Task IrAlSpotAsync(FilaDeSpot fila)
    {
        ArgumentNullException.ThrowIfNull(fila);

        Entrada.PonerDesdeElSpot(fila.Indicativo, fila.Spot.Frecuencia, fila.Modo);
        Mapa.TrazarHastaElSpot(fila);

        if (!Equipo.Conectado) return;

        try
        {
            await _control.PonerFrecuenciaAsync(fila.Spot.Frecuencia).ConfigureAwait(true);

            if (Dominio.Valores.Modo.TryParse(fila.Modo, null, out var modo))
            {
                await _control.PonerModoAsync(modo).ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "No se ha podido llevar el equipo al spot de {Indicativo}.", fila.Indicativo);
        }
    }

    private void MostrarPanel(Ajustes.PanelDeOperacion panel)
    {
        PanelElegido = panel;
        PanelDeOperacionPedido = true;
    }

    private void RecuperarEstadoDeLosPaneles()
    {
        TemaOscuro = _estadoDeLosPaneles.TemaOscuro;
        EscalaDeLetra = Escalas.Contains(_estadoDeLosPaneles.EscalaDeLetra)
            ? _estadoDeLosPaneles.EscalaDeLetra
            : 100;
        IndiceDeLaPestana = Math.Clamp(_estadoDeLosPaneles.Pestana, 0, Pestanas.Count - 1);
        FrontalDesplegado = _estadoDeLosPaneles.EquipoDesplegado;
        PanelDeOperacionPedido = _estadoDeLosPaneles.PanelVisible;
        PanelElegido = _estadoDeLosPaneles.PanelElegido;
        AnchoDelPanelEnLetras = _estadoDeLosPaneles.AnchoEnLetras;
        Mapa.MostrarPasoGris = _estadoDeLosPaneles.PasoGris;
        Mapa.MostrarFondo = _estadoDeLosPaneles.FondoDelMapa;
        Mapa.MostrarContactos = _estadoDeLosPaneles.ContactosEnElMapa;
        Mapa.MostrarSpots = _estadoDeLosPaneles.SpotsEnElMapa;
    }

    /// <summary>Cambia entre el tema claro y el oscuro.</summary>
    [RelayCommand]
    public void AlternarTema() => TemaOscuro = !TemaOscuro;

    /// <summary>
    /// Pliega la zona de entrada para dejarle toda la ventana al cuaderno, y la devuelve.
    /// La hora UTC y el perfil siguen a la vista en la barra de estado.
    /// </summary>
    [RelayCommand]
    public void AlternarEntrada() => EntradaPlegada = !EntradaPlegada;

    /// <summary>Pliega y despliega la segunda fila de la entrada.</summary>
    [RelayCommand]
    public void AlternarDatosAmpliados() => DatosAmpliadosPedidos = !DatosAmpliadosPedidos;

    /// <summary>
    /// Sube un paso la escala de letra.
    /// </summary>
    /// <remarks>
    /// Existe porque con la letra muy grande la cabecera se pliega para dejarle sitio al
    /// cuaderno, y con ella se iria el selector de escala: sin estos atajos, el operador no
    /// tendria manera de volver atras.
    /// </remarks>
    [RelayCommand]
    public void AumentarLetra() => MoverEscala(1);

    /// <summary>Baja un paso la escala de letra.</summary>
    [RelayCommand]
    public void ReducirLetra() => MoverEscala(-1);

    /// <summary>Devuelve la escala de letra al 100 %.</summary>
    [RelayCommand]
    public void LetraNormal() => EscalaDeLetra = 100;

    private void MoverEscala(int pasos)
    {
        var actual = Escalas.IndexOf(EscalaDeLetra);
        if (actual < 0) actual = Escalas.IndexOf(100);

        var destino = Math.Clamp(actual + pasos, 0, Escalas.Count - 1);
        EscalaDeLetra = Escalas[destino];
    }

    /// <summary>
    /// Deja de plegar por falta de sitio, para que la ventana vuelva a medir desde cero.
    /// Lo llama la vista antes de cada reparto de alto.
    /// </summary>
    public void OlvidarPliegueAutomatico()
    {
        PanelDeOperacionSinSitio = false;
        CabeceraSinSitio = false;
        DatosAmpliadosSinSitio = false;
        ContactosPreviosSinSitio = false;
        LeyendaSinSitio = false;
        AvisoCompacto = false;
    }

    /// <summary>Vuelve a leer el cuaderno: la pagina visible y el contador total.</summary>
    public async Task RefrescarTodoAsync()
    {
        await Cuaderno.RefrescarAsync().ConfigureAwait(true);
        TotalDeQsos = await _buscar.ContarTodoAsync().ConfigureAwait(true);
    }

    /// <summary>Detiene el reloj y suelta los paneles al cerrar la ventana.</summary>
    public void Detener()
    {
        _reloj.Stop();
        Equipo.Detener();
        Cluster.Detener();
        Digital.Detener();
    }

    partial void OnEstacionActivaChanged(Estacion? value)
    {
        Entrada.EstacionId = value?.Id;
        Digital.EstacionId = value?.Id;
        OnPropertyChanged(nameof(PerfilActivo));

        if (value is not null) Mapa.FijarEstacion(value.MyGridsquare, value.StationCallsign.Valor);
    }

    partial void OnEscalaDeLetraChanged(int value)
    {
        OnPropertyChanged(nameof(TamanoDeLetra));
        OnPropertyChanged(nameof(AnchoDelPanel));
    }

    partial void OnAnchoDelPanelEnLetrasChanged(double value) => OnPropertyChanged(nameof(AnchoDelPanel));

    partial void OnTemaOscuroChanged(bool value) => Recursos.Temas.Aplicar(value);

    private void Latir()
    {
        var ahora = DateTimeOffset.UtcNow;
        FechaUtc = ahora.UtcDateTime.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);
        HoraUtc = ahora.UtcDateTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        Entrada.ActualizarReloj(ahora);
        Mapa.ActualizarReloj(ahora);
    }
}
