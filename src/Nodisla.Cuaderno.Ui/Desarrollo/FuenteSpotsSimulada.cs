using System.Globalization;
using System.Net.Sockets;
using System.Text.Json;
using System.Timers;
using Nodisla.Cuaderno.Idiomas;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Integraciones.Cluster;
using Nodisla.Cuaderno.Ui.Ajustes;
using Temporizador = System.Timers.Timer;

namespace Nodisla.Cuaderno.Ui.Desarrollo;

/// <summary>Ajustes con los nodos de muestra del cluster simulado. Nunca se guardan.</summary>
/// <param name="Ajustes">Copia de los ajustes del programa con la lista de nodos de muestra.</param>
public sealed record AjustesDelClusterDeMuestra(AjustesDelPrograma Ajustes);

/// <summary>
/// Un nodo de cluster de DX de mentira que escupe anuncios y charla como el de verdad.
/// </summary>
/// <remarks>
/// <para>
/// Un cluster real manda dos cosas por el mismo hilo: anuncios y todo lo demas —avisos del
/// nodo, mensajes de otros operadores, respuestas a las ordenes—. La interfaz tiene que
/// ensenar las dos, y por eso esta fuente falsa manda las dos tambien: si solo mandara spots,
/// la consola cruda se habria disenado a ciegas.
/// </para>
/// <para>
/// <b>Varios nodos.</b> Cada estacion inventada tiene su sitio fijo en la banda, el mismo en
/// todos los nodos simulados: asi dos nodos anuncian a menudo la misma estacion, como pasa de
/// verdad entre nodos de la misma red, y se ve la fusion. Un nodo cuyo servidor acaba en
/// <c>.invalid</c> (dominio reservado, que no existe) se queda reintentando con el error de
/// un nombre que no resuelve, para que se vea como se pinta un nodo caido.
/// </para>
/// </remarks>
public sealed class FuenteSpotsSimulada : IFuenteSpots, IFuenteConDiagnostico
{
    private static readonly string[] Indicativos =
    [
        "JA1NUT", "VK6LC", "ZL3ABC", "BY1CQ", "9M6XRO", "VU2PTT", "HS0ZIA", "YB0ANN",
        "K1ABC", "W6XYZ", "VE7CQ", "KL7RA", "PY2NY", "LU8EOT", "CE3FZ", "HK3TU",
        "ZS6CCY", "5H3EE", "TZ4AM", "3B8CF", "FR4NT", "V51YJ", "TR8CA", "9G5AR",
        "OH2BH", "SM5EDX", "LA5HE", "DL1XYZ", "G3TXF", "F6BEE", "I4UQF", "EA3KU",
        "T88XX", "KH6CQ", "VP8LP", "FT4YM", "3Y0J", "VK0EK", "ZD8W", "PJ2T",
    ];

    private static readonly string[] Anunciantes =
    [
        "EA1ABC", "F5XYZ", "DL9AB", "G4CQ", "I2ZZZ", "SP9RTT", "OK1DX", "EA8TL",
        "ON4UN", "PA3EWP", "HB9DX", "OE1XA", "K3LR", "W1AW", "VE3NE", "JA1ZLO",
    ];

    private static readonly string[] Escuchas =
    [
        "EA8URL-#", "DL8LAS-#", "G4ZFE-#", "OH6BG-#", "W3OA-#", "VE7CC-#", "SM6FMB-#",
    ];

    private static readonly (decimal Mhz, string? Modo)[] Huecos =
    [
        (3.505m, "CW"), (3.795m, "SSB"), (3.573m, "FT8"),
        (7.005m, "CW"), (7.145m, "SSB"), (7.074m, "FT8"),
        (10.104m, "CW"), (10.136m, "FT8"),
        (14.018m, "CW"), (14.195m, "SSB"), (14.074m, "FT8"), (14.080m, "RTTY"),
        (18.075m, "CW"), (18.130m, "SSB"), (18.100m, "FT8"),
        (21.023m, "CW"), (21.295m, "SSB"), (21.074m, "FT8"),
        (24.895m, "CW"), (24.945m, "SSB"),
        (28.020m, "CW"), (28.490m, "SSB"), (28.074m, "FT8"),
        (50.110m, "SSB"), (50.313m, "FT8"),
    ];

