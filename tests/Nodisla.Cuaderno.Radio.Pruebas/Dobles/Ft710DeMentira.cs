using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Nodisla.Cuaderno.Radio.Pruebas.Dobles;

/// <summary>
/// Un FT-710 de mentira que contesta por TCP lo mismo que contesto el de verdad.
/// </summary>
/// <remarks>
/// Las respuestas salen de la captura de <c>docs/04-ft710-cat.md</c>, hecha con el equipo de
/// EA8DLF encendido. Existe para que las pruebas no dependan de que la radio este enchufada:
/// aqui no se transmite nunca, y el <c>TX1;</c> solo mueve un contador.
/// </remarks>
internal sealed class Ft710DeMentira : IAsyncDisposable
{
    private readonly TcpListener _escucha;
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentQueue<string> _recibidas = new();
    private readonly ConcurrentDictionary<string, string> _valores = new(StringComparer.Ordinal);
    private readonly Task _bucle;

    private int _enAntena;
    private int _mudo;
    private int _apagado;

    internal Ft710DeMentira(bool mudo = false)
    {
        Volatile.Write(ref _mudo, mudo ? 1 : 0);
        PrepararRespuestas();

        _escucha = new TcpListener(IPAddress.Loopback, 0);
        _escucha.Start();
        Puerto = ((IPEndPoint)_escucha.LocalEndpoint).Port;
        _bucle = Task.Run(() => AtenderSiempreAsync(_cts.Token));
    }

    /// <summary>Puerto TCP donde escucha.</summary>
    internal int Puerto { get; }

    /// <summary>Ordenes que ha recibido, en orden.</summary>
    internal IReadOnlyCollection<string> Recibidas => _recibidas;

    /// <summary>El equipo de mentira tiene el PTT puesto.</summary>
    internal bool EnAntena => Volatile.Read(ref _enAntena) != 0;

    /// <summary>Deja de contestar, como un equipo apagado con el cable puesto.</summary>
    internal void Enmudecer() => Volatile.Write(ref _mudo, 1);

    /// <summary>Vuelve a contestar.</summary>
    internal void Despertar() => Volatile.Write(ref _mudo, 0);

    /// <summary>Corta las conexiones, como si se desenchufara el cable.</summary>
    internal void Desenchufar()
    {
        Volatile.Write(ref _apagado, 1);
        Volatile.Write(ref _mudo, 1);
    }

