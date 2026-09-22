using Nodisla.Cuaderno.Audio.Cascada;
using Nodisla.Cuaderno.Audio.Reloj;

namespace Nodisla.Cuaderno.Audio;

/// <summary>Como trabaja el audio del modem.</summary>
public sealed class OpcionesDeAudio
{
    private int _frecuenciaDeMuestreo = 48000;
    private int _milisegundosPorBloque = 100;
    private int _segundosDeColchon = 8;
    private int _latenciaDeCapturaMs = 50;
    private int _latenciaDeSalidaMs = 100;

    /// <summary>
    /// Muestras por segundo con las que trabaja el modem. Por omision, 48.000, que es lo que
    /// da el codec del FT-710 y lo que esperan FT8 y FT4.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Si no es positiva.</exception>
    public int FrecuenciaDeMuestreo
    {
        get => _frecuenciaDeMuestreo;
        set
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    "La frecuencia de muestreo tiene que ser positiva.");
            }

            _frecuenciaDeMuestreo = value;
        }
    }

    /// <summary>
    /// Lo que dura cada bloque que se entrega. Por omision, 100 ms: bastante fino para que la
    /// cascada vaya suelta y bastante grueso para no ahogar a nadie a base de avisos.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Si no esta entre 10 y 1000 ms.</exception>
    public int MilisegundosPorBloque
    {
        get => _milisegundosPorBloque;
        set
        {
            if (value < 10 || value > 1000)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    "El bloque tiene que durar entre 10 y 1000 milisegundos.");
            }

            _milisegundosPorBloque = value;
        }
    }

    /// <summary>
    /// Segundos de audio que aguanta el colchon entre el hilo de la tarjeta y el que trabaja.
    /// </summary>
    /// <remarks>
    /// Por omision, ocho segundos: mas de medio periodo de FT8. Es de sobra para que un
    /// decodificador que tarde no haga perder muestras, y lo que cuesta es medio mega de
    /// memoria. Quedarse corto aqui sale carisimo: un hueco se lleva el periodo entero.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">Si no esta entre 1 y 120 segundos.</exception>
    public int SegundosDeColchon
    {
        get => _segundosDeColchon;
        set
        {
            if (value < 1 || value > 120)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    "El colchón tiene que ser de entre 1 y 120 segundos.");
            }

            _segundosDeColchon = value;
        }
    }

    /// <summary>Lo que se le pide a Windows de colchon propio al capturar, en milisegundos.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Si no esta entre 10 y 500 ms.</exception>
    public int LatenciaDeCapturaMs
    {
        get => _latenciaDeCapturaMs;
        set
        {
            if (value < 10 || value > 500)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    "La latencia de captura tiene que estar entre 10 y 500 milisegundos.");
            }

            _latenciaDeCapturaMs = value;
        }
    }

    /// <summary>Lo que se le pide a Windows de colchon propio al reproducir, en milisegundos.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Si no esta entre 20 y 500 ms.</exception>
    public int LatenciaDeSalidaMs
    {
        get => _latenciaDeSalidaMs;
        set
        {
            if (value < 20 || value > 500)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    "La latencia de salida tiene que estar entre 20 y 500 milisegundos.");
            }

            _latenciaDeSalidaMs = value;
        }
    }

    /// <summary>Ajustes del reloj del modem.</summary>
    public OpcionesDelReloj Reloj { get; set; } = new();

    /// <summary>Ajustes de la cascada.</summary>
    public OpcionesDeCascada Cascada { get; set; } = new();

    /// <summary>Muestras que lleva cada bloque, segun la frecuencia y la duracion elegidas.</summary>
    public int MuestrasPorBloque => Math.Max(1, _frecuenciaDeMuestreo * _milisegundosPorBloque / 1000);

    /// <summary>Muestras que caben en el colchon.</summary>
    public int MuestrasDeColchon => _frecuenciaDeMuestreo * _segundosDeColchon;

    /// <summary>Copia estos ajustes, para no compartir el objeto con quien lo configuro.</summary>
    /// <returns>Una copia independiente.</returns>
    public OpcionesDeAudio Copiar() => new()
    {
        _frecuenciaDeMuestreo = _frecuenciaDeMuestreo,
        _milisegundosPorBloque = _milisegundosPorBloque,
        _segundosDeColchon = _segundosDeColchon,
        _latenciaDeCapturaMs = _latenciaDeCapturaMs,
        _latenciaDeSalidaMs = _latenciaDeSalidaMs,
        Reloj = Reloj.Copiar(),
        Cascada = Cascada.Copiar(),
    };
}