    private static readonly string[] Comentarios =
    [
        "up 2", "QSX 14205", "señal fuerte", "sale por Europa", "pileup enorme",
        "operando hasta las 22z", "QRV 15m después", "trabajando por zonas", "muy débil aquí",
    ];

    private static readonly string[] Localizadores =
    [
        "PM95", "OF78", "RE66", "FN42", "CM87", "GG66", "KG33", "KP20",
        "JO59", "JO62", "IO91", "JN18", "JN54", "JO89", "KO85", "CN89",
        "IL18", "HK78", "FK68", "GF15", "LG78", "MN18", "OK33", "QF56",
    ];

    private static readonly string[] Charla =
    [
        "Bienvenido al nodo simulado de pruebas.",
        "*** Aviso: este cluster no existe; los anuncios son inventados.",
        "EA8TL de EA1ABC: buenas tardes, ¿cómo va esa apertura?",
        "Se han conectado 214 operadores.",
        "*** El nodo se reiniciará a las 04:00z para mantenimiento.",
        "F5XYZ: 3Y0J se oye 59 en 20 metros por camino largo.",
    ];

    private readonly Temporizador _relojDeSpots;
    private readonly Temporizador _relojDeCharla = new(9_000) { AutoReset = true };
    private readonly Random _azar;
    private readonly Indicativo _propio;
    private readonly string? _nombre;
    private readonly bool _falla;
    private readonly bool _esSkimmer;

    private int _charlaDicha;
    private bool _liberado;

    /// <summary>Monta un nodo falso suelto, con el nombre de siempre.</summary>
    /// <param name="indicativoPropio">Indicativo con el que se anuncia la conexion.</param>
    public FuenteSpotsSimulada(string indicativoPropio = "EA8DLF")
        : this(null, indicativoPropio, 20_260_921, falla: false, esSkimmer: false)
    {
    }

    /// <summary>Monta el nodo falso que hace las veces de un nodo de la lista.</summary>
    /// <param name="opciones">Opciones del nodo: nombre, servidor y si es de escucha automatica.</param>
    public FuenteSpotsSimulada(OpcionesCluster opciones)
        : this(
            (opciones ?? throw new ArgumentNullException(nameof(opciones))).Nombre,
            opciones.IndicativoDeAcceso.Length > 0 ? opciones.IndicativoDeAcceso : "EA8DLF",
            Semilla(opciones.Clave),
            falla: opciones.Servidor.EndsWith(".invalid", StringComparison.OrdinalIgnoreCase),
            esSkimmer: opciones.EsSkimmer)
    {
    }

    private FuenteSpotsSimulada(string? nombre, string indicativoPropio, int semilla, bool falla, bool esSkimmer)
    {
        _nombre = nombre;
        _propio = Indicativo.Crudo(indicativoPropio);
        _azar = new Random(semilla);
        _falla = falla;
        _esSkimmer = esSkimmer;

        // Cada nodo a su ritmo, y el de escucha automatica mas deprisa, como el de verdad.
        var intervalo = esSkimmer ? 900 : 1_400 + (semilla % 7 * 150);
        _relojDeSpots = new Temporizador(intervalo) { AutoReset = true };
        _relojDeSpots.Elapsed += AlTocarSpot;
        _relojDeCharla.Elapsed += AlTocarCharla;
    }

    /// <inheritdoc />
    public event EventHandler<EstadoDeConexion>? EstadoCambiado;

    /// <inheritdoc />
    public event EventHandler<Spot>? SpotRecibido;

    /// <inheritdoc />
    public event EventHandler<string>? LineaRecibida;

    /// <inheritdoc />
    public string Nombre => _nombre ?? Textos.T("Dialogos.Simulado.Cluster");

    /// <inheritdoc />
    public EstadoDeConexion Estado { get; private set; } = EstadoDeConexion.Desconectado;

    /// <inheritdoc />
    public string? UltimoError { get; private set; }

