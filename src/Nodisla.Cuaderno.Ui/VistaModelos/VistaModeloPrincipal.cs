using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nodisla.Cuaderno.Aplicacion.CasosDeUso;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Idiomas;

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
    /// <param name="modem">Panel del modem propio de FT8 y FT4.</param>
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
        VistaModeloModemPropio modem,
        VistaModeloMapa mapa,
        VistaModeloSolar solar,
        VistaModeloRetrato retrato,
        VistaModeloBandmap bandmap,
        VistaModeloDiplomas diplomas,
        VistaModeloAjustes configuracion,
        VistaModeloSatelites satelites,
        VistaModeloImpresion impresion,
        VistaModeloRonda ronda,
        IRepositorioEstacion estaciones,
        BuscarEnCuaderno buscar,
        IControlEquipo control,
        Ajustes.EstadoDeLosPaneles estadoDeLosPaneles,
        Ajustes.ArranqueDeOperacion arranque,
        VistaModeloSubidas? subidas = null,
        VistaModeloFonia? fonia = null,
        VistaModeloAnalizador? analizador = null,
        VistaModeloActualizaciones? actualizaciones = null,
        VistaModeloDisenadorDeDiplomas? disenadorDeDiplomas = null,
        VistaModeloAyuda? ayuda = null,
        VistaModeloCw? cw = null)
    {
        // Los textos calculados (pliegues, perfil, contador) siguen al idioma en caliente.
        Textos.AlCambiar(this, static vm =>
        {
            if (vm._avisoDelPerfilDeSiempre) vm.AvisoDelPerfil = Textos.T("Principal.Perfil.Aviso");
            vm.OnPropertyChanged(string.Empty);
        });
        Actualizaciones = actualizaciones;
        DisenadorDeDiplomas = disenadorDeDiplomas;
        Ayuda = ayuda;
        Cw = cw;
        Subidas = subidas;
        Fonia = fonia;
        Analizador = analizador ?? new VistaModeloAnalizador(null);
        ArgumentNullException.ThrowIfNull(arranque);
        _arranque = arranque;
        ArgumentNullException.ThrowIfNull(entrada);
        ArgumentNullException.ThrowIfNull(cuaderno);
        ArgumentNullException.ThrowIfNull(equipo);
        ArgumentNullException.ThrowIfNull(cluster);
        ArgumentNullException.ThrowIfNull(modem);
        ArgumentNullException.ThrowIfNull(mapa);
        ArgumentNullException.ThrowIfNull(solar);
        ArgumentNullException.ThrowIfNull(retrato);
        ArgumentNullException.ThrowIfNull(bandmap);
        ArgumentNullException.ThrowIfNull(diplomas);
        ArgumentNullException.ThrowIfNull(configuracion);
        ArgumentNullException.ThrowIfNull(satelites);
        ArgumentNullException.ThrowIfNull(impresion);
        ArgumentNullException.ThrowIfNull(ronda);
        ArgumentNullException.ThrowIfNull(estadoDeLosPaneles);

        Entrada = entrada;
        Cuaderno = cuaderno;
        Equipo = equipo;
        Cluster = cluster;
        Modem = modem;
        Mapa = mapa;
        Solar = solar;
        Retrato = retrato;
        Bandmap = bandmap;
        Diplomas = diplomas;
        Configuracion = configuracion;
        Satelites = satelites;
        Impresion = impresion;
        Ronda = ronda;
        _estaciones = estaciones;
        _buscar = buscar;
        _control = control;
        _estadoDeLosPaneles = estadoDeLosPaneles;

        // Lo que el CAT sabe del analizador de la radio (teclas de la pantalla, o cambios hechos
        // en la propia radio que ve el sondeo) llega al dibujo sin esperar a la trama.
        Analizador.Seguir(Equipo);

        Entrada.CuadernoCambiado += async (_, _) => await RefrescarTodoAsync().ConfigureAwait(true);
        if (subidas is not null)
        {
            subidas.CuadernoCambiado += async (_, _) => await RefrescarTodoAsync().ConfigureAwait(true);
        }

        // El retrato sigue al formulario: lo que se teclea en el indicativo, la banda o el
        // modo cambia la respuesta a «le llamo o no».
        Entrada.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(VistaModeloEntradaQso.Indicativo)
                or nameof(VistaModeloEntradaQso.Banda)
                or nameof(VistaModeloEntradaQso.Modo))
            {
                MirarElRetrato();
            }
        };
        // Modificar desde el cuaderno lleva a Operar, que es donde esta el formulario: antes se
        // cargaba el contacto en un formulario de otra pestaña y en pantalla no pasaba nada.
        Cuaderno.SolicitaEditar += (_, qso) =>
        {
            Entrada.CargarParaEditar(qso);
            IndiceDeLaPestana = 0;
        };

        // El dial manda sobre el formulario mientras el equipo este conectado.
        Equipo.DialCambiado += (_, dial) =>
        {
            Entrada.SeguirAlDial(dial.Frecuencia, dial.Modo, dial.FrecuenciaRx);
            // El bandmap sigue lo que se escucha: con split, el VFO activo (el de recepcion).
            Bandmap.PonerElDial(dial.FrecuenciaRx ?? dial.Frecuencia, dial.Modo.NombreUsual);

            // El modem propio necesita el dial para poder componer el contacto: lo que se
            // apunta es el dial mas el tono de audio, no el dial a secas.
            Modem.PonerElDial(dial.Frecuencia);
        };

        // Tocar un anuncio del bandmap hace lo mismo que tocarlo en la lista: llevar el equipo
        // a esa frecuencia y poner el indicativo en el formulario.
        Bandmap.SpotElegido += async (_, fila) => await IrAlSpotAsync(fila).ConfigureAwait(true);
        Equipo.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(VistaModeloEquipo.Conectado))
            {
                Entrada.SiguiendoAlEquipo = Equipo.Conectado;
            }

            // FrontalDibujadoVisible se calcula a partir de Equipo.EsAvanzado, pero es una
            // propiedad de ESTA clase: WPF no la vuelve a mirar solo porque EsAvanzado haya
            // cambiado alli dentro. Sin este aviso, cambiar de equipo con Aplicar (o que el
            // control conmutable pase de ControlNulo al FT-710 real al conectar) dejaba el
            // frontal sin dibujar hasta que algo mas disparase un refresco por casualidad -
            // que es justo lo que le paso al operador: el equipo contestaba y admitia 27 mandos,
            // pero el panel seguia diciendo «Todavia no hay equipo conectado».
            if (args.PropertyName == nameof(VistaModeloEquipo.EsAvanzado))
            {
                OnPropertyChanged(nameof(FrontalDibujadoVisible));
                OnPropertyChanged(nameof(SinEquipoAvanzado));
                OnPropertyChanged(nameof(FrontalPlegado));
            }
        };

        // Arranque con un indicativo ya escrito, para poder capturar la ventana con el retrato
        // lleno sin tener que teclear. Es el mismo recurso que CUADERNO_SIN_PERFILES: una
        // variable de entorno que en uso normal no esta puesta y no hace nada.
        if (Environment.GetEnvironmentVariable("CUADERNO_INDICATIVO") is { Length: > 0 } inicial)
        {
            Entrada.Indicativo = inicial;

            // Y con CUADERNO_REGISTRAR puesta, ademas lo registra. Sirve para comprobar que el
            // contacto sobrevive a cerrar y abrir sin tener que teclear en la ventana de nadie.
            if (Environment.GetEnvironmentVariable("CUADERNO_REGISTRAR") is { Length: > 0 })
            {
                _ = Entrada.GuardarCommand.ExecuteAsync(null);
            }
        }

        // Con CUADERNO_PROBAR_CAT puesta, se pulsa solo el «Probar» del apartado CAT nada mas
        // abrir. Sirve para comprobar de verdad que la busqueda del equipo termina y que el
        // parte sale en pantalla, sin tener que darle clics a la ventana del operador —que es
        // justo lo que no se puede hacer—. En uso normal la variable no esta y esto no existe.
        if (Environment.GetEnvironmentVariable("CUADERNO_PROBAR_CAT") is { Length: > 0 }
            && Configuracion.Cat is { } cat)
        {
            _ = cat.ProbarCommand.ExecuteAsync(null);
        }

        Cluster.SpotElegido += async (_, fila) => await IrAlSpotAsync(fila).ConfigureAwait(true);
        Cluster.SpotsCambiaron += (_, _) =>
        {
            Mapa.PonerSpots(Cluster.Spots);

            // Los dos VFO ensenan quien esta anunciado en su propia frecuencia: es el dato
            // que decide si merece la pena llamar.
            Equipo.PonerSpots(Cluster.Spots);
            Bandmap.PonerSpots(Cluster.Spots);
        };
        Modem.CuadernoCambiado += async (_, _) => await RefrescarTodoAsync().ConfigureAwait(true);
        Ronda.CuadernoCambiado += async (_, _) => await RefrescarTodoAsync().ConfigureAwait(true);
        Cuaderno.CuadernoCambiado += async (_, _) => await RefrescarTodoAsync().ConfigureAwait(true);
        Mapa.MarcaElegida += (_, marca) => Entrada.Indicativo = marca.Etiqueta;

        // Telegrafía: un indicativo pulsado en el texto va al contacto nuevo, y el panel sigue
        // el modo y el pitch del equipo en cuanto cambian (y además cada segundo, en Latir).
        if (Cw is not null)
        {
            Cw.IndicativoElegido += (_, indicativo) => Entrada.Indicativo = indicativo;
            Equipo.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName is nameof(VistaModeloEquipo.Modo) or nameof(VistaModeloEquipo.Conectado)) SeguirAlEquipoEnCw();
            };
            if (Configuracion.Audio is { } audio) audio.AjustesDeCwGuardados += (_, _) => Cw.AplicarAjustes();
        }

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

    /// <summary>
    /// Subida automatica a LoTW, eQSL, Club Log y QRZ, y el completado con QRZ: las pastillas
    /// de la barra de estado. Nulo si no se ha montado.
    /// </summary>
    public VistaModeloSubidas? Subidas { get; }

    /// <summary>Rejilla del cuaderno.</summary>
    public VistaModeloCuaderno Cuaderno { get; }

    /// <summary>Panel del equipo.</summary>
    public VistaModeloEquipo Equipo { get; }

    /// <summary>Fonía por el PC: altavoces, micrófono y PTT de fonía. Nulo si no se registró.</summary>
    public VistaModeloFonia? Fonia { get; }

    /// <summary>El decodificador de telegrafía de la cabina. Nulo si no se registró.</summary>
    public VistaModeloCw? Cw { get; }

    /// <summary>Hay decodificador de telegrafía (se ofrece el botón «CW»).</summary>
    public bool HayCw => Cw is not null;

    /// <summary>El analizador de espectro de la propia radio, para la pantalla del frontal.</summary>
    public VistaModeloAnalizador Analizador { get; }

    /// <summary>Panel del cluster de DX.</summary>
    public VistaModeloCluster Cluster { get; }

    /// <summary>
    /// El modem propio de modos digitales: cascada, decodificaciones, reloj y secuencia del
    /// contacto. No hay puente con ningun programa de fuera.
    /// </summary>
    public VistaModeloModemPropio Modem { get; }

    /// <summary>Panel del mapa.</summary>
    public VistaModeloMapa Mapa { get; }

    /// <summary>
    /// La franja solar: indices del Sol y horas de orto y ocaso.
    /// </summary>
    /// <remarks>
    /// El programa ya calculaba todo esto y no se ensenaba en ninguna parte. Va arriba del
    /// todo porque es de lo primero que se mira antes de decidir en que banda llamar.
    /// </remarks>
    public VistaModeloSolar Solar { get; }

    /// <summary>
    /// El retrato del indicativo que se esta tecleando: si es nuevo y por donde lo tienes.
    /// </summary>
    /// <remarks>
    /// Responde a «le llamo o no» sin leer una frase. Sale del mismo cuaderno de siempre; lo
    /// que faltaba era ensenarlo.
    /// </remarks>
    public VistaModeloRetrato Retrato { get; }

    /// <summary>
    /// El bandmap: los mismos anuncios del cluster, pero por frecuencia.
    /// </summary>
    /// <remarks>
    /// Come de la misma lista que el panel de cluster y del mismo dial que el frontal: no hay
    /// una segunda fuente que se pueda desincronizar.
    /// </remarks>
    public VistaModeloBandmap Bandmap { get; }

    /// <summary>La pantalla de diplomas.</summary>
    public VistaModeloDiplomas Diplomas { get; }

    /// <summary>
    /// La pantalla de ajustes.
    /// </summary>
    /// <remarks>
    /// Se llama <c>Configuracion</c> y no <c>Ajustes</c> porque <c>Ajustes</c> es ya el
    /// espacio de nombres donde viven el estado de los paneles y la seleccion de diplomas, y
    /// una propiedad con ese nombre lo taparia dentro de esta clase.
    /// </remarks>
    public VistaModeloAjustes Configuracion { get; }

    /// <summary>El panel de satelites: catalogo, pasos, seguimiento en vivo y Doppler.</summary>
    public VistaModeloSatelites Satelites { get; }

    /// <summary>La pantalla de impresion de etiquetas de QSL.</summary>
    public VistaModeloImpresion Impresion { get; }

    /// <summary>
    /// La ronda de control (NET Control): abrir una red, ir anadiendo participantes por su
    /// indicativo y confirmar cada uno como contacto real del cuaderno.
    /// </summary>
    public VistaModeloRonda Ronda { get; }

    /// <summary>
    /// El aviso de versiones nuevas: el MISMO objeto para la barra de arriba, el apartado
    /// «Actualizaciones» de Configuración y el botón de la Ayuda. Nulo en pruebas.
    /// </summary>
    public VistaModeloActualizaciones? Actualizaciones { get; }

    /// <summary>El diseñador de diplomas (QSL → Diplomas). Nulo en pruebas.</summary>
    public VistaModeloDisenadorDeDiplomas? DisenadorDeDiplomas { get; }

    /// <summary>La ayuda integrada. Nula en pruebas.</summary>
    public VistaModeloAyuda? Ayuda { get; }

    /// <summary>
    /// La aplicacion esta corriendo con los puertos simulados.
    /// </summary>
    /// <remarks>
    /// <b>Tiene que verse en pantalla.</b> Con los simulados detras, el cuaderno son veinte mil
    /// contactos de relleno que se pierden al cerrar, la radio no existe y los anuncios del
    /// cluster son inventados. Creer que se esta mirando el cuaderno de verdad seria el peor
    /// malentendido posible.
    /// </remarks>
    public static bool ModoSimulado => ConfiguracionDeServicios.ConPuertosSimulados;

    /// <summary>Paginas de la ventana, por su indice.</summary>
    /// <remarks>
    /// <para>
    /// El indice es el de siempre y no se reordena: es lo que se guarda al cerrar, lo que pide
    /// <c>CUADERNO_PESTANA</c> y lo que va detras de Ctrl 1…Ctrl 9. Lo que ha cambiado es como
    /// se ENSEÑAN: ya no son nueve pestañas en fila sino cuatro entradas agrupadas por uso
    /// (Operar, Libro, QSL, Diplomas) y, a la derecha del todo, Ayuda y Configuración. Ver
    /// <see cref="Grupos"/>.
    /// </para>
    /// <para>
    /// «Etiquetas» va la ultima porque antes era una pestaña DENTRO de «Imprimir»; ahora es
    /// una pagina del grupo QSL como las demas.
    /// </para>
    /// </remarks>
    public IReadOnlyList<string> Pestanas { get; } =
        ["Operar", "Digital", "Cuaderno", "Mapa", "Diplomas", "Configuración", "Satélites", "Tarjeta QSL", "Ronda", "Etiquetas",
            "Diseñador de diplomas", "Ayuda"];

    /// <summary>
    /// Las entradas de primer nivel de la barra y las paginas que agrupa cada una.
    /// </summary>
    /// <remarks>
    /// Operar: la cabina, lo digital, los satelites y la ronda de control, que es todo lo que
    /// se hace con el equipo en la mano. Libro: los contactos y su mapa. QSL: la tarjeta y las
    /// etiquetas del buro. Diplomas. Y, aparte y a la derecha, Configuración.
    /// </remarks>
    public static IReadOnlyDictionary<string, int[]> Grupos { get; } = new Dictionary<string, int[]>
    {
        ["Operar"] = [PaginaOperar, PaginaDigital, PaginaSatelites, PaginaRonda],
        ["Libro"] = [PaginaCuaderno, PaginaMapa],
        ["QSL"] = [PaginaQsl, PaginaEtiquetas, PaginaDisenadorDeDiplomas],
        ["Diplomas"] = [PaginaDiplomas],
        ["Ayuda"] = [PaginaAyuda],
        ["Configuración"] = [PaginaConfiguracion],
    };

    /// <summary>Indices de las paginas.</summary>
    public const int PaginaOperar = 0, PaginaDigital = 1, PaginaCuaderno = 2, PaginaMapa = 3,
        PaginaDiplomas = 4, PaginaConfiguracion = 5, PaginaSatelites = 6, PaginaQsl = 7,
        PaginaRonda = 8, PaginaEtiquetas = 9, PaginaDisenadorDeDiplomas = 10, PaginaAyuda = 11;

    /// <summary>
    /// El capitulo de la ayuda que explica cada pagina: lo que abre F1 desde ella.
    /// </summary>
    public static IReadOnlyDictionary<int, string> CapituloDeCadaPagina { get; } = new Dictionary<int, string>
    {
        [PaginaOperar] = "02-operar",
        [PaginaDigital] = "03-digital",
        [PaginaCuaderno] = "04-cuaderno",
        [PaginaMapa] = "06-mapa",
        [PaginaDiplomas] = "05-diplomas",
        [PaginaConfiguracion] = "09-ajustes",
        [PaginaSatelites] = "07-satelites",
        [PaginaQsl] = "13-tarjeta-qsl",
        [PaginaRonda] = "11-ronda-de-control",
        [PaginaEtiquetas] = "08-impresion-qsl",
        [PaginaDisenadorDeDiplomas] = "14-disenador-de-diplomas",
    };

    /// <summary>La ultima pagina vista de cada grupo: al volver al grupo se vuelve a ella.</summary>
    private readonly Dictionary<string, int> _ultimaDelGrupo = [];

    /// <summary>Nombre del grupo al que pertenece una pagina.</summary>
    /// <param name="pagina">Indice de la pagina.</param>
    /// <returns>El grupo, o «Operar» si el indice no es de ninguno.</returns>
    public static string GrupoDe(int pagina) =>
        Grupos.FirstOrDefault(g => g.Value.Contains(pagina)).Key ?? "Operar";

    /// <summary>Grupo de la pagina que se esta viendo.</summary>
    public string GrupoActivo => GrupoDe(IndiceDeLaPestana);

    /// <summary>Se ve una pagina del grupo Operar: salen sus subentradas.</summary>
    public bool EnElGrupoOperar => GrupoActivo == "Operar";

    /// <summary>Se ve una pagina del grupo Libro.</summary>
    public bool EnElGrupoLibro => GrupoActivo == "Libro";

    /// <summary>Se ve una pagina del grupo QSL.</summary>
    public bool EnElGrupoQsl => GrupoActivo == "QSL";

    /// <summary>
    /// Va a un grupo de la barra: a la pagina de ese grupo que se vio la ultima vez, o a la
    /// primera si no se ha visto ninguna.
    /// </summary>
    /// <param name="grupo">Nombre del grupo, tal y como sale en <see cref="Grupos"/>.</param>
    [RelayCommand]
    public void VerElGrupo(string? grupo)
    {
        if (grupo is null || !Grupos.TryGetValue(grupo, out var paginas)) return;

        IndiceDeLaPestana = _ultimaDelGrupo.TryGetValue(grupo, out var ultima) ? ultima : paginas[0];
    }

    /// <summary>Perfiles de estacion disponibles.</summary>
    public ObservableCollection<Estacion> Estaciones { get; } = [];

    /// <summary>Escalas de letra que se ofrecen, en tanto por ciento.</summary>
    public IReadOnlyList<int> EscalasDeLetra { get; } = [100, 125, 150, 175, 200];

    /// <summary>
    /// La escala de letra escrita, para el mando fijo de la barra de estado.
    /// </summary>
    /// <remarks>
    /// Ese mando no puede seguir a la escala —si creciera con ella, al 200 % se saldria de la
    /// ventana igual que la cabecera— asi que ensena el numero el mismo.
    /// </remarks>
    public string EscalaDeLetraTexto => $"{EscalaDeLetra} %";

    /// <summary>Escalas ya como lista, para poder buscar la posicion de la actual.</summary>
    private List<int> Escalas => _escalas ??= [.. EscalasDeLetra];

    private List<int>? _escalas;

    [ObservableProperty]
    private string _horaUtc = string.Empty;

    [ObservableProperty]
    private string _fechaUtc = string.Empty;

    /// <summary>Hora local del PC (en Canarias, UTC+0 en invierno y UTC+1 en verano).</summary>
    [ObservableProperty]
    private string _horaLocal = string.Empty;

    /// <summary>Fecha local y diferencia con UTC, p. ej. «28-09-2026 · UTC+1».</summary>
    [ObservableProperty]
    private string _fechaLocal = string.Empty;

    [ObservableProperty]
    private Estacion? _estacionActiva;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalDeQsosTexto))]
    private int _totalDeQsos;

    [ObservableProperty]
    private bool _temaOscuro = true;

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
    /// recuerda; nunca se pliega solo. Plegarlo solo fue el error que dejo al operador sin ver
    /// nunca la radio.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextoDelPliegueDelFrontal))]
    // Sin este aviso el frontal se quedaba congelado: el pliegue cambiaba el texto del boton
    // pero FrontalDibujadoVisible no se volvia a mirar, y con el estado guardado «plegado» el operador
    // veia «Todavia no hay equipo conectado» con el FT-710 conectado (27-09-2026).
    [NotifyPropertyChangedFor(nameof(FrontalDibujadoVisible))]
    [NotifyPropertyChangedFor(nameof(FrontalPlegado))]
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
        Textos.T(PanelDeOperacionVisible ? "Principal.Pliegue.OcultarOperacion" : "Principal.Pliegue.MostrarOperacion");

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

    /// <summary>
    /// No hay equipo que hable CAT: solo entonces sale el cartel «Todavía no hay equipo conectado».
    /// </summary>
    /// <remarks>
    /// Antes ese cartel salía siempre que el frontal no se dibujaba, también con el FT-710
    /// conectado y el frontal simplemente plegado: mentía, y confundió al operador (27-09-2026).
    /// </remarks>
    public bool SinEquipoAvanzado => !Equipo.EsAvanzado;

    /// <summary>Hay equipo conectado pero el operador ha plegado el frontal.</summary>
    public bool FrontalPlegado => Equipo.EsAvanzado && !FrontalDesplegado;

    /// <summary>Texto del boton que pliega y despliega el frontal del equipo.</summary>
    public string TextoDelPliegueDelFrontal => Textos.T(FrontalDesplegado ? "Principal.Pliegue.OcultarEquipo" : "Principal.Operar.MostrarEquipo");

    /// <summary>Texto del boton que abre y cierra la lista completa de mandos.</summary>
    public string TextoDeLaListaDeMandos => Textos.T(ListaDeMandosVisible ? "Principal.Pliegue.OcultarMandos" : "Principal.Operar.TodosLosMandos");

    /// <summary>Ancho de la columna de operacion en puntos, medido en letras.</summary>
    /// <remarks>
    /// Va en letras y no en pixeles para que al 200 % de escala la columna crezca con el
    /// texto: si se quedara en pixeles, a esa escala no cabrian ni los titulos.
    /// </remarks>
    public double AnchoDelPanel => Math.Round(TamanoDeLetra * AnchoDelPanelEnLetras, 0);

    /// <summary>Pestana que se esta viendo.</summary>
    [ObservableProperty]
    private int _indiceDeLaPestana;

    /// <summary>Posicion de la pestana de los modos digitales.</summary>
    private const int PestanaDeLosDigitales = 1;

    /// <summary>
    /// Al entrar en la pestana Digital se empieza a mirar el reloj.
    /// </summary>
    /// <remarks>
    /// Aqui y no al arrancar: abrir el programa no tiene por que ponerse a hablar con
    /// servidores de hora de nadie. Al entrar en la pestana si, porque el desvio es lo primero
    /// que hay que ver antes de operar en FT8.
    /// </remarks>
    partial void OnIndiceDeLaPestanaChanged(int value)
    {
        if (value == PestanaDeLosDigitales) Modem.Asomarse();

        _ultimaDelGrupo[GrupoDe(value)] = value;
        OnPropertyChanged(nameof(GrupoActivo));
        OnPropertyChanged(nameof(EnElGrupoOperar));
        OnPropertyChanged(nameof(EnElGrupoLibro));
        OnPropertyChanged(nameof(EnElGrupoQsl));
    }

    /// <summary>
    /// Que se mira a la derecha del contacto nuevo: cero la lista del cluster, uno el bandmap.
    /// </summary>
    [ObservableProperty]
    private int _indiceDeLaListaDeSpots;

    /// <summary>Texto del boton que pliega y despliega la zona de entrada.</summary>
    public string TextoDelPliegue => Textos.T(EntradaPlegada ? "Principal.Pliegue.MostrarEntrada" : "Principal.Pliegue.OcultarEntrada");

    /// <summary>Texto del boton que pliega y despliega la segunda fila de la entrada.</summary>
    public string TextoDeDatosAmpliados => Textos.T(DatosAmpliadosVisibles ? "Principal.Pliegue.MenosDatos" : "Principal.Pliegue.MasDatos");

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
    public string TotalDeQsosTexto => TotalDeQsos.ToString("N0", Textos.Cultura);

    /// <summary>Nombre del perfil activo, para la barra de estado.</summary>
    public string PerfilActivo => EstacionActiva is { } e
        ? $"{e.NombrePerfil} · {e.StationCallsign.Valor}"
        : Textos.T("Principal.Perfil.SinPerfil");

    /// <summary>Carga los perfiles de estacion, la primera pagina del cuaderno y el mapa.</summary>
    public async Task InicializarAsync()
    {
        var perfiles = await _estaciones.TodasAsync().ConfigureAwait(true);
        Estaciones.Clear();
        foreach (var e in perfiles) Estaciones.Add(e);

        // Se elige POR IDENTIFICADOR, no por objeto. Con el cuaderno de verdad detras, cada
        // consulta devuelve instancias distintas —vienen de la base y no se comparten—, asi
        // que el perfil predeterminado NO es el mismo objeto que el de la lista y la casilla
        // se quedaba en blanco aunque el perfil estuviera cargado. Con el repositorio en
        // memoria no pasaba, porque alli era el mismo objeto.
        var predeterminado = await _estaciones.PredeterminadaAsync().ConfigureAwait(true);

        var elegida = predeterminado is null
            ? Estaciones.FirstOrDefault()
            : Estaciones.FirstOrDefault(e => e.Id == predeterminado.Id) ?? predeterminado;

        if (elegida is not null) await CompletarElPerfilAsync(elegida).ConfigureAwait(true);
        EstacionActiva = elegida;

        await RefrescarTodoAsync().ConfigureAwait(true);
        _reloj.Start();

        // El mapa carga aparte y sin bloquear: son decenas de miles de contactos y la ventana
        // tiene que poder usarse desde el primer segundo.
        _ = Mapa.CargarAsync();
        _mapaCargado = true;

        // La cache de elementos orbitales es lectura de disco, no de red: se trae aparte y sin
        // bloquear el arranque, igual que el mapa.
        _ = Satelites.CargarCacheAsync();

        // Si quedo una ronda de control abierta la ultima vez, se reabre sola: es el propio
        // repositorio quien sabe si hay una sin cerrar, no un fichero de estado aparte.
        _ = Ronda.CargarAsync();

        // Si la ventana abre directamente en la pestaña Digital —porque así se dejó la última
        // vez—, el reloj se pone a vigilar ya: el cambio de pestaña no llega a saltar y sin
        // esto el desvío se quedaría sin medir justo donde más falta hace.
        if (IndiceDeLaPestana == PestanaDeLosDigitales) Modem.Asomarse();

        if (_arranque.ConectarSolo) _ = ArrancarLaOperacionAsync();

        // Solo para verificar con la radio de verdad (instancia apartada, CUADERNO_CAPTURA): el
        // operador no pulsa «Conectar», asi que se pide con CUADERNO_CONECTAR. Sin ella, nada.
        else if (Environment.GetEnvironmentVariable("CUADERNO_CONECTAR") is { Length: > 0 }) _ = Equipo.ConectarAsync();
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

                     // El modem propio también, y sin miedo: con los puertos simulados lo que
                     // hay detrás es un modem de mentira y una entrada de audio de mentira,
                     // así que esto NO abre ninguna tarjeta de sonido. Con los puertos de
                     // verdad no se llega hasta aquí, porque entonces no se conecta nada solo.
                     () => Modem.EscucharAsync(),
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

    /// <summary>Pone a la vista los modos digitales.</summary>
    [RelayCommand]
    public void VerLoDigital() => IndiceDeLaPestana = 1;

    /// <summary>Pone a la vista el cuaderno.</summary>
    [RelayCommand]
    public void VerElCuaderno() => IndiceDeLaPestana = 2;

    /// <summary>Pone a la vista el mapa.</summary>
    [RelayCommand]
    public void VerElMapa() => IndiceDeLaPestana = 3;

    /// <summary>Pone a la vista los diplomas.</summary>
    [RelayCommand]
    public void VerLosDiplomas() => IndiceDeLaPestana = 4;

    /// <summary>Pone a la vista la configuración.</summary>
    [RelayCommand]
    public void VerLosAjustes() => IndiceDeLaPestana = PaginaConfiguracion;

    /// <summary>Pone a la vista los satelites.</summary>
    [RelayCommand]
    public void VerLosSatelites() => IndiceDeLaPestana = PaginaSatelites;

    /// <summary>Pone a la vista la tarjeta QSL.</summary>
    [RelayCommand]
    public void VerLaQsl() => IndiceDeLaPestana = PaginaQsl;

    /// <summary>Pone a la vista las etiquetas del buro.</summary>
    [RelayCommand]
    public void VerLasEtiquetas() => IndiceDeLaPestana = PaginaEtiquetas;

    /// <summary>Pone a la vista la ronda de control.</summary>
    [RelayCommand]
    public void VerLaRonda() => IndiceDeLaPestana = PaginaRonda;

    /// <summary>Pone a la vista el diseñador de diplomas (QSL → Diplomas).</summary>
    [RelayCommand]
    public void VerElDisenadorDeDiplomas() => IndiceDeLaPestana = PaginaDisenadorDeDiplomas;

    /// <summary>Pone a la vista la ayuda, en el capitulo que tuviera abierto.</summary>
    [RelayCommand]
    public void VerLaAyuda() => IndiceDeLaPestana = PaginaAyuda;

    /// <summary>
    /// F1: abre la ayuda por el capitulo que explica la pagina en la que se esta. Desde la
    /// propia ayuda no hace nada: ya se esta en ella.
    /// </summary>
    [RelayCommand]
    public void VerLaAyudaDeEstaPagina()
    {
        if (IndiceDeLaPestana == PaginaAyuda) return;

        if (Ayuda is not null && CapituloDeCadaPagina.TryGetValue(IndiceDeLaPestana, out var capitulo))
        {
            Ayuda.AbrirCapitulo(capitulo);
        }

        IndiceDeLaPestana = PaginaAyuda;
    }

    /// <summary>Guarda como han quedado los paneles. Lo llama la ventana al cerrarse.</summary>
    /// <param name="carpeta">Carpeta de datos del programa.</param>
    public void GuardarEstadoDeLosPaneles(string carpeta)
    {
        _estadoDeLosPaneles.TemaOscuro = TemaOscuro;
        _estadoDeLosPaneles.EscalaDeLetra = EscalaDeLetra;
        _estadoDeLosPaneles.Pestana = IndiceDeLaPestana;

        // El modo y el tono del modem viven en el fichero de configuracion, no en el del
        // estado de los paneles: son cosa del operador, no de como dejo la ventana.
        Modem.GuardarLoElegido(carpeta);
        _estadoDeLosPaneles.ListaDeSpots = IndiceDeLaListaDeSpots;
        _estadoDeLosPaneles.EquipoDesplegado = FrontalDesplegado;
        _estadoDeLosPaneles.PanelVisible = PanelDeOperacionPedido;
        _estadoDeLosPaneles.PanelElegido = PanelElegido;
        _estadoDeLosPaneles.AnchoEnLetras = AnchoDelPanelEnLetras;
        _estadoDeLosPaneles.PasoGris = Mapa.MostrarPasoGris;
        _estadoDeLosPaneles.FondoDelMapa = Mapa.MostrarFondo;
        _estadoDeLosPaneles.ContactosEnElMapa = Mapa.MostrarContactos;
        _estadoDeLosPaneles.SpotsEnElMapa = Mapa.MostrarSpots;
        _estadoDeLosPaneles.ColumnasDelCuaderno = Cuaderno.ColumnasVisibles();
        _estadoDeLosPaneles.ContactosPorPagina = Cuaderno.TamanoDePagina;
        _estadoDeLosPaneles.PlanDeCanalesCb = Equipo.PlanCb.ToString();
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

                // Cambiar de modo corre el dial en el FT-710 (visto en la radio el 29-09-2026: un
                // spot de FT8 en 14.074.000 quedaba en 14.074.700 al pasar a DATA-U). Se vuelve a
                // poner la frecuencia del spot, que es la que manda.
                await _control.PonerFrecuenciaAsync(fila.Spot.Frecuencia).ConfigureAwait(true);
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

        IndiceDeLaListaDeSpots = Math.Clamp(_estadoDeLosPaneles.ListaDeSpots, 0, 1);
        if (Enum.TryParse<PlanCb>(_estadoDeLosPaneles.PlanDeCanalesCb, out var plan)) Equipo.PlanCb = plan;

        if (Environment.GetEnvironmentVariable("CUADERNO_LISTA_DE_SPOTS") is { Length: > 0 } lista
            && int.TryParse(lista, System.Globalization.NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var cual))
        {
            IndiceDeLaListaDeSpots = Math.Clamp(cual, 0, 1);
        }

        // Con que pestana abre, si se ha pedido. Va DESPUES de recuperar lo guardado, porque
        // si no lo guardado lo pisa. Sirve para capturar cada pantalla sin tener que darle
        // clics a la ventana del operador, que es lo que no se puede hacer.
        if (Environment.GetEnvironmentVariable("CUADERNO_PESTANA") is { Length: > 0 } pestana
            && int.TryParse(pestana, System.Globalization.NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var indice))
        {
            IndiceDeLaPestana = Math.Clamp(indice, 0, Pestanas.Count - 1);
        }
        FrontalDesplegado = _estadoDeLosPaneles.EquipoDesplegado;

        // Y si el frontal va plegado o no, para capturar las pestañas que lo llevan encima.
        if (Environment.GetEnvironmentVariable("CUADERNO_FRONTAL") is { Length: > 0 } frontal)
        {
            FrontalDesplegado = frontal != "0";
        }

        // Y la lista de todos los mandos abierta, para poder revisar sus enlaces en una captura.
        if (Environment.GetEnvironmentVariable("CUADERNO_MANDOS") is { Length: > 0 } mandos)
        {
            ListaDeMandosVisible = mandos != "0";
        }
        PanelDeOperacionPedido = _estadoDeLosPaneles.PanelVisible;
        PanelElegido = _estadoDeLosPaneles.PanelElegido;
        AnchoDelPanelEnLetras = _estadoDeLosPaneles.AnchoEnLetras;
        Mapa.MostrarPasoGris = _estadoDeLosPaneles.PasoGris;
        Mapa.MostrarFondo = _estadoDeLosPaneles.FondoDelMapa;
        Mapa.MostrarContactos = _estadoDeLosPaneles.ContactosEnElMapa;
        Mapa.MostrarSpots = _estadoDeLosPaneles.SpotsEnElMapa;
        if (_estadoDeLosPaneles.ColumnasDelCuaderno is { } columnas) Cuaderno.PonerColumnasVisibles(columnas);
        if (Cuaderno.TamanosDePagina.Contains(_estadoDeLosPaneles.ContactosPorPagina))
        {
            Cuaderno.TamanoDePagina = _estadoDeLosPaneles.ContactosPorPagina;
        }
    }

    /// <summary>
    /// Dice si el cuaderno tiene algun contacto.
    /// </summary>
    /// <remarks>
    /// Lo usa la ventana para decidir si ofrece traerse un ADIF. Si la consulta falla se
    /// responde que SI hay contactos: mejor no ofrecer nada que ofrecer importar encima de un
    /// cuaderno que a lo mejor esta lleno.
    /// </remarks>
    /// <returns>Cierto si hay al menos un contacto.</returns>
    public async Task<bool> HayContactosAsync()
    {
        try
        {
            return await _buscar.ContarTodoAsync().ConfigureAwait(true) > 0;
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "No se ha podido contar el cuaderno.");
            return true;
        }
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

    /// <summary>
    /// Si al perfil le falta el localizador, lo completa con el del ultimo contacto hecho desde
    /// esa estacion y lo guarda. Sin el, cada contacto nuevo se guardaba sin MY_GRIDSQUARE.
    /// </summary>
    /// <param name="perfil">Perfil que se va a activar.</param>
    private async Task CompletarElPerfilAsync(Estacion perfil)
    {
        if (!perfil.MyGridsquare.EsVacio) return;

        try
        {
            var recientes = await _buscar
                .EjecutarAsync(new CriterioQso { OrdenarPor = CampoDeOrden.Fecha, Descendente = true }, 0, 200)
                .ConfigureAwait(true);
            var modelo = recientes.Elementos.FirstOrDefault(
                q => q.StationCallsign == perfil.StationCallsign && !q.MyGridsquare.EsVacio);
            if (modelo is null) return;

            var rellenados = CompletarPerfilDeEstacion.DesdeContacto(perfil, modelo);
            if (rellenados.Count == 0) return;

            await _estaciones.ActualizarAsync(perfil).ConfigureAwait(true);
            Serilog.Log.Warning(
                "El perfil {Perfil} no tenía localizador; se ha completado con el del contacto con {Indicativo} " +
                "del {Fecha:dd-MM-yyyy}: {Campos}.",
                perfil.NombrePerfil, modelo.Call.Valor, modelo.InicioUtc, string.Join(", ", rellenados));
            _avisoDelPerfilDeSiempre = false;
            AvisoDelPerfil = Textos.F(
                "Principal.Perfil.LocatorCompletado", perfil.NombrePerfil, perfil.MyGridsquare.Valor, string.Join(", ", rellenados));
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "No se ha podido completar el perfil {Perfil}.", perfil.NombrePerfil);
        }
    }

    /// <summary>Lo que se hizo con el perfil al arrancar, para la ayuda emergente del selector.</summary>
    [ObservableProperty]
    private string _avisoDelPerfil = Textos.T("Principal.Perfil.Aviso");

    /// <summary>El aviso del perfil es el de siempre (y se traduce al cambiar de idioma).</summary>
    private bool _avisoDelPerfilDeSiempre = true;

    /// <summary>Vuelve a leer el cuaderno: la pagina visible y el contador total.</summary>
    public async Task RefrescarTodoAsync()
    {
        await Cuaderno.RefrescarAsync().ConfigureAwait(true);
        TotalDeQsos = await _buscar.ContarTodoAsync().ConfigureAwait(true);

        // Lo que depende del cuaderno entero se pone al dia tambien. Antes solo se refrescaba
        // la rejilla: un contacto recien registrado, modificado o borrado no llegaba al mapa
        // ni al progreso de los diplomas hasta cerrar y abrir el programa. Van sin esperar,
        // cada uno a su ritmo, para que registrar siga siendo instantaneo.
        if (_mapaCargado) _ = Mapa.CargarAsync();
        if (Diplomas.Mios.Count > 0) _ = Diplomas.RefrescarAsync();
    }

    /// <summary>El mapa ya hizo su primera carga: a partir de ahi se recarga con cada cambio.</summary>
    private bool _mapaCargado;

    /// <summary>Detiene el reloj y suelta los paneles al cerrar la ventana.</summary>
    public void Detener()
    {
        _reloj.Stop();
        Equipo.Detener();
        Cluster.Detener();

        // El modem suelta la tarjeta de sonido y deja de mirar el reloj; el apartado de audio
        // cierra la entrada si se habia quedado probando el nivel. Una tarjeta abierta despues
        // de cerrar el programa es un microfono abierto.
        Modem.Detener();
        Fonia?.Detener();
        Configuracion.Audio?.Detener();
        Satelites.Detener();
    }

    partial void OnEstacionActivaChanged(Estacion? value)
    {
        Entrada.EstacionId = value?.Id;
        Modem.EstacionId = value?.Id;

        // El módem necesita el indicativo, no sólo el identificador del perfil: con él sabe
        // cuándo un mensaje va dirigido A UNO —y no a cualquiera— y compone la respuesta.
        Modem.MiIndicativo = value?.StationCallsign ?? Dominio.Valores.Indicativo.Vacio;
        if (Cw is not null) Cw.MiIndicativo = value?.StationCallsign.Valor;
        Modem.MiLocalizador = value?.MyGridsquare ?? Dominio.Valores.Locator.Vacio;
        Retrato.EstacionId = value?.Id;

        // La impresion completa con esto lo que no traiga el contacto: el indicativo y el
        // localizador propios de cuando se trabajo, no los de hoy.
        Impresion.EstacionId = value?.Id;
        Impresion.MiIndicativo = value?.StationCallsign ?? Dominio.Valores.Indicativo.Vacio;
        Impresion.MiLocalizador = value?.MyGridsquare ?? Dominio.Valores.Locator.Vacio;

        OnPropertyChanged(nameof(PerfilActivo));

        if (value is null) return;

        Mapa.FijarEstacion(value.MyGridsquare, value.StationCallsign.Valor);
        Satelites.FijarEstacion(value.MyGridsquare);

        // La columna «Prop.» del cluster se calcula desde el localizador del perfil activo.
        Cluster.FijarEstacion(value.MyGridsquare);

        // El orto y el ocaso son los del sitio desde donde se opera, no los de un sitio
        // cualquiera: salen del localizador del perfil activo.
        Solar.FijarEstacion(
            value.MyGridsquare.EsVacio
                ? null
                : new Dominio.Valores.Coordenada(
                    value.MyGridsquare.ACoordenadas().Latitud,
                    value.MyGridsquare.ACoordenadas().Longitud));
    }

    /// <summary>
    /// Le dice al retrato que mire el indicativo que se esta tecleando.
    /// </summary>
    /// <remarks>
    /// La banda y el modo llegan al retrato como valores del dominio, no como texto: de un
    /// «20m» escrito a mano no se puede contar nada en el cuaderno. Si lo que hay escrito no
    /// es una banda valida —pasa de verdad, el dial del operador estaba en 27.555 MHz— se pasa la
    /// banda vacia y el retrato responde con lo que puede.
    /// </remarks>
    private void MirarElRetrato()
    {
        var banda = Dominio.Valores.Banda.TryParse(Entrada.Banda, out var b) ? b : Dominio.Valores.Banda.Vacia;
        var modo = Dominio.Valores.Modo.TryParse(Entrada.Modo, null, out var m) ? m : Dominio.Valores.Modo.Vacio;

        Retrato.Mirar(Entrada.Indicativo, banda, modo);
    }

    partial void OnEscalaDeLetraChanged(int value)
    {
        OnPropertyChanged(nameof(TamanoDeLetra));
        OnPropertyChanged(nameof(AnchoDelPanel));
        OnPropertyChanged(nameof(EscalaDeLetraTexto));
    }

    partial void OnAnchoDelPanelEnLetrasChanged(double value) => OnPropertyChanged(nameof(AnchoDelPanel));

    partial void OnTemaOscuroChanged(bool value) => Recursos.Temas.Aplicar(value);

    /// <summary>
    /// Lleva al panel de telegrafía lo que dice el CAT: el modo y el tono de CW (pitch). Solo se
    /// lee lo que el control del equipo ya tiene; no se manda nada a la radio.
    /// </summary>
    private void SeguirAlEquipoEnCw()
    {
        if (Cw is null) return;
        double? pitch = Equipo.Conectado && Equipo.MandoDe(MandoDeEquipo.TonoCw) is { Disponible: true } tono ? tono.Valor : null;
        Cw.SeguirAlEquipo(Equipo.Conectado ? Equipo.Modo : null, pitch);
    }

    private void Latir()
    {
        SeguirAlEquipoEnCw();
        var ahora = DateTimeOffset.UtcNow;
        FechaUtc = ahora.UtcDateTime.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);
        HoraUtc = ahora.UtcDateTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        var local = ahora.ToLocalTime();
        HoraLocal = local.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        var desfase = local.Offset;
        var signo = desfase < TimeSpan.Zero ? "-" : "+";
        var desfaseTexto = desfase == TimeSpan.Zero
            ? "UTC"
            : desfase.Minutes == 0
                ? $"UTC{signo}{Math.Abs(desfase.Hours)}"
                : $"UTC{signo}{Math.Abs(desfase.Hours)}:{Math.Abs(desfase.Minutes):00}";
        FechaLocal = Textos.F("Principal.Reloj.FechaLocal", local.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture), desfaseTexto);
        Entrada.ActualizarReloj(ahora);
        Mapa.ActualizarReloj(ahora);
    }
}
