using System.Globalization;
using System.IO.Ports;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Nodisla.Cuaderno.Radio.Ptt;

namespace Nodisla.Cuaderno.Radio.Control.Icom;

/// <summary>
/// Canal CI-V por puerto serie (el USB de las radios ICOM es un puerto COM; tambien vale el
/// conector REMOTE con un CT-17).
/// </summary>
/// <remarks>
/// <para>
/// Hay un lector en segundo plano que mira el buzon del puerto y reparte lo que llega:
/// respuestas a quien pregunta, avisos del transceive al control, y el eco se tira. <b>No se
/// lanza nunca una lectura que no se pueda terminar</b> (lo aprendido con el FT-710, ver
/// <c>CanalSerieCat</c>): solo se lee lo que <c>BytesToRead</c> dice que hay.
/// </para>
/// <para>
/// <c>DTR</c> y <c>RTS</c> abren en falso: en las ICOM, por USB, el menu «USB SEND»/«USB Keying»
/// puede tener una de esas lineas como PTT o manipulacion, y abrir el puerto no puede poner la
/// radio en antena. Solo se levanta la que haga de PTT, y solo por el vigilante.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class CanalSerieCiv : CanalCiv
{
    private readonly string _puerto;
    private readonly int _baudios;
    private readonly ViaDePtt _viaDePtt;
    private readonly object _candadoSerie = new();

    private SerialPort? _serie;
    private CancellationTokenSource? _ctsLector;
    private Task? _lector;

    /// <summary>Crea el canal.</summary>
    /// <param name="puerto">Puerto, por ejemplo <c>COM5</c>.</param>
    /// <param name="baudios">Velocidad.</param>
    /// <param name="direccionDelEquipo">Direccion CI-V de la radio.</param>
    /// <param name="espera">Lo que se espera por respuesta.</param>
    /// <param name="registro">Donde anotar.</param>
    /// <param name="viaDePtt">Por donde va el PTT.</param>
    public CanalSerieCiv(
        string puerto,
        int baudios,
        byte direccionDelEquipo,
        TimeSpan? espera = null,
        ILogger? registro = null,
        ViaDePtt viaDePtt = ViaDePtt.Cat)
        : base(direccionDelEquipo, espera, registro)
    {
        _puerto = puerto;
        _baudios = baudios;
        _viaDePtt = viaDePtt;
    }

    /// <inheritdoc />
    public override bool Abierto => _serie is { IsOpen: true };

    /// <inheritdoc />
    public override string Descripcion =>
        string.Create(CultureInfo.InvariantCulture, $"{_puerto} a {_baudios} (CI-V {DireccionDelEquipo:X2})");

    /// <inheritdoc />
    public override bool PuedeAccionarLineas => _viaDePtt != ViaDePtt.Cat;

    /// <inheritdoc />
    public override Task PonerLineaDePttAsync(bool transmitir, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        PonerLineaDePttSincrono(transmitir);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override void PonerLineaDePttSincrono(bool transmitir)
    {
        if (_viaDePtt == ViaDePtt.Cat)
        {
            throw new NotSupportedException($"El canal {Descripcion} tiene el PTT por CI-V: no hay línea que accionar.");
        }

        var serie = _serie ?? throw new InvalidOperationException($"El canal {Descripcion} no está abierto.");
        if (_viaDePtt == ViaDePtt.Rts) serie.RtsEnable = transmitir;
        else serie.DtrEnable = transmitir;
    }

    /// <inheritdoc />
    protected override async Task AbrirNucleoAsync(CancellationToken ct)
    {
        await PararLectorAsync().ConfigureAwait(false);
        CerrarPuerto();

        var serie = new SerialPort(_puerto, _baudios, Parity.None, 8, StopBits.One)
        {
            DtrEnable = false,
            RtsEnable = false,
            Handshake = Handshake.None,
            ReadTimeout = (int)Espera.TotalMilliseconds,
            WriteTimeout = (int)Espera.TotalMilliseconds,
        };

        try
        {
            serie.Open();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
            or InvalidOperationException or FileNotFoundException)
        {
            serie.Dispose();
            throw new CanalNoDisponibleException(Descripcion, ex);
        }

        serie.DiscardInBuffer();
        serie.DiscardOutBuffer();
        lock (_candadoSerie)
        {
            _serie = serie;
        }

        _ctsLector = new CancellationTokenSource();
        var token = _ctsLector.Token;
        _lector = Task.Run(() => LeerSiempreAsync(serie, token), CancellationToken.None);
        Registro.LogInformation("Abierto el canal CI-V por {Canal}.", Descripcion);
    }

    /// <inheritdoc />
    protected override async Task EscribirNucleoAsync(byte[] bytes, CancellationToken ct)
    {
        var serie = _serie is { IsOpen: true } s ? s : throw new InvalidOperationException($"El canal {Descripcion} no está abierto.");
        await serie.BaseStream.WriteAsync(bytes.AsMemory(), ct).ConfigureAwait(false);
        await serie.BaseStream.FlushAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override void EscribirNucleoSincrono(byte[] bytes)
    {
        var serie = _serie ?? throw new InvalidOperationException($"El canal {Descripcion} no está abierto.");
        serie.Write(bytes, 0, bytes.Length);
    }

    /// <inheritdoc />
    protected override void CerrarNucleo()
    {
        _ctsLector?.Cancel();
        try
        {
            _lector?.Wait(TimeSpan.FromMilliseconds(250));
        }
        catch (AggregateException)
        {
            // El lector termina por cancelacion.
        }

        _ctsLector?.Dispose();
        _ctsLector = null;
        _lector = null;
        CerrarPuerto();
    }

    private async Task PararLectorAsync()
    {
        if (_ctsLector is null) return;
        await _ctsLector.CancelAsync().ConfigureAwait(false);
        if (_lector is not null)
        {
            try
            {
                await _lector.WaitAsync(TimeSpan.FromMilliseconds(250)).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or TimeoutException)
            {
                // Ya esta parado o se para solo.
            }
        }

        _ctsLector.Dispose();
        _ctsLector = null;
        _lector = null;
    }

    private async Task LeerSiempreAsync(SerialPort serie, CancellationToken ct)
    {
        var buzon = new byte[512];
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var esperando = serie.IsOpen ? serie.BytesToRead : 0;
                if (esperando <= 0)
                {
                    await Task.Delay(5, ct).ConfigureAwait(false);
                    continue;
                }

                var leidos = serie.Read(buzon, 0, Math.Min(buzon.Length, esperando));
                if (leidos > 0) AlRecibir(buzon.AsSpan(0, leidos));
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex) when (ex is TimeoutException or IOException or InvalidOperationException)
            {
                Registro.LogDebug(ex, "Lectura fallida en {Canal}.", Descripcion);
                await Task.Delay(20, CancellationToken.None).ConfigureAwait(false);
                if (!serie.IsOpen) return;
            }
        }
    }

    private void CerrarPuerto()
    {
        lock (_candadoSerie)
        {
            try
            {
                if (_serie is { IsOpen: true })
                {
                    // La linea de PTT se baja a mano antes de cerrar.
                    if (_viaDePtt == ViaDePtt.Rts) _serie.RtsEnable = false;
                    if (_viaDePtt == ViaDePtt.Dtr) _serie.DtrEnable = false;
                    _serie.Close();
                }

                _serie?.Dispose();
            }
            catch (Exception ex)
            {
                Registro.LogDebug(ex, "Fallo al cerrar {Canal}.", Descripcion);
            }
            finally
            {
                _serie = null;
            }
        }
    }
}