    /// <summary>
    /// Copia de los ajustes con varios nodos de muestra, si los de verdad solo tienen uno.
    /// </summary>
    /// <remarks>
    /// Es una copia: los ajustes del operador no se tocan, ni en memoria ni en disco. Lleva
    /// el nodo que hubiera, dos mas que se conectan, uno de escucha automatica, uno caido y uno
    /// desactivado: todos los estados que hay que ver en pantalla.
    /// </remarks>
    /// <param name="reales">Ajustes del programa.</param>
    /// <returns>Una copia con la lista de nodos de muestra.</returns>
    public static AjustesDelPrograma ConNodosDeMuestra(AjustesDelPrograma reales)
    {
        ArgumentNullException.ThrowIfNull(reales);

        reales.Cluster.Migrar();
        var cluster = JsonSerializer.Deserialize<AjustesDeCluster>(JsonSerializer.Serialize(reales.Cluster))
            ?? new AjustesDeCluster();
        cluster.Migrar();

        if (cluster.Nodos.Count <= 1)
        {
            cluster.Nodos.Add(DeLaLista("dxfun.com", 8000, "muestra-dxfun"));
            cluster.Nodos.Add(DeLaLista("telnet.reversebeacon.net", 7000, "muestra-rbn"));
            cluster.Nodos.Add(new AjustesDeNodoDeCluster
            {
                Id = "muestra-caido",
                Nombre = Textos.T("Dialogos.Simulado.NodoCaido"),
                Servidor = "nodo-caido.invalid",
                Puerto = 7300,
            });
            var apagado = DeLaLista("hamqth.com", 7300, "muestra-hamqth");
            apagado.Activo = false;
            cluster.Nodos.Add(apagado);
        }

        return new AjustesDelPrograma { Cluster = cluster };

        static AjustesDeNodoDeCluster DeLaLista(string servidor, int puerto, string id)
        {
            var conocido = NodosConocidos.Buscar(servidor, puerto)!;
            return new AjustesDeNodoDeCluster
            {
                Id = id,
                Nombre = conocido.Nombre,
                Servidor = conocido.Servidor,
                Puerto = conocido.Puerto,
                EsSkimmer = conocido.EsSkimmer,
                GuionDeArranque = [.. conocido.GuionRecomendado],
            };
        }
    }

