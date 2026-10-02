namespace Nodisla.Cuaderno.Audio.Procesado;

/// <summary>
/// Transformada rapida de Fourier compleja, de base dos y con los giros calculados de antemano.
/// </summary>
/// <remarks>
/// La de la cascada calcula senos y cosenos en cada mariposa; aqui se hacen cientos de
/// transformadas por segundo en el hilo de audio, asi que los giros se guardan una vez. Trabaja
/// en el sitio y en doble precision, como WDSP con FFTW.
/// </remarks>
internal sealed class TransformadaCompleja
{
    private readonly double[] _coseno;
    private readonly double[] _seno;
    private readonly int[] _reves;

    /// <summary>Prepara la transformada para un tamano.</summary>
    /// <param name="tamano">Potencia de dos, mayor que uno.</param>
    public TransformadaCompleja(int tamano)
    {
        if (tamano < 2 || (tamano & (tamano - 1)) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tamano), tamano, "El tamano tiene que ser potencia de dos.");
        }

        Tamano = tamano;
        _coseno = new double[tamano / 2];
        _seno = new double[tamano / 2];
        for (var k = 0; k < tamano / 2; k++)
        {
            var angulo = -2.0 * Math.PI * k / tamano;
            _coseno[k] = Math.Cos(angulo);
            _seno[k] = Math.Sin(angulo);
        }

        _reves = new int[tamano];
        var bits = 0;
        while ((1 << bits) < tamano) bits++;
        for (var i = 0; i < tamano; i++)
        {
            var r = 0;
            for (var b = 0; b < bits; b++)
            {
                if ((i & (1 << b)) != 0) r |= 1 << (bits - 1 - b);
            }

            _reves[i] = r;
        }
    }

    /// <summary>Numero de puntos.</summary>
    public int Tamano { get; }

    /// <summary>Directa, sin normalizar.</summary>
    public void Directa(double[] real, double[] imaginaria) => Transformar(real, imaginaria, 1.0);

    /// <summary>Inversa, sin normalizar (hay que dividir por el tamano).</summary>
    public void Inversa(double[] real, double[] imaginaria) => Transformar(real, imaginaria, -1.0);

    private void Transformar(double[] real, double[] imaginaria, double signo)
    {
        var n = Tamano;
        for (var i = 0; i < n; i++)
        {
            var j = _reves[i];
            if (i < j)
            {
                (real[i], real[j]) = (real[j], real[i]);
                (imaginaria[i], imaginaria[j]) = (imaginaria[j], imaginaria[i]);
            }
        }

        for (var salto = 2; salto <= n; salto <<= 1)
        {
            var mitad = salto >> 1;
            var pasoDeGiro = n / salto;
            for (var bloque = 0; bloque < n; bloque += salto)
            {
                for (var k = 0; k < mitad; k++)
                {
                    var c = _coseno[k * pasoDeGiro];
                    var s = signo * _seno[k * pasoDeGiro];
                    var i = bloque + k;
                    var j = i + mitad;
                    var rj = (real[j] * c) - (imaginaria[j] * s);
                    var ij = (real[j] * s) + (imaginaria[j] * c);
                    real[j] = real[i] - rj;
                    imaginaria[j] = imaginaria[i] - ij;
                    real[i] += rj;
                    imaginaria[i] += ij;
                }
            }
        }
    }
}
