namespace Nodisla.Cuaderno.Audio.Cascada;

/// <summary>Como se calcula la cascada.</summary>
/// <remarks>
/// Los valores de partida estan elegidos para FT8 a 48.000 muestras por segundo: un tramo de
/// 8192 muestras dura 0,17 segundos, casi lo que dura un simbolo de FT8, y deja casillas de
/// 5,86 Hz, menos que los 6,25 Hz que separan sus tonos. Con medio tramo de salto salen unas
/// doce columnas por segundo, que es lo que pide el puerto: varias veces por segundo.
/// </remarks>
public sealed class OpcionesDeCascada
{
    private int _tamanoDeTransformada = 8192;
    private int _salto = 4096;
    private double _frecuenciaMaximaHz = 4000.0;
    private double _sueloDb = -140.0;

    /// <summary>Muestras de cada tramo. Tiene que ser potencia de dos.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Si no es potencia de dos o es menor de 64.</exception>
    public int TamanoDeTransformada
    {
        get => _tamanoDeTransformada;
        set
        {
            if (!TransformadaRapida.EsPotenciaDeDos(value) || value < 64)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    "El tamaño de la transformada tiene que ser potencia de dos y al menos 64.");
            }

            _tamanoDeTransformada = value;
        }
    }

    /// <summary>Muestras que se avanza de una columna a la siguiente.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Si no es positivo.</exception>
    public int Salto
    {
        get => _salto;
        set
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    "El salto tiene que ser positivo.");
            }

            _salto = value;
        }
    }

    /// <summary>
    /// Hasta que frecuencia se entregan casillas. Por encima del paso de audio del equipo no
    /// hay nada que mirar, y mandar casillas de mas solo hace trabajar a la pantalla.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Si no es positiva.</exception>
    public double FrecuenciaMaximaHz
    {
        get => _frecuenciaMaximaHz;
        set
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    "La frecuencia máxima tiene que ser positiva.");
            }

            _frecuenciaMaximaHz = value;
        }
    }

    /// <summary>Suelo en decibelios: por debajo de aqui todo vale lo mismo.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Si no es negativo.</exception>
    public double SueloDb
    {
        get => _sueloDb;
        set
        {
            if (value >= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    "El suelo en decibelios tiene que ser negativo.");
            }

            _sueloDb = value;
        }
    }

    /// <summary>Copia estos ajustes, para no compartir el objeto con quien lo configuro.</summary>
    /// <returns>Una copia independiente, con el salto recortado al tamaño del tramo.</returns>
    public OpcionesDeCascada Copiar() => new()
    {
        _tamanoDeTransformada = _tamanoDeTransformada,
        _salto = Math.Min(_salto, _tamanoDeTransformada),
        _frecuenciaMaximaHz = _frecuenciaMaximaHz,
        _sueloDb = _sueloDb,
    };
}
