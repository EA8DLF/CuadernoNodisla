namespace Nodisla.Cuaderno.Audio.Procesado;

/// <summary>Un trozo de audio de un canal en memoria.</summary>
/// <param name="Muestras">Muestras de -1 a 1.</param>
/// <param name="Frecuencia">Muestras por segundo.</param>
public sealed record AudioEnMemoria(float[] Muestras, int Frecuencia)
{
    /// <summary>Lo que dura.</summary>
    public TimeSpan Duracion => Frecuencia > 0 ? TimeSpan.FromSeconds((double)Muestras.Length / Frecuencia) : TimeSpan.Zero;
}

/// <summary>
/// Grabador continuo de la recepcion: guarda en memoria los ultimos minutos de lo que llega de
/// la radio, para poder quedarse con «lo que acaba de pasar».
/// </summary>
/// <remarks>
/// <para>
/// Escribe en un anillo de enteros de 16 bits (5 minutos a 48 kHz son unos 29 MB). Graba el
/// audio <b>tal como llega</b>, sin volumen ni reductor: es el archivo del contacto, no lo que
/// sono. Nada sale de memoria hasta que el operador lo pide.
/// </para>
/// <para>
/// Lo escribe el hilo de audio y lo lee la pantalla: un cerrojo corto por bloque basta.
/// </para>
/// </remarks>
public sealed class GrabadorCircular : IProcesadorDeAudio
{
    /// <summary>Lo minimo y lo maximo que se puede guardar.</summary>
    public const int MinutosMinimos = 1;

    /// <summary>Lo maximo que se puede guardar.</summary>
    public const int MinutosMaximos = 10;

    private readonly object _candado = new();
    private short[] _anillo = [];
    private int _frecuencia;
    private int _escritura;
    private long _escritas;
    private volatile bool _activo = true;
    private int _minutos = 5;

    /// <summary>Esta grabando. Apagado suelta la memoria.</summary>
    public bool Activo
    {
        get => _activo;
        set
        {
            _activo = value;
            if (!value) Vaciar();
        }
    }

    /// <summary>Minutos que se guardan (1 a 10). Cambiarlo vacia lo grabado.</summary>
    public int Minutos
    {
        get => Volatile.Read(ref _minutos);
        set
        {
            var nuevo = Math.Clamp(value, MinutosMinimos, MinutosMaximos);
            if (Interlocked.Exchange(ref _minutos, nuevo) != nuevo) Vaciar();
        }
    }

    /// <summary>Lo que hay grabado ahora.</summary>
    public TimeSpan Grabado
    {
        get
        {
            lock (_candado)
            {
                return _frecuencia > 0
                    ? TimeSpan.FromSeconds((double)Math.Min(_escritas, _anillo.Length) / _frecuencia)
                    : TimeSpan.Zero;
            }
        }
    }

    /// <inheritdoc />
    /// <remarks>No toca el audio: solo lo copia.</remarks>
    public void Procesar(Span<float> muestras, int frecuencia)
    {
        if (!_activo || frecuencia <= 0 || muestras.IsEmpty) return;

        lock (_candado)
        {
            var capacidad = frecuencia * 60 * Minutos;
            if (frecuencia != _frecuencia || _anillo.Length != capacidad)
            {
                _anillo = new short[capacidad];
                _frecuencia = frecuencia;
                _escritura = 0;
                _escritas = 0;
            }

            for (var i = 0; i < muestras.Length; i++)
            {
                var v = float.IsFinite(muestras[i]) ? Math.Clamp(muestras[i], -1f, 1f) : 0f;
                _anillo[_escritura] = (short)Math.Round(v * short.MaxValue);
                if (++_escritura == _anillo.Length) _escritura = 0;
            }

            _escritas += muestras.Length;
        }
    }

    /// <summary>Copia lo ultimo grabado, de lo mas viejo a lo mas nuevo.</summary>
    /// <param name="cuanto">Cuanto; nulo, todo lo que haya.</param>
    /// <returns>El audio, o nulo si no hay nada.</returns>
    public AudioEnMemoria? Instantanea(TimeSpan? cuanto = null)
    {
        lock (_candado)
        {
            if (_frecuencia <= 0 || _escritas == 0) return null;

            var hay = (int)Math.Min(_escritas, _anillo.Length);
            var piden = cuanto is { } c && c > TimeSpan.Zero ? (int)Math.Min(hay, c.TotalSeconds * _frecuencia) : hay;
            var salida = new float[piden];
            var inicio = _escritura - piden;
            if (inicio < 0) inicio += _anillo.Length;
            for (var i = 0; i < piden; i++)
            {
                salida[i] = _anillo[(inicio + i) % _anillo.Length] / (float)short.MaxValue;
            }

            return new AudioEnMemoria(salida, _frecuencia);
        }
    }

    /// <summary>Olvida lo grabado y suelta la memoria.</summary>
    public void Vaciar()
    {
        lock (_candado)
        {
            _anillo = [];
            _frecuencia = 0;
            _escritura = 0;
            _escritas = 0;
        }
    }
}
