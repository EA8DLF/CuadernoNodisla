using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Nodisla.Cuaderno.Aplicacion.CasosDeUso;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Idiomas;

namespace Nodisla.Cuaderno.Ui.VistaModelos;

/// <summary>
/// Uno de los dos VFO del equipo, tal y como se lee en la cabina.
/// </summary>
/// <remarks>
/// Los dos VFO se ven <b>siempre a la vez</b>, y de cada uno hay que poder decir de un vistazo
/// tres cosas: en que frecuencia esta, en que modo, y si por el se transmite o se recibe.
/// Confundir esas dos ultimas es transmitir donde no se debia, que es el error que este panel
/// existe para evitar.
///
/// Debajo de cada VFO se ensena, cuando la hay, la estacion que el cluster esta anunciando en
/// esa misma frecuencia: es el dato que decide si merece la pena llamar.
/// </remarks>
public sealed partial class VistaModeloVfo : ObservableObject
{
    /// <summary>
    /// Cuanto se admite de diferencia para dar por buena la frecuencia de un spot.
    /// </summary>
    /// <remarks>
    /// Medio kilohercio: la misma tolerancia con la que se juntan los anuncios repetidos, y
    /// por el mismo motivo —dos receptores no leen el mismo dial al hercio—.
    /// </remarks>
    public const decimal ToleranciaDelSpotEnKilohercios = 0.5m;

    private readonly IBandplan? _bandplan;

    /// <summary>Monta el VFO.</summary>
    /// <param name="nombre">Cual de los dos es.</param>
    /// <param name="bandplan">Plan de bandas con el que se avisa, o nulo para no avisar.</param>
    public VistaModeloVfo(NombreDeVfo nombre, IBandplan? bandplan)
    {
        Nombre = nombre;
        _bandplan = bandplan;
        Etiqueta = nombre == NombreDeVfo.A ? "VFO A" : "VFO B";
        Textos.AlCambiar(this, static vm => vm.OnPropertyChanged(nameof(AvisoCortoDelBandplan)));
    }

    /// <summary>Cual de los dos VFO es.</summary>
    public NombreDeVfo Nombre { get; }

