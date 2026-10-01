namespace Nodisla.Cuaderno.Modos.Q65;

/// <summary>
/// La energia de la ventana por instante y frecuencia, con la rejilla de Q65.
/// </summary>
/// <remarks>
/// <para>
/// Cada columna es la energia de un trozo de audio de un simbolo de largo, y las columnas van
/// a saltos de un cuarto de simbolo para poder situar el comienzo de la senal con ese error
/// como mucho. Cada casilla de frecuencia mide media separacion de tonos del submodo A, asi que
/// los 65 tonos de cualquier submodo caen en casillas exactas y el tono base se localiza con un
/// error de un cuarto de tono.
/// </para>
/// <para>
/// La ventana de cada columna es rectangular a proposito: es la que casa con un tono que dura
/// exactamente un simbolo, y cualquier otra regalaria decibelios. Se transforman dos columnas
/// por cada transformada compleja, metiendo una en la parte real y otra en la imaginaria, que
/// es un truco viejo y rentable con senal real.
/// </para>
/// <para>
/// Solo se guarda la banda que interesa, de 200 a 2950 Hz: para Q65-300A, que es el mas fino,
/// son unos veinte mil valores por columna, y aun asi cabe holgadamente en memoria.
/// </para>
/// </remarks>
public sealed class EspectrogramaDeQ65
{
    private readonly float[] _energias;

    private EspectrogramaDeQ65(ParametrosDeQ65 parametros, float[] energias, int columnas, int casillaMinima, int casillas, double ruidoDeFondo)
    {
        Parametros = parametros;
        _energias = energias;
        Columnas = columnas;
        CasillaMinima = casillaMinima;
        Casillas = casillas;
        RuidoDeFondo = ruidoDeFondo;
    }

    /// <summary>Parametros con los que se calculo.</summary>
    public ParametrosDeQ65 Parametros { get; }

    /// <summary>Columnas de tiempo.</summary>
    public int Columnas { get; }

    /// <summary>Primera casilla de frecuencia guardada.</summary>
    public int CasillaMinima { get; }

    /// <summary>Casillas guardadas por columna.</summary>
    public int Casillas { get; }

    /// <summary>Ultima casilla guardada.</summary>
    public int CasillaMaxima => CasillaMinima + Casillas - 1;

    /// <summary>Energia media del ruido por casilla, estimada por la mediana de todo el espectrograma.</summary>
    public double RuidoDeFondo { get; }

    /// <summary>Energia de una casilla en una columna.</summary>
    public float Energia(int columna, int casilla) => _energias[(columna * Casillas) + casilla - CasillaMinima];

    /// <summary>Casilla que corresponde a una frecuencia.</summary>
    public int CasillaDe(double hercios) => (int)Math.Round(hercios / Parametros.HzPorCasilla);

    /// <summary>Frecuencia del centro de una casilla.</summary>
    public double FrecuenciaDe(int casilla) => casilla * Parametros.HzPorCasilla;

    /// <summary>Instante, respecto al comienzo del audio, en que empieza una columna.</summary>
    public double SegundosDe(int columna) => (double)columna * Parametros.Salto / ParametrosDeQ65.FrecuenciaDeAnalisis;

    /// <summary>Calcula el espectrograma de una ventana de audio a la frecuencia de analisis.</summary>
    public static EspectrogramaDeQ65 Calcular(ReadOnlySpan<float> audio, ParametrosDeQ65 parametros)
    {
        ArgumentNullException.ThrowIfNull(parametros);
        var nsps = parametros.MuestrasPorSimbolo;
        var puntos = parametros.PuntosDeLaTransformada;
        var salto = parametros.Salto;
        var columnas = audio.Length < nsps ? 0 : ((audio.Length - nsps) / salto) + 1;

        var casillaMinima = (int)Math.Round(ParametrosDeQ65.FrecuenciaMinimaHz / parametros.HzPorCasilla);
        var casillaMaxima = Math.Min((int)Math.Round(ParametrosDeQ65.FrecuenciaMaximaHz / parametros.HzPorCasilla), nsps);
        var casillas = casillaMaxima - casillaMinima + 1;
        var energias = new float[columnas * casillas];

        var transformada = new TransformadaMixta(puntos);
        var re = new double[puntos];
        var im = new double[puntos];

        for (var c = 0; c < columnas; c += 2)
        {
            Array.Clear(re);
            Array.Clear(im);
            var a = audio.Slice(c * salto, nsps);
            for (var i = 0; i < nsps; i++) re[i] = a[i];
            var hayB = c + 1 < columnas;
            if (hayB)
            {
                var b = audio.Slice((c + 1) * salto, nsps);
                for (var i = 0; i < nsps; i++) im[i] = b[i];
            }
            transformada.Transformar(re, im);

            // Separar las dos senales reales: la de la parte real es la mitad simetrica del
            // espectro y la de la imaginaria, la mitad antisimetrica.
            for (var k = casillaMinima; k <= casillaMaxima; k++)
            {
                var j = (puntos - k) % puntos;
                var aRe = 0.5 * (re[k] + re[j]);
                var aIm = 0.5 * (im[k] - im[j]);
                energias[(c * casillas) + k - casillaMinima] = (float)((aRe * aRe) + (aIm * aIm));
                if (hayB)
                {
                    var bRe = 0.5 * (im[k] + im[j]);
                    var bIm = -0.5 * (re[k] - re[j]);
                    energias[((c + 1) * casillas) + k - casillaMinima] = (float)((bRe * bRe) + (bIm * bIm));
                }
            }
        }

        return new EspectrogramaDeQ65(parametros, energias, columnas, casillaMinima, casillas, EstimarRuido(energias));
    }

    /// <summary>
    /// Nivel de ruido: la mediana de una muestra de las energias, pasada a media. Las senales
    /// ocupan una fraccion minima de las casillas y no mueven la mediana.
    /// </summary>
    private static double EstimarRuido(float[] energias)
    {
        if (energias.Length == 0) return 1;
        const int Muestra = 20000;
        var paso = Math.Max(1, energias.Length / Muestra);
        var valores = new List<float>(Muestra + 1);
        for (var i = 0; i < energias.Length; i += paso) valores.Add(energias[i]);
        valores.Sort();
        var mediana = valores[valores.Count / 2];
        return Math.Max(mediana / Math.Log(2), 1e-30);
    }
}
