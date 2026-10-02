using Nodisla.Cuaderno.Audio.Fonia;
using Nodisla.Cuaderno.Audio.Procesado;

namespace Nodisla.Cuaderno.Ui.Desarrollo;

/// <summary>
/// Camino de audio de mentira para trabajar la fonia sin tarjeta, sin microfono y sin radio.
/// </summary>
/// <remarks>
/// No abre ningun dispositivo: los medidores se mueven con un vaiven inventado. Mientras esta
/// abierto pasa cada 20 ms un bloque inventado (ruido y un tono) por los pasos de procesado,
/// para que el grabador y el voice keyer avancen como con una tarjeta de verdad. Lo que sale no
/// va a ningun sitio.
/// </remarks>
public sealed class PuenteDeAudioSimulado : IPuenteDeAudio
{
    private const int Frecuencia = 48000;
    private const int Bloque = 960;

    private readonly double _fase;
    private readonly float[] _bloque = new float[Bloque];
    private readonly Random _azar = new(7);
    private Timer? _reloj;
    private long _muestra;
    private DateTimeOffset? _ultimoAvance;

    /// <summary>Monta el camino.</summary>
    /// <param name="fase">Para que los dos medidores no se muevan igual.</param>
    public PuenteDeAudioSimulado(double fase = 0) => _fase = fase;

    /// <inheritdoc />
    public bool EstaAbierto { get; private set; }

    /// <inheritdoc />
    public float Ganancia { get; set; } = 1f;

    /// <inheritdoc />
    public bool Silenciado { get; set; }

    /// <inheritdoc />
    public IProcesadorDeAudio? AntesDeLaGanancia { get; set; }

    /// <inheritdoc />
    public IProcesadorDeAudio? TrasLaGanancia { get; set; }

    /// <inheritdoc />
    public double Nivel
    {
        get
        {
            if (!EstaAbierto) return 0;
            var t = DateTime.UtcNow.TimeOfDay.TotalSeconds + _fase;
            return Math.Clamp((0.35 + (0.25 * Math.Sin(t * 5.3)) + (0.1 * Math.Sin(t * 13.1))) * Ganancia, 0, 1);
        }
    }

    /// <inheritdoc />
    public bool Saturando => Nivel >= 0.99;

    /// <inheritdoc />
    public DateTimeOffset? UltimoAvanceUtc => EstaAbierto ? _ultimoAvance ?? DateTimeOffset.UtcNow : null;

    /// <inheritdoc />
    public event EventHandler<Exception>? Fallo
    {
        add { }
        remove { }
    }

    /// <inheritdoc />
    public Task AbrirAsync(string idEntrada, string idSalida, CancellationToken ct = default)
    {
        EstaAbierto = true;
        _ultimoAvance = DateTimeOffset.UtcNow;
        _reloj ??= new Timer(_ => Bombear(), null, TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(20));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task CerrarAsync()
    {
        EstaAbierto = false;
        _reloj?.Dispose();
        _reloj = null;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => new(CerrarAsync());

    private void Bombear()
    {
        if (!EstaAbierto) return;
        try
        {
            for (var i = 0; i < Bloque; i++, _muestra++)
            {
                var t = (double)_muestra / Frecuencia;
                var voz = 0.2 * Math.Sin(2 * Math.PI * 700 * t) * (0.5 + (0.5 * Math.Sin(2 * Math.PI * 3 * t)));
                _bloque[i] = (float)(voz + (0.05 * ((_azar.NextDouble() * 2) - 1)));
            }

            AntesDeLaGanancia?.Procesar(_bloque, Frecuencia);
            for (var i = 0; i < Bloque; i++) _bloque[i] *= Ganancia;
            TrasLaGanancia?.Procesar(_bloque, Frecuencia);
            _ultimoAvance = DateTimeOffset.UtcNow;
        }
        catch (Exception fallo)
        {
            Serilog.Log.Debug(fallo, "Fallo en el camino de audio simulado.");
        }
    }
}
