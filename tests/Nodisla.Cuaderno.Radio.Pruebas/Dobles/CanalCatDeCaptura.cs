using Nodisla.Cuaderno.Radio.Control.Ft710;

namespace Nodisla.Cuaderno.Radio.Pruebas.Dobles;

/// <summary>
/// Canal CAT en memoria que contesta con las respuestas <b>reales</b> del FT-710 de EA8DLF.
/// </summary>
/// <remarks>
/// <para>
/// Las respuestas salen de <c>Capturas/ft710-2026-09-27.tsv</c>, capturado en solo lectura con
/// la radio encendida. No hay red, ni hilos, ni esperas: cada pregunta se contesta en el acto,
/// asi que las pruebas no dependen del reloj.
/// </para>
/// <para>
/// Las escrituras que la interfaz puede pedir (<c>FA</c>/<c>FB</c> con nueve cifras,
/// <c>VS0</c>/<c>VS1</c>, <c>MD0x</c>/<c>MD1x</c>, <c>AB</c>, <c>BA</c>, <c>SV</c>) cambian lo
/// que se contesta despues, como haria el equipo. Lo que no esta en la captura se contesta
/// <c>?;</c>, igual que el firmware.
/// </para>
/// </remarks>
internal sealed class CanalCatDeCaptura : ICanalCat
{
    /// <summary>Fichero con la captura real del 27-09-2026.</summary>
    internal const string Captura = "ft710-2026-09-27.tsv";

    private readonly object _candado = new();
    private readonly Dictionary<string, string?> _respuestas = new(StringComparer.Ordinal);
    private readonly List<string> _recibidas = [];

    /// <summary>
    /// Captura del 28-09-2026 (estado de la radio antes de pulsar los botones). Completa la del
    /// 27-09 con las consultas que aquella no tenia (CF, SS, SF, DA, FN, LK…), sin pisar ninguna.
    /// </summary>
    internal const string CapturaDeLosBotones = "ft710-2026-09-28.tsv";

    private readonly Dictionary<string, Queue<string?>> _secuencias = new(StringComparer.Ordinal);

    internal CanalCatDeCaptura(string captura = Captura)
    {
        foreach (var (orden, respuesta) in LeerCaptura(captura))
        {
            _respuestas[orden] = respuesta;
        }

        if (captura == Captura)
        {
            foreach (var (orden, respuesta) in LeerCaptura(CapturaDeLosBotones))
            {
                _respuestas.TryAdd(orden, respuesta);
            }
        }
    }

    /// <summary>Todas las consultas que se han hecho de verdad a la radio, de las dos capturas.</summary>
    /// <returns>Las ordenes.</returns>
    internal static IReadOnlySet<string> ConsultasCapturadas() =>
        LeerCaptura(Captura).Concat(LeerCaptura(CapturaDeLosBotones)).Select(p => p.Orden).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Hace que una consulta conteste, por turno, lo que contesto la radio en una secuencia real
    /// (por ejemplo <c>RI0;</c> mientras el acoplador sintoniza). Agotada, sigue con la ultima.
    /// </summary>
    /// <param name="orden">Consulta con punto y coma.</param>
    /// <param name="respuestas">Respuestas con punto y coma, en orden.</param>
    internal void ResponderEnSecuencia(string orden, IEnumerable<string?> respuestas)
    {
        lock (_candado)
        {
            _secuencias[orden] = new Queue<string?>(respuestas);
        }
    }

    /// <inheritdoc />
    public bool Abierto { get; private set; }

    /// <inheritdoc />
    public string Descripcion => "captura del FT-710 real";

    /// <summary>Ordenes recibidas, en orden, preguntas y escrituras.</summary>
    internal IReadOnlyList<string> Recibidas
    {
        get
        {
            lock (_candado)
            {
                return _recibidas.ToList();
            }
        }
    }

    /// <summary>Lee las parejas orden-respuesta de un fichero de captura.</summary>
    /// <param name="captura">Nombre del fichero dentro de <c>Capturas</c>.</param>
    /// <returns>Cada orden con su respuesta tal cual, o nulo si no contesto.</returns>
    internal static IReadOnlyList<(string Orden, string? Respuesta)> LeerCaptura(string captura = Captura)
    {
        var ruta = Path.Combine(AppContext.BaseDirectory, "Capturas", captura);
        var parejas = new List<(string, string?)>();
        foreach (var linea in File.ReadAllLines(ruta))
        {
            if (linea.Length == 0 || linea.StartsWith('#'))
            {
                continue;
            }

            var partes = linea.Split('\t');
            parejas.Add((partes[0], partes[1] == "(sin respuesta)" ? null : partes[1]));
        }

        return parejas;
    }

    /// <summary>Cambia lo que se contesta a una consulta.</summary>
    /// <param name="orden">Consulta con punto y coma.</param>
    /// <param name="respuesta">Respuesta con punto y coma, o nulo para no contestar.</param>
    internal void Responder(string orden, string? respuesta)
    {
        lock (_candado)
        {
            _respuestas[orden] = respuesta;
        }
    }

    /// <inheritdoc />
    public Task AbrirAsync(CancellationToken ct = default)
    {
        Abierto = true;
        Interlocked.Increment(ref _aperturas);
        return Task.CompletedTask;
    }

    private int _aperturas;

    /// <summary>Veces que alguien ha abierto el puerto.</summary>
    internal int Aperturas => Volatile.Read(ref _aperturas);

    /// <summary>Rompe el canal, como un cable que se suelta: queda cerrado.</summary>
    internal void Romper() => Abierto = false;

