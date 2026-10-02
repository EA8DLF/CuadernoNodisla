namespace Nodisla.Cuaderno.Modos.Cw;

/// <summary>
/// Baja el audio de la tarjeta a unos 8 kHz, en continuo, para trabajar la telegrafía.
/// </summary>
/// <remarks>
/// La telegrafía cabe de sobra en 4 kHz de audio. Se diezma por un factor entero (6 a 48 kHz,
/// 3 a 24 kHz, 12 a 96 kHz) con un filtro FIR de seno cardinal con ventana de Blackman que corta
/// antes de la mitad de la frecuencia nueva. Si el factor sale 1 (12 kHz o menos) pasa tal cual.
/// </remarks>
internal sealed class DiezmadorCw
{
    private const double FrecuenciaObjetivo = 8000;

    private readonly int _factor;
    private readonly float[] _coeficientes;
    private readonly float[] _historia;
    private int _posicion;
    private int _fase;

    public DiezmadorCw(int frecuenciaDeEntrada)
    {
        if (frecuenciaDeEntrada <= 0) throw new ArgumentOutOfRangeException(nameof(frecuenciaDeEntrada));
        FrecuenciaDeEntrada = frecuenciaDeEntrada;
        _factor = Math.Max(1, (int)Math.Round(frecuenciaDeEntrada / FrecuenciaObjetivo));
        FrecuenciaDeSalida = frecuenciaDeEntrada / (double)_factor;

        var taps = _factor == 1 ? 1 : (8 * _factor) + 1;
        _coeficientes = new float[taps];
        _historia = new float[taps];
        if (_factor == 1)
        {
            _coeficientes[0] = 1;
            return;
        }

        // Corte a 0,42 de la frecuencia de salida: deja hasta 3,3 kHz limpios a 8 kHz.
        var corte = 0.42 / _factor;
        var centro = (taps - 1) / 2.0;
        double suma = 0;
        for (var i = 0; i < taps; i++)
        {
            var x = i - centro;
            var sinc = x == 0 ? 2 * corte : Math.Sin(2 * Math.PI * corte * x) / (Math.PI * x);
            var ventana = 0.42 - (0.5 * Math.Cos(2 * Math.PI * i / (taps - 1))) + (0.08 * Math.Cos(4 * Math.PI * i / (taps - 1)));
            _coeficientes[i] = (float)(sinc * ventana);
            suma += _coeficientes[i];
        }

        for (var i = 0; i < taps; i++) _coeficientes[i] = (float)(_coeficientes[i] / suma);
    }

    public int FrecuenciaDeEntrada { get; }

    public double FrecuenciaDeSalida { get; }

    /// <summary>Pasa un bloque y deja en <paramref name="salida"/> lo diezmado.</summary>
    /// <returns>Cuántas muestras se han escrito.</returns>
    public int Procesar(ReadOnlySpan<float> entrada, Span<float> salida)
    {
        var n = 0;
        var taps = _coeficientes.Length;
        foreach (var x in entrada)
        {
            _historia[_posicion] = x;
            _posicion = (_posicion + 1) % taps;
            if (++_fase < _factor) continue;
            _fase = 0;

            float acumulado = 0;
            var p = _posicion;
            for (var k = 0; k < taps; k++)
            {
                acumulado += _coeficientes[k] * _historia[p];
                if (++p == taps) p = 0;
            }

            salida[n++] = acumulado;
        }

        return n;
    }

    /// <summary>Lo más que puede salir de un bloque de <paramref name="muestras"/> muestras.</summary>
    public int SalidaMaxima(int muestras) => (muestras / _factor) + 1;
}
