using System.Runtime.Versioning;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Nodisla.Cuaderno.Audio.Captura;
using Nodisla.Cuaderno.Audio.Procesado;
using Nodisla.Cuaderno.Idiomas;

namespace Nodisla.Cuaderno.Audio.Fonia;

/// <summary>Graba el microfono del PC con WASAPI compartido, en memoria y con tope de duracion.</summary>
[SupportedOSPlatform("windows")]
public sealed class GrabadorDeMicrofonoWasapi : IGrabadorDeMicrofono
{
    private readonly object _candado = new();
    private readonly List<float> _grabado = [];
    private WasapiCapture? _captura;
    private MMDevice? _dispositivo;
    private int _frecuencia;
    private int _canales;
    private int _bits;
    private bool _flotante;
    private float[] _mono = [];
    private double _nivel;
    private TaskCompletionSource? _parada;

    /// <inheritdoc />
    public bool Grabando
    {
        get
        {
            lock (_candado) return _captura is not null;
        }
    }

    /// <inheritdoc />
    public double Nivel => Volatile.Read(ref _nivel);

    /// <inheritdoc />
    public Task EmpezarAsync(string idMicrofono, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idMicrofono);
        return Task.Run(
            () =>
            {
                lock (_candado)
                {
                    if (_captura is not null) return;
                    MMDevice dispositivo;
                    try
                    {
                        using var enumerador = new MMDeviceEnumerator();
                        dispositivo = enumerador.GetDevice(idMicrofono);
                    }
                    catch (Exception fallo)
                    {
                        throw new InvalidOperationException(Textos.T("Servicios.Audio.SinDispositivosElegidos"), fallo);
                    }

                    var captura = new WasapiCapture(dispositivo, true, 30);
                    var formato = captura.WaveFormat;
                    _frecuencia = formato.SampleRate;
                    _canales = formato.Channels;
                    _bits = formato.BitsPerSample;
                    _flotante = formato.Encoding == WaveFormatEncoding.IeeeFloat
                        || (formato is WaveFormatExtensible && formato.BitsPerSample == 32);
                    _grabado.Clear();
                    _parada = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    captura.DataAvailable += AlLlegar;
                    captura.RecordingStopped += AlParar;
                    _captura = captura;
                    _dispositivo = dispositivo;
                    captura.StartRecording();
                }
            },
            ct);
    }

    /// <inheritdoc />
    public async Task<AudioEnMemoria?> PararAsync()
    {
        WasapiCapture? captura;
        TaskCompletionSource? parada;
        lock (_candado)
        {
            captura = _captura;
            parada = _parada;
        }

        if (captura is null) return null;

        captura.StopRecording();
        if (parada is not null) await parada.Task.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);

        lock (_candado)
        {
            captura.DataAvailable -= AlLlegar;
            captura.RecordingStopped -= AlParar;
            captura.Dispose();
            _dispositivo?.Dispose();
            _captura = null;
            _dispositivo = null;
            Volatile.Write(ref _nivel, 0);
            return _grabado.Count == 0 ? null : new AudioEnMemoria([.. _grabado], _frecuencia);
        }
    }

    private void AlLlegar(object? origen, WaveInEventArgs datos)
    {
        lock (_candado)
        {
            if (_captura is null || datos.BytesRecorded <= 0) return;
            var cuadros = ConversorDeMuestras.CuantasMuestras(datos.BytesRecorded, _canales, _bits);
            if (_mono.Length < cuadros) _mono = new float[cuadros * 2];
            ConversorDeMuestras.AMono(datos.Buffer.AsSpan(0, datos.BytesRecorded), _canales, _bits, _flotante, _mono);

            var tope = (int)(AlmacenDeMensajesDeVoz.DuracionMaxima.TotalSeconds * _frecuencia);
            var pico = 0f;
            for (var i = 0; i < cuadros && _grabado.Count < tope; i++)
            {
                _grabado.Add(_mono[i]);
                pico = Math.Max(pico, Math.Abs(_mono[i]));
            }

            var anterior = Volatile.Read(ref _nivel);
            Volatile.Write(ref _nivel, pico >= anterior ? pico : Math.Max(pico, anterior * 0.85));
        }
    }

    private void AlParar(object? origen, StoppedEventArgs e) => _parada?.TrySetResult();
}

/// <summary>Suena audio por un dispositivo de salida con WASAPI compartido.</summary>
[SupportedOSPlatform("windows")]
public sealed class ReproductorLocalWasapi : IReproductorLocal
{
    private CancellationTokenSource? _corte;

    /// <inheritdoc />
    public bool Sonando => Volatile.Read(ref _corte) is not null;

    /// <inheritdoc />
    public async Task ReproducirAsync(AudioEnMemoria audio, string idAltavoces, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(audio);
        ArgumentException.ThrowIfNullOrWhiteSpace(idAltavoces);
        Parar();

        using var corte = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Volatile.Write(ref _corte, corte);
        try
        {
            await Task.Run(() => Sonar(audio, idAltavoces, corte.Token), CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            Interlocked.CompareExchange(ref _corte, null, corte);
        }
    }

    /// <inheritdoc />
    public void Parar()
    {
        try
        {
            Volatile.Read(ref _corte)?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Ya habia terminado.
        }
    }

    private static void Sonar(AudioEnMemoria audio, string idAltavoces, CancellationToken ct)
    {
        using var enumerador = new MMDeviceEnumerator();
        using var dispositivo = enumerador.GetDevice(idAltavoces);
        var mezcla = dispositivo.AudioClient.MixFormat;
        var plano = mezcla is WaveFormatExtensible ext ? ext.ToStandardWaveFormat() : mezcla;
        var flotante = plano.Encoding == WaveFormatEncoding.IeeeFloat;

        // Al formato del dispositivo: remuestreo una vez y repartir a sus canales.
        var remuestreador = new Remuestreador(audio.Frecuencia, mezcla.SampleRate);
        var convertido = new float[remuestreador.MuestrasMaximas(audio.Muestras.Length)];
        var n = remuestreador.Convertir(audio.Muestras, convertido);
        var bytes = new byte[n * plano.Channels * (plano.BitsPerSample / 8)];
        var escritos = ConversorDeMuestras.DesdeMono(convertido.AsSpan(0, n), plano.Channels, plano.BitsPerSample, flotante, bytes);

        using var fuente = new RawSourceWaveStream(new MemoryStream(bytes, 0, escritos), mezcla);
        using var salida = new WasapiOut(dispositivo, AudioClientShareMode.Shared, true, 60);
        using var fin = new ManualResetEventSlim(false);
        salida.PlaybackStopped += (_, _) => fin.Set();
        salida.Init(fuente);
        salida.Play();
        WaitHandle.WaitAny([fin.WaitHandle, ct.WaitHandle]);
        salida.Stop();
    }
}