    /// <inheritdoc />
    public Task<string?> PreguntarAsync(string orden, CancellationToken ct = default)
    {
        OrdenesFt710.ComprobarQueEsSegura(orden);
        if (Interlocked.Exchange(ref _parada, null) is { } parada)
        {
            if (parada.Orden == orden)
            {
                // Se para esta consulta hasta que la prueba la suelte: asi se ve que pasa si otra
                // tarea (el sondeo) esta a medio leer cuando se escribe algo.
                return ContestarDespuesAsync(orden, parada.Suelta.Task);
            }

            Interlocked.Exchange(ref _parada, parada);
        }

        return Contestar(orden);
    }

    private sealed record Parada(string Orden, TaskCompletionSource Llegada, TaskCompletionSource Suelta);

    private Parada? _parada;

    /// <summary>
    /// La proxima vez que llegue <paramref name="orden"/>, se queda esperando hasta que se
    /// complete la tarea devuelta por <see cref="Soltar"/>. Devuelve la tarea que se completa
    /// cuando la consulta llega.
    /// </summary>
    /// <param name="orden">Consulta con punto y coma.</param>
    /// <returns>Tarea que se completa al llegar la consulta.</returns>
    internal Task PararEn(string orden)
    {
        var parada = new Parada(
            orden,
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously),
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        _ultimaParada = parada;
        _parada = parada;
        return parada.Llegada.Task;
    }

    private Parada? _ultimaParada;

    /// <summary>Suelta la consulta parada con <see cref="PararEn"/>.</summary>
    internal void Soltar() => _ultimaParada?.Suelta.TrySetResult();

    private async Task<string?> ContestarDespuesAsync(string orden, Task suelta)
    {
        // La respuesta es la de ANTES de soltar, como la que ya iba por el cable.
        var respuesta = await Contestar(orden).ConfigureAwait(false);
        _ultimaParada?.Llegada.TrySetResult();
        await suelta.ConfigureAwait(false);
        return respuesta;
    }

    private Task<string?> Contestar(string orden)
    {
        lock (_candado)
        {
            _recibidas.Add(orden);
            if (_secuencias.TryGetValue(orden, out var cola) && cola.Count > 0)
            {
                var siguiente = cola.Dequeue();
                if (cola.Count == 0)
                {
                    _respuestas[orden] = siguiente;
                }

                return Task.FromResult(siguiente?.TrimEnd(';'));
            }

            var respuesta = _respuestas.TryGetValue(orden, out var r) ? r : "?;";
            return Task.FromResult(respuesta?.TrimEnd(';'));
        }
    }

    /// <inheritdoc />
    public Task MandarAsync(string orden, CancellationToken ct = default)
    {
        OrdenesFt710.ComprobarQueEsSegura(orden);

        // Como el puerto serie de verdad: por un canal cerrado no sale nada.
        if (!Abierto)
        {
            return Task.FromException(new InvalidOperationException($"El canal {Descripcion} no está abierto."));
        }

        lock (_candado)
        {
            _recibidas.Add(orden);
            Aplicar(orden);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void MandarSincrono(string orden)
    {
        lock (_candado)
        {
            _recibidas.Add(orden);
        }
    }

    /// <inheritdoc />
    public void Cerrar() => Abierto = false;

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        Abierto = false;
        return ValueTask.CompletedTask;
    }

    private void Aplicar(string orden)
    {
        var cuerpo = orden.TrimEnd(';');
        switch (cuerpo)
        {
            case ['F', 'A' or 'B', ..] when cuerpo.Length == 11:
                _respuestas[cuerpo[..2] + ";"] = orden;
                return;
            case "VS0" or "VS1":
                _respuestas["VS;"] = orden;
                return;
            case ['M', 'D', '0' or '1', _]:
                _respuestas[cuerpo[..3] + ";"] = orden;
                return;
            case "AB":
                Copiar(de: 'A', a: 'B');
                return;
            case "BA":
                Copiar(de: 'B', a: 'A');
                return;
            case "SV":
                // En la radio real (28-09-2026) SV NO intercambia FA y FB: cambia el VFO con el
                // que se opera, VS0 <-> VS1.
                _respuestas["VS;"] = _respuestas["VS;"] == "VS1;" ? "VS0;" : "VS1;";
                return;
            case ['S', 'S', '0', '6', var modo, ..]:
                // Los modos ampliados se leen distinto de como se escriben (visto en la radio).
                var leido = modo switch { '3' => '5', '6' => '8', '9' => 'B', _ => modo };
                _respuestas["SS06;"] = $"SS06{leido}0000;";
                return;
            case ['S', 'T', '1']:
                // Visto en la radio: poner SPLIT apaga el clarificador de recepcion.
                _respuestas["ST;"] = orden;
                _respuestas["CF000;"] = "CF00000000;";
                return;
        }

        // Cualquier otra escritura deja su valor como respuesta a la consulta mas larga que le
        // sirve de prefijo, como hace el equipo: «NB01;» -> «NB0;» contesta «NB01;».
        var consulta = _respuestas.Keys
            .Select(k => k.TrimEnd(';'))
            .Where(k => cuerpo.StartsWith(k, StringComparison.Ordinal) && cuerpo.Length > k.Length)
            .OrderByDescending(k => k.Length)
            .FirstOrDefault();
        if (consulta is not null && !cuerpo.StartsWith("TX", StringComparison.Ordinal))
        {
            _respuestas[consulta + ";"] = orden;
        }
    }

    private void Copiar(char de, char a)
    {
        var desde = de == 'A' ? '0' : '1';
        var hacia = a == 'A' ? '0' : '1';
        _respuestas[$"F{a};"] = $"F{a}" + _respuestas[$"F{de};"]![2..];
        _respuestas[$"MD{hacia};"] = $"MD{hacia}" + _respuestas[$"MD{desde};"]![3..];
    }
}
