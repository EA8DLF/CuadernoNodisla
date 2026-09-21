using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Integraciones.Digital;

/// <summary>
/// Puente UDP con WSJT-X, JTDX, MSHV y JS8Call.
/// </summary>
/// <remarks>
/// Los cuatro hablan el mismo protocolo sobre el mismo puerto, asi que todos caen en el mismo
/// socket y se distinguen por el identificador que cada uno pone en sus mensajes. El socket
/// admite tambien multicast, porque WSJT-X puede emitir asi cuando hay varios programas
/// escuchando en la misma red.
///
/// Nada de lo que llegue puede tirar el bucle de recepcion: ni basura de otro programa que
/// comparta el puerto, ni un tipo de mensaje que todavia no existia, ni un suscriptor que
/// lanza. La unica forma de salir del bucle es que lo paren.
/// </remarks>
public sealed class PuenteDigitalUdp : IPuenteDigital
{
    private readonly OpcionesPuenteDigital _opciones;
    private readonly ILogger _registro;
    private readonly ReceptorWsjt _receptor;
    private readonly SemaphoreSlim _cerrojo = new(1, 1);

    private Socket? _socket;
    private CancellationTokenSource? _parada;
    private Task? _bucle;
    private Task? _vigilancia;

    /// <summary>Crea el puente.</summary>
    /// <param name="opciones">Puerto, multicast y caducidad de las instancias.</param>
    /// <param name="registro">Donde dejar constancia de lo que pasa. Nulo para no registrar nada.</param>
    public PuenteDigitalUdp(OpcionesPuenteDigital? opciones = null, ILogger? registro = null)
    {
        _opciones = opciones ?? new OpcionesPuenteDigital();
        _registro = registro ?? NullLogger.Instance;
        _receptor = new ReceptorWsjt(_registro);
        _receptor.Decodificado += (_, d) => Decodificado?.Invoke(this, d);
        _receptor.EstadoRecibido += (_, e) => EstadoRecibido?.Invoke(this, e);
        _receptor.QsoRegistrado += (_, q) => QsoRegistrado?.Invoke(this, q);
        _receptor.AdifRecibido += (_, a) => AdifRecibido?.Invoke(this, a);
        _receptor.InstanciaPerdida += (_, i) => InstanciaPerdida?.Invoke(this, i);
    }

    /// <inheritdoc/>
    public int Puerto => _opciones.Puerto;

    /// <inheritdoc/>
    public IReadOnlyCollection<EstadoDigital> Instancias =>
        _receptor.Instancias.Select(i => i.Estado).ToArray();

    /// <summary>Instancias con todo su detalle: dialecto, version y de donde vienen.</summary>
    public IReadOnlyCollection<InstanciaDigital> Detalle => _receptor.Instancias;

    /// <inheritdoc/>
    public event EventHandler<DecodificacionDigital>? Decodificado;

    /// <inheritdoc/>
    public event EventHandler<EstadoDigital>? EstadoRecibido;

    /// <inheritdoc/>
    public event EventHandler<Qso>? QsoRegistrado;

    /// <inheritdoc/>
    public event EventHandler<string>? InstanciaPerdida;

    /// <inheritdoc/>
    public event EventHandler<string>? AdifRecibido;

    /// <inheritdoc/>
    public CapacidadesDigitales Capacidades(string identificador) =>
        _receptor.TryInstancia(identificador, out var instancia)
            ? instancia.Capacidades
            : CapacidadesDigitales.Desconocidas;

    /// <summary>Dialecto atribuido a una instancia.</summary>
    /// <param name="identificador">Nombre de la instancia.</param>
    public DialectoDigital DialectoDe(string identificador) =>
        _receptor.TryInstancia(identificador, out var instancia)
            ? instancia.Dialecto
            : DialectoDigital.Desconocido;

    /// <inheritdoc/>
    public async Task ArrancarAsync(CancellationToken ct = default)
    {
        await _cerrojo.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_socket is not null) return;

            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            try
            {
                socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                EvitarCorteDeConexionEnWindows(socket);
                socket.Bind(new IPEndPoint(DireccionDeEscucha(), _opciones.Puerto));
                UnirseAlGrupoMulticast(socket);
            }
            catch
            {
                socket.Dispose();
                throw;
            }

