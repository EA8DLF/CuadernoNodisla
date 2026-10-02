using System.Net;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nodisla.Cuaderno.Aplicacion.Puertos;

namespace Nodisla.Cuaderno.Servidores.Tci;

/// <summary>
/// Servidor TCI (protocolo de Expert Electronics) sobre WebSocket, con Kestrel, el servidor de
/// .NET: nada de WebSocket hecho a mano.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Solo entran las IP que admite <see cref="ControlDeAcceso"/>.</item>
/// <item>Se rechaza cualquier conexion que traiga cabecera <c>Origin</c>: la mandan los
/// navegadores, y una pagina web no tiene por que hablar con la radio (los programas de verdad
/// no la mandan).</item>
/// <item>Con contraseña puesta, hay que conectar a <c>ws://host:puerto/?token=...</c> o mandarla
/// en la cabecera <c>Authorization: Bearer ...</c>; se compara en tiempo constante.</item>
/// </list>
/// </remarks>
public sealed class ServidorTci : IAsyncDisposable
{
    private const int MensajeMaximo = 8 * 1024;

    private readonly RadioCompartida _radio;
    private readonly ILogger _registro;
    private readonly List<Conexion> _conexiones = [];
    private readonly object _candado = new();
    private readonly CancellationTokenSource _cierre = new();
    private ControlDeAcceso _acceso;
    private WebApplication? _aplicacion;
    private long _segundoDeSpots;
    private int _spotsEnElSegundo;

    /// <summary>Crea el servidor, sin arrancarlo.</summary>
    /// <param name="radio">La radio compartida.</param>
    /// <param name="acceso">Quien entra.</param>
    /// <param name="registro">Registro.</param>
    public ServidorTci(RadioCompartida radio, ControlDeAcceso acceso, ILogger? registro = null)
    {
        _radio = radio ?? throw new ArgumentNullException(nameof(radio));
        _acceso = acceso ?? throw new ArgumentNullException(nameof(acceso));
        _registro = registro ?? NullLogger.Instance;
        _radio.Control.EstadoCambiado += AlCambiarElEstado;
        _radio.ClientesCambiados += AlCambiarLosClientes;
    }

    /// <summary>Spots por segundo, como mucho, que se reparten a los clientes.</summary>
    public int SpotsPorSegundo { get; set; } = 20;

    /// <summary>Donde escucha, una vez arrancado.</summary>
    public IPEndPoint? Escuchando { get; private set; }

    /// <summary>Salta con un spot que manda un cliente.</summary>
    public event EventHandler<Spot>? SpotRecibido;

    /// <summary>Salta cuando un cliente borra spots (nulo: todos los suyos).</summary>
    public event EventHandler<string?>? SpotsBorrados;

    /// <summary>Cambia la lista de IP sin reiniciar.</summary>
    /// <param name="acceso">El nuevo control.</param>
    public void CambiarAcceso(ControlDeAcceso acceso) => Volatile.Write(ref _acceso, acceso);

