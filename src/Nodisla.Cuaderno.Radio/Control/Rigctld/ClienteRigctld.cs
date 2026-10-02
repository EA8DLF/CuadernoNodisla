using System.Globalization;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nodisla.Cuaderno.Idiomas;

namespace Nodisla.Cuaderno.Radio.Control.Rigctld;

/// <summary>
/// Habla el protocolo de texto de <c>rigctld</c> por TCP.
/// </summary>
/// <remarks>
/// Todas las ordenes van en modo extendido (prefijo <c>+</c>) porque asi la respuesta termina
/// siempre en <c>RPRT n</c> y no hay que adivinar cuantas lineas trae. Las ordenes se serializan
/// con un semaforo: <c>rigctld</c> atiende una peticion cada vez por conexion.
/// </remarks>
internal sealed class ClienteRigctld : IAsyncDisposable
{
    private readonly string _maquina;
    private readonly int _puerto;
    private readonly TimeSpan _espera;
    private readonly ILogger _registro;
    private readonly SemaphoreSlim _puerta = new(1, 1);

    private TcpClient? _tcp;
    private StreamReader? _lector;
    private StreamWriter? _escritor;

    /// <summary>Crea el cliente apuntando a una maquina y puerto.</summary>
    /// <param name="maquina">Maquina donde escucha el demonio.</param>
    /// <param name="puerto">Puerto TCP.</param>
    /// <param name="espera">Lo que se espera por cada orden.</param>
    /// <param name="registro">Donde anotar el ir y venir.</param>
    internal ClienteRigctld(string maquina, int puerto, TimeSpan espera, ILogger registro)
    {
        _maquina = maquina;
        _puerto = puerto;
        _espera = espera;
        _registro = registro;
    }

    /// <summary>Hay socket abierto.</summary>
    internal bool Conectado => _tcp is { Connected: true };

