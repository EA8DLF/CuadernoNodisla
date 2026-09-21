using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Entidades;

namespace Nodisla.Cuaderno.Integraciones.Concurso;

/// <summary>Como se escucha a N1MM+.</summary>
public sealed class OpcionesPuenteN1Mm
{
    /// <summary>
    /// Puerto UDP de escucha. El 12060 es el que sugiere la propia documentacion de N1MM+ para
    /// los programas externos que quieren oir sus emisiones.
    /// </summary>
    public int Puerto { get; set; } = 12060;

    /// <summary>Direccion local por la que escuchar. Vacia significa todas.</summary>
    public string DireccionDeEscucha { get; set; } = "0.0.0.0";

    /// <summary>Grupo multicast al que unirse, cuando N1MM+ emite asi.</summary>
    public string? GrupoMulticast { get; set; }

    /// <summary>Tamano del buffer de recepcion.</summary>
    public int TamanoDeBuffer { get; set; } = 64 * 1024;
}

/// <summary>
/// Puente con N1MM+ por UDP.
/// </summary>
/// <remarks>
/// Mucho mas sencillo que el de modos digitales: N1MM+ manda XML en texto plano, un documento
/// entero por datagrama, y no espera respuesta. Lo unico que hay que cuidar es lo de siempre,
/// que nada de lo que llegue tire el bucle: los avisos de radio llegan varias veces por
/// segundo y una caida ahi dejaria el cuaderno sordo sin que se note.
/// </remarks>
public sealed class PuenteN1MmUdp : IPuenteConcurso
{
    private readonly OpcionesPuenteN1Mm _opciones;
    private readonly ILogger _registro;
    private readonly SemaphoreSlim _cerrojo = new(1, 1);

    private Socket? _socket;
    private CancellationTokenSource? _parada;
    private Task? _bucle;

    /// <summary>Crea el puente.</summary>
    /// <param name="opciones">Puerto y multicast.</param>
    /// <param name="registro">Donde dejar constancia de lo que pasa. Nulo para no registrar nada.</param>
    public PuenteN1MmUdp(OpcionesPuenteN1Mm? opciones = null, ILogger? registro = null)
    {
        _opciones = opciones ?? new OpcionesPuenteN1Mm();
        _registro = registro ?? NullLogger.Instance;
    }

    /// <inheritdoc/>
    public int Puerto => _opciones.Puerto;

    /// <inheritdoc/>
    public event EventHandler<EstadoDeRadioConcurso>? RadioRecibida;

    /// <inheritdoc/>
    public event EventHandler<Qso>? QsoRegistrado;

    /// <inheritdoc/>
    public event EventHandler<ContactoDeConcurso>? ContactoReemplazado;

    /// <inheritdoc/>
    public event EventHandler<BorradoDeConcurso>? ContactoBorrado;

    /// <summary>Ultimo estado conocido de cada radio, por numero de radio.</summary>
    public IReadOnlyDictionary<int, EstadoDeRadioConcurso> Radios => _radios;

    private readonly Dictionary<int, EstadoDeRadioConcurso> _radios = [];

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
            Trazar($"Escuchando a N1MM+ en el puerto {_opciones.Puerto}.");
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

            if (_bucle is not null)
            {
                try
                {
                    await _bucle.ConfigureAwait(false);
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
            Trazar("Puente con N1MM+ parado.");
        }
        finally
        {
            _cerrojo.Release();
        }
    }

    /// <summary>
    /// Mete un datagrama en el puente como si hubiera llegado por la red. Es la puerta que
    /// usan las pruebas para no depender de un socket de verdad.
    /// </summary>
    /// <param name="xml">Texto del datagrama.</param>
    /// <returns>Cierto si se reconocio el mensaje.</returns>
    public bool Admitir(string xml)
    {
        MensajeDeConcurso? mensaje;
        try
        {
            mensaje = AnalizadorN1Mm.Analizar(xml, out var motivo);
            if (mensaje is null)
            {
                Trazar($"Datagrama de N1MM+ descartado: {motivo}");
                return false;
            }
        }
        catch (Exception e)
        {
            Advertir($"Un datagrama de N1MM+ rompio el analizador: {e.Message}");
            return false;
        }

        try
        {
            switch (mensaje)
            {
                case EstadoDeRadioConcurso radio:
                    lock (_radios) _radios[radio.NumeroDeRadio] = radio;
                    Avisar(RadioRecibida, radio);
                    break;

                case ContactoDeConcurso contacto when contacto.Accion == AccionSobreContacto.Anadido:
                    Avisar(QsoRegistrado, AnalizadorN1Mm.AQso(contacto));
                    break;

                case ContactoDeConcurso contacto:
                    Avisar(ContactoReemplazado, contacto);
                    break;

                case BorradoDeConcurso borrado:
                    Avisar(ContactoBorrado, borrado);
                    break;
            }
            return true;
        }
        catch (Exception e)
        {
            Advertir($"Fallo al tratar un mensaje de N1MM+: {e.Message}");
            return false;
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        try
        {
            await PararAsync().ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Advertir($"Fallo al parar el puente con N1MM+: {e.Message}");
        }
        _cerrojo.Dispose();
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
                recibido = await socket
                    .ReceiveFromAsync(buffer.AsMemory(), SocketFlags.None, cualquiera, ct)
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
                Advertir($"Incidencia en el socket ({e.SocketErrorCode}); se sigue escuchando.");
                continue;
            }

            if (recibido.ReceivedBytes <= 0) continue;

            string texto;
            try
            {
                texto = Encoding.UTF8.GetString(buffer, 0, recibido.ReceivedBytes);
            }
            catch (ArgumentException)
            {
                continue;
            }

            Admitir(texto);
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
            Trazar($"Unido al grupo multicast {grupo}.");
        }
        catch (SocketException e)
        {
            Trazar($"No se pudo entrar en el grupo multicast {grupo}: {e.SocketErrorCode}.");
        }
    }

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

    private void Avisar<T>(EventHandler<T>? evento, T argumento)
    {
        if (evento is null) return;
        foreach (var suscriptor in evento.GetInvocationList())
        {
            try
            {
                ((EventHandler<T>)suscriptor)(this, argumento);
            }
            catch (Exception e)
            {
                Advertir($"Un suscriptor lanzo al recibir {typeof(T).Name}: {e.Message}");
            }
        }
    }

    /// <summary>Deja constancia de algo que se descarta o se ignora.</summary>
    private void Trazar(string texto) => _registro.LogDebug("{Traza}", texto);

    /// <summary>Deja constancia de algo que no deberia pasar y conviene mirar.</summary>
    private void Advertir(string texto) => _registro.LogWarning("{Traza}", texto);
}
