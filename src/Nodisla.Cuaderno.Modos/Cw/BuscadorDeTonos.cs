using Nodisla.Cuaderno.Modos.Senal;

namespace Nodisla.Cuaderno.Modos.Cw;

/// <summary>Un tono de telegrafía visto en el espectro.</summary>
/// <param name="Hz">Frecuencia del pico, interpolada.</param>
/// <param name="SobreElRuidoDb">Lo que destaca sobre el ruido de la banda (dB, potencia media).</param>
public readonly record struct PicoCw(double Hz, double SobreElRuidoDb);

/// <summary>
/// Busca los tonos de telegrafía en la banda de paso: espectro promediado y picos que destacan.
/// </summary>
/// <remarks>
/// <para>
/// Transformada de unos 125 ms con ventana de Hann (casillas de unos 8 Hz) cada medio bloque.
/// Cada casilla se promedia con una constante de un segundo: la telegrafía va y viene, pero su
/// media sigue destacando del ruido, que se mide como la <b>mediana</b> de la banda (los picos de
/// unas pocas señales no la mueven).
/// </para>
/// <para>
/// Un pico vale si es máximo local, si destaca <see cref="UmbralDb"/> sobre el ruido y si cae en la
/// ventana de búsqueda. Su frecuencia se afina con una parábola sobre las tres casillas.
/// </para>
/// </remarks>
public sealed class BuscadorDeTonos
{
    /// <summary>Lo que tiene que destacar un pico sobre la mediana para contar como tono.</summary>
    public const double UmbralDb = 7;

    private readonly int _n;
    private readonly float[] _ventana;
    private readonly float[] _bloque;
    private readonly float[] _re;
    private readonly float[] _im;
    private readonly double[] _media;
    private readonly double _alfa;
    private int _lleno;
    private bool _primero = true;

    /// <summary>Monta el buscador.</summary>
    /// <param name="frecuenciaDeMuestreo">Muestras por segundo.</param>
    public BuscadorDeTonos(double frecuenciaDeMuestreo)
    {
        FrecuenciaDeMuestreo = frecuenciaDeMuestreo;
        _n = Fft.PotenciaDeDosQueCubre((int)(frecuenciaDeMuestreo / 8));
        HzPorCasilla = frecuenciaDeMuestreo / _n;
        _ventana = new float[_n];
        for (var i = 0; i < _n; i++) _ventana[i] = (float)(0.5 - (0.5 * Math.Cos(2 * Math.PI * i / _n)));
        _bloque = new float[_n];
        _re = new float[_n];
        _im = new float[_n];
        _media = new double[(_n / 2) + 1];
        var salto = _n / 2 / frecuenciaDeMuestreo;
        _alfa = 1 - Math.Exp(-salto / 1.0);
    }

    /// <summary>Muestras por segundo.</summary>
    public double FrecuenciaDeMuestreo { get; }

    /// <summary>Ancho de cada casilla del espectro (Hz).</summary>
    public double HzPorCasilla { get; }

    /// <summary>Cada cuánto sale un espectro nuevo (s).</summary>
    public double SegundosPorEspectro => _n / 2 / FrecuenciaDeMuestreo;

    /// <summary>Potencia media de cada casilla.</summary>
    public IReadOnlyList<double> Media => _media;

    /// <summary>Ruido de la banda: mediana de la potencia media (lineal).</summary>
    public double Ruido { get; private set; } = 1e-20;

    /// <summary>Mete una muestra; devuelve verdadero cuando hay espectro nuevo.</summary>
    public bool Anadir(float x)
    {
        _bloque[_lleno++] = x;
        if (_lleno < _n) return false;

        for (var i = 0; i < _n; i++)
        {
            _re[i] = _bloque[i] * _ventana[i];
            _im[i] = 0;
        }

        Fft.Transformar(_re, _im);
        for (var k = 0; k < _media.Length; k++)
        {
            var p = ((double)_re[k] * _re[k]) + ((double)_im[k] * _im[k]);
            _media[k] = _primero ? p : _media[k] + (_alfa * (p - _media[k]));
        }

        _primero = false;

        // Medio bloque de solape.
        Array.Copy(_bloque, _n / 2, _bloque, 0, _n / 2);
        _lleno = _n / 2;
        return true;
    }