    /// <summary>Como se llama en pantalla.</summary>
    public string Etiqueta { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FrecuenciaDelVisor))]
    private string _frecuencia = "—";

    /// <summary>
    /// La frecuencia como la escribe el visor del equipo: en hercios y con puntos de millar.
    /// </summary>
    /// <remarks>
    /// El equipo ensena <c>14.195.000</c>, no <c>14.195</c>. No choca con la regla del punto
    /// decimal de ADIF porque aqui no hay decimales: son hercios enteros, y los puntos separan
    /// millares. La frecuencia en megahercios sigue escribiendose con punto decimal en el
    /// formulario y en el cuaderno, que es donde manda ADIF.
    /// </remarks>
    public string FrecuenciaDelVisor
    {
        get
        {
            var frecuencia = TextoDeFrecuencia.Leer(Frecuencia);
            return frecuencia.EsCero
                ? "—"
                : frecuencia.Hercios.ToString("#,##0", CultureInfo.CurrentCulture);
        }
    }

    [ObservableProperty]
    private string _modo = "—";

    [ObservableProperty]
    private string _banda = "—";

    [ObservableProperty]
    private string _anchoDeFiltro = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Papel))]
    private bool _transmite;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Papel))]
    private bool _recibe;

    [ObservableProperty]
    private bool _esElActivo;

    [ObservableProperty]
    private bool _fueraDeBanda;

    /// <summary>
    /// Dónde cae la frecuencia dentro de su banda, de 0 (borde bajo) a 1 (borde alto), para la
    /// marca de la miniatura de banda del visor. Negativo: sin frecuencia o fuera de banda.
    /// </summary>
    [ObservableProperty]
    private double _posicionEnBanda = -1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AvisoCortoDelBandplan))]
    private string _avisoDelBandplan = string.Empty;

    /// <summary>Uso del tramo del plan en que cae el dial, para el aviso corto.</summary>
    private UsoDelTramo? _usoDelTramo;

    [ObservableProperty]
    private string _tramoDelBandplan = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayAnuncio))]
    private string _anunciado = string.Empty;

    [ObservableProperty]
    private bool _anuncioEsNuevo;

    /// <summary>Si por este VFO se transmite, se recibe o las dos cosas.</summary>
    public string Papel => this switch
    {
        { Transmite: true, Recibe: true } => "TX · RX",
        { Transmite: true } => "TX",
        { Recibe: true } => "RX",
        _ => string.Empty,
    };

    /// <summary>
    /// El aviso del plan de bandas en dos o tres palabras, para la pantalla del equipo: «Fuera de
    /// banda», «Tramo de CW», «Tramo de fonía»… La explicacion entera va en la ayuda emergente
    /// (<see cref="AvisoDelBandplan"/> y <see cref="TramoDelBandplan"/>).
    /// </summary>
    public string AvisoCortoDelBandplan => AvisoDelBandplan.Length == 0
        ? string.Empty
        : FueraDeBanda || _usoDelTramo is null
            ? Textos.T("Cabina.Vfo.FueraDeBanda")
            : _usoDelTramo switch
            {
                UsoDelTramo.Cw => Textos.T("Cabina.Vfo.TramoCw"),
                UsoDelTramo.DigitalEstrecho or UsoDelTramo.DigitalAncho => Textos.T("Cabina.Vfo.TramoDigital"),
                UsoDelTramo.Fonia or UsoDelTramo.FoniaEImagen => Textos.T("Cabina.Vfo.TramoFonia"),
                UsoDelTramo.Baliza => Textos.T("Cabina.Vfo.TramoBalizas"),
                UsoDelTramo.Reservado => Textos.T("Cabina.Vfo.TramoReservado"),
                _ => Textos.T("Cabina.Vfo.FueraDelPlan"),
            };

    /// <summary>El cluster esta anunciando a alguien en esta frecuencia.</summary>
    public bool HayAnuncio => Anunciado.Length > 0;

    /// <summary>Recoge el estado del VFO tal y como lo informa el equipo.</summary>
    /// <param name="estado">Estado del VFO.</param>
    public void Recoger(EstadoDeUnVfo estado)
    {
        ArgumentNullException.ThrowIfNull(estado);

        EsElActivo = estado.EsElActivo;
        Transmite = estado.Transmite;
        Recibe = estado.Recibe;

        if (estado.Frecuencia.EsCero)
        {
            Frecuencia = "—";
            Modo = "—";
            Banda = "—";
            AnchoDeFiltro = string.Empty;
            FueraDeBanda = false;
            PosicionEnBanda = -1;
            AvisoDelBandplan = string.Empty;
            TramoDelBandplan = string.Empty;
            Anunciado = string.Empty;
            return;
        }

        // La frecuencia va SIEMPRE con punto decimal, como el dial y como ADIF.
        Frecuencia = TextoDeFrecuencia.Escribir(estado.Frecuencia);
        Modo = estado.Modo.EsVacio ? "—" : estado.Modo.NombreUsual;

        FueraDeBanda = estado.Banda.EsVacia;
        Banda = FueraDeBanda ? Textos.T("Cabina.Vfo.FueraDeBandaMinus") : estado.Banda.Nombre;
        PosicionEnBanda = FueraDeBanda ? -1 : PosicionDentroDe(estado.Banda, estado.Frecuencia);

        AnchoDeFiltro = estado.AnchoDeFiltroHz is { } hz and > 0
            ? $"{hz.ToString("N0", CultureInfo.CurrentCulture)} Hz"
            : string.Empty;

        ConsultarElBandplan(estado);
    }

    private static double PosicionDentroDe(Banda banda, Frecuencia frecuencia)
    {
        var (inferior, superior) = banda.Limite;
        return superior <= inferior
            ? 0.5
            : Math.Clamp((double)((frecuencia.Megahercios - inferior) / (superior - inferior)), 0, 1);
    }

    /// <summary>
    /// Busca entre los spots el que este en esta misma frecuencia.
    /// </summary>
    /// <param name="spots">Spots que se estan viendo en el panel de cluster.</param>
    public void PonerAnuncios(IEnumerable<FilaDeSpot> spots)
    {
        ArgumentNullException.ThrowIfNull(spots);

        var frecuencia = TextoDeFrecuencia.Leer(Frecuencia);
        if (frecuencia.EsCero)
        {
            Anunciado = string.Empty;
            AnuncioEsNuevo = false;
            return;
        }

        FilaDeSpot? mejor = null;
        foreach (var fila in spots)
        {
            var salto = Math.Abs(fila.Spot.Frecuencia.Kilohercios - frecuencia.Kilohercios);
            if (salto > ToleranciaDelSpotEnKilohercios) continue;

            // Entre dos que caigan igual de cerca, manda el que aporta algo al cuaderno.
            if (mejor is null || (fila.EsInteresante && !mejor.EsInteresante)) mejor = fila;
        }

        if (mejor is null)
        {
            Anunciado = string.Empty;
            AnuncioEsNuevo = false;
            return;
        }

        var pais = mejor.Pais.Length > 0 ? $" · {mejor.Pais}" : string.Empty;
        var novedad = mejor.Novedad.Length > 0 ? $" · {mejor.Novedad}" : string.Empty;

        Anunciado = $"{mejor.Indicativo}{pais}{novedad}";
        AnuncioEsNuevo = mejor.EsInteresante;
    }

    /// <summary>
    /// Pregunta al plan de bandas y guarda lo que diga.
    /// </summary>
    /// <remarks>
    /// El plan <b>avisa, no impide</b>. Hay motivos legitimos para estar fuera del tramo —
    /// escuchar, una autorizacion especial, un equipo abierto—, y el dial de Jose estaba en
    /// 27.555 MHz el dia que se capturo su CAT.
    /// </remarks>
    private void ConsultarElBandplan(EstadoDeUnVfo estado)
    {
        if (_bandplan is null)
        {
            _usoDelTramo = null;
            AvisoDelBandplan = FueraDeBanda
                ? Textos.T("Cabina.Vfo.SinBanda")
                : string.Empty;
            TramoDelBandplan = string.Empty;
            return;
        }

        try
        {
            var consulta = _bandplan.Consultar(estado.Frecuencia, estado.Modo);
            _usoDelTramo = consulta.DentroDeBanda ? consulta.Tramo?.Uso : null;
            OnPropertyChanged(nameof(AvisoCortoDelBandplan));
            AvisoDelBandplan = consulta.Aviso ?? string.Empty;
            TramoDelBandplan = consulta.Tramo?.Descripcion ?? string.Empty;
        }
        catch (Exception)
        {
            // Un plan de bandas que no sepa contestar no puede dejar sin frecuencia al panel.
            AvisoDelBandplan = string.Empty;
            TramoDelBandplan = string.Empty;
        }
    }
}