            _socket = socket;
            _parada = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _bucle = Task.Run(() => EscucharAsync(socket, _parada.Token), CancellationToken.None);
            _vigilancia = Task.Run(() => VigilarAsync(_parada.Token), CancellationToken.None);
            Trazar($"Escuchando modos digitales en el puerto {_opciones.Puerto}.");
        }
        finally
        {
            _cerrojo.Release();
        }
    }

    /// <inheritdoc/>
    public async Task PararAsync(CancellationToken ct = default)
    {
        await _cerrojo.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_socket is null) return;

            if (_parada is not null) await _parada.CancelAsync().ConfigureAwait(false);
            _socket.Dispose();

            foreach (var tarea in new[] { _bucle, _vigilancia })
            {
                if (tarea is null) continue;
                try
                {
                    await tarea.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Es la forma normal de terminar.
                }
            }

            _parada?.Dispose();
            _socket = null;
            _parada = null;
            _bucle = null;
            _vigilancia = null;
            Trazar("Puente de modos digitales parado.");
        }
        finally
        {
            _cerrojo.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<bool> ResponderAAsync(
        DecodificacionDigital decodificacion, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(decodificacion);

        if (!_receptor.TryInstancia(decodificacion.Identificador, out var instancia)) return false;
        if (!instancia.Capacidades.PuedeResponder) return false;

        var datagrama = ConstructorDeMensajesWsjt.Respuesta(decodificacion, instancia.Dialecto);
        return await EnviarAsync(instancia, datagrama, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<bool> ResaltarAsync(
        string identificador, Indicativo indicativo, bool esNuevo, CancellationToken ct = default)
    {
        if (indicativo.EsVacio) return false;
        if (!_receptor.TryInstancia(identificador, out var instancia)) return false;
        if (!instancia.Capacidades.PuedeResaltar)
        {
            Trazar($"«{identificador}» habla {DeteccionDeDialecto.Nombre(instancia.Dialecto)}, "
                + "que no tiene resaltado de indicativos.");
            return false;
        }

        var datagrama = ConstructorDeMensajesWsjt.Resaltar(identificador, indicativo.Valor, esNuevo);
        return await EnviarAsync(instancia, datagrama, ct).ConfigureAwait(false);
    }

    /// <summary>Fija el desplazamiento de transmision. Solo lo atiende JTDX.</summary>
    /// <param name="identificador">Instancia a la que se le pide.</param>
    /// <param name="hercios">Desplazamiento dentro del ancho de banda de audio.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    public async Task<bool> FijarTxDeltaFreqAsync(
        string identificador, uint hercios, CancellationToken ct = default)
    {
        if (!_receptor.TryInstancia(identificador, out var instancia)) return false;
        if (!instancia.Capacidades.PuedeCambiarTonoTx) return false;
        var datagrama = ConstructorDeMensajesWsjt.FijarTxDeltaFreq(identificador, hercios);
        return await EnviarAsync(instancia, datagrama, ct).ConfigureAwait(false);
    }

    /// <summary>Fija la llamada general y opcionalmente la dispara. Solo lo atiende JTDX.</summary>
    /// <param name="identificador">Instancia a la que se le pide.</param>
    /// <param name="direccion">Direccion de la llamada: <c>DX</c>, <c>EU</c>…</param>
    /// <param name="periodoTx">Periodo en el que transmitir.</param>
    /// <param name="enviar">Cierto para que empiece a llamar ya.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    public async Task<bool> DispararCqAsync(
        string identificador, string direccion, bool periodoTx, bool enviar, CancellationToken ct = default)
    {
        if (!_receptor.TryInstancia(identificador, out var instancia)) return false;
        if (!instancia.Capacidades.PuedeLlamarCq) return false;
        var datagrama = ConstructorDeMensajesWsjt.DispararCq(identificador, direccion, periodoTx, enviar);
        return await EnviarAsync(instancia, datagrama, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Mete un datagrama en el puente como si hubiera llegado por la red. Es la puerta que usan
    /// las pruebas para no depender de un socket de verdad.
    /// </summary>
    /// <param name="datagrama">Bytes del datagrama.</param>
    /// <param name="remitente">Quien lo envia, o nulo.</param>
    public bool Admitir(ReadOnlySpan<byte> datagrama, EndPoint? remitente = null) =>
        _receptor.Recibir(datagrama, remitente, DateTimeOffset.UtcNow);

    /// <summary>Repasa ahora mismo que instancias llevan demasiado tiempo calladas.</summary>
    public void RepasarInstancias() =>
        _receptor.CaducarInactivas(_opciones.Caducidad, DateTimeOffset.UtcNow);

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        try
        {
            await PararAsync().ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Advertir($"Fallo al parar el puente: {e.Message}");
        }
        _cerrojo.Dispose();
    }

    private async Task<bool> EnviarAsync(InstanciaDigital instancia, byte[] datagrama, CancellationToken ct)
    {
        var socket = _socket;
        if (socket is null || instancia.Remitente is null) return false;

        try
        {
            await socket.SendToAsync(datagrama, SocketFlags.None, instancia.Remitente, ct)
                .ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            Advertir($"No se pudo enviar a «{instancia.Identificador}»: {e.Message}");
            return false;
        }
    }

    private async Task EscucharAsync(Socket socket, CancellationToken ct)
    {
        var buffer = new byte[Math.Max(1024, _opciones.TamanoDeBuffer)];
        EndPoint cualquiera = new IPEndPoint(IPAddress.Any, 0);

        while (!ct.IsCancellationRequested)
        {
            SocketReceiveFromResult recibido;
            try
            {
                recibido = await socket.ReceiveFromAsync(buffer, SocketFlags.None, cualquiera, ct)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (SocketException e)
            {
                // Un ICMP de vuelta o un corte momentaneo no son motivo para dejar de escuchar.
                Advertir($"Incidencia en el socket ({e.SocketErrorCode}); se sigue escuchando.");
                continue;
            }

            if (recibido.ReceivedBytes <= 0) continue;

            _receptor.Recibir(
                buffer.AsSpan(0, recibido.ReceivedBytes),
                recibido.RemoteEndPoint,
                DateTimeOffset.UtcNow);
        }
    }

    private async Task VigilarAsync(CancellationToken ct)
    {
        var periodo = _opciones.Repaso <= TimeSpan.Zero ? TimeSpan.FromSeconds(15) : _opciones.Repaso;
        using var reloj = new PeriodicTimer(periodo);
        try
        {
            while (await reloj.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                _receptor.CaducarInactivas(_opciones.Caducidad, DateTimeOffset.UtcNow);
            }
        }
        catch (OperationCanceledException)
        {
            // Salida normal.
        }
    }

    private IPAddress DireccionDeEscucha() =>
        string.IsNullOrWhiteSpace(_opciones.DireccionDeEscucha)
            || !IPAddress.TryParse(_opciones.DireccionDeEscucha, out var direccion)
            ? IPAddress.Any
            : direccion;

    private void UnirseAlGrupoMulticast(Socket socket)
    {
        if (string.IsNullOrWhiteSpace(_opciones.GrupoMulticast)) return;
        if (!IPAddress.TryParse(_opciones.GrupoMulticast, out var grupo))
        {
            Trazar($"El grupo multicast «{_opciones.GrupoMulticast}» no es una direccion valida.");
            return;
        }

        try
        {
            socket.SetSocketOption(
                SocketOptionLevel.IP,
                SocketOptionName.AddMembership,
                new MulticastOption(grupo, DireccionDeEscucha()));
            socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastLoopback, true);
            Trazar($"Unido al grupo multicast {grupo}.");
        }
        catch (SocketException e)
        {
            Trazar($"No se pudo entrar en el grupo multicast {grupo}: {e.SocketErrorCode}.");
        }
    }

    /// <summary>
    /// En Windows, un socket UDP que recibe un ICMP de «puerto inalcanzable» empieza a lanzar
    /// en la siguiente recepcion. Esto lo desactiva: aqui se escucha a varios programas que
    /// van y vienen, y que uno se cierre no puede tirar la escucha de los demas.
    /// </summary>
    private void EvitarCorteDeConexionEnWindows(Socket socket)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
        try
        {
            const int SioUdpConnreset = unchecked((int)0x9800000C);
            socket.IOControl(SioUdpConnreset, [0, 0, 0, 0], null);
        }
        catch (SocketException e)
        {
            Trazar($"No se pudo desactivar el corte por ICMP: {e.SocketErrorCode}.");
        }
    }

    /// <summary>Deja constancia de algo que se descarta o se ignora.</summary>
    private void Trazar(string texto) => _registro.LogDebug("{Traza}", texto);

    /// <summary>Deja constancia de algo que no deberia pasar y conviene mirar.</summary>
    private void Advertir(string texto) => _registro.LogWarning("{Traza}", texto);
}
