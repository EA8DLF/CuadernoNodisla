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
    private readonly ConcurrentDictionary<string, TaskCompletionSource> _esperadas = new(StringComparer.Ordinal);
    private readonly Thread _bucle;

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

        // Hilos propios, no tareas: si el equipo de mentira compitiera por el repartidor de
        // tareas, al medir con la máquina ahogada estaríamos midiendo el doble y no el módulo.
        _bucle = new Thread(AtenderSiempre) { IsBackground = true, Name = "FT-710 de mentira" };
        _bucle.Start();
    }

    /// <summary>Puerto TCP donde escucha.</summary>
    internal int Puerto { get; }

    /// <summary>Ordenes que ha recibido, en orden.</summary>
    internal IReadOnlyCollection<string> Recibidas => _recibidas;

    /// <summary>
    /// Espera a que llegue una orden concreta.
    /// </summary>
    /// <remarks>
    /// Las ordenes de escritura del CAT no llevan respuesta, asi que no hay forma de confirmar
    /// que han llegado mas que verlas aqui. Esto avisa en cuanto llega, en vez de obligar a la
    /// prueba a esperar un plazo a ver si aparece: el plazo que se le pasa es solo una red por
    /// si no llega nunca, no una medida de nada.
    /// </remarks>
    /// <param name="orden">Orden esperada, con su punto y coma.</param>
    /// <param name="plazo">Tope de espera.</param>
    /// <returns>La tarea que termina cuando llega la orden.</returns>
    internal async Task EsperarOrdenAsync(string orden, TimeSpan plazo)
    {
        if (_recibidas.Contains(orden))
        {
            return;
        }

        var llegada = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _esperadas[orden] = llegada;

        // Pudo llegar justo entre la comprobacion y el apunte.
        if (_recibidas.Contains(orden))
        {
            llegada.TrySetResult();
        }

        try
        {
            await llegada.Task.WaitAsync(plazo);
        }
        finally
        {
            _esperadas.TryRemove(orden, out _);
        }
    }

    /// <summary>Espera a que el equipo de mentira este —o deje de estar— en antena.</summary>
    /// <param name="enAntena">Lo que se espera.</param>
    /// <param name="plazo">Tope de espera.</param>
    /// <returns>La tarea que termina cuando el PTT queda como se pide.</returns>
    internal async Task EsperarAntenaAsync(bool enAntena, TimeSpan plazo)
    {
        var esperado = enAntena ? "TX1;" : "TX0;";
        if (EnAntena == enAntena)
        {
            return;
        }

        await EsperarOrdenAsync(esperado, plazo);
    }

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
    public ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _escucha.Stop();
        _bucle.Join(TimeSpan.FromSeconds(2));
        _cts.Dispose();
        return ValueTask.CompletedTask;
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

            // Segunda captura, 22-09-2026: el VFO B estaba en LSB.
            ["MD1;"] = "MD11;",
            ["NB1;"] = "NB10;",
            ["AG1;"] = "AG1000;",
            ["PA1;"] = "PA00;",
            ["CN00;"] = "CN00012;",
            ["IS0;"] = "IS00+0000;",
            ["IS1;"] = "IS00+0000;",
            ["OS0;"] = "OS00;",
            ["OS1;"] = "OS10;",
            ["RI0;"] = "RI00000000;",
            ["DT1;"] = "DT1071700;",

            // El equipo contesta al silenciador del segundo VFO con el índice del primero.
            ["SQ1;"] = "SQ0000;",
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

    private void AtenderSiempre()
    {
        while (!_cts.IsCancellationRequested)
        {
            TcpClient cliente;
            try
            {
                cliente = _escucha.AcceptTcpClient();
            }
            catch (Exception)
            {
                return;
            }

            var atencion = new Thread(() => Atender(cliente)) { IsBackground = true, Name = "FT-710 de mentira (sesión)" };
            atencion.Start();
        }
    }

    private void Atender(TcpClient cliente)
    {
        using (cliente)
        {
            var flujo = cliente.GetStream();
            var buzon = new byte[256];
            var pendiente = new StringBuilder();

            while (!_cts.IsCancellationRequested)
            {
                int leidos;
                try
                {
                    leidos = flujo.Read(buzon, 0, buzon.Length);
                }
                catch (Exception)
                {
                    return;
                }

                if (leidos <= 0 || Volatile.Read(ref _apagado) != 0)
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
                    if (respuesta is null)
                    {
                        continue;
                    }

                    try
                    {
                        var bytes = Encoding.ASCII.GetBytes(respuesta);
                        flujo.Write(bytes, 0, bytes.Length);
                        flujo.Flush();
                    }
                    catch (Exception)
                    {
                        return;
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
        if (_esperadas.TryGetValue(orden, out var esperando))
        {
            esperando.TrySetResult();
        }

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
