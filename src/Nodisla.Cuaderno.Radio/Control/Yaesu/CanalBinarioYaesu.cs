using System.IO.Ports;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nodisla.Cuaderno.Radio.Ptt;

namespace Nodisla.Cuaderno.Radio.Control.Yaesu;

/// <summary>
/// Canal del CAT antiguo de Yaesu: se mandan bloques de 5 bytes y el equipo contesta, cuando
/// contesta, con un numero fijo de bytes (sin terminador).
/// </summary>
public interface ICanalBinario : IAsyncDisposable
{
    /// <summary>El canal esta abierto.</summary>
    bool Abierto { get; }

    /// <summary>Para los mensajes (<c>COM4 a 9600</c>).</summary>
    string Descripcion { get; }

    /// <summary>Abre el canal.</summary>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <returns>La tarea.</returns>
    Task AbrirAsync(CancellationToken ct = default);

    /// <summary>Manda un bloque y lee <paramref name="bytes"/> bytes de respuesta.</summary>
    /// <param name="bloque">Los 5 bytes.</param>
    /// <param name="bytes">Cuantos bytes contesta el equipo.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <returns>La respuesta, o nulo si no llega entera a tiempo.</returns>
    Task<byte[]?> PreguntarAsync(byte[] bloque, int bytes, CancellationToken ct = default);

    /// <summary>Manda un bloque sin esperar respuesta.</summary>
    /// <param name="bloque">Los 5 bytes.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <returns>La tarea.</returns>
    Task MandarAsync(byte[] bloque, CancellationToken ct = default);

    /// <summary>Manda un bloque sin hilos ni esperas (para soltar el PTT al cerrar el proceso).</summary>
    /// <param name="bloque">Los 5 bytes.</param>
    void MandarSincrono(byte[] bloque);

    /// <summary>Cierra.</summary>
    void Cerrar();

    /// <summary>Puede subir el PTT con RTS o DTR.</summary>
    bool PuedeAccionarLineas => false;

    /// <summary>Sube o baja la linea del PTT.</summary>
    /// <param name="transmitir">Subir.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <returns>La tarea.</returns>
    Task PonerLineaDePttAsync(bool transmitir, CancellationToken ct = default) =>
        Task.FromException(new NotSupportedException($"El canal {Descripcion} no tiene líneas de control que accionar."));

    /// <summary>Baja la linea del PTT sin esperas.</summary>
    /// <param name="transmitir">Subir.</param>
    void PonerLineaDePttSincrono(bool transmitir) =>
        throw new NotSupportedException($"El canal {Descripcion} no tiene líneas de control que accionar.");
}

/// <summary>El puerto serie del CAT antiguo: 8 bits, sin paridad, 2 bits de parada.</summary>
public sealed class CanalSerieBinario : ICanalBinario
{
    private readonly string _puerto;
    private readonly int _baudios;
    private readonly TimeSpan _espera;
    private readonly ViaDePtt _viaDePtt;
    private readonly ILogger _registro;
    private readonly SemaphoreSlim _turno = new(1, 1);
    private SerialPort? _serie;

    /// <summary>Crea el canal sin abrirlo.</summary>
    /// <param name="puerto">Puerto (<c>COM4</c>).</param>
    /// <param name="baudios">4800, 9600 o 38400 (menu CAT RATE del equipo).</param>
    /// <param name="espera">Lo que se espera a la respuesta.</param>
    /// <param name="registro">Donde anotar.</param>
    /// <param name="viaDePtt">Por donde va el PTT.</param>
    public CanalSerieBinario(string puerto, int baudios, TimeSpan espera, ILogger? registro = null, ViaDePtt viaDePtt = ViaDePtt.Cat)
    {
        _puerto = puerto;
        _baudios = baudios;
        _espera = espera;
        _viaDePtt = viaDePtt;
        _registro = registro ?? NullLogger.Instance;
    }

    /// <inheritdoc />
    public bool Abierto => _serie is { IsOpen: true };

    /// <inheritdoc />
    public string Descripcion => $"{_puerto} a {_baudios}";

    /// <inheritdoc />
    public bool PuedeAccionarLineas => _viaDePtt != ViaDePtt.Cat;

    /// <inheritdoc />
    public Task AbrirAsync(CancellationToken ct = default)
    {
        Cerrar();
        try
        {
            var serie = new SerialPort(_puerto, _baudios, Parity.None, 8, StopBits.Two)
            {
                Handshake = Handshake.None,
                ReadTimeout = (int)_espera.TotalMilliseconds,
                WriteTimeout = 1000,
                DtrEnable = false,
                RtsEnable = false,
            };
            serie.Open();
            _serie = serie;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            throw new CanalNoDisponibleException(Descripcion, ex);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<byte[]?> PreguntarAsync(byte[] bloque, int bytes, CancellationToken ct = default)
    {
        await _turno.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var serie = _serie ?? throw new CanalNoDisponibleException(Descripcion);
            serie.DiscardInBuffer();
            serie.Write(bloque, 0, bloque.Length);
            var respuesta = new byte[bytes];
            var leidos = 0;
            var limite = DateTime.UtcNow + _espera;
            while (leidos < bytes && DateTime.UtcNow < limite)
            {
                ct.ThrowIfCancellationRequested();
                if (serie.BytesToRead == 0)
                {
                    await Task.Delay(5, ct).ConfigureAwait(false);
                    continue;
                }

                leidos += serie.Read(respuesta, leidos, bytes - leidos);
            }

            return leidos == bytes ? respuesta : null;
        }
        finally
        {
            _turno.Release();
        }
    }

    /// <inheritdoc />
    public async Task MandarAsync(byte[] bloque, CancellationToken ct = default)
    {
        await _turno.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var serie = _serie ?? throw new CanalNoDisponibleException(Descripcion);
            serie.Write(bloque, 0, bloque.Length);
        }
        finally
        {
            _turno.Release();
        }
    }

    /// <inheritdoc />
    public void MandarSincrono(byte[] bloque)
    {
        var serie = _serie ?? throw new CanalNoDisponibleException(Descripcion);
        serie.Write(bloque, 0, bloque.Length);
    }

    /// <inheritdoc />
    public Task PonerLineaDePttAsync(bool transmitir, CancellationToken ct = default)
    {
        PonerLineaDePttSincrono(transmitir);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void PonerLineaDePttSincrono(bool transmitir)
    {
        var serie = _serie ?? throw new CanalNoDisponibleException(Descripcion);
        if (_viaDePtt == ViaDePtt.Rts) serie.RtsEnable = transmitir;
        else if (_viaDePtt == ViaDePtt.Dtr) serie.DtrEnable = transmitir;
    }

    /// <inheritdoc />
    public void Cerrar()
    {
        var serie = _serie;
        _serie = null;
        if (serie is null) return;
        try
        {
            serie.Close();
        }
        catch (Exception ex)
        {
            _registro.LogDebug(ex, "Fallo al cerrar {Puerto}.", _puerto);
        }

        serie.Dispose();
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        Cerrar();
        _turno.Dispose();
        return ValueTask.CompletedTask;
    }
}
