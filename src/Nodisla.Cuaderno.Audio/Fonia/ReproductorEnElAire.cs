using Nodisla.Cuaderno.Audio.Captura;
using Nodisla.Cuaderno.Audio.Procesado;

namespace Nodisla.Cuaderno.Audio.Fonia;

/// <summary>
/// Pone un mensaje grabado en lugar de la voz del microfono, en el camino de transmision de la
/// fonia: el voice keyer.
/// </summary>
/// <remarks>
/// <para>
/// No abre nada ni sube el PTT: se coloca delante de la ganancia del camino de transmision y,
/// mientras esta <see cref="Armado"/>, cambia cada bloque del microfono por un trozo del
/// mensaje. Asi el mensaje pasa por <b>lo mismo</b> que la voz en vivo —la ganancia, el
/// procesado del micro con su techo, el silencio del camino y el vigilante—, y el reloj del
/// microfono marca el paso, de modo que el latido del PTT sigue siendo honrado.
/// </para>
/// <para>
/// Hasta que <see cref="ControlDeFonia"/> lo arma —con el PTT ya arriba—, saca silencio y no
/// avanza. Al acabar el mensaje deja una cola de silencio para que se vacie la salida hacia el
/// equipo, y avisa con <see cref="Terminado"/> una sola vez.
/// </para>
/// </remarks>
public sealed class ReproductorEnElAire : IProcesadorDeAudio
{
    /// <summary>Silencio que se deja al final antes de soltar el PTT.</summary>
    public static readonly TimeSpan Cola = TimeSpan.FromMilliseconds(300);

    private readonly AudioEnMemoria _mensaje;
    private float[] _muestras = [];
    private int _frecuencia;
    private int _posicion;
    private int _colaRestante;
    private volatile bool _armado;
    private int _avisado;

    /// <summary>Prepara el mensaje, sin armarlo.</summary>
    public ReproductorEnElAire(AudioEnMemoria mensaje)
    {
        ArgumentNullException.ThrowIfNull(mensaje);
        _mensaje = mensaje;
    }

    /// <summary>Lo que dura el mensaje mas la cola.</summary>
    public TimeSpan Duracion => _mensaje.Duracion + Cola;

    /// <summary>Ya suena: el PTT esta arriba y el camino abierto.</summary>
    public bool Armado
    {
        get => _armado;
        set => _armado = value;
    }

    /// <summary>Lo que va sonado, de 0 a 1.</summary>
    public double Avance => _muestras.Length == 0 ? 0 : Math.Min(1.0, (double)_posicion / _muestras.Length);

    /// <summary>Ha sonado entero, cola incluida.</summary>
    public bool Acabado => Volatile.Read(ref _avisado) != 0;

    /// <summary>Salta una vez, desde el hilo de audio, al acabar mensaje y cola.</summary>
    public event EventHandler? Terminado;

    /// <inheritdoc />
    public void Procesar(Span<float> muestras, int frecuencia)
    {
        if (frecuencia <= 0) return;
        if (frecuencia != _frecuencia) Preparar(frecuencia);

        if (!_armado || Acabado)
        {
            // Hasta que se arma, y despues de acabar, nada del microfono pasa.
            muestras.Clear();
            return;
        }

        var quedan = _muestras.Length - _posicion;
        var copiar = Math.Min(quedan, muestras.Length);
        if (copiar > 0)
        {
            _muestras.AsSpan(_posicion, copiar).CopyTo(muestras);
            _posicion += copiar;
        }

        if (copiar < muestras.Length)
        {
            muestras[copiar..].Clear();
            _colaRestante -= muestras.Length - copiar;
            if (_colaRestante <= 0 && Interlocked.Exchange(ref _avisado, 1) == 0)
            {
                Terminado?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private void Preparar(int frecuencia)
    {
        _frecuencia = frecuencia;
        if (_mensaje.Frecuencia == frecuencia || _mensaje.Frecuencia <= 0)
        {
            _muestras = _mensaje.Muestras;
        }
        else
        {
            // Se graba con el microfono y se suelta al mismo; si no coinciden, se convierte una vez.
            var remuestreador = new Remuestreador(_mensaje.Frecuencia, frecuencia);
            var destino = new float[remuestreador.MuestrasMaximas(_mensaje.Muestras.Length)];
            var escritas = remuestreador.Convertir(_mensaje.Muestras, destino);
            _muestras = destino.AsSpan(0, escritas).ToArray();
        }

        _posicion = 0;
        _colaRestante = (int)(Cola.TotalSeconds * frecuencia);
    }
}
