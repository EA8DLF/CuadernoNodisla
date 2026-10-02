using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nodisla.Cuaderno.Idiomas;

namespace Nodisla.Cuaderno.Radio.Control.Ft710;

/// <summary>
/// Canal CAT por TCP.
/// </summary>
/// <remarks>
/// Sirve para dos cosas: hablar con un puente de serie a red —hay quien tiene el equipo en otra
/// habitacion— y, sobre todo, para las pruebas, que necesitan un FT-710 de mentira al que
/// preguntar sin que la radio del operador tenga que estar encendida.
/// </remarks>
public sealed class CanalTcpCat : ICanalCat
{
    private readonly string _maquina;
    private readonly int _puerto;
    private readonly TimeSpan _espera;
    private readonly ILogger _registro;
    private readonly SemaphoreSlim _puerta = new(1, 1);

    private TcpClient? _tcp;
    private NetworkStream? _flujo;

    /// <summary>Crea el canal.</summary>
    /// <param name="maquina">Maquina donde escucha el equipo o el puente.</param>
    /// <param name="puerto">Puerto TCP.</param>
    /// <param name="espera">Lo que se espera por cada respuesta.</param>
    /// <param name="registro">Donde anotar lo que pasa.</param>
    public CanalTcpCat(
        string maquina,
        int puerto,
        TimeSpan? espera = null,
        ILogger? registro = null)
    {
        _maquina = maquina;
        _puerto = puerto;
        _espera = espera ?? TimeSpan.FromMilliseconds(500);
        _registro = registro ?? NullLogger.Instance;
    }

    /// <inheritdoc />
    public bool Abierto => _tcp is { Connected: true };

    /// <inheritdoc />
    public string Descripcion => $"{_maquina}:{_puerto}";

    /// <inheritdoc />
    public async Task AbrirAsync(CancellationToken ct = default)
    {
        await _puerta.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            CerrarSinCandado();
            var tcp = new TcpClient { NoDelay = true };
            using (var espera = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                espera.CancelAfter(_espera);
                await tcp.ConnectAsync(_maquina, _puerto, espera.Token).ConfigureAwait(false);
            }

            _tcp = tcp;
            _flujo = tcp.GetStream();
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
            var flujo = Flujo();
            await flujo.WriteAsync(Encoding.ASCII.GetBytes(orden).AsMemory(), ct).ConfigureAwait(false);
            await flujo.FlushAsync(ct).ConfigureAwait(false);

            var recibido = new StringBuilder();
            var buzon = new byte[256];
            using var espera = CancellationTokenSource.CreateLinkedTokenSource(ct);
            espera.CancelAfter(_espera);

            try
            {
                while (true)
                {
                    var leidos = await flujo.ReadAsync(buzon.AsMemory(), espera.Token).ConfigureAwait(false);
                    if (leidos <= 0)
                    {
                        return null;
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
                        return null;
                    }
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return null;
            }
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
            var flujo = Flujo();
            await flujo.WriteAsync(Encoding.ASCII.GetBytes(orden).AsMemory(), ct).ConfigureAwait(false);
            await flujo.FlushAsync(ct).ConfigureAwait(false);
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

        // Sin pedir el semaforo: esto se usa para bajar el PTT cuando no hay tiempo que perder.
        var flujo = _flujo ?? throw new InvalidOperationException(Textos.F("Servicios.Radio.CanalCerrado", Descripcion));
        var bytes = Encoding.ASCII.GetBytes(orden);
        flujo.Write(bytes, 0, bytes.Length);
        flujo.Flush();
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

    private NetworkStream Flujo() => _flujo is not null && Abierto
        ? _flujo
        : throw new InvalidOperationException(Textos.F("Servicios.Radio.CanalCerrado", Descripcion));

    private void CerrarSinCandado()
    {
        try
        {
            _flujo?.Dispose();
            _tcp?.Close();
            _tcp?.Dispose();
        }
        catch (Exception ex)
        {
            _registro.LogDebug(ex, "Fallo al cerrar el canal {Canal}.", Descripcion);
        }
        finally
        {
            _flujo = null;
            _tcp = null;
        }
    }
}
