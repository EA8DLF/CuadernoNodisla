using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Nodisla.Cuaderno.Integraciones.Pruebas.Cluster;

/// <summary>
/// Un cluster de mentira: escucha en un puerto suelto de la maquina y hace lo que le diga
/// el guion de la prueba.
/// </summary>
/// <remarks>
/// Las pruebas no pueden depender de la red: un cluster de verdad se cae, cambia de formato
/// y tarda lo que quiere. Este servidor escupe lineas reales de cluster y permite provocar a
/// mano lo que en la vida real pasa solo de madrugada: la linea partida, la secuencia IAC,
/// el corte a mitad de anuncio y la reconexion.
/// </remarks>
internal sealed class ServidorDeMentira : IAsyncDisposable
{
    private readonly TcpListener _escucha;
    private readonly Func<SesionDeMentira, CancellationToken, Task> _guion;
    private readonly CancellationTokenSource _cancelacion = new();
    private readonly Task _bucle;
    private int _conexiones;

    /// <summary>Arranca el servidor con el guion que atendera cada conexion.</summary>
    public ServidorDeMentira(Func<SesionDeMentira, CancellationToken, Task> guion)
    {
        _guion = guion;
        _escucha = new TcpListener(IPAddress.Loopback, 0);
        _escucha.Start();
        Puerto = ((IPEndPoint)_escucha.LocalEndpoint).Port;
        _bucle = Task.Run(() => AtenderAsync(_cancelacion.Token));
    }

    /// <summary>Puerto en el que escucha.</summary>
    public int Puerto { get; }

    /// <summary>Cuantas veces se ha conectado el programa.</summary>
    public int Conexiones => Volatile.Read(ref _conexiones);

    /// <summary>Todo lo que el programa ha enviado, linea a linea.</summary>
    public ConcurrentQueue<string> Recibido { get; } = new();

    private async Task AtenderAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient cliente;
            try
            {
                cliente = await _escucha.AcceptTcpClientAsync(ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (SocketException)
            {
                return;
            }

            var numero = Interlocked.Increment(ref _conexiones);
            _ = Task.Run(async () =>
            {
                using (cliente)
                {
                    var sesion = new SesionDeMentira(cliente, numero, Recibido);
                    try
                    {
                        await _guion(sesion, ct);
                    }
                    catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
                    {
                        // El programa ha cerrado: es lo normal al terminar la prueba.
                    }
                }
            }, CancellationToken.None);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _cancelacion.CancelAsync();
        _escucha.Stop();
        try
        {
            await _bucle;
        }
        catch (Exception ex) when (ex is OperationCanceledException or SocketException)
        {
            // Esperado al parar.
        }
        _cancelacion.Dispose();
    }
}

/// <summary>Una conexion atendida por el cluster de mentira.</summary>
internal sealed class SesionDeMentira
{
    private readonly TcpClient _cliente;
    private readonly ConcurrentQueue<string> _recibido;

    public SesionDeMentira(TcpClient cliente, int numero, ConcurrentQueue<string> recibido)
    {
        _cliente = cliente;
        Numero = numero;
        _recibido = recibido;
        Flujo = cliente.GetStream();
    }

    /// <summary>Numero de conexion, empezando por uno.</summary>
    public int Numero { get; }

    /// <summary>Flujo de la conexion.</summary>
    public NetworkStream Flujo { get; }

    /// <summary>Envia texto tal cual, sin anadir saltos de linea.</summary>
    public async Task EscribirAsync(string texto, CancellationToken ct = default)
    {
        var bytes = Encoding.ASCII.GetBytes(texto);
        await Flujo.WriteAsync(bytes, ct);
        await Flujo.FlushAsync(ct);
    }

    /// <summary>Envia bytes en crudo, para poder colar secuencias de control.</summary>
    public async Task EscribirCrudoAsync(byte[] bytes, CancellationToken ct = default)
    {
        await Flujo.WriteAsync(bytes, ct);
        await Flujo.FlushAsync(ct);
    }

    /// <summary>Envia una linea terminada como la termina un cluster.</summary>
    public Task EscribirLineaAsync(string linea, CancellationToken ct = default) =>
        EscribirAsync(linea + "\r\n", ct);

    /// <summary>Lee una linea de lo que manda el programa y la apunta.</summary>
    public async Task<string> LeerLineaAsync(CancellationToken ct = default)
    {
        var acumulado = new StringBuilder();
        var buffer = new byte[1];
        while (true)
        {
            var leidos = await Flujo.ReadAsync(buffer, ct);
            if (leidos == 0) break;
            var c = (char)buffer[0];
            if (c == '\n') break;
            if (c != '\r') acumulado.Append(c);
        }
        var linea = acumulado.ToString();
        _recibido.Enqueue(linea);
        return linea;
    }

    /// <summary>
    /// Lee una linea y la apunta, o devuelve nulo si el programa ha cerrado la conexion.
    /// </summary>
    public async Task<string?> LeerLineaONadaAsync(CancellationToken ct = default)
    {
        var acumulado = new StringBuilder();
        var buffer = new byte[1];
        while (true)
        {
            var leidos = await Flujo.ReadAsync(buffer, ct);
            if (leidos == 0) return acumulado.Length > 0 ? Apuntar(acumulado.ToString()) : null;
            var c = (char)buffer[0];
            if (c == '\n') return Apuntar(acumulado.ToString());
            if (c != '\r') acumulado.Append(c);
        }
    }

    /// <summary>Atiende al programa hasta que cierre: apunta todo lo que mande.</summary>
    public async Task EscucharHastaQueCierreAsync(CancellationToken ct = default)
    {
        while (await LeerLineaONadaAsync(ct) is not null)
        {
            // Solo se apunta.
        }
    }

    private string Apuntar(string linea)
    {
        _recibido.Enqueue(linea);
        return linea;
    }

    /// <summary>Cierra la conexion de golpe, como cuando se cae un nodo.</summary>
    public void Cortar() => _cliente.Close();
}