    /// <summary>Abre la conexion, cerrando antes la anterior si la hubiera.</summary>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <returns>La tarea de la conexion.</returns>
    internal async Task ConectarAsync(CancellationToken ct)
    {
        await _puerta.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            CerrarSinCandado();

            var tcp = new TcpClient();
            tcp.NoDelay = true;
            using (var espera = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                espera.CancelAfter(_espera);
                await tcp.ConnectAsync(_maquina, _puerto, espera.Token).ConfigureAwait(false);
            }

            var flujo = tcp.GetStream();
            _tcp = tcp;
            _lector = new StreamReader(flujo, Encoding.ASCII, false, 1024, true);
            _escritor = new StreamWriter(flujo, Encoding.ASCII, 1024, true) { AutoFlush = false, NewLine = "\n" };
            _registro.LogInformation("Conectado a rigctld en {Maquina}:{Puerto}.", _maquina, _puerto);
        }
        finally
        {
            _puerta.Release();
        }
    }

    /// <summary>
    /// Manda una orden y espera la respuesta.
    /// </summary>
    /// <param name="orden">Orden sin el prefijo de modo extendido, por ejemplo <c>\get_freq</c>.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <returns>La respuesta del demonio.</returns>
    /// <exception cref="InvalidOperationException">Si no hay conexion abierta.</exception>
    internal async Task<RespuestaRigctld> OrdenAsync(string orden, CancellationToken ct)
    {
        await _puerta.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await OrdenSinCandadoAsync(orden, ct).ConfigureAwait(false);
        }
        finally
        {
            _puerta.Release();
        }
    }

    /// <summary>
    /// Manda una orden solo si el canal esta libre. Lo usa el sondeo, que jamas debe hacer cola
    /// detras de las ordenes del operador ni inundar el puerto serie.
    /// </summary>
    /// <param name="orden">Orden a mandar.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <returns>La respuesta, o nulo si el canal estaba ocupado.</returns>
    internal async Task<RespuestaRigctld?> OrdenSiEstaLibreAsync(string orden, CancellationToken ct)
    {
        if (!await _puerta.WaitAsync(TimeSpan.Zero, ct).ConfigureAwait(false))
        {
            return null;
        }

        try
        {
            return await OrdenSinCandadoAsync(orden, ct).ConfigureAwait(false);
        }
        finally
        {
            _puerta.Release();
        }
    }

    /// <summary>Cierra la conexion.</summary>
    internal void Cerrar()
    {
        _puerta.Wait(TimeSpan.FromMilliseconds(250));
        try
        {
            CerrarSinCandado();
        }
        finally
        {
            try
            {
                _puerta.Release();
            }
            catch (SemaphoreFullException)
            {
                // El semaforo no llego a tomarse; no pasa nada.
            }
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        Cerrar();
        _puerta.Dispose();
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Baja el PTT abriendo un socket nuevo y bloqueando, sin tocar la conexion de trabajo.
    /// </summary>
    /// <remarks>
    /// Es la via de emergencia: vale cuando la conexion normal esta colgada y tambien durante
    /// el cierre del proceso, donde no se puede confiar en el planificador de tareas.
    /// </remarks>
    /// <param name="maquina">Maquina del demonio.</param>
    /// <param name="puerto">Puerto del demonio.</param>
    /// <param name="milisegundos">Tope de tiempo para todo el intento.</param>
    /// <exception cref="InvalidOperationException">Si el demonio no confirma la bajada.</exception>
    internal static void SoltarPttDeGolpe(string maquina, int puerto, int milisegundos)
    {
        using var tcp = new TcpClient
        {
            SendTimeout = milisegundos,
            ReceiveTimeout = milisegundos,
            NoDelay = true,
        };

        if (!tcp.ConnectAsync(maquina, puerto).Wait(milisegundos))
        {
            throw new InvalidOperationException($"No se pudo abrir un socket nuevo con rigctld en {maquina}:{puerto}.");
        }

        var flujo = tcp.GetStream();
        var orden = Encoding.ASCII.GetBytes("+\\set_ptt 0\n");
        flujo.Write(orden, 0, orden.Length);
        flujo.Flush();

        // La respuesta llega en varios trozos: el eco de la orden y luego el RPRT. Hay que leer
        // hasta el RPRT, no quedarse con lo primero que aparezca.
        var buzon = new byte[256];
        var texto = new StringBuilder();
        var limite = Environment.TickCount64 + milisegundos;
        while (Environment.TickCount64 < limite)
        {
            int leidos;
            try
            {
                leidos = flujo.Read(buzon, 0, buzon.Length);
            }
            catch (Exception ex) when (ex is IOException or SocketException)
            {
                break;
            }

            if (leidos <= 0)
            {
                break;
            }

            texto.Append(Encoding.ASCII.GetString(buzon, 0, leidos));
            if (texto.ToString().Contains("RPRT ", StringComparison.Ordinal))
            {
                break;
            }
        }

        if (!texto.ToString().Contains("RPRT 0", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"rigctld no confirmó la bajada del PTT por socket nuevo. Contestó: {texto.ToString().Trim()}");
        }
    }

    private async Task<RespuestaRigctld> OrdenSinCandadoAsync(string orden, CancellationToken ct)
    {
        var escritor = _escritor;
        var lector = _lector;
        if (escritor is null || lector is null || !Conectado)
        {
            throw new InvalidOperationException(Textos.T("Servicios.Radio.RigctldSinConexion"));
        }

        using var espera = CancellationTokenSource.CreateLinkedTokenSource(ct);
        espera.CancelAfter(_espera);

        await escritor.WriteAsync($"+{orden}\n".AsMemory(), espera.Token).ConfigureAwait(false);
        await escritor.FlushAsync(espera.Token).ConfigureAwait(false);

        var lineas = new List<string>();
        var primera = true;
        for (var cuenta = 0; cuenta < 256; cuenta++)
        {
            var linea = await lector.ReadLineAsync(espera.Token).ConfigureAwait(false)
                ?? throw new IOException(Textos.T("Servicios.Radio.RigctldCerro"));

            if (linea.StartsWith("RPRT ", StringComparison.Ordinal))
            {
                var codigo = int.TryParse(
                    linea.AsSpan(5).Trim(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var valor)
                    ? valor
                    : -1;
                return new RespuestaRigctld(codigo, lineas);
            }

            if (primera)
            {
                // La primera linea es el eco de la orden en modo extendido.
                primera = false;
                continue;
            }

            lineas.Add(linea);
        }

        throw new IOException(Textos.T("Servicios.Radio.RigctldDemasiadasLineas"));
    }

    private void CerrarSinCandado()
    {
        try
        {
            _escritor?.Dispose();
            _lector?.Dispose();
            _tcp?.Close();
            _tcp?.Dispose();
        }
        catch (Exception ex)
        {
            _registro.LogDebug(ex, "Fallo al cerrar la conexión con rigctld.");
        }
        finally
        {
            _escritor = null;
            _lector = null;
            _tcp = null;
        }
    }
}
