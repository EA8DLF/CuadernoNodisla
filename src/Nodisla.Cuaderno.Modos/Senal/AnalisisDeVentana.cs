using Nodisla.Cuaderno.Modos.Ft8;

namespace Nodisla.Cuaderno.Modos.Senal;

/// <summary>
/// Prepara una ventana de audio para que se pueda buscar y demodular sin volver a tocarla.
/// </summary>
/// <remarks>
/// <para>
/// De una ventana de quince segundos hay que sacar dos cosas muy distintas, y cada una pide un
/// tratamiento propio:
/// </para>
/// <list type="number">
/// <item>
/// <b>Un mapa para buscar.</b> Un espectrograma corriente —espectro de cada medio simbolo— con
/// el que localizar donde hay algo parecido a una senal. Se calcula una vez y se mira muchas.
/// </item>
/// <item>
/// <b>Una lupa para mirar de cerca.</b> Una vez encontrada una candidata, hay que aislar sus
/// cincuenta hercios del resto de la banda. Aqui se hace con la transformada de la ventana
/// entera: quedarse con un trozo del espectro y deshacer la transformada equivale a filtrar
/// con un filtro perfecto, sin la cola ni el retardo que tendria un filtro de verdad, y ademas
/// deja la senal ya trasladada a banda base y remuestreada a cien muestras por segundo.
/// </item>
/// </list>
/// <para>
/// Esa segunda idea es lo que hace que el decodificador quepa en el tiempo disponible: pasar de
/// ciento noventa mil muestras a mil doscientas antes de ponerse a demodular abarata cada
/// candidata treinta veces, y se pueden mirar candidatas de sobra en lo que dura la ventana.
/// </para>
/// </remarks>
public sealed class AnalisisDeVentana
{
    /// <summary>
    /// Margen, en tonos, que se deja por debajo de la senal al recortar su banda.
    /// </summary>
    /// <remarks>
    /// El suavizado de la modulacion ensancha un poco la senal por debajo del tono cero. Sin
    /// margen, ese trozo se perderia y con el algo de la energia de los simbolos. Dos tonos es
    /// suficiente y mantiene la cuenta redonda: cada tono cae justo en una casilla del analisis
    /// por simbolo.
    /// </remarks>
    public const int MargenEnTonos = 2;

    private readonly float[] _espectroReal;
    private readonly float[] _espectroImaginaria;
    private readonly int _longitudDeLaTransformada;

    private AnalisisDeVentana(
        ParametrosDelModo parametros,
        float[] espectroReal,
        float[] espectroImaginaria,
        int longitudDeLaTransformada,
        float[] potenciaPorBloque,
        int bloques,
        int casillasPorBloque,
        double ruidoDeFondo)
    {
        Parametros = parametros;
        _espectroReal = espectroReal;
        _espectroImaginaria = espectroImaginaria;
        _longitudDeLaTransformada = longitudDeLaTransformada;
        PotenciaPorBloque = potenciaPorBloque;
        Bloques = bloques;
        CasillasPorBloque = casillasPorBloque;
        RuidoDeFondo = ruidoDeFondo;
    }

    /// <summary>Parametros del modo que se esta analizando.</summary>
    public ParametrosDelModo Parametros { get; }

    /// <summary>Potencia del espectrograma, ordenada por bloque y casilla.</summary>
    public float[] PotenciaPorBloque { get; }

    /// <summary>Bloques de medio simbolo que tiene el espectrograma.</summary>
    public int Bloques { get; }

    /// <summary>Casillas de frecuencia de cada bloque.</summary>
    public int CasillasPorBloque { get; }

    /// <summary>Potencia mediana de la banda, que se toma como nivel de ruido.</summary>
    /// <remarks>
    /// Se usa la mediana y no la media porque las senales fuertes tiran de la media hacia
    /// arriba: con una sola estacion potente en la banda, la media diria que hay mucho mas
    /// ruido del que hay y todos los informes saldrian bajos.
    /// </remarks>
    public double RuidoDeFondo { get; }

    /// <summary>Hercios que abarca cada casilla del espectrograma: medio tono.</summary>
    public double HzPorCasilla => Parametros.EspaciadoDeTonosHz / 2;

    /// <summary>Casillas de separacion entre dos tonos vecinos en el espectrograma.</summary>
    public const int CasillasPorTono = 2;

    /// <summary>Muestras por simbolo de la senal ya recortada y llevada a banda base.</summary>
    public int MuestrasPorSimboloEnBase => 2 * Parametros.Tonos;

    /// <summary>Muestras que tiene la senal recortada, para la ventana entera.</summary>
    public int MuestrasDeLaBandaBase => MuestrasPorSimboloEnBase * _longitudDeLaTransformada / Parametros.MuestrasPorSimboloDeAnalisis;

