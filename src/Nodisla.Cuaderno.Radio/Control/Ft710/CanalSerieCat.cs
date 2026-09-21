using System.IO.Ports;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Nodisla.Cuaderno.Radio.Control.Ft710;

/// <summary>
/// Canal CAT por puerto serie.
/// </summary>
/// <remarks>
/// El FT-710 se presenta con un CP2105 doble: el puerto <i>Enhanced</i> a 115200 es el bueno y
/// el <i>Standard</i> a 4800 tambien contesta. Ninguno responde a otras velocidades, asi que la
/// velocidad no se negocia: se prueba. <c>DTR</c> y <c>RTS</c> van en cierto.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class CanalSerieCat : ICanalCat
{
    private readonly string _puerto;
    private readonly int _baudios;
    private readonly TimeSpan _espera;
    private readonly ILogger _registro;
    private readonly SemaphoreSlim _puerta = new(1, 1);

    private SerialPort? _serie;

    /// <summary>Crea el canal sobre un puerto serie.</summary>
    /// <param name="puerto">Nombre del puerto, por ejemplo <c>COM3</c>.</param>
    /// <param name="baudios">Velocidad; en el FT-710, 115200 o 4800.</param>
    /// <param name="espera">Lo que se espera por cada respuesta.</param>
    /// <param name="registro">Donde anotar el ir y venir.</param>
    public CanalSerieCat(
        string puerto,
        int baudios = 115200,
        TimeSpan? espera = null,
        ILogger? registro = null)
    {
        _puerto = puerto;
        _baudios = baudios;
        _espera = espera ?? TimeSpan.FromMilliseconds(350);
        _registro = registro ?? NullLogger.Instance;
    }

    /// <inheritdoc />
    public bool Abierto => _serie is { IsOpen: true };

    /// <inheritdoc />
    public string Descripcion => $"{_puerto} a {_baudios}";

    /// <inheritdoc />
    public async Task AbrirAsync(CancellationToken ct = default)
    {
        await _puerta.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            CerrarSinCandado();

            var serie = new SerialPort(_puerto, _baudios, Parity.None, 8, StopBits.One)
            {
                DtrEnable = true,
                RtsEnable = true,
                Handshake = Handshake.None,
                ReadTimeout = (int)_espera.TotalMilliseconds,
                WriteTimeout = (int)_espera.TotalMilliseconds,
                Encoding = Encoding.ASCII,
            };

            try
            {
                serie.Open();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                or InvalidOperationException or FileNotFoundException)
            {
                serie.Dispose();

                // El puerto no existe, esta cogido por otro programa o el cable no esta. No es
                // lo mismo que un equipo apagado, y la interfaz tiene que poder distinguirlo.
                throw new CanalNoDisponibleException(Descripcion, ex);
            }

            serie.DiscardInBuffer();
            serie.DiscardOutBuffer();
            _serie = serie;
            _registro.LogInformation("Abierto el canal CAT por {Canal}.", Descripcion);
        }
        finally
        {
            _puerta.Release();
        }
    }

    /// <inheritdoc />
    public async Task<string?> PreguntarAsync(string orden, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orden);
        OrdenesFt710.ComprobarQueEsSegura(orden);

        await _puerta.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var serie = Serie();
            serie.DiscardInBuffer();
            var bytes = Encoding.ASCII.GetBytes(orden);
            await serie.BaseStream.WriteAsync(bytes.AsMemory(), ct).ConfigureAwait(false);
            await serie.BaseStream.FlushAsync(ct).ConfigureAwait(false);

            return await LeerRespuestaAsync(serie, ct).ConfigureAwait(false);
        }
        finally
        {
            _puerta.Release();
        }
    }

    /// <inheritdoc />
    public async Task MandarAsync(string orden, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orden);
        OrdenesFt710.ComprobarQueEsSegura(orden);

        await _puerta.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var serie = Serie();
            var bytes = Encoding.ASCII.GetBytes(orden);
            await serie.BaseStream.WriteAsync(bytes.AsMemory(), ct).ConfigureAwait(false);
            await serie.BaseStream.FlushAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _puerta.Release();
        }
    }

    /// <inheritdoc />
    public void MandarSincrono(string orden)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orden);

        // Aqui no se pide el semaforo a proposito: esto se llama para bajar el PTT cuando todo
        // lo demas ha fallado o el proceso se esta muriendo, y esperar a un candado tomado por
        // una orden colgada seria justo lo contrario de lo que hace falta.
        var serie = _serie ?? throw new InvalidOperationException($"El canal {Descripcion} no está abierto.");
        var bytes = Encoding.ASCII.GetBytes(orden);
        serie.Write(bytes, 0, bytes.Length);
    }

    /// <inheritdoc />
    public void Cerrar()
    {
        if (!_puerta.Wait(TimeSpan.FromMilliseconds(250)))
        {
            CerrarSinCandado();
            return;
        }

        try
        {
            CerrarSinCandado();
        }
        finally
        {
            _puerta.Release();
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        Cerrar();
        _puerta.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task<string?> LeerRespuestaAsync(SerialPort serie, CancellationToken ct)
    {
        var recibido = new StringBuilder();
        var buzon = new byte[256];
        using var espera = CancellationTokenSource.CreateLinkedTokenSource(ct);
        espera.CancelAfter(_espera);

        try
        {
            while (true)
            {
                var leidos = await serie.BaseStream.ReadAsync(buzon.AsMemory(), espera.Token).ConfigureAwait(false);
                if (leidos <= 0)
                {
                    continue;
                }

                recibido.Append(Encoding.ASCII.GetString(buzon, 0, leidos));
                var texto = recibido.ToString();
                var fin = texto.IndexOf(Cat.Fin);
                if (fin >= 0)
                {
                    return texto[..fin];
                }

                if (recibido.Length > 4096)
                {
                    // Alguien esta escupiendo texto que no es CAT: en esta maquina, por ejemplo,
                    // COM8 es un Meshtastic que suelta su registro de depuracion.
                    return null;
                }
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Sin respuesta a tiempo: puede ser una orden de accion, que no contesta.
            return null;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException)
        {
            return null;
        }
    }

    private SerialPort Serie() => _serie is { IsOpen: true }
        ? _serie
        : throw new InvalidOperationException($"El canal {Descripcion} no está abierto.");

    private void CerrarSinCandado()
    {
        try
        {
            if (_serie is { IsOpen: true })
            {
                _serie.Close();
            }

            _serie?.Dispose();
        }
        catch (Exception ex)
        {
            _registro.LogDebug(ex, "Fallo al cerrar {Canal}.", Descripcion);
        }
        finally
        {
            _serie = null;
        }
    }
}
