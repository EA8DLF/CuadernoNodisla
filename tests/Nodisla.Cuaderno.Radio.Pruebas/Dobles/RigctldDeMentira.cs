using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Nodisla.Cuaderno.Radio.Pruebas.Dobles;

/// <summary>
/// Un <c>rigctld</c> de mentira que habla el protocolo extendido de Hamlib.
/// </summary>
/// <remarks>
/// Contesta como el de verdad: eco de la orden, las lineas de datos y <c>RPRT n</c> al final.
/// Aqui no hay equipo ni se transmite: el PTT solo mueve un contador.
/// </remarks>
internal sealed class RigctldDeMentira : IAsyncDisposable
{
    private readonly TcpListener _escucha;
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentQueue<string> _recibidas = new();
    private readonly Task _bucle;

    private long _hercios = 14_074_000;
    private string _modo = "USB";
    private int _enAntena;
    private int _sabeDelPtt = 1;

    internal RigctldDeMentira()
    {
        _escucha = new TcpListener(IPAddress.Loopback, 0);
        _escucha.Start();
        Puerto = ((IPEndPoint)_escucha.LocalEndpoint).Port;
        _bucle = Task.Run(() => AtenderSiempreAsync(_cts.Token));
    }

    /// <summary>Puerto TCP donde escucha.</summary>
    internal int Puerto { get; }

    /// <summary>Ordenes recibidas, en orden.</summary>
    internal IReadOnlyCollection<string> Recibidas => _recibidas;

    /// <summary>El equipo de mentira tiene el PTT puesto.</summary>
    internal bool EnAntena => Volatile.Read(ref _enAntena) != 0;

    /// <summary>Frecuencia que tiene puesta.</summary>
    internal long Hercios
    {
        get => Interlocked.Read(ref _hercios);
        set => Interlocked.Exchange(ref _hercios, value);
    }

    /// <summary>Hace que el equipo no sepa decir si esta en antena, como algunos de verdad.</summary>
    internal void NoSabeDelPtt() => Volatile.Write(ref _sabeDelPtt, 0);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        _escucha.Stop();
        try
        {
            await _bucle.WaitAsync(TimeSpan.FromSeconds(2));
        }
        catch (Exception)
        {
            // Es un doble de pruebas: da igual como termine.
        }

        _cts.Dispose();
    }

    private async Task AtenderSiempreAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient cliente;
            try
            {
                cliente = await _escucha.AcceptTcpClientAsync(ct);
            }
            catch (Exception)
            {
                return;
            }

            _ = Task.Run(() => AtenderAsync(cliente, ct), CancellationToken.None);
        }
    }

    private async Task AtenderAsync(TcpClient cliente, CancellationToken ct)
    {
        using (cliente)
        {
            var flujo = cliente.GetStream();
            using var lector = new StreamReader(flujo, Encoding.ASCII, false, 256, true);
            await using var escritor = new StreamWriter(flujo, Encoding.ASCII, 256, true) { NewLine = "\n", AutoFlush = true };

            while (!ct.IsCancellationRequested)
            {
                string? linea;
                try
                {
                    linea = await lector.ReadLineAsync(ct);
                }
                catch (Exception)
                {
                    return;
                }

                if (linea is null)
                {
                    return;
                }

                _recibidas.Enqueue(linea);
                foreach (var respuesta in Responder(linea))
                {
                    await escritor.WriteLineAsync(respuesta.AsMemory(), ct);
                }
            }
        }
    }

    private IEnumerable<string> Responder(string linea)
    {
        var orden = linea.TrimStart('+').Trim();
        var partes = orden.Split(' ', 2, StringSplitOptions.TrimEntries);
        var nombre = partes[0].TrimStart('\\');
        var argumento = partes.Length > 1 ? partes[1] : string.Empty;

        switch (nombre)
        {
            case "get_freq" or "f":
                yield return "get_freq:";
                yield return string.Create(CultureInfo.InvariantCulture, $"Frequency: {Interlocked.Read(ref _hercios)}");
                yield return "RPRT 0";
                break;

            case "set_freq" or "F":
                yield return $"set_freq: {argumento}";
                if (long.TryParse(argumento, NumberStyles.Integer, CultureInfo.InvariantCulture, out var hercios))
                {
                    Interlocked.Exchange(ref _hercios, hercios);
                    yield return "RPRT 0";
                }
                else
                {
                    yield return "RPRT -1";
                }

                break;

            case "get_mode" or "m":
                yield return "get_mode:";
                yield return $"Mode: {Volatile.Read(ref _modo)}";
                yield return "Passband: 2400";
                yield return "RPRT 0";
                break;

            case "set_mode" or "M":
                yield return $"set_mode: {argumento}";
                var nombreDeModo = argumento.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                if (nombreDeModo is not null)
                {
                    Volatile.Write(ref _modo, nombreDeModo);
                    yield return "RPRT 0";
                }
                else
                {
                    yield return "RPRT -1";
                }

                break;

            case "get_ptt" or "t":
                yield return "get_ptt:";
                if (Volatile.Read(ref _sabeDelPtt) == 0)
                {
                    // Como el equipo ficticio de Hamlib: «función no disponible».
                    yield return "RPRT -11";
                }
                else
                {
                    yield return string.Create(CultureInfo.InvariantCulture, $"PTT: {Volatile.Read(ref _enAntena)}");
                    yield return "RPRT 0";
                }

                break;

            case "set_ptt" or "T":
                yield return $"set_ptt: {argumento}";
                Volatile.Write(ref _enAntena, argumento.Trim() == "1" ? 1 : 0);
                yield return "RPRT 0";
                break;

            case "get_vfo" or "v":
                yield return "get_vfo:";
                yield return "VFO: VFOA";
                yield return "RPRT 0";
                break;

            case "get_level" or "l":
                yield return $"get_level: {argumento}";
                yield return "-9";
                yield return "RPRT 0";
                break;

            default:
                yield return "RPRT -1";
                break;
        }
    }
}