    /// <summary>Arranca en un puerto (0: uno libre, para las pruebas).</summary>
    /// <param name="puerto">El puerto.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    public async Task ArrancarAsync(int puerto, CancellationToken ct = default)
    {
        if (_aplicacion is not null) return;
        var direccion = _acceso.DireccionDeEscucha;

        // El constructor vacio: sin configuracion de ficheros ni variables de entorno (nada de
        // ASPNETCORE_URLS abriendo otro puerto), sin HTTPS y sin registro propio.
        var constructor = WebApplication.CreateEmptyBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(ServidorTci).Assembly.GetName().Name,
            ContentRootPath = AppContext.BaseDirectory,
        });
        constructor.WebHost.UseKestrelCore();
        constructor.WebHost.ConfigureKestrel(k =>
        {
            k.AddServerHeader = false;
            k.Limits.MaxConcurrentConnections = OpcionesDeServidores.MaximoDeClientes * 2;
            k.Limits.MaxConcurrentUpgradedConnections = OpcionesDeServidores.MaximoDeClientes;
            k.Limits.MaxRequestHeadersTotalSize = 16 * 1024;
            k.Limits.MaxRequestBodySize = 0;
            k.Listen(direccion, puerto);
        });
        constructor.Services.AddRouting();
        var aplicacion = constructor.Build();
        aplicacion.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20) });
        aplicacion.Run(AtenderAsync);
        await aplicacion.StartAsync(ct).ConfigureAwait(false);
        _aplicacion = aplicacion;

        var direcciones = aplicacion.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses;
        if (direcciones?.FirstOrDefault() is { } url && Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            Escuchando = new IPEndPoint(direccion, uri.Port);
        }

        _registro.LogInformation("Servidor TCI escuchando en {Punto}.", Escuchando);
    }

    private async Task AtenderAsync(HttpContext contexto)
    {
        var remoto = new IPEndPoint(contexto.Connection.RemoteIpAddress ?? IPAddress.None, contexto.Connection.RemotePort);
        if (!Volatile.Read(ref _acceso).Admite(contexto.Connection.RemoteIpAddress))
        {
            _registro.LogWarning("Conexion TCI rechazada desde {Remoto}: no esta en la lista de IP permitidas.", remoto);
            contexto.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        if (contexto.Request.Headers.Origin.Count > 0 && contexto.Request.Headers.Origin.Any(o => !string.IsNullOrEmpty(o)))
        {
            _registro.LogWarning("Conexion TCI rechazada desde {Remoto}: trae Origin «{Origen}» (un navegador).", remoto, contexto.Request.Headers.Origin.ToString());
            contexto.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        if (!contexto.WebSockets.IsWebSocketRequest)
        {
            contexto.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        if (_radio.Opciones.TokenTci is { Length: > 0 } token && !TokenValido(contexto, token))
        {
            _registro.LogWarning("Conexion TCI rechazada desde {Remoto}: contraseña ausente o incorrecta.", remoto);
            contexto.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        using var socket = await contexto.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(_cierre.Token, contexto.RequestAborted);
        var cliente = _radio.Alta(ProtocoloExterno.Tci, remoto, async () =>
        {
            await cts.CancelAsync().ConfigureAwait(false);
            socket.Abort();
        });
        if (cliente is null)
        {
            await socket.CloseAsync(WebSocketCloseStatus.PolicyViolation, "demasiados clientes", CancellationToken.None).ConfigureAwait(false);
            return;
        }

        var sesion = new SesionTci(_radio, cliente, _registro);
        sesion.SpotRecibido += (_, spot) => SpotRecibido?.Invoke(this, spot);
        sesion.SpotsBorrados += (_, indicativo) => SpotsBorrados?.Invoke(this, indicativo);
        var conexion = new Conexion(sesion, cliente);
        lock (_candado) _conexiones.Add(conexion);
        var envio = Task.Run(() => EnviarAsync(socket, conexion, cts.Token), CancellationToken.None);
        try
        {
            foreach (var m in sesion.Saludo()) conexion.Poner(m);
            await RecibirAsync(socket, sesion, conexion, cts.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException or IOException or InvalidDataException)
        {
            _registro.LogDebug(ex, "Sesion TCI de {Remoto} terminada.", remoto);
        }
        finally
        {
            lock (_candado) _conexiones.Remove(conexion);

            // Si se va con el PTT arriba, se baja en el acto, antes de nada mas.
            await _radio.BajaAsync(cliente).ConfigureAwait(false);
            conexion.Salida.Writer.TryComplete();
            await cts.CancelAsync().ConfigureAwait(false);
            try
            {
                await envio.ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or WebSocketException or IOException)
            {
                // Ya se esta cerrando.
            }
        }
    }

    private static bool TokenValido(HttpContext contexto, string token)
    {
        string? dado = contexto.Request.Query["token"];
        if (string.IsNullOrEmpty(dado))
        {
            var cabecera = contexto.Request.Headers.Authorization.ToString();
            if (cabecera.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) dado = cabecera[7..].Trim();
        }

        if (string.IsNullOrEmpty(dado)) return false;
        return CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(dado)),
            SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }

    private static async Task RecibirAsync(WebSocket socket, SesionTci sesion, Conexion conexion, CancellationToken ct)
    {
        var bufer = new byte[4096];
        var mensaje = new MemoryStream();
        while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            var leido = await socket.ReceiveAsync(bufer, ct).ConfigureAwait(false);
            if (leido.MessageType == WebSocketMessageType.Close) return;

            // Binario seria audio o IQ por TCI: no hay (fase posterior); se ignora.
            mensaje.Write(bufer, 0, leido.Count);
            if (mensaje.Length > MensajeMaximo) throw new InvalidDataException("Mensaje TCI demasiado largo.");
            if (!leido.EndOfMessage) continue;
            if (leido.MessageType == WebSocketMessageType.Text)
            {
                var texto = Encoding.UTF8.GetString(mensaje.GetBuffer(), 0, (int)mensaje.Length);
                foreach (var respuesta in await sesion.AtenderAsync(texto, ct).ConfigureAwait(false)) conexion.Poner(respuesta);
            }

            mensaje.SetLength(0);
        }
    }

    private static async Task EnviarAsync(WebSocket socket, Conexion conexion, CancellationToken ct)
    {
        await foreach (var mensaje in conexion.Salida.Reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            if (socket.State != WebSocketState.Open) return;
            await socket.SendAsync(Encoding.UTF8.GetBytes(mensaje), WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
        }
    }

    private void AlCambiarElEstado(object? origen, EstadoDelEquipo estado) => Repartir();

    private void AlCambiarLosClientes(object? origen, EventArgs e) => Repartir();

    /// <summary>Manda a cada cliente lo que ha cambiado (solo lo que ha cambiado).</summary>
    private void Repartir()
    {
        List<Conexion> todas;
        lock (_candado) todas = [.. _conexiones];
        foreach (var c in todas)
        {
            foreach (var m in c.Sesion.Cambios()) c.Poner(m);
        }
    }

    /// <summary>Manda un spot del Cuaderno a los clientes (si esta permitido y no viene de un cliente TCI).</summary>
    /// <param name="spot">El spot.</param>
    public void EnviarSpot(Spot spot)
    {
        ArgumentNullException.ThrowIfNull(spot);
        if (!_radio.Opciones.EnviarSpots || spot.Fuente.StartsWith("TCI", StringComparison.Ordinal)) return;
        var segundo = Environment.TickCount64 / 1000;
        lock (_candado)
        {
            if (segundo != _segundoDeSpots)
            {
                _segundoDeSpots = segundo;
                _spotsEnElSegundo = 0;
            }

            if (++_spotsEnElSegundo > SpotsPorSegundo) return;
        }

        var mensaje = SesionTci.MensajeDeSpot(spot);
        List<Conexion> todas;
        lock (_candado) todas = [.. _conexiones];
        foreach (var c in todas) c.Poner(mensaje);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_cierre.IsCancellationRequested) return;
        _radio.Control.EstadoCambiado -= AlCambiarElEstado;
        _radio.ClientesCambiados -= AlCambiarLosClientes;
        await _cierre.CancelAsync().ConfigureAwait(false);
        if (_aplicacion is { } aplicacion)
        {
            try
            {
                await aplicacion.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                _registro.LogWarning("El servidor TCI ha tardado en pararse.");
            }

            await aplicacion.DisposeAsync().ConfigureAwait(false);
        }
    }

    private sealed class Conexion(SesionTci sesion, ClienteExterno cliente)
    {
        public SesionTci Sesion { get; } = sesion;

        public ClienteExterno Cliente { get; } = cliente;

        /// <summary>Cola de salida acotada: con un cliente lento se pierden los mas viejos.</summary>
        public Channel<string> Salida { get; } = Channel.CreateBounded<string>(new BoundedChannelOptions(512)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });

        public void Poner(string mensaje) => Salida.Writer.TryWrite(mensaje);
    }
}
