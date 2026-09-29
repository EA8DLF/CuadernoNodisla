using Nodisla.Cuaderno.Modos.Senal;

namespace Nodisla.Cuaderno.Modos.Wspr;

/// <summary>
/// Una senal compleja en banda base: parte real, parte imaginaria y a que muestras por segundo va.
/// </summary>
/// <param name="Real">Parte real.</param>
/// <param name="Imaginaria">Parte imaginaria.</param>
/// <param name="FrecuenciaDeMuestreo">Muestras por segundo.</param>
/// <param name="ResiduoHz">
/// Lo que la senal buscada queda desplazada de cero por haber tenido que centrar la extraccion
/// en una casilla entera de la transformada. Es minusculo, pero se arrastra para que la
/// frecuencia que se apunta sea exacta.
/// </param>
public sealed record SenalCompleja(float[] Real, float[] Imaginaria, double FrecuenciaDeMuestreo, double ResiduoHz)
{
    /// <summary>Muestras que tiene.</summary>
    public int Longitud => Real.Length;
}

/// <summary>
/// Prepara una ventana de dos minutos para buscar y demodular WSPR.
/// </summary>
/// <remarks>
/// <para>
/// Todo sale de una sola transformada de la ventana entera. Con ella, quedarse con un trozo del
/// espectro y deshacer la transformada equivale a filtrar con un filtro perfecto y remuestrear
/// a la vez, sin disenar filtros ni arrastrar colas. Se usa dos veces:
/// </para>
/// <list type="number">
/// <item>Para sacar la <b>banda base</b>: los 375 Hz centrados en 1500 Hz, a 375 muestras
/// complejas por segundo, donde se buscan las candidatas con un espectrograma.</item>
/// <item>Para sacar la <b>senal estrecha</b> de cada candidata: 23,4 Hz alrededor de su
/// frecuencia, a 23,4 muestras por segundo, donde demodular sale dieciseis veces mas barato
/// que en la banda base y no se pierde nada, porque la senal solo ocupa seis hercios.</item>
/// </list>
/// <para>
/// La transformada es de dos millones de puntos, que en este ordenador cuesta una fraccion de
/// segundo; el hueco disponible son dos minutos.
/// </para>
/// </remarks>
public sealed class AnalisisWspr
{
    /// <summary>Puntos de la transformada: la potencia de dos que cubre 120 s a 12000 Hz.</summary>
    public const int LongitudDeLaTransformada = 1 << 21;

    /// <summary>Muestras de la banda base que caben en la transformada.</summary>
    public const int MuestrasDeBandaBase = 1 << 16;

    /// <summary>Muestras de la senal estrecha que caben en la transformada.</summary>
    public const int MuestrasEstrechas = 1 << 12;

    private readonly float[] _espectroReal;
    private readonly float[] _espectroImaginaria;

    /// <summary>Muestras de audio que llegaron de verdad.</summary>
    public int MuestrasDeAudio { get; }

    /// <summary>Hercios que separan dos casillas de la transformada.</summary>
    public double HzPorCasilla => (double)ParametrosWspr.FrecuenciaDeAnalisis / LongitudDeLaTransformada;

    /// <summary>
    /// Analiza la ventana.
    /// </summary>
    /// <param name="audio">Audio a 12000 muestras por segundo. Si hay mas de 174 s se descarta el resto.</param>
    public AnalisisWspr(ReadOnlySpan<float> audio)
    {
        MuestrasDeAudio = Math.Min(audio.Length, LongitudDeLaTransformada);
        _espectroReal = new float[LongitudDeLaTransformada];
        _espectroImaginaria = new float[LongitudDeLaTransformada];
        audio[..MuestrasDeAudio].CopyTo(_espectroReal);
        Fft.Transformar(_espectroReal, _espectroImaginaria);
    }

    /// <summary>La banda de WSPR trasladada a cero, a 375 muestras complejas por segundo.</summary>
    public SenalCompleja BandaBase() =>
        Extraer(ParametrosWspr.CentroDeLaBandaHz, MuestrasDeBandaBase);

    /// <summary>
    /// La senal estrecha alrededor de una frecuencia de audio, a 23,4375 muestras por segundo.
    /// </summary>
    /// <param name="frecuenciaHz">Frecuencia de audio que se quiere en el centro, por ejemplo 1487,3.</param>
    public SenalCompleja Estrecha(double frecuenciaHz) => Extraer(frecuenciaHz, MuestrasEstrechas);

    private SenalCompleja Extraer(double centroHz, int puntos)
    {
        var casillaCentral = (int)Math.Round(centroHz / HzPorCasilla);
        var residuo = centroHz - (casillaCentral * HzPorCasilla);
        var mitad = puntos / 2;

        var real = new float[puntos];
        var imaginaria = new float[puntos];
        for (var k = -mitad; k < mitad; k++)
        {
            var origen = casillaCentral + k;
            if (origen < 0 || origen >= LongitudDeLaTransformada) continue;
            var destino = k < 0 ? k + puntos : k;
            real[destino] = _espectroReal[origen];
            imaginaria[destino] = _espectroImaginaria[origen];
        }

        Fft.TransformarInversa(real, imaginaria);

        // La transformada inversa divide por su longitud, no por la de la directa; se compensa
        // para que la amplitud no dependa de cuantos puntos se extrajeron.
        var escala = (float)puntos / LongitudDeLaTransformada;
        for (var i = 0; i < puntos; i++)
        {
            real[i] *= escala;
            imaginaria[i] *= escala;
        }

        var frecuencia = (double)ParametrosWspr.FrecuenciaDeAnalisis * puntos / LongitudDeLaTransformada;
        return new SenalCompleja(real, imaginaria, frecuencia, residuo);
    }
}
