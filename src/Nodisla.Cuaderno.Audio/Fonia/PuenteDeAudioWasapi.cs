using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Nodisla.Cuaderno.Audio.Captura;
using Nodisla.Cuaderno.Idiomas;

namespace Nodisla.Cuaderno.Audio.Fonia;

/// <summary>
/// Camino de audio en vivo con WASAPI compartido: captura de un dispositivo y reproduccion por
/// otro, con poca latencia.
/// </summary>
/// <remarks>
/// <para>
/// Todo en <b>modo compartido</b>: la entrada del codec del equipo la puede estar leyendo a la
/// vez el modem propio, y la salida hacia el equipo la puede tener abierta su salida. Windows
/// mezcla y reparte; nadie se queda con el dispositivo.
/// </para>
/// <para>
/// Cada dispositivo va a su formato de mezcla (el codec del FT-710 suele estar a 44 100 Hz y los
/// altavoces del PC a 48 000): se pasa a un canal, se aplica la ganancia, se remuestrea y se
/// reparte a los canales de la salida. La cola se mantiene corta —si crece por la deriva entre
/// los dos relojes, se tira el bloque que llega— para que la escucha no se vaya retrasando.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class PuenteDeAudioWasapi : IPuenteDeAudio
{
    private readonly object _candado = new();
    private readonly ILogger _registro;
    private readonly int _latenciaMs;
    private readonly TimeSpan _colaMaxima;

    private WasapiCapture? _captura;
    private WasapiOut? _salida;
    private BufferedWaveProvider? _cola;
    private MMDevice? _dispositivoDeEntrada;
    private MMDevice? _dispositivoDeSalida;
    private Remuestreador? _remuestreador;
    private (int Canales, int Bits, bool Flotante) _formatoDeEntrada;
    private (int Canales, int Bits, bool Flotante) _formatoDeSalida;

    private float[] _mono = [];
    private float[] _convertido = [];
    private byte[] _bytes = [];

    private long _ultimaCapturaTicks;
    private long _ultimaLecturaTicks;
    private long _ultimaSaturacionTicks;
    private double _nivel;
    private float _ganancia = 1f;
    private volatile bool _silenciado;

    /// <summary>Monta el camino, sin abrir nada.</summary>
    /// <param name="latenciaMs">Latencia pedida a WASAPI en cada extremo.</param>
    /// <param name="colaMaximaMs">Retraso maximo que se deja acumular en la cola.</param>
    /// <param name="registro">Donde se anota; si es nulo, en ningun sitio.</param>
    public PuenteDeAudioWasapi(int latenciaMs = 30, int colaMaximaMs = 120, ILogger? registro = null)
    {
        _latenciaMs = Math.Clamp(latenciaMs, 10, 200);
        _colaMaxima = TimeSpan.FromMilliseconds(Math.Clamp(colaMaximaMs, 40, 1000));
        _registro = registro ?? NullLogger.Instance;
    }

    /// <inheritdoc />
    public bool EstaAbierto
    {
        get
        {
            lock (_candado) return _captura is not null;
        }
    }

    /// <inheritdoc />
    public float Ganancia
    {
        get => Volatile.Read(ref _ganancia);
        set => Volatile.Write(ref _ganancia, float.IsFinite(value) ? Math.Clamp(value, 0f, 16f) : 1f);
    }

    /// <inheritdoc />
    public bool Silenciado
    {
        get => _silenciado;
        set => _silenciado = value;
    }

    /// <inheritdoc />
    public double Nivel
    {
        get
        {
            // El medidor cae solo si deja de llegar audio.
            var ultima = Interlocked.Read(ref _ultimaCapturaTicks);
            return ultima == 0 || DateTime.UtcNow.Ticks - ultima > TimeSpan.TicksPerSecond / 2
                ? 0
                : Volatile.Read(ref _nivel);
        }
    }

    /// <inheritdoc />
    public bool Saturando => DateTime.UtcNow.Ticks - Interlocked.Read(ref _ultimaSaturacionTicks) < TimeSpan.TicksPerSecond;

    /// <inheritdoc />
    public DateTimeOffset? UltimoAvanceUtc
    {
        get
        {
            if (!EstaAbierto) return null;
            var captura = Interlocked.Read(ref _ultimaCapturaTicks);
            var lectura = Interlocked.Read(ref _ultimaLecturaTicks);
            if (captura == 0 || lectura == 0) return null;
            return new DateTimeOffset(Math.Min(captura, lectura), TimeSpan.Zero);
        }
    }

    /// <inheritdoc />
    public event EventHandler<Exception>? Fallo;

    /// <inheritdoc />
    public async Task AbrirAsync(string idEntrada, string idSalida, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idEntrada);
        ArgumentException.ThrowIfNullOrWhiteSpace(idSalida);

        await CerrarAsync().ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();

        await Task.Run(() => Abrir(idEntrada, idSalida), ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task CerrarAsync()
    {
        WasapiCapture? captura;
        WasapiOut? salida;
        MMDevice? entrada;
        MMDevice? dispositivoDeSalida;

        lock (_candado)
        {
            captura = _captura;
            salida = _salida;
            entrada = _dispositivoDeEntrada;
            dispositivoDeSalida = _dispositivoDeSalida;
            _captura = null;
            _salida = null;
            _cola = null;
            _dispositivoDeEntrada = null;
            _dispositivoDeSalida = null;
            _remuestreador = null;
        }

        Interlocked.Exchange(ref _ultimaCapturaTicks, 0);
        Interlocked.Exchange(ref _ultimaLecturaTicks, 0);
        Volatile.Write(ref _nivel, 0);

        if (captura is null && salida is null) return Task.CompletedTask;

        return Task.Run(() =>
        {
            if (captura is not null)
            {
                captura.DataAvailable -= AlLlegarAudio;
                captura.RecordingStopped -= AlPararLaCaptura;
                Intentar(captura.StopRecording, "parar la captura");
                Intentar(captura.Dispose, "soltar la captura");
            }

            if (salida is not null)
            {
                salida.PlaybackStopped -= AlPararLaSalida;
                Intentar(salida.Stop, "parar la salida");
                Intentar(salida.Dispose, "soltar la salida");
            }

            Intentar(() => entrada?.Dispose(), "soltar el dispositivo de entrada");
            Intentar(() => dispositivoDeSalida?.Dispose(), "soltar el dispositivo de salida");
        });
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync() => await CerrarAsync().ConfigureAwait(false);

    private void Abrir(string idEntrada, string idSalida)
    {
        MMDevice entrada;
        MMDevice salidaDispositivo;
        try
        {
            using var enumerador = new MMDeviceEnumerator();
            entrada = enumerador.GetDevice(idEntrada);
            salidaDispositivo = enumerador.GetDevice(idSalida);
        }
        catch (Exception fallo)
        {
            throw new InvalidOperationException(
                Textos.T("Servicios.Audio.SinDispositivosElegidos"),
                fallo);
        }

        WasapiCapture? captura = null;
        WasapiOut? salida = null;
        try
        {
            captura = new WasapiCapture(entrada, true, _latenciaMs);
            var formatoDeEntrada = captura.WaveFormat;
            var formatoDeSalida = salidaDispositivo.AudioClient.MixFormat;

            var cola = new BufferedWaveProvider(formatoDeSalida)
            {
                BufferDuration = TimeSpan.FromSeconds(1),
                DiscardOnBufferOverflow = true,
                ReadFully = true,
            };

            salida = new WasapiOut(salidaDispositivo, AudioClientShareMode.Shared, true, _latenciaMs);
            salida.Init(new ProveedorQueAvisa(cola, this));

            lock (_candado)
            {
                _dispositivoDeEntrada = entrada;
                _dispositivoDeSalida = salidaDispositivo;
                _captura = captura;
                _salida = salida;
                _cola = cola;
                _formatoDeEntrada = Interpretar(formatoDeEntrada);
                _formatoDeSalida = Interpretar(formatoDeSalida);
                _remuestreador = new Remuestreador(formatoDeEntrada.SampleRate, formatoDeSalida.SampleRate);
            }

            captura.DataAvailable += AlLlegarAudio;
            captura.RecordingStopped += AlPararLaCaptura;
            salida.PlaybackStopped += AlPararLaSalida;

            salida.Play();
            captura.StartRecording();

            _registro.LogInformation(
                "Camino de audio «{Entrada}» ({FrecuenciaEntrada} Hz) → «{Salida}» ({FrecuenciaSalida} Hz), latencia {Latencia} ms.",
                entrada.FriendlyName,
                formatoDeEntrada.SampleRate,
                salidaDispositivo.FriendlyName,
                formatoDeSalida.SampleRate,
                _latenciaMs);
        }
        catch
        {
            lock (_candado)
            {
                _captura = null;
                _salida = null;
                _cola = null;
                _dispositivoDeEntrada = null;
                _dispositivoDeSalida = null;
            }

            Intentar(() => captura?.Dispose(), "soltar la captura a medio abrir");
            Intentar(() => salida?.Dispose(), "soltar la salida a medio abrir");
            Intentar(entrada.Dispose, "soltar la entrada");
            Intentar(salidaDispositivo.Dispose, "soltar la salida");
            throw;
        }
    }

    private void AlLlegarAudio(object? origen, WaveInEventArgs datos)
    {
        try
        {
            BufferedWaveProvider? cola;
            Remuestreador? remuestreador;
            (int Canales, int Bits, bool Flotante) entrada;
            (int Canales, int Bits, bool Flotante) salida;

            lock (_candado)
            {
                cola = _cola;
                remuestreador = _remuestreador;
                entrada = _formatoDeEntrada;
                salida = _formatoDeSalida;
            }

            if (cola is null || remuestreador is null || datos.BytesRecorded <= 0) return;

            var cuadros = ConversorDeMuestras.CuantasMuestras(datos.BytesRecorded, entrada.Canales, entrada.Bits);
            if (cuadros <= 0) return;

            if (_mono.Length < cuadros) _mono = new float[cuadros * 2];
            var mono = _mono.AsSpan(0, cuadros);
            ConversorDeMuestras.AMono(datos.Buffer.AsSpan(0, datos.BytesRecorded), entrada.Canales, entrada.Bits, entrada.Flotante, mono);

            var ganancia = Ganancia;
            var pico = 0f;
            for (var i = 0; i < mono.Length; i++)
            {
                var valor = mono[i] * ganancia;
                var absoluto = Math.Abs(valor);
                if (absoluto > pico) pico = absoluto;
                mono[i] = valor;
            }

            // Medidor con caida suave: sube de golpe y baja poco a poco.
            if (pico >= 0.99f) Interlocked.Exchange(ref _ultimaSaturacionTicks, DateTime.UtcNow.Ticks);
            pico = Math.Min(pico, 1f);
            var anterior = Volatile.Read(ref _nivel);
            Volatile.Write(ref _nivel, pico >= anterior ? pico : Math.Max(pico, anterior * 0.85));
            Interlocked.Exchange(ref _ultimaCapturaTicks, DateTime.UtcNow.Ticks);

            if (_silenciado) mono.Clear();

            // Si la cola ya lleva mas retraso del que se admite, se tira este bloque: un salto de
            // unos milisegundos se nota menos que una escucha que llega cada vez mas tarde.
            if (cola.BufferedDuration > _colaMaxima) return;

            ReadOnlySpan<float> paraSalir = mono;
            if (!remuestreador.EsPasoDirecto)
            {
                var tope = remuestreador.MuestrasMaximas(cuadros);
                if (_convertido.Length < tope) _convertido = new float[tope * 2];
                var escritas = remuestreador.Convertir(mono, _convertido);
                paraSalir = _convertido.AsSpan(0, escritas);
            }

            var bytesNecesarios = paraSalir.Length * salida.Canales * (salida.Bits / 8);
            if (_bytes.Length < bytesNecesarios) _bytes = new byte[bytesNecesarios * 2];
            var escritos = ConversorDeMuestras.DesdeMono(paraSalir, salida.Canales, salida.Bits, salida.Flotante, _bytes);
            cola.AddSamples(_bytes, 0, escritos);
        }
        catch (Exception fallo)
        {
            // Es el hilo de Windows: no se deja salir nada.
            _registro.LogError(fallo, "Fallo al pasar audio por el camino de fonía.");
        }
    }

    private void AlPararLaCaptura(object? origen, StoppedEventArgs e) => AlRomperse(e.Exception);

    private void AlPararLaSalida(object? origen, StoppedEventArgs e) => AlRomperse(e.Exception);

    /// <summary>
    /// Un extremo se ha parado solo —la radio se apago, se desenchufo el USB—: se cierra el
    /// camino entero y se avisa. Un camino a medias que dice estar abierto engaña al panel.
    /// </summary>
    private void AlRomperse(Exception? fallo)
    {
        if (fallo is null) return;

        _registro.LogWarning(fallo, "Un extremo del camino de audio se ha parado solo; se cierra el camino.");
        _ = CerrarAsync();
        Fallo?.Invoke(this, fallo);
    }

    private (int Canales, int Bits, bool Flotante) Interpretar(WaveFormat formato)
    {
        var plano = formato;
        var flotante = formato.Encoding == WaveFormatEncoding.IeeeFloat;

        if (formato is WaveFormatExtensible extendido)
        {
            try
            {
                plano = extendido.ToStandardWaveFormat();
                flotante = plano.Encoding == WaveFormatEncoding.IeeeFloat;
            }
            catch (Exception fallo)
            {
                _registro.LogDebug(fallo, "No se ha podido simplificar el formato; se supone coma flotante a 32 bits.");
                flotante = formato.BitsPerSample == 32;
            }
        }

        return (plano.Channels, plano.BitsPerSample, flotante);
    }

    private void Intentar(Action accion, string que)
    {
        try
        {
            accion();
        }
        catch (Exception fallo)
        {
            _registro.LogDebug(fallo, "Fallo al {Que}.", que);
        }
    }

    /// <summary>Envoltorio que anota cuando la salida pide muestras: es la prueba de que avanza.</summary>
    private sealed class ProveedorQueAvisa(IWaveProvider origen, PuenteDeAudioWasapi puente) : IWaveProvider
    {
        public WaveFormat WaveFormat => origen.WaveFormat;

        public int Read(byte[] buffer, int offset, int count)
        {
            Interlocked.Exchange(ref puente._ultimaLecturaTicks, DateTime.UtcNow.Ticks);
            return origen.Read(buffer, offset, count);
        }
    }
}