    /// <summary>Los picos de la ventana, del más fuerte al más débil.</summary>
    /// <param name="desdeHz">Límite inferior de la búsqueda.</param>
    /// <param name="hastaHz">Límite superior.</param>
    /// <param name="separacionHz">Lo mínimo que tienen que distar dos picos.</param>
    public IReadOnlyList<PicoCw> Picos(double desdeHz, double hastaHz, double separacionHz)
    {
        // El ruido se mide algo más ancho que la ventana, para que una banda llena de señales no
        // lo suba.
        var k0 = Math.Max(2, (int)((desdeHz - 200) / HzPorCasilla));
        var k1 = Math.Min(_media.Length - 3, (int)Math.Ceiling((hastaHz + 400) / HzPorCasilla));
        if (k1 <= k0) return [];
        var copia = new double[k1 - k0 + 1];
        Array.Copy(_media, k0, copia, 0, copia.Length);
        Array.Sort(copia);
        Ruido = Math.Max(copia[copia.Length / 2], 1e-20);

        var umbral = Ruido * Math.Pow(10, UmbralDb / 10);
        var vecinas = Math.Max(1, (int)Math.Round(separacionHz / HzPorCasilla / 2));
        var b0 = Math.Max(1, (int)Math.Floor(desdeHz / HzPorCasilla));
        var b1 = Math.Min(_media.Length - 2, (int)Math.Ceiling(hastaHz / HzPorCasilla));
        var picos = new List<PicoCw>();
        for (var k = b0; k <= b1; k++)
        {
            var v = _media[k];
            if (v < umbral) continue;
            var esMaximo = true;
            for (var j = Math.Max(0, k - vecinas); j <= Math.Min(_media.Length - 1, k + vecinas); j++)
            {
                if (j != k && (_media[j] > v || (_media[j] == v && j < k)))
                {
                    esMaximo = false;
                    break;
                }
            }

            if (!esMaximo) continue;

            // Parábola sobre los logaritmos de las tres casillas.
            var a = Math.Log(_media[k - 1] + 1e-30);
            var b = Math.Log(v + 1e-30);
            var c = Math.Log(_media[k + 1] + 1e-30);
            var den = a - (2 * b) + c;
            var desplazamiento = den < 0 ? Math.Clamp(0.5 * (a - c) / den, -0.5, 0.5) : 0;
            var hz = (k + desplazamiento) * HzPorCasilla;
            if (hz < desdeHz || hz > hastaHz) continue;
            picos.Add(new PicoCw(hz, 10 * Math.Log10(v / Ruido)));
        }

        picos.Sort((x, y) => y.SobreElRuidoDb.CompareTo(x.SobreElRuidoDb));
        return picos;
    }

    /// <summary>El espectro medio en dB sobre el ruido, de <paramref name="desdeHz"/> a <paramref name="hastaHz"/>.</summary>
    public float[] EspectroDb(double desdeHz, double hastaHz, out double primeraHz)
    {
        var b0 = Math.Max(0, (int)Math.Floor(desdeHz / HzPorCasilla));
        var b1 = Math.Min(_media.Length - 1, (int)Math.Ceiling(hastaHz / HzPorCasilla));
        primeraHz = b0 * HzPorCasilla;
        var salida = new float[Math.Max(0, b1 - b0 + 1)];
        for (var k = b0; k <= b1; k++) salida[k - b0] = (float)(10 * Math.Log10((_media[k] + 1e-30) / Ruido));
        return salida;
    }
}
