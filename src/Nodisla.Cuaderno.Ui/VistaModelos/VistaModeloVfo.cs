using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Nodisla.Cuaderno.Aplicacion.CasosDeUso;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Valores;

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
    }

    /// <summary>Cual de los dos VFO es.</summary>
    public NombreDeVfo Nombre { get; }

    /// <summary>Como se llama en pantalla.</summary>
    public string Etiqueta { get; }

    [ObservableProperty]
    private string _frecuencia = "—";

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

    [ObservableProperty]
    private string _avisoDelBandplan = string.Empty;

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
            AvisoDelBandplan = string.Empty;
            TramoDelBandplan = string.Empty;
            Anunciado = string.Empty;
            return;
        }

        // La frecuencia va SIEMPRE con punto decimal, como el dial y como ADIF.
        Frecuencia = TextoDeFrecuencia.Escribir(estado.Frecuencia);
        Modo = estado.Modo.EsVacio ? "—" : estado.Modo.NombreUsual;

        FueraDeBanda = estado.Banda.EsVacia;
        Banda = FueraDeBanda ? "fuera de banda" : estado.Banda.Nombre;

        AnchoDeFiltro = estado.AnchoDeFiltroHz is { } hz and > 0
            ? $"{hz.ToString("N0", CultureInfo.CurrentCulture)} Hz"
            : string.Empty;

        ConsultarElBandplan(estado);
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
            AvisoDelBandplan = FueraDeBanda
                ? "Esta frecuencia no cae en ninguna banda de aficionado."
                : string.Empty;
            TramoDelBandplan = string.Empty;
            return;
        }

        try
        {
            var consulta = _bandplan.Consultar(estado.Frecuencia, estado.Modo);
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
