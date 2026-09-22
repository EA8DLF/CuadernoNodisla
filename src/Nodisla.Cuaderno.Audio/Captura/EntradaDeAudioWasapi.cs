using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Audio.Dispositivos;

namespace Nodisla.Cuaderno.Audio.Captura;

/// <summary>
/// Captura de audio por WASAPI, que es por donde entra lo que oye la radio.
/// </summary>
/// <remarks>
/// <para>
/// El reparto de trabajo es el que manda: el hilo que Windows usa para entregar el audio
/// <b>solo convierte y copia</b> al colchon; los bloques, los avisos y todo lo que venga detras
/// —cascada, decodificacion— pasan en un hilo propio. Si el decodificador tarda, lo que se
/// llena es el colchon, no la tarjeta.
/// </para>
/// <para>
/// Y si aun asi se pierden muestras, se cuentan y se avisa. Un hueco de medio segundo en mitad
/// de un periodo de FT8 se lleva por delante las decodificaciones de esos quince segundos, y el
/// operador tiene derecho a saber por que su pantalla se quedo en blanco.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class EntradaDeAudioWasapi : IEntradaDeAudio
{
    private readonly IRelojDelModem _reloj;
    private readonly OpcionesDeAudio _opciones;
    private readonly ILogger _registro;
    private readonly object _candado = new();
    private readonly SemaphoreSlim _hayAudio = new(0, 1);

    private IReadOnlyList<DispositivoDeAudio> _dispositivos = Array.Empty<DispositivoDeAudio>();
    private WasapiCapture? _captura;
    private MMDevice? _dispositivo;
    private AmortiguadorCircular? _colchon;
    private EnsambladorDeBloques? _ensamblador;
    private Remuestreador? _remuestreador;
    private CancellationTokenSource? _paro;
    private Task _trabajador = Task.CompletedTask;

    private float[] _monoDelHiloDeAudio = Array.Empty<float>();
    private float[] _convertidoDelHiloDeAudio = Array.Empty<float>();
    private float[] _leidas = Array.Empty<float>();

    private int _canales;
    private int _bitsPorMuestra;
    private bool _esComaFlotante;
    private long _perdidasAvisadas;
    private double _nivel;
    private bool _desechado;

    /// <summary>Crea la entrada de audio.</summary>
    /// <param name="reloj">
    /// El reloj corregido del modem. No es un adorno: la hora de cada bloque sale de aqui, y si
    /// saliera de la del sistema, un ordenador desviado dos segundos no decodificaria nada.
    /// </param>
    /// <param name="opciones">Ajustes; si es nulo, los de partida.</param>
    /// <param name="registro">Donde se anota lo que pasa; si es nulo, no se anota nada.</param>
    /// <exception cref="ArgumentNullException">Si no se da reloj.</exception>
    public EntradaDeAudioWasapi(
        IRelojDelModem reloj,
        OpcionesDeAudio? opciones = null,
        ILogger? registro = null)
    {
        ArgumentNullException.ThrowIfNull(reloj);

        _reloj = reloj;
        _opciones = (opciones ?? new OpcionesDeAudio()).Copiar();
        _registro = registro ?? NullLogger.Instance;

        Refrescar();
    }

    /// <inheritdoc />
    public IReadOnlyList<DispositivoDeAudio> Dispositivos => _dispositivos;

    /// <inheritdoc />
    public DispositivoDeAudio? Abierto { get; private set; }

    /// <inheritdoc />
    public double Nivel => Volatile.Read(ref _nivel);

    /// <summary>Muestras perdidas desde que se abrio el dispositivo.</summary>
    public long MuestrasPerdidasEnTotal => _colchon?.MuestrasPerdidas ?? 0;

    /// <inheritdoc />
    public event EventHandler<BloqueDeAudio>? BloqueCapturado;

    /// <inheritdoc />
    public event EventHandler<HuecoDeAudio>? MuestrasPerdidas;

    /// <summary>Vuelve a mirar que dispositivos hay.</summary>
    /// <remarks>
    /// Hay que llamarlo cuando se enciende o se apaga la radio: el codec USB del FT-710
    /// aparece y desaparece con el equipo.
    /// </remarks>
    public void Refrescar() => _dispositivos = CatalogoDeDispositivos.Entradas(_registro);

    /// <inheritdoc />
    /// <exception cref="ArgumentException">Si no se encuentra el dispositivo.</exception>
    /// <exception cref="ObjectDisposedException">Si la entrada ya se ha desechado.</exception>
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

        var captura = new WasapiCapture(dispositivo, true, _opciones.LatenciaDeCapturaMs);
        var formato = captura.WaveFormat;
        var (canales, bits, esFlotante) = Interpretar(formato);

        lock (_candado)
        {
            _dispositivo = dispositivo;
            _captura = captura;
            _canales = canales;
            _bitsPorMuestra = bits;
            _esComaFlotante = esFlotante;

            _remuestreador = new Remuestreador(formato.SampleRate, frecuenciaDeMuestreo);
            _colchon = new AmortiguadorCircular(
                Math.Max(_opciones.MuestrasDeColchon, frecuenciaDeMuestreo * _opciones.SegundosDeColchon));
            _ensamblador = new EnsambladorDeBloques(
                frecuenciaDeMuestreo,
                Math.Max(1, frecuenciaDeMuestreo * _opciones.MilisegundosPorBloque / 1000),
                _reloj.Ahora);

            _leidas = new float[Math.Max(_ensamblador.MuestrasPorBloque * 4, 4096)];
            _perdidasAvisadas = 0;
            Volatile.Write(ref _nivel, 0);

            Abierto = _dispositivos.FirstOrDefault(
                candidato => string.Equals(candidato.Id, idDispositivo, StringComparison.OrdinalIgnoreCase))
                ?? new DispositivoDeAudio(idDispositivo, dispositivo.FriendlyName, EsDeEntrada: true, EsDelEquipo: false);
        }

        if (!_remuestreador!.EsPasoDirecto)
        {
            _registro.LogWarning(
                "El dispositivo «{Nombre}» está a {Suya} muestras por segundo y el módem trabaja a " +
                "{Nuestra}. Se convierte por interpolación, pero lo suyo es ponerlo en Windows a la " +
                "frecuencia del módem.",
                Abierto?.Nombre,
                formato.SampleRate,
                frecuenciaDeMuestreo);
        }

        _paro = new CancellationTokenSource();
        var testigo = _paro.Token;
        _trabajador = Task.Factory.StartNew(
            () => TrabajarAsync(testigo),
            testigo,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default).Unwrap();

        captura.DataAvailable += AlLlegarAudio;
        captura.RecordingStopped += AlPararLaGrabacion;
        captura.StartRecording();

        _registro.LogInformation(
            "Escuchando por «{Nombre}»: {Frecuencia} Hz, {Canales} canal(es), {Bits} bits.",
            Abierto?.Nombre,
            formato.SampleRate,
            canales,
            bits);
    }

    /// <inheritdoc />
    public Task CerrarAsync(CancellationToken ct = default)
    {
        WasapiCapture? captura;
        MMDevice? dispositivo;
        CancellationTokenSource? paro;
        Task trabajador;

        lock (_candado)
        {
            captura = _captura;
            dispositivo = _dispositivo;
            paro = _paro;
            trabajador = _trabajador;

            _captura = null;
            _dispositivo = null;
            _paro = null;
            _trabajador = Task.CompletedTask;
            Abierto = null;
        }

        if (captura is not null)
        {
            captura.DataAvailable -= AlLlegarAudio;
            captura.RecordingStopped -= AlPararLaGrabacion;

            try
            {
                captura.StopRecording();
            }
            catch (Exception fallo)
            {
                _registro.LogDebug(fallo, "Algo falló al parar la captura.");
            }

            captura.Dispose();
        }

        paro?.Cancel();

        // Se despierta al trabajador para que vea que hay que parar.
        DespertarAlTrabajador();

        try
        {
            if (!trabajador.IsCompleted)
            {
                trabajador.Wait(TimeSpan.FromSeconds(2));
            }
        }
        catch (Exception fallo)
        {
            _registro.LogDebug(fallo, "Algo falló al parar el hilo de trabajo del audio.");
        }

        paro?.Dispose();
        dispositivo?.Dispose();

        Volatile.Write(ref _nivel, 0);
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
        _hayAudio.Dispose();
    }

    /// <summary>
    /// Lo que corre en el hilo de audio de Windows: convertir y copiar, nada mas.
    /// </summary>
    private void AlLlegarAudio(object? remitente, WaveInEventArgs datos)
    {
        var colchon = _colchon;
        var remuestreador = _remuestreador;

        if (colchon is null || remuestreador is null || datos.BytesRecorded <= 0)
        {
            return;
        }

        try
        {
            var cuadros = ConversorDeMuestras.CuantasMuestras(datos.BytesRecorded, _canales, _bitsPorMuestra);
            if (cuadros <= 0)
            {
                return;
            }

            if (_monoDelHiloDeAudio.Length < cuadros)
            {
                _monoDelHiloDeAudio = new float[cuadros * 2];
            }

            var mono = _monoDelHiloDeAudio.AsSpan(0, cuadros);
            ConversorDeMuestras.AMono(
                datos.Buffer.AsSpan(0, datos.BytesRecorded),
                _canales,
                _bitsPorMuestra,
                _esComaFlotante,
                mono);

            if (remuestreador.EsPasoDirecto)
            {
                colchon.Escribir(mono);
            }
            else
            {
                var tope = remuestreador.MuestrasMaximas(cuadros);
                if (_convertidoDelHiloDeAudio.Length < tope)
                {
                    _convertidoDelHiloDeAudio = new float[tope * 2];
                }

                var escritas = remuestreador.Convertir(mono, _convertidoDelHiloDeAudio);
                colchon.Escribir(_convertidoDelHiloDeAudio.AsSpan(0, escritas));
            }

            DespertarAlTrabajador();
        }
        catch (Exception fallo)
        {
            // Aqui no se puede dejar salir nada: es el hilo de Windows.
            _registro.LogError(fallo, "Fallo al recoger el audio de la tarjeta.");
        }
    }

    /// <summary>Avisa al hilo de trabajo de que hay audio nuevo, sin bloquear al de audio.</summary>
    private void DespertarAlTrabajador()
    {
        try
        {
            if (_hayAudio.CurrentCount == 0)
            {
                _hayAudio.Release();
            }
        }
        catch (SemaphoreFullException)
        {
            // Ya estaba avisado.
        }
        catch (ObjectDisposedException)
        {
            // Se esta cerrando.
        }
    }

    private void AlPararLaGrabacion(object? remitente, StoppedEventArgs datos)
    {
        if (datos.Exception is { } fallo)
        {
            _registro.LogError(fallo, "La captura de audio se ha parado por un fallo.");
        }
    }

    /// <summary>El hilo que hace el trabajo: saca bloques del colchon y los reparte.</summary>
    private async Task TrabajarAsync(CancellationToken ct)
    {
        var bloques = new List<BloqueDeAudio>(8);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await _hayAudio.WaitAsync(TimeSpan.FromMilliseconds(250), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }

            var colchon = _colchon;
            var ensamblador = _ensamblador;
            if (colchon is null || ensamblador is null)
            {
                continue;
            }

            AvisarDeLoPerdido(colchon, ensamblador);

            int leidas;
            while ((leidas = colchon.Leer(_leidas)) > 0)
            {
                bloques.Clear();
                ensamblador.Anadir(_leidas.AsSpan(0, leidas), bloques);

                foreach (var bloque in bloques)
                {
                    ActualizarNivel(bloque.Muestras.Span);
                    Repartir(bloque);
                }

                if (ct.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }

    /// <summary>Cuenta lo que se ha perdido, corre la hora y avisa.</summary>
    private void AvisarDeLoPerdido(AmortiguadorCircular colchon, EnsambladorDeBloques ensamblador)
    {
        var perdidas = colchon.MuestrasPerdidas;
        var nuevas = perdidas - _perdidasAvisadas;
        if (nuevas <= 0)
        {
            return;
        }

        _perdidasAvisadas = perdidas;

        // El hueco empieza justo donde el lector iba a leer: el colchon tira lo mas viejo, asi
        // que las muestras que faltan son exactamente las siguientes que tocaban. Se anota el
        // instante antes de correr la hora, porque es el del comienzo del hueco.
        var comienzoDelHueco = ensamblador.InstanteDeLaSiguienteMuestra;

        // La hora se corre lo que se perdio: el periodo del hueco esta perdido de todas formas,
        // pero los siguientes tienen que seguir cayendo en su ventana.
        ensamblador.Saltar(nuevas);

        _registro.LogWarning(
            "Se han perdido {Muestras} muestras de audio a las {Instante:HH:mm:ss.fff}: esa ventana " +
            "puede no decodificar.",
            nuevas,
            comienzoDelHueco);

        try
        {
            MuestrasPerdidas?.Invoke(
                this,
                new HuecoDeAudio((int)Math.Min(nuevas, int.MaxValue), comienzoDelHueco));
        }
        catch (Exception fallo)
        {
            _registro.LogError(fallo, "Falló quien escuchaba el aviso de muestras perdidas.");
        }
    }

    private void Repartir(BloqueDeAudio bloque)
    {
        try
        {
            BloqueCapturado?.Invoke(this, bloque);
        }
        catch (Exception fallo)
        {
            // Que un oyente se rompa no puede parar la captura: seria perder el periodo entero.
            _registro.LogError(fallo, "Falló quien escuchaba los bloques de audio.");
        }
    }

    /// <summary>Nivel de entrada para el medidor: el pico del bloque, bajando poco a poco.</summary>
    private void ActualizarNivel(ReadOnlySpan<float> muestras)
    {
        var pico = 0f;
        foreach (var muestra in muestras)
        {
            var valor = Math.Abs(muestra);
            if (valor > pico)
            {
                pico = valor;
            }
        }

        var anterior = Volatile.Read(ref _nivel);
        var nuevo = pico >= anterior ? pico : (anterior * 0.8) + (pico * 0.2);
        Volatile.Write(ref _nivel, Math.Clamp(nuevo, 0.0, 1.0));
    }

    /// <summary>
    /// Saca del formato de Windows lo que hace falta para leer sus bytes.
    /// </summary>
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
