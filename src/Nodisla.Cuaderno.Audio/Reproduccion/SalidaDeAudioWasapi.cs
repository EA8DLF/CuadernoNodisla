using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Audio.Captura;
using Nodisla.Cuaderno.Audio.Dispositivos;

namespace Nodisla.Cuaderno.Audio.Reproduccion;

/// <summary>
/// Reproduccion de audio por WASAPI hacia el equipo.
/// </summary>
/// <remarks>
/// <para>
/// <b>Esto emite.</b> Lo que suene por aqui sale por la antena, asi que nunca se llama suelto:
/// va dentro de una transmision pedida al vigilante del PTT. Lo normal es usar
/// <see cref="EmisionVigilada"/>, que se encarga de pedirla, latir mientras suena y soltarla.
/// </para>
/// <para>
/// Se reproduce en el formato que tenga puesto el dispositivo, convirtiendo aqui: en modo
/// compartido Windows siempre acepta su propio formato de mezcla, asi que no hace falta que
/// nadie mas remuestree ni haya sorpresas al abrir.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class SalidaDeAudioWasapi : ISalidaDeAudio
{
    private readonly OpcionesDeAudio _opciones;
    private readonly ILogger _registro;
    private readonly object _candado = new();
    private readonly Func<DateTimeOffset> _relojDelSistema;

    private IReadOnlyList<DispositivoDeAudio> _dispositivos = Array.Empty<DispositivoDeAudio>();
    private WasapiOut? _salida;
    private MMDevice? _dispositivo;
    private BufferedWaveProvider? _cola;
    private Remuestreador? _remuestreador;

    private int _canales;
    private int _bitsPorMuestra;
    private bool _esComaFlotante;
    private int _frecuenciaDelModem = 48000;
    private long _ultimoAvance;
    private bool _sonando;
    private bool _desechado;

    /// <summary>Crea la salida de audio.</summary>
    /// <param name="opciones">Ajustes; si es nulo, los de partida.</param>
    /// <param name="registro">Donde se anota lo que pasa; si es nulo, no se anota nada.</param>
    /// <param name="relojDelSistema">De donde se lee la hora; si es nulo, la del sistema.</param>
    public SalidaDeAudioWasapi(
        OpcionesDeAudio? opciones = null,
        ILogger? registro = null,
        Func<DateTimeOffset>? relojDelSistema = null)
    {
        _opciones = (opciones ?? new OpcionesDeAudio()).Copiar();
        _registro = registro ?? NullLogger.Instance;
        _relojDelSistema = relojDelSistema ?? (() => DateTimeOffset.UtcNow);

        Volatile.Write(ref _ultimoAvance, _relojDelSistema().UtcTicks);
        Refrescar();
    }

    /// <inheritdoc />
    public IReadOnlyList<DispositivoDeAudio> Dispositivos => _dispositivos;

    /// <inheritdoc />
    public DispositivoDeAudio? Abierto { get; private set; }

    /// <inheritdoc />
    /// <remarks>
    /// Con el dispositivo cerrado se devuelve nulo: sin tarjeta abierta no hay nada que avance
    /// y decir una hora seria inventarse una senal de vida. Quien emita debe entenderlo como
    /// «no se puede saber» y dejar de latir, no como «acaba de avanzar».
    /// </remarks>
    public DateTimeOffset? UltimoAvanceUtc => _salida is null
        ? null
        : new DateTimeOffset(Volatile.Read(ref _ultimoAvance), TimeSpan.Zero);

    /// <summary>Hay audio sonando ahora mismo.</summary>
    public bool EstaSonando => Volatile.Read(ref _sonando);

    /// <summary>Muestras por segundo con las que se le habla a esta salida.</summary>
    public int FrecuenciaDeMuestreo => _frecuenciaDelModem;

    /// <summary>Vuelve a mirar que dispositivos hay.</summary>
    public void Refrescar() => _dispositivos = CatalogoDeDispositivos.Salidas(_registro);

    /// <inheritdoc />
    /// <exception cref="ArgumentException">Si no se encuentra el dispositivo.</exception>
    /// <exception cref="ObjectDisposedException">Si la salida ya se ha desechado.</exception>
    public async Task AbrirAsync(string idDispositivo, int frecuenciaDeMuestreo = 48000, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_desechado, this);

        if (string.IsNullOrWhiteSpace(idDispositivo))
        {
            throw new ArgumentException("Hay que decir qué dispositivo se abre.", nameof(idDispositivo));
        }

        if (frecuenciaDeMuestreo <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(frecuenciaDeMuestreo),
                frecuenciaDeMuestreo,
                "La frecuencia de muestreo tiene que ser positiva.");
        }

        await CerrarAsync(ct).ConfigureAwait(false);

        MMDevice dispositivo;
        try
        {
            using var enumerador = new MMDeviceEnumerator();
            dispositivo = enumerador.GetDevice(idDispositivo);
        }
        catch (Exception fallo)
        {
            throw new ArgumentException(
                "No se encuentra ese dispositivo de sonido; puede que la radio esté apagada.",
                nameof(idDispositivo),
                fallo);
        }

        var formato = dispositivo.AudioClient.MixFormat;
        var (canales, bits, esFlotante) = Interpretar(formato);

        var cola = new BufferedWaveProvider(formato)
        {
            // Los segundos de cola son los que aguantan una emision entera de FT8 sin que el
            // programa tenga que ir alimentando al ritmo exacto de la tarjeta.
            BufferDuration = TimeSpan.FromSeconds(Math.Max(4, _opciones.SegundosDeColchon)),

            // Si algo se descontrola, mejor quedarse sin audio que tirar por lo alto y liarla:
            // se prefiere que la escritura espere sitio.
            DiscardOnBufferOverflow = false,
            ReadFully = true,
        };

        var salida = new WasapiOut(
            dispositivo,
            AudioClientShareMode.Shared,
            true,
            _opciones.LatenciaDeSalidaMs);

        salida.Init(cola);

        lock (_candado)
        {
            _dispositivo = dispositivo;
            _salida = salida;
            _cola = cola;
            _canales = canales;
            _bitsPorMuestra = bits;
            _esComaFlotante = esFlotante;
            _frecuenciaDelModem = frecuenciaDeMuestreo;
            _remuestreador = new Remuestreador(frecuenciaDeMuestreo, formato.SampleRate);

            Abierto = _dispositivos.FirstOrDefault(
                candidato => string.Equals(candidato.Id, idDispositivo, StringComparison.OrdinalIgnoreCase))
                ?? new DispositivoDeAudio(idDispositivo, dispositivo.FriendlyName, EsDeEntrada: false, EsDelEquipo: false);
        }

        Avanzar();

        _registro.LogInformation(
            "Salida de audio por «{Nombre}»: {Frecuencia} Hz, {Canales} canal(es), {Bits} bits.",
            Abierto?.Nombre,
            formato.SampleRate,
            canales,
            bits);
    }

    /// <inheritdoc />
    public Task CerrarAsync(CancellationToken ct = default)
    {
        WasapiOut? salida;
        MMDevice? dispositivo;

        lock (_candado)
        {
            salida = _salida;
            dispositivo = _dispositivo;
            _salida = null;
            _dispositivo = null;
            _cola = null;
            _remuestreador = null;
            Abierto = null;
        }

        Volatile.Write(ref _sonando, false);

        if (salida is not null)
        {
            try
            {
                salida.Stop();
            }
            catch (Exception fallo)
            {
                _registro.LogDebug(fallo, "Algo falló al parar la salida de audio.");
            }

            salida.Dispose();
        }

        dispositivo?.Dispose();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">Si no hay dispositivo abierto.</exception>
    public async Task ReproducirAsync(ReadOnlyMemory<float> muestras, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_desechado, this);

        BufferedWaveProvider cola;
        WasapiOut salida;
        Remuestreador remuestreador;

        lock (_candado)
        {
            if (_salida is null || _cola is null || _remuestreador is null)
            {
                throw new InvalidOperationException(
                    "No hay ningún dispositivo de salida abierto: no se puede reproducir.");
            }

            cola = _cola;
            salida = _salida;
            remuestreador = _remuestreador;
        }

        if (muestras.IsEmpty)
        {
            return;
        }

        var bytes = PrepararBytes(muestras.Span, remuestreador);

        Volatile.Write(ref _sonando, true);
        Avanzar();

        try
        {
            cola.ClearBuffer();
            salida.Play();

            await MeterEnLaColaAsync(cola, bytes, ct).ConfigureAwait(false);
            await EsperarAQueSuenTodoAsync(cola, ct).ConfigureAwait(false);
        }
        finally
        {
            Volatile.Write(ref _sonando, false);
        }
    }

    /// <inheritdoc />
    public Task SilenciarAsync(CancellationToken ct = default)
    {
        BufferedWaveProvider? cola;
        WasapiOut? salida;

        lock (_candado)
        {
            cola = _cola;
            salida = _salida;
        }

        try
        {
            cola?.ClearBuffer();
            salida?.Stop();
        }
        catch (Exception fallo)
        {
            _registro.LogDebug(fallo, "Algo falló al silenciar la salida de audio.");
        }

        Volatile.Write(ref _sonando, false);
        Avanzar();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_desechado)
        {
            return;
        }

        _desechado = true;
        await CerrarAsync(CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>Convierte las muestras del modem al formato del dispositivo.</summary>
    private byte[] PrepararBytes(ReadOnlySpan<float> muestras, Remuestreador remuestreador)
    {
        ReadOnlySpan<float> aEnviar;

        if (remuestreador.EsPasoDirecto)
        {
            aEnviar = muestras;
        }
        else
        {
            var convertidas = new float[remuestreador.MuestrasMaximas(muestras.Length)];
            remuestreador.Reiniciar();
            var escritas = remuestreador.Convertir(muestras, convertidas);
            aEnviar = convertidas.AsSpan(0, escritas);
        }

        var bytes = new byte[aEnviar.Length * _canales * (_bitsPorMuestra / 8)];
        ConversorDeMuestras.DesdeMono(aEnviar, _canales, _bitsPorMuestra, _esComaFlotante, bytes);
        return bytes;
    }

    /// <summary>Va metiendo bytes en la cola a medida que la tarjeta hace sitio.</summary>
    private async Task MeterEnLaColaAsync(BufferedWaveProvider cola, byte[] bytes, CancellationToken ct)
    {
        var puestos = 0;

        while (puestos < bytes.Length)
        {
            ct.ThrowIfCancellationRequested();

            var sitio = cola.BufferLength - cola.BufferedBytes;
            if (sitio <= 0)
            {
                await Task.Delay(10, ct).ConfigureAwait(false);
                continue;
            }

            var cuantos = Math.Min(sitio, bytes.Length - puestos);
            cola.AddSamples(bytes, puestos, cuantos);
            puestos += cuantos;
            Avanzar();
        }
    }

    /// <summary>
    /// Espera a que la tarjeta se coma lo que queda en la cola.
    /// </summary>
    /// <remarks>
    /// Aqui esta el peligro de verdad: si la tarjeta se queda parada con la cola llena, esta
    /// espera no termina nunca. No se pone un plazo maximo porque no se sabe cuanto dura lo que
    /// se esta emitiendo; lo que se hace es <b>dejar de decir que el audio avanza</b>, y eso es
    /// lo que hace que el vigilante del PTT baje el PTT por su cuenta.
    /// </remarks>
    private async Task EsperarAQueSuenTodoAsync(BufferedWaveProvider cola, CancellationToken ct)
    {
        var anterior = cola.BufferedBytes;

        while (cola.BufferedBytes > 0)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(20, ct).ConfigureAwait(false);

            var ahora = cola.BufferedBytes;
            if (ahora < anterior)
            {
                Avanzar();
            }

            anterior = ahora;
        }

        // Lo que ya salio de la cola todavia esta sonando en el colchon de Windows.
        await Task.Delay(_opciones.LatenciaDeSalidaMs, ct).ConfigureAwait(false);
        Avanzar();
    }

    /// <summary>Anota que el audio acaba de avanzar.</summary>
    private void Avanzar() => Volatile.Write(ref _ultimoAvance, _relojDelSistema().UtcTicks);

    /// <summary>Saca del formato de Windows lo que hace falta para escribir sus bytes.</summary>
    private (int Canales, int Bits, bool EsComaFlotante) Interpretar(WaveFormat formato)
    {
        var plano = formato;

        if (formato is WaveFormatExtensible extendido)
        {
            try
            {
                plano = extendido.ToStandardWaveFormat();
            }
            catch (Exception fallo)
            {
                _registro.LogDebug(fallo, "No se ha podido simplificar el formato del dispositivo.");
            }
        }

        var esComaFlotante = plano.Encoding == WaveFormatEncoding.IeeeFloat
            || (plano.Encoding == WaveFormatEncoding.Extensible && plano.BitsPerSample == 32);

        return (plano.Channels, plano.BitsPerSample, esComaFlotante);
    }
}
