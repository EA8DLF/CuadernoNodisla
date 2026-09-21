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
    private readonly DispatcherTimer _reloj;

    /// <summary>Monta la ventana con sus dos paneles y arranca el reloj UTC.</summary>
    public VistaModeloPrincipal(
        VistaModeloEntradaQso entrada,
        VistaModeloCuaderno cuaderno,
        IRepositorioEstacion estaciones,
        BuscarEnCuaderno buscar)
    {
        Entrada = entrada;
        Cuaderno = cuaderno;
        _estaciones = estaciones;
        _buscar = buscar;

        Entrada.CuadernoCambiado += async (_, _) => await RefrescarTodoAsync().ConfigureAwait(true);
        Cuaderno.SolicitaEditar += (_, qso) => Entrada.CargarParaEditar(qso);

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

    /// <summary>Carga los perfiles de estacion y la primera pagina del cuaderno.</summary>
    public async Task InicializarAsync()
    {
        var perfiles = await _estaciones.TodasAsync().ConfigureAwait(true);
        Estaciones.Clear();
        foreach (var e in perfiles) Estaciones.Add(e);

        EstacionActiva = await _estaciones.PredeterminadaAsync().ConfigureAwait(true) ?? Estaciones.FirstOrDefault();

        await RefrescarTodoAsync().ConfigureAwait(true);
        _reloj.Start();
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

    /// <summary>Detiene el reloj al cerrar la ventana.</summary>
    public void Detener() => _reloj.Stop();

    partial void OnEstacionActivaChanged(Estacion? value)
    {
        Entrada.EstacionId = value?.Id;
        OnPropertyChanged(nameof(PerfilActivo));
    }

    partial void OnEscalaDeLetraChanged(int value) => OnPropertyChanged(nameof(TamanoDeLetra));

    partial void OnTemaOscuroChanged(bool value) => Recursos.Temas.Aplicar(value);

    private void Latir()
    {
        var ahora = DateTimeOffset.UtcNow;
        FechaUtc = ahora.UtcDateTime.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);
        HoraUtc = ahora.UtcDateTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        Entrada.ActualizarReloj(ahora);
    }
}