    /// <summary>Cambia el valor con el que contesta a una consulta.</summary>
    /// <param name="consulta">Consulta con punto y coma, por ejemplo <c>FA;</c>.</param>
    /// <param name="respuesta">Respuesta con punto y coma.</param>
    internal void Responder(string consulta, string respuesta) => _valores[consulta] = respuesta;

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
            // Da igual como termine el bucle: esto es un doble de pruebas.
        }

        _cts.Dispose();
    }

    private void PrepararRespuestas()
    {
        // Tal cual las capturo el coordinador del equipo de Jose.
        var captura = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ID;"] = "ID0800;",
            ["PS;"] = "PS1;",
            ["FA;"] = "FA027555000;",
            ["FB;"] = "FB007000000;",
            ["VS;"] = "VS0;",
            ["IF;"] = "IF000027555000+000000200000;",
            ["MD0;"] = "MD02;",
            ["MD1;"] = "MD12;",
            ["SH0;"] = "SH0020;",
            ["NA0;"] = "NA00;",
            ["NB0;"] = "NB00;",
            ["NL0;"] = "NL0000;",
            ["NR0;"] = "NR00;",
            ["RL0;"] = "RL001;",
            ["BP00;"] = "BP00000;",
            ["BP01;"] = "BP01150;",
            ["CO00;"] = "CO000000;",
            ["CO01;"] = "CO011500;",
            ["AG0;"] = "AG0089;",
            ["RG0;"] = "RG0255;",
            ["SQ0;"] = "SQ0000;",
            ["MG;"] = "MG080;",
            ["PC;"] = "PC100;",
            ["AC;"] = "AC000;",
            ["VX;"] = "VX0;",
            ["VG;"] = "VG070;",
            ["VD;"] = "VD08;",
            ["GT0;"] = "GT06;",
            ["PA0;"] = "PA00;",
            ["RA0;"] = "RA00;",
            ["KS;"] = "KS020;",
            ["KP;"] = "KP40;",
            ["BI;"] = "BI0;",
            ["BC0;"] = "BC00;",
            ["SM0;"] = "SM0041;",
            ["RM1;"] = "RM1000000;",
            ["RM3;"] = "RM3000000;",
            ["RM4;"] = "RM4000000;",
            ["RM5;"] = "RM5000000;",
            ["RM6;"] = "RM6000000;",
            ["RM7;"] = "RM7000000;",
            ["FT;"] = "FT0;",
            ["ST;"] = "ST0;",
            ["MC;"] = "MC001;",
            ["LK;"] = "LK0;",
            ["MS;"] = "MS50;",
            ["SD;"] = "SD04;",
            ["PB0;"] = "PB00;",
            ["TS;"] = "TS0;",
            ["DT0;"] = "DT020260921;",

            // El menu, con sus tres formas de valor.
            ["EX010101;"] = "EX010101+00;",
            ["EX030101;"] = "EX030101020;",
            ["EX040101;"] = "EX040101FT-710      ;",
            ["EX060101;"] = "EX060101FT-8        ;",
        };

        foreach (var (consulta, respuesta) in captura)
        {
            _valores[consulta] = respuesta;
        }
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
            var buzon = new byte[256];
            var pendiente = new StringBuilder();

            while (!ct.IsCancellationRequested)
            {
                int leidos;
                try
                {
                    leidos = await flujo.ReadAsync(buzon.AsMemory(), ct);
                }
                catch (Exception)
                {
                    return;
                }

                if (leidos <= 0)
                {
                    return;
                }

                if (Volatile.Read(ref _apagado) != 0)
                {
                    return;
                }

                pendiente.Append(Encoding.ASCII.GetString(buzon, 0, leidos));
                var texto = pendiente.ToString();
                int fin;
                while ((fin = texto.IndexOf(';')) >= 0)
                {
                    var orden = texto[..(fin + 1)];
                    texto = texto[(fin + 1)..];
                    var respuesta = Responder(orden);
                    if (respuesta is not null)
                    {
                        await flujo.WriteAsync(Encoding.ASCII.GetBytes(respuesta).AsMemory(), ct);
                        await flujo.FlushAsync(ct);
                    }
                }

                pendiente.Clear();
                pendiente.Append(texto);
            }
        }
    }

    private string? Responder(string orden)
    {
        _recibidas.Enqueue(orden);

        if (Volatile.Read(ref _mudo) != 0)
        {
            // Equipo apagado con el cable puesto: el puerto existe y nadie contesta.
            return null;
        }

        // El PTT: aqui no transmite nadie, solo se apunta.
        if (orden is "TX1;")
        {
            Volatile.Write(ref _enAntena, 1);
            return null;
        }

        if (orden is "TX0;")
        {
            Volatile.Write(ref _enAntena, 0);
            return null;
        }

        if (_valores.TryGetValue(orden, out var respuesta))
        {
            return respuesta;
        }

        // Escrituras que el equipo acepta sin contestar: se quedan como nuevo valor.
        if (orden.StartsWith("FA", StringComparison.Ordinal) && orden.Length == 12)
        {
            _valores["FA;"] = orden;
            return null;
        }

        if (orden.StartsWith("MD0", StringComparison.Ordinal) && orden.Length == 5)
        {
            _valores["MD0;"] = orden;
            return null;
        }

        if (orden.StartsWith("KP", StringComparison.Ordinal) && orden.Length == 5)
        {
            _valores["KP;"] = orden;
            return null;
        }

        if (orden.StartsWith("MC", StringComparison.Ordinal) && orden.Length == 6)
        {
            _valores["MC;"] = orden;
            return null;
        }

        if (orden.StartsWith("PC", StringComparison.Ordinal) && orden.Length == 6)
        {
            _valores["PC;"] = orden;
            return null;
        }

        // Todo lo demas, como el equipo de verdad: no lo admito.
        return string.Create(CultureInfo.InvariantCulture, $"?;");
    }
}
