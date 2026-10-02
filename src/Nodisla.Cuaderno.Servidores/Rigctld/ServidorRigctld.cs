using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Nodisla.Cuaderno.Servidores.Rigctld;

/// <summary>
/// Servidor TCP compatible con rigctld (protocolo de red de Hamlib): lo que habla WSJT-X, JTDX,
/// fldigi o GridTracker con la radio «Hamlib NET rigctl».
/// </summary>
/// <remarks>
/// Varios clientes a la vez, cada uno con su sesion. Solo entran las IP que admite
/// <see cref="ControlDeAcceso"/>; las demas se cierran nada mas conectar. Lineas de mas de
/// 1 KB se consideran basura y cortan la conexion.
/// </remarks>
public sealed class ServidorRigctld : IAsyncDisposable
{
    private const int LineaMaxima = 1024;

    private readonly RadioCompartida _radio;
    private readonly ILogger _registro;
    private readonly CancellationTokenSource _cierre = new();
    private readonly List<Task> _sesiones = [];
    private TcpListener? _oyente;
    private Task? _aceptando;
    private ControlDeAcceso _acceso;

    /// <summary>Crea el servidor, sin arrancarlo.</summary>
    /// <param name="radio">La radio compartida.</param>
    /// <param name="acceso">Quien entra.</param>
    /// <param name="registro">Registro.</param>
    public ServidorRigctld(RadioCompartida radio, ControlDeAcceso acceso, ILogger? registro = null)
    {
        _radio = radio ?? throw new ArgumentNullException(nameof(radio));
        _acceso = acceso ?? throw new ArgumentNullException(nameof(acceso));
        _registro = registro ?? NullLogger.Instance;
    }

    /// <summary>Donde escucha, una vez arrancado.</summary>
    public IPEndPoint? Escuchando => _oyente?.LocalEndpoint as IPEndPoint;

    /// <summary>Cambia la lista de IP sin reiniciar (la direccion de escucha no cambia).</summary>
    /// <param name="acceso">El nuevo control.</param>
    public void CambiarAcceso(ControlDeAcceso acceso) => Volatile.Write(ref _acceso, acceso);

    /// <summary>Arranca en un puerto (0: uno libre, para las pruebas).</summary>
    /// <param name="puerto">El puerto.</param>
    public void Arrancar(int puerto)
    {
        if (_oyente is not null) return;
        var oyente = new TcpListener(_acceso.DireccionDeEscucha, puerto);
        oyente.Server.ExclusiveAddressUse = true;
        oyente.Start(16);
        _oyente = oyente;
        _registro.LogInformation("Servidor rigctld escuchando en {Punto}.", oyente.LocalEndpoint);
        _aceptando = Task.Run(() => AceptarAsync(oyente, _cierre.Token));
    }

    private async Task AceptarAsync(TcpListener oyente, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient conexion;
            try
            {
                conexion = await oyente.AcceptTcpClientAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                return;
            }

            var remoto = conexion.Client.RemoteEndPoint as IPEndPoint;
            if (remoto is null || !Volatile.Read(ref _acceso).Admite(remoto.Address))
            {
                _registro.LogWarning("Conexion rigctld rechazada desde {Remoto}: no esta en la lista de IP permitidas.", remoto);
                conexion.Dispose();
                continue;
            }

            var sesion = Task.Run(() => AtenderAsync(conexion, remoto, ct), CancellationToken.None);
            lock (_sesiones)
            {
                _sesiones.RemoveAll(t => t.IsCompleted);
                _sesiones.Add(sesion);
            }
        }
    }

    private async Task AtenderAsync(TcpClient conexion, IPEndPoint remoto, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        using (conexion)
        {
            conexion.NoDelay = true;
            var cliente = _radio.Alta(ProtocoloExterno.Rigctld, remoto, () =>
            {
                cts.Cancel();
                conexion.Close();
                return Task.CompletedTask;
            });
            if (cliente is null)
            {
                _registro.LogWarning("Conexion rigctld rechazada desde {Remoto}: demasiados clientes.", remoto);
                return;
            }

            try
            {
                var flujo = conexion.GetStream();
                var sesion = new SesionRigctld(_radio, cliente, _registro);
                var linea = new StringBuilder();
                var bufer = new byte[512];
                while (!cts.IsCancellationRequested)
                {
                    var leidos = await flujo.ReadAsync(bufer, cts.Token).ConfigureAwait(false);
                    if (leidos == 0) break;
                    for (var i = 0; i < leidos; i++)
                    {
                        var c = (char)bufer[i];
                        if (c != '\n')
                        {
                            if (c != '\0') linea.Append(c);
                            if (linea.Length > LineaMaxima) throw new InvalidDataException("Linea demasiado larga.");
                            continue;
                        }

                        var respuesta = await sesion.AtenderAsync(linea.ToString(), cts.Token).ConfigureAwait(false);
                        linea.Clear();
                        if (respuesta is null) return;
                        if (respuesta.Length > 0)
                        {
                            var bytes = Encoding.ASCII.GetBytes(respuesta);
                            await flujo.WriteAsync(bytes, cts.Token).ConfigureAwait(false);
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException or SocketException or ObjectDisposedException or InvalidDataException)
            {
                _registro.LogDebug(ex, "Sesion rigctld de {Remoto} terminada.", remoto);
            }
            finally
            {
                // Si se va con el PTT arriba, se baja en el acto.
                await _radio.BajaAsync(cliente).ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_cierre.IsCancellationRequested) return;
        await _cierre.CancelAsync().ConfigureAwait(false);
        _oyente?.Stop();
        if (_aceptando is not null)
        {
            await _aceptando.ConfigureAwait(false);
        }

        Task[] pendientes;
        lock (_sesiones) pendientes = [.. _sesiones];
        try
        {
            await Task.WhenAll(pendientes).WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            _registro.LogWarning("Alguna sesion rigctld no ha terminado a tiempo al apagar el servidor.");
        }

    }
}
