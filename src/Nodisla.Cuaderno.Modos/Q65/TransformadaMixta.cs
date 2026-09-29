namespace Nodisla.Cuaderno.Modos.Q65;

/// <summary>
/// Transformada de Fourier para longitudes que no son potencia de dos.
/// </summary>
/// <remarks>
/// <para>
/// Los simbolos de Q65 duran 900, 1800, 3600, 8000 o 20736 muestras a la frecuencia de
/// analisis, y para que las casillas del espectro caigan justo en los tonos hay que
/// transformar el doble de eso, que no es potencia de dos. Rellenar hasta la siguiente potencia
/// desalinearia las casillas con los tonos y habria que interpolar, perdiendo decibelios. En
/// cambio, todas esas longitudes se descomponen en factores dos, tres y cinco, y con eso basta
/// para una transformada rapida de raices mixtas: se divide la senal en tantas subsecuencias
/// como diga el primer factor, se transforma cada una y se recombinan con una transformada
/// pequena del tamano del factor.
/// </para>
/// <para>
/// Es propia, como la de raiz dos de <c>Senal/</c>, y por el mismo motivo: nada de terceros en
/// el modem. Los senos y cosenos se tabulan una vez por longitud.
/// </para>
/// </remarks>
public sealed class TransformadaMixta
{
    private readonly int _n;
    private readonly int[] _factores;
    private readonly double[] _cos;
    private readonly double[] _sin;
    private readonly double[] _salidaRe;
    private readonly double[] _salidaIm;

    /// <summary>Prepara una transformada de la longitud indicada.</summary>
    /// <exception cref="ArgumentException">Si la longitud tiene algun factor primo mayor que cinco.</exception>
    public TransformadaMixta(int n)
    {
        if (n < 1) throw new ArgumentOutOfRangeException(nameof(n));
        _n = n;
        var factores = new List<int>();
        var resto = n;
        foreach (var f in new[] { 4, 2, 3, 5 })
            while (resto % f == 0) { factores.Add(f); resto /= f; }
        if (resto != 1) throw new ArgumentException($"La longitud {n} tiene factores primos mayores que cinco.", nameof(n));
        _factores = [.. factores];

        _cos = new double[n];
        _sin = new double[n];
        for (var k = 0; k < n; k++)
        {
            _cos[k] = Math.Cos(2 * Math.PI * k / n);
            _sin[k] = -Math.Sin(2 * Math.PI * k / n);
        }
        _salidaRe = new double[n];
        _salidaIm = new double[n];
    }

    /// <summary>Longitud de la transformada.</summary>
    public int Longitud => _n;

    /// <summary>Transforma en el sitio. Las partes real e imaginaria van en vectores separados.</summary>
    public void Transformar(Span<double> real, Span<double> imaginaria)
    {
        if (real.Length != _n || imaginaria.Length != _n)
            throw new ArgumentException($"Los vectores tienen que medir {_n}.", nameof(real));
        Calcular(real, imaginaria, 0, 1, _n, 0, 0);
        _salidaRe.AsSpan().CopyTo(real);
        _salidaIm.AsSpan().CopyTo(imaginaria);
    }

    private void Calcular(ReadOnlySpan<double> entradaRe, ReadOnlySpan<double> entradaIm, int desde, int paso, int n, int factor, int salidaDesde)
    {
        if (n == 1)
        {
            _salidaRe[salidaDesde] = entradaRe[desde];
            _salidaIm[salidaDesde] = entradaIm[desde];
            return;
        }

        var r = _factores[factor];
        var m = n / r;
        for (var j = 0; j < r; j++)
            Calcular(entradaRe, entradaIm, desde + (j * paso), paso * r, m, factor + 1, salidaDesde + (j * m));

        // Recombinacion: cada salida k de las r subtransformadas se gira por su factor de giro y
        // se pasa por una transformada de r puntos. Las r entradas y las r salidas ocupan las
        // mismas posiciones, asi que se leen todas antes de escribir ninguna.
        Span<double> tRe = stackalloc double[r];
        Span<double> tIm = stackalloc double[r];
        var giroDeLaSubtransformada = _n / n;
        var giroDelFactor = _n / r;
        for (var k = 0; k < m; k++)
        {
            for (var j = 0; j < r; j++)
            {
                var indice = salidaDesde + (j * m) + k;
                var g = (j * k * giroDeLaSubtransformada) % _n;
                var re = _salidaRe[indice];
                var im = _salidaIm[indice];
                tRe[j] = (re * _cos[g]) - (im * _sin[g]);
                tIm[j] = (re * _sin[g]) + (im * _cos[g]);
            }

            for (var q = 0; q < r; q++)
            {
                double sRe = 0, sIm = 0;
                for (var j = 0; j < r; j++)
                {
                    var g = (j * q * giroDelFactor) % _n;
                    sRe += (tRe[j] * _cos[g]) - (tIm[j] * _sin[g]);
                    sIm += (tRe[j] * _sin[g]) + (tIm[j] * _cos[g]);
                }
                _salidaRe[salidaDesde + (q * m) + k] = sRe;
                _salidaIm[salidaDesde + (q * m) + k] = sIm;
            }
        }
    }
}
