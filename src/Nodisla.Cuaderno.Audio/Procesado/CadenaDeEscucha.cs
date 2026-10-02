namespace Nodisla.Cuaderno.Audio.Procesado;

/// <summary>Que reductor de ruido se usa en la escucha.</summary>
public enum TipoDeReductor
{
    /// <summary>Espectral (EMNR, el «NR2» de Thetis). El que mas quita; anade unos 11 ms.</summary>
    Espectral,

    /// <summary>Adaptativo LMS (ANR, el «NR1» de Thetis). Mas suave; sin retraso.</summary>
    Adaptativo,
}

/// <summary>
/// Lo que se le hace al audio de la radio antes de que suene por los altavoces del PC: notch
/// automatico, reductor de ruido y limitador, en ese orden.
/// </summary>
/// <remarks>
/// <para>
/// Solo va en el camino de la escucha. El modem propio y el decodificador de CW leen el codec
/// del equipo por su cuenta, en modo compartido, y reciben el audio <b>sin tocar</b>.
/// </para>
/// <para>
/// Cada paso se enciende y apaga en vivo: los ajustes se leen al principio de cada bloque, y al
/// encender un paso se empieza de cero para que no arrastre lo que aprendio en otra senal.
/// </para>
/// </remarks>
public sealed class CadenaDeEscucha : IProcesadorDeAudio
{
    private readonly FiltroLmsAdaptativo _notch = new(UsoDelFiltroLms.Notch);
    private readonly FiltroLmsAdaptativo _lms = new(UsoDelFiltroLms.Reductor);
    private readonly ReductorEspectral _espectral = new();
    private readonly Limitador _limitador = new();

    private volatile bool _reductorActivo;
    private volatile TipoDeReductor _tipo = TipoDeReductor.Espectral;
    private volatile bool _notchActivo;
    private volatile bool _limitadorActivo = true;
    private double _nivel = 0.7;
    private double _techoDb = -3.0;
    private volatile GananciaEspectral _metodo = GananciaEspectral.Gaussiana;

    private bool _notchEnMarcha;
    private TipoDeReductor? _reductorEnMarcha;
    private bool _limitadorEnMarcha;
    private int _ultimaFrecuencia;

    /// <summary>Reductor de ruido encendido.</summary>
    public bool ReductorActivo { get => _reductorActivo; set => _reductorActivo = value; }

    /// <summary>Cual de los dos reductores.</summary>
    public TipoDeReductor Tipo { get => _tipo; set => _tipo = value; }

    /// <summary>Metodo de ganancia del reductor espectral.</summary>
    public GananciaEspectral Metodo { get => _metodo; set => _metodo = value; }

    /// <summary>Cuanto quita el reductor espectral, de 0 a 1.</summary>
    public double Nivel
    {
        get => Volatile.Read(ref _nivel);
        set => Volatile.Write(ref _nivel, double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0.7);
    }

    /// <summary>Notch automatico encendido.</summary>
    public bool NotchActivo { get => _notchActivo; set => _notchActivo = value; }

    /// <summary>Limitador encendido.</summary>
    public bool LimitadorActivo { get => _limitadorActivo; set => _limitadorActivo = value; }

    /// <summary>Techo del limitador en dBFS (de -20 a -1).</summary>
    public double TechoDb
    {
        get => Volatile.Read(ref _techoDb);
        set => Volatile.Write(ref _techoDb, double.IsFinite(value) ? Math.Clamp(value, -20, -1) : -3.0);
    }

    /// <summary>Reduccion del limitador en curso, en dB (0 o negativo).</summary>
    public double ReduccionDelLimitadorDb =>
        _limitadorEnMarcha ? 20.0 * Math.Log10(Math.Max(_limitador.GananciaActual, 1e-6)) : 0.0;

    /// <summary>Retraso que anade la cadena con los ajustes de ahora.</summary>
    /// <param name="frecuencia">Frecuencia de muestreo; si es cero, la ultima vista o 48 kHz.</param>
    public TimeSpan LatenciaAnadida(int frecuencia = 0)
    {
        var f = frecuencia > 0 ? frecuencia : (_ultimaFrecuencia > 0 ? _ultimaFrecuencia : 48000);
        return ReductorActivo && Tipo == TipoDeReductor.Espectral
            ? TimeSpan.FromSeconds((double)ReductorEspectral.RetrasoEnMuestras(f) / f)
            : TimeSpan.Zero;
    }

    /// <inheritdoc />
    public void Procesar(Span<float> muestras, int frecuencia)
    {
        if (frecuencia <= 0 || muestras.IsEmpty) return;

        if (frecuencia != _ultimaFrecuencia)
        {
            _ultimaFrecuencia = frecuencia;
            _notch.Reiniciar();
            _lms.Reiniciar();
            _espectral.Reiniciar();
            _limitador.Reiniciar();
        }

        // 1. Notch: antes que el reductor, para que la portadora no cuente como «voz».
        var notch = NotchActivo;
        if (notch && !_notchEnMarcha) _notch.Reiniciar();
        _notchEnMarcha = notch;
        if (notch) _notch.Procesar(muestras);

        // 2. Reductor.
        TipoDeReductor? reductor = ReductorActivo ? Tipo : null;
        if (reductor != _reductorEnMarcha)
        {
            if (reductor == TipoDeReductor.Espectral) _espectral.Reiniciar();
            if (reductor == TipoDeReductor.Adaptativo) _lms.Reiniciar();
            _reductorEnMarcha = reductor;
        }

        if (reductor == TipoDeReductor.Espectral)
        {
            _espectral.Nivel = Nivel;
            _espectral.Metodo = Metodo;
            _espectral.Procesar(muestras, frecuencia);
        }
        else if (reductor == TipoDeReductor.Adaptativo)
        {
            _lms.Procesar(muestras);
        }

        // 3. Limitador: el ultimo, para que nada de lo anterior pase del techo.
        var limitar = LimitadorActivo;
        if (limitar && !_limitadorEnMarcha) _limitador.Reiniciar();
        _limitadorEnMarcha = limitar;
        if (limitar)
        {
            _limitador.TechoDb = TechoDb;
            _limitador.Procesar(muestras, frecuencia);
        }

        // Red de seguridad: un filtro adaptativo que se dispara no llega a los altavoces.
        for (var i = 0; i < muestras.Length; i++)
        {
            if (!float.IsFinite(muestras[i]))
            {
                muestras.Clear();
                _notch.Reiniciar();
                _lms.Reiniciar();
                _espectral.Reiniciar();
                break;
            }
        }
    }
}