    /// <summary>
    /// «Probar» de mentira: contesta sin abrir ningun puerto.
    /// </summary>
    /// <param name="servidor">Maquina.</param>
    /// <param name="puerto">Puerto.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <returns>Que responde, salvo los <c>.invalid</c>.</returns>
    public static async Task<PruebaDeNodo> ProbarAsync(string servidor, int puerto, CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(250), ct).ConfigureAwait(false);
        return servidor.EndsWith(".invalid", StringComparison.OrdinalIgnoreCase)
            ? new PruebaDeNodo(false, TimeSpan.FromMilliseconds(40), null, new SocketException((int)SocketError.HostNotFound).Message)
            : new PruebaDeNodo(true, TimeSpan.FromMilliseconds(180), "login:", null);
    }

    /// <inheritdoc />
    public async Task ConectarAsync(CancellationToken ct = default)
    {
        Cambiar(EstadoDeConexion.Conectando);

        // Un poco de espera para que se vea el estado «conectando», como pasa de verdad.
        await Task.Delay(TimeSpan.FromMilliseconds(600), ct).ConfigureAwait(false);

        if (_falla)
        {
            // Como un nodo cuyo nombre no resuelve: se queda reintentando, con su motivo.
            UltimoError = new SocketException((int)SocketError.HostNotFound).Message;
            LineaRecibida?.Invoke(this, Textos.F("Servicios.Cluster.ConexionPerdida", Nombre, UltimoError));
            Cambiar(EstadoDeConexion.Reintentando);
            return;
        }

        UltimoError = null;
        Cambiar(EstadoDeConexion.Conectado);
        LineaRecibida?.Invoke(this, $"Conectado como {_propio.Valor} al nodo simulado.");

        _relojDeSpots.Start();
        if (!_esSkimmer) _relojDeCharla.Start();
    }

    /// <inheritdoc />
    public Task DesconectarAsync(CancellationToken ct = default)
    {
        _relojDeSpots.Stop();
        _relojDeCharla.Stop();
        Cambiar(EstadoDeConexion.Desconectado);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task EnviarAsync(string orden, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (Estado != EstadoDeConexion.Conectado)
        {
            throw new InvalidOperationException(Textos.F("Servicios.Cluster.OrdenSinConexion", Nombre));
        }

        // Un cluster contesta a todo, aunque sea para decir que no entiende.
        LineaRecibida?.Invoke(this, $"> {orden}");
        LineaRecibida?.Invoke(
            this,
            orden.StartsWith("sh/dx", StringComparison.OrdinalIgnoreCase)
                ? "No hay anuncios guardados en el nodo simulado."
                : $"Orden «{orden}» aceptada por el nodo simulado.");

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (_liberado) return ValueTask.CompletedTask;
        _liberado = true;

        _relojDeSpots.Elapsed -= AlTocarSpot;
        _relojDeCharla.Elapsed -= AlTocarCharla;
        _relojDeSpots.Dispose();
        _relojDeCharla.Dispose();
        return ValueTask.CompletedTask;
    }

    /// <summary>Semilla estable sacada del identificador, para que cada nodo tenga su azar.</summary>
    private static int Semilla(string clave)
    {
        var suma = 17;
        foreach (var c in clave) suma = unchecked((suma * 31) + c);
        return Math.Abs(suma % 1_000_000) + 1;
    }

    private void AlTocarSpot(object? origen, ElapsedEventArgs e)
    {
        if (_liberado) return;

        // Cada estacion vive en su hueco de banda, el mismo en todos los nodos: asi los nodos
        // se pisan anunciando a la misma, como de verdad.
        var cual = _azar.Next(Indicativos.Length);
        var (mhz, modo) = Huecos[cual * 7 % Huecos.Length];

        // La escucha automatica solo oye telegrafia y modos digitales.
        if (_esSkimmer && modo is not ("CW" or "FT8" or "RTTY")) return;

        // Se mueve unos hercios el dial: dos anuncios de la misma estacion nunca dan la misma
        // frecuencia exacta, y la lista tiene que soportarlo.
        var frecuencia = Frecuencia.DesdeMegahercios(
            decimal.Round(mhz + ((decimal)(_azar.NextDouble() - 0.5) * 0.0008m), 5));

        // Una de cada tres veces el anuncio lo pone una estacion automatica de escucha, que
        // es mas o menos la proporcion de un cluster de verdad a media tarde.
        var automatico = _esSkimmer || _azar.Next(3) == 0;
        var referencia = _azar.Next(8) == 0
            ? new[] { new ReferenciaAnunciada(TipoDeReferencia.Pota, "EA8-0012") }
            : _azar.Next(11) == 0
                ? new[] { new ReferenciaAnunciada(TipoDeReferencia.Sota, "EA8/GC-001") }
                : [];

        var spot = new Spot(
            Indicativo.Crudo(Indicativos[cual]),
            frecuencia,
            Indicativo.Crudo(automatico ? Escuchas[_azar.Next(Escuchas.Length)] : Anunciantes[_azar.Next(Anunciantes.Length)]),
            !automatico && _azar.Next(3) == 0 ? Comentarios[_azar.Next(Comentarios.Length)] : null,
            DateTimeOffset.UtcNow,
            Nombre)
        {
            ModoAnunciado = Modo.Crudo(modo),
            Locator = _azar.Next(2) == 0 ? Locator.Parse(Localizadores[_azar.Next(Localizadores.Length)]) : Locator.Vacio,
            Referencias = referencia,
            EsDeEscuchaAutomatica = automatico,
            PalabrasPorMinuto = modo == "CW" ? _azar.Next(14, 38) : null,
            Decibelios = modo is "FT8" or "CW" ? _azar.Next(-22, 20) : null,
        };

        SpotRecibido?.Invoke(this, spot);
    }

    private void AlTocarCharla(object? origen, ElapsedEventArgs e)
    {
        if (_liberado) return;

        var hora = DateTimeOffset.UtcNow.ToString("HH:mm", CultureInfo.InvariantCulture);
        LineaRecibida?.Invoke(this, $"{hora}z  {Charla[_charlaDicha++ % Charla.Length]}");
    }

    private void Cambiar(EstadoDeConexion estado)
    {
        Estado = estado;
        EstadoCambiado?.Invoke(this, estado);
    }
}