    /// <summary>
    /// Analiza una ventana de audio que ya esta a la frecuencia de analisis del modo.
    /// </summary>
    /// <param name="muestras">Audio de la ventana, a <see cref="ParametrosDelModo.FrecuenciaDeAnalisis"/>.</param>
    /// <param name="parametros">Parametros del modo.</param>
    public static AnalisisDeVentana Calcular(ReadOnlySpan<float> muestras, ParametrosDelModo parametros)
    {
        ArgumentNullException.ThrowIfNull(parametros);
        var nsps = parametros.MuestrasPorSimboloDeAnalisis;
        if (muestras.Length < nsps * 4)
            throw new ArgumentException($"La ventana es demasiado corta: {muestras.Length} muestras.", nameof(muestras));

        // La transformada larga se hace sobre la ventana entera rellenada con ceros. El relleno
        // no inventa senal; solo afina las casillas, que es justo lo que hace falta para poder
        // recortar la banda de una candidata con precision de centesimas de hercio.
        var longitud = Fft.PotenciaDeDosQueCubre(muestras.Length);
        var real = new float[longitud];
        var imaginaria = new float[longitud];
        muestras.CopyTo(real);
        Fft.Transformar(real, imaginaria);

        var (potencia, bloques, casillas, ruido) = Espectrograma(muestras, parametros);
        return new AnalisisDeVentana(parametros, real, imaginaria, longitud, potencia, bloques, casillas, ruido);
    }

    private static (float[] Potencia, int Bloques, int Casillas, double Ruido) Espectrograma(
        ReadOnlySpan<float> muestras, ParametrosDelModo parametros)
    {
        var nsps = parametros.MuestrasPorSimboloDeAnalisis;
        var paso = nsps / 2;
        var longitudFft = nsps * 2;
        var casillas = nsps + 1;
        var bloques = ((muestras.Length - nsps) / paso) + 1;

        var potencia = new float[bloques * casillas];
        var real = new float[longitudFft];
        var imaginaria = new float[longitudFft];

        for (var b = 0; b < bloques; b++)
        {
            Array.Clear(real);
            Array.Clear(imaginaria);
            muestras.Slice(b * paso, nsps).CopyTo(real);
            // Sin ventana de suavizado a proposito: con la ventana rectangular, y solo con ella,
            // las casillas pares caen exactamente sobre los tonos del modo y no se roban energia
            // unas a otras. Cualquier suavizado ensancharia cada tono sobre sus vecinos.
            Fft.Transformar(real, imaginaria);
            for (var c = 0; c < casillas; c++)
                potencia[(b * casillas) + c] = (real[c] * real[c]) + (imaginaria[c] * imaginaria[c]);
        }

        return (potencia, bloques, casillas, Mediana(potencia));
    }

    private static double Mediana(float[] valores)
    {
        if (valores.Length == 0) return 0;
        // Se muestrea en vez de ordenar los millones de valores enteros: la mediana de unos
        // miles de muestras repartidas es indistinguible y cuesta mil veces menos.
        var paso = Math.Max(1, valores.Length / 20000);
        var lista = new List<float>(valores.Length / paso + 1);
        for (var i = 0; i < valores.Length; i += paso) lista.Add(valores[i]);
        lista.Sort();
        return lista[lista.Count / 2];
    }

    /// <summary>Potencia del espectrograma en un bloque y una casilla, o cero si se sale.</summary>
    public float Potencia(int bloque, int casilla)
    {
        if (bloque < 0 || bloque >= Bloques || casilla < 0 || casilla >= CasillasPorBloque) return 0;
        return PotenciaPorBloque[(bloque * CasillasPorBloque) + casilla];
    }

    /// <summary>Casilla del espectrograma que corresponde a una frecuencia.</summary>
    public int CasillaDe(double hercios) => (int)Math.Round(hercios / HzPorCasilla);

    /// <summary>Frecuencia del centro de una casilla del espectrograma.</summary>
    public double FrecuenciaDe(int casilla) => casilla * HzPorCasilla;

    /// <summary>
    /// Recorta los cincuenta hercios de una candidata y los devuelve en banda base.
    /// </summary>
    /// <param name="tonoBaseHz">Frecuencia del tono cero de la candidata.</param>
    /// <param name="real">Destino de la parte real, de <see cref="MuestrasDeLaBandaBase"/> muestras.</param>
    /// <param name="imaginaria">Destino de la parte imaginaria.</param>
    /// <remarks>
    /// Al quedarse con un trozo del espectro y deshacer la transformada salen tres cosas de una
    /// vez: la senal filtrada, trasladada a frecuencia cero y remuestreada. El tono cero acaba
    /// en la casilla <see cref="MargenEnTonos"/> del analisis por simbolo, y cada tono siguiente
    /// en la de al lado.
    /// </remarks>
    public void ExtraerBandaBase(double tonoBaseHz, Span<float> real, Span<float> imaginaria)
    {
        var muestras = MuestrasDeLaBandaBase;
        if (real.Length < muestras || imaginaria.Length < muestras)
            throw new ArgumentException($"Hacen falta {muestras} muestras de destino.", nameof(real));

        var casillasPorTono = _longitudDeLaTransformada / Parametros.MuestrasPorSimboloDeAnalisis;
        var hzPorCasillaLarga = Parametros.FrecuenciaDeAnalisis / _longitudDeLaTransformada;
        var primera = (int)Math.Round(tonoBaseHz / hzPorCasillaLarga) - (MargenEnTonos * casillasPorTono);

        real[..muestras].Clear();
        imaginaria[..muestras].Clear();
        for (var i = 0; i < muestras; i++)
        {
            var c = primera + i;
            if (c < 0 || c >= _espectroReal.Length) continue;
            real[i] = _espectroReal[c];
            imaginaria[i] = _espectroImaginaria[c];
        }
        Fft.TransformarInversa(real[..muestras], imaginaria[..muestras]);
    }

    /// <summary>Hercios que se desplaza la banda base por cada casilla de la transformada larga.</summary>
    public double HzPorCasillaLarga => Parametros.FrecuenciaDeAnalisis / _longitudDeLaTransformada;
}
