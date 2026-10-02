using System.Diagnostics;
using Nodisla.Cuaderno.Ui.Ajustes;
using Nodisla.Cuaderno.Ui.Recursos;
using Serilog;

namespace Nodisla.Cuaderno.Ui.VistaModelos;

/// <summary>
/// El analizador como mando: los spots del cluster encima, clic para sintonizar, rueda para
/// mover el VFO y los ajustes de color, suelo de ruido y picos.
/// </summary>
/// <remarks>
/// <para>
/// Los spots son los de la lista del cluster (<see cref="VistaModeloCluster.Spots"/>), que ya
/// salen de la fuente fusionada de todos los nodos y pasan el filtro del panel. Aqui solo se
/// quedan los que caen en lo que se ve, uno por indicativo (el mas reciente). Colocarlos en
/// carriles lo hace la vista, que es la que sabe cuantos puntos mide (<see cref="CarrilesDeSpots"/>).
/// </para>
/// <para>
/// Mandar a la radio lo hace <see cref="SintoniaDelAnalizador"/>; sin ella (pruebas, o sin
/// equipo), el clic no hace nada.
/// </para>
/// </remarks>
public sealed partial class VistaModeloAnalizador
{
    /// <summary>Spots que se ponen como mucho a la vez (los mas recientes).</summary>
    public const int SpotsMaximos = 80;

    /// <summary>Cada cuanto se avisa de los picos a la vista, como mucho.</summary>
    private const int AvisoDePicosMs = 250;

    private IReadOnlyList<FilaDeSpot> _spots = [];
    private EscalaDelEspectro _escala;
    private bool _desplazarCascada = true;
    private long _ultimoAvisoDePicos;
    private double _tiempoMedioMs = double.NaN;
    private long _pasadasMedidas;

    /// <summary>Lo que el analizador puede pedirle a la radio; nula si no hay con que.</summary>
    public SintoniaDelAnalizador? Sintonia { get; set; }

    /// <summary>De que frecuencia a que frecuencia va lo que se ve.</summary>
    public EscalaDelEspectro Escala => _escala;

    /// <summary>Spots que caen en lo que se ve, del mas reciente al mas antiguo.</summary>
    public IReadOnlyList<FilaDeSpot> SpotsALaVista { get; private set; } = [];

    /// <summary>Picos marcados en la traza (vacio si no se marcan).</summary>
    public IReadOnlyList<PicoDeLaTraza> Picos { get; private set; } = [];

    /// <summary>Se ponen los spots encima.</summary>
    public bool SpotsEncima { get; private set; } = true;

    /// <summary>Carriles de rotulos de spots.</summary>
    public int CarrilesDeSpots { get; private set; } = 3;

    /// <summary>El clic sintoniza y la rueda mueve el VFO.</summary>
    public bool ClicParaSintonizar { get; private set; } = true;

    /// <summary>Paso elegido en los ajustes; 0, automatico.</summary>
    public int PasoElegidoHz { get; private set; }

    /// <summary>Lo que tardo en pintarse la ultima pasada (pintor e imagenes), en milisegundos.</summary>
    public double UltimoTiempoDePintadoMs { get; private set; }

    /// <summary>Media movil de lo que tarda en pintarse una pasada, en milisegundos.</summary>
    public double TiempoMedioDePintadoMs => double.IsNaN(_tiempoMedioMs) ? 0 : _tiempoMedioMs;

    /// <summary>El suelo de ruido que se esta usando (0-255).</summary>
    public double SueloDeRuido => _pintor.Suelo;

    /// <summary>Pone los ajustes guardados.</summary>
    /// <param name="ajustes">Ajustes del analizador.</param>
    public void Aplicar(AjustesDelAnalizador ajustes)
    {
        ArgumentNullException.ThrowIfNull(ajustes);
        ajustes.Acotar();
        _pintor.Paleta = PaletasDelAnalizador.DesdeNombre(ajustes.Paleta);
        _pintor.SueloAutomatico = ajustes.SueloAutomatico;
        _pintor.NivelBajo = ajustes.NivelBajo;
        _pintor.Contraste = ajustes.Contraste;
        _pintor.Brillo = ajustes.Brillo;
        _pintor.MarcarPicos = ajustes.MarcarPicos;
        _desplazarCascada = ajustes.DesplazarCascada;

        SpotsEncima = ajustes.SpotsEncima;
        CarrilesDeSpots = ajustes.CarrilesDeSpots;
        ClicParaSintonizar = ajustes.ClicParaSintonizar;
        PasoElegidoHz = ajustes.PasoHz;
        if (!ajustes.MarcarPicos && Picos.Count > 0) Picos = [];

        OnPropertyChanged(nameof(SpotsEncima));
        OnPropertyChanged(nameof(CarrilesDeSpots));
        OnPropertyChanged(nameof(ClicParaSintonizar));
        OnPropertyChanged(nameof(PasoElegidoHz));
        OnPropertyChanged(nameof(Picos));
        RehacerLosSpots();
    }

    /// <summary>Dice que spots hay (los de la lista del cluster, ya filtrados).</summary>
    /// <param name="spots">Del mas reciente al mas antiguo.</param>
    public void PonerSpots(IEnumerable<FilaDeSpot> spots)
    {
        ArgumentNullException.ThrowIfNull(spots);
        _spots = [.. spots];
        RehacerLosSpots();
    }

