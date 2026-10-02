namespace Nodisla.Cuaderno.Modos.Cw;

/// <summary>Lo que se puede ajustar del decodificador de telegrafía.</summary>
public sealed record OpcionesCw
{
    /// <summary>Tono con que se arranca y al que se fija sin otro dato (Hz).</summary>
    public double TonoPorOmisionHz { get; init; } = 700;

    /// <summary>Ancho del filtro de cada canal, a −3 dB (Hz). Más estrecho, más sensible y más lento.</summary>
    public double AnchoDelFiltroHz { get; init; } = 100;

    /// <summary>
    /// Sensibilidad, de 1 a 10. Más alta, saca señales más débiles a cambio de algún error más
    /// con ruido. Se traduce en cuánto tiene que destacar la señal sobre el ruido para abrir.
    /// </summary>
    public int Sensibilidad { get; init; } = 6;

    /// <summary>Velocidad mínima que se sigue (palabras por minuto).</summary>
    public double WpmMinima { get; init; } = 5;

    /// <summary>Velocidad máxima que se sigue (palabras por minuto).</summary>
    public double WpmMaxima { get; init; } = 60;

    /// <summary>Límite inferior de la ventana donde se buscan tonos (Hz).</summary>
    public double BandaDesdeHz { get; init; } = 300;

    /// <summary>Límite superior de la ventana donde se buscan tonos (Hz).</summary>
    public double BandaHastaHz { get; init; } = 1200;

    /// <summary>
    /// En automático, segundos sin señal tras los que la principal suelta el tono al que se había
    /// enganchado y vuelve a buscar.
    /// </summary>
    public double SegundosSinSenalParaBuscar { get; init; } = 5;

    /// <summary>Cuántas señales se decodifican a la vez (1 = solo la principal).</summary>
    public int CanalesMaximos { get; init; } = 4;

    /// <summary>
    /// Lo que tiene que destacar la envolvente sobre el ruido para abrir el canal (dB), según la
    /// sensibilidad.
    /// </summary>
    public double UmbralDb => 15.0 - Math.Clamp(Sensibilidad, 1, 10);

    /// <summary>Lo mismo, acotado a valores con sentido.</summary>
    public OpcionesCw Acotada()
    {
        var desde = Math.Clamp(BandaDesdeHz, 100, 2500);
        var hasta = Math.Clamp(BandaHastaHz, desde + 100, 3000);
        var minima = Math.Clamp(WpmMinima, 5, 55);
        var maxima = Math.Clamp(WpmMaxima, minima + 5, 60);
        return this with
        {
            TonoPorOmisionHz = Math.Clamp(TonoPorOmisionHz, 200, 2000),
            AnchoDelFiltroHz = Math.Clamp(AnchoDelFiltroHz, 30, 500),
            Sensibilidad = Math.Clamp(Sensibilidad, 1, 10),
            WpmMinima = minima,
            WpmMaxima = maxima,
            BandaDesdeHz = desde,
            BandaHastaHz = hasta,
            CanalesMaximos = Math.Clamp(CanalesMaximos, 1, 8),
            SegundosSinSenalParaBuscar = Math.Clamp(SegundosSinSenalParaBuscar, 1, 60),
        };
    }
}