    /// <summary>El paso del clic y de la rueda para lo que se ve ahora.</summary>
    /// <param name="fino">Con Mayúsculas: la décima parte.</param>
    /// <returns>Hercios.</returns>
    public long PasoHz(bool fino = false)
    {
        var paso = PasoElegidoHz > 0 ? PasoElegidoHz : _escala.Valida ? _escala.PasoAutomatico() : 100;
        return fino ? Math.Max(1, paso / 10) : paso;
    }

    /// <summary>La frecuencia a la que llevaria un clic en ese sitio, ya ajustada al paso.</summary>
    /// <param name="fraccion">Donde se ha pulsado, de 0 (izquierda) a 1 (derecha).</param>
    /// <param name="fino">Con Mayúsculas: paso fino.</param>
    /// <returns>Hercios; 0 si no hay escala.</returns>
    public long FrecuenciaDelClic(double fraccion, bool fino = false) =>
        _escala.Valida ? EscalaDelEspectro.AjustarAlPaso(_escala.HzEn(fraccion, 1), PasoHz(fino)) : 0;

    /// <summary>Clic en el espectro: lleva el VFO activo ahi, ajustado al paso.</summary>
    /// <param name="fraccion">Donde se ha pulsado, de 0 (izquierda) a 1 (derecha).</param>
    /// <param name="fino">Con Mayúsculas: paso fino.</param>
    /// <returns>Cierto si se ha pedido a la radio.</returns>
    public Task<bool> ClicAsync(double fraccion, bool fino = false)
    {
        if (!ClicParaSintonizar || Sintonia is null) return Task.FromResult(false);
        var hz = FrecuenciaDelClic(fraccion, fino);
        return hz > 0 ? Sintonia.IrAAsync(hz) : Task.FromResult(false);
    }

    /// <summary>Rueda del raton: mueve el VFO activo por pasos.</summary>
    /// <param name="muescas">Positivas suben.</param>
    /// <param name="fino">Con Mayúsculas: paso fino.</param>
    /// <returns>Cierto si se ha pedido a la radio.</returns>
    public Task<bool> RuedaAsync(int muescas, bool fino = false)
    {
        if (!ClicParaSintonizar || Sintonia is null || UltimaTraza is null) return Task.FromResult(false);
        return Sintonia.MoverAsync(muescas, PasoHz(fino), UltimaTraza.VfoHz);
    }

    /// <summary>Clic en un spot: lo mismo que el doble clic de la lista de spots.</summary>
    /// <param name="fila">El spot.</param>
    /// <returns>Cierto si se ha ido.</returns>
    public Task<bool> ElegirSpotAsync(FilaDeSpot fila)
    {
        ArgumentNullException.ThrowIfNull(fila);
        return Sintonia is null ? Task.FromResult(false) : Sintonia.IrAlSpotAsync(fila);
    }

    /// <summary>La escala de lo que se ve, con lo que dicen la trama y el CAT.</summary>
    private EscalaDelEspectro EscalaActual()
    {
        if (UltimaTraza is not { } traza || SpanHz <= 0) return default;
        var inicio = InicioFijoHz > 0 ? InicioFijoHz : traza.VfoHz - (SpanHz / 2);
        return new EscalaDelEspectro(inicio, inicio + SpanHz);
    }

    /// <summary>Si lo que se ve ha cambiado, se recolocan los spots.</summary>
    private void RevisarLaEscala()
    {
        var escala = EscalaActual();
        if (escala == _escala) return;
        _escala = escala;
        OnPropertyChanged(nameof(Escala));
        RehacerLosSpots();
    }

    private void RehacerLosSpots()
    {
        var antes = SpotsALaVista;
        IReadOnlyList<FilaDeSpot> ahora = [];
        if (SpotsEncima && _escala.Valida && _spots.Count > 0)
        {
            var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var lista = new List<FilaDeSpot>();
            foreach (var fila in _spots)
            {
                if (!_escala.Contiene(fila.Spot.Frecuencia.Hercios)) continue;
                if (!vistos.Add(fila.Indicativo)) continue;
                lista.Add(fila);
                if (lista.Count >= SpotsMaximos) break;
            }

            ahora = lista;
        }

        if (antes.Count == 0 && ahora.Count == 0) return;
        SpotsALaVista = ahora;
        OnPropertyChanged(nameof(SpotsALaVista));
    }

    /// <summary>Mide lo que ha tardado la pasada y avisa de los picos (sin agobiar a la vista).</summary>
    private void DespuesDePintar(long empieza)
    {
        var ms = Stopwatch.GetElapsedTime(empieza).TotalMilliseconds;
        UltimoTiempoDePintadoMs = ms;
        _tiempoMedioMs = double.IsNaN(_tiempoMedioMs) ? ms : (_tiempoMedioMs * 0.95) + (ms * 0.05);
        if (++_pasadasMedidas % 3000 == 0)
        {
            Log.Debug("Analizador: {Ms:0.00} ms de media por pasada.", _tiempoMedioMs);
        }

        if (!_pintor.MarcarPicos) return;
        var ahora = _milisegundos();
        if (ahora - _ultimoAvisoDePicos < AvisoDePicosMs) return;
        _ultimoAvisoDePicos = ahora;
        Picos = [.. _pintor.Picos];
        OnPropertyChanged(nameof(Picos));
    }
}
