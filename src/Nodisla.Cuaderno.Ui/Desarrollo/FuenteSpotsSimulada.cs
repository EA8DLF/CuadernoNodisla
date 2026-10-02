using System.Globalization;
using System.Timers;
using Nodisla.Cuaderno.Idiomas;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Dominio.Valores;
using Temporizador = System.Timers.Timer;

namespace Nodisla.Cuaderno.Ui.Desarrollo;

/// <summary>
/// Un cluster de DX de mentira que escupe anuncios y charla como el de verdad.
/// </summary>
/// <remarks>
/// Un cluster real manda dos cosas por el mismo hilo: anuncios y todo lo demas —avisos del
/// nodo, mensajes de otros operadores, respuestas a las ordenes—. La interfaz tiene que
/// ensenar las dos, y por eso esta fuente falsa manda las dos tambien: si solo mandara spots,
/// la consola cruda se habria disenado a ciegas.
/// </remarks>
public sealed class FuenteSpotsSimulada : IFuenteSpots, IAsyncDisposable
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

    private readonly Temporizador _relojDeSpots = new(1_400) { AutoReset = true };
    private readonly Temporizador _relojDeCharla = new(9_000) { AutoReset = true };
    private readonly Random _azar = new(20_260_921);
    private readonly Indicativo _propio;

    private int _charlaDicha;
    private bool _liberado;

    /// <summary>Monta la fuente falsa.</summary>
    /// <param name="indicativoPropio">Indicativo con el que se anuncia la conexion.</param>
    public FuenteSpotsSimulada(string indicativoPropio = "EA8DLF")
    {
        _propio = Indicativo.Crudo(indicativoPropio);
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
    public string Nombre => Textos.T("Dialogos.Simulado.Cluster");

    /// <inheritdoc />
    public EstadoDeConexion Estado { get; private set; } = EstadoDeConexion.Desconectado;

    /// <inheritdoc />
    public async Task ConectarAsync(CancellationToken ct = default)
    {
        Cambiar(EstadoDeConexion.Conectando);

        // Un poco de espera para que se vea el estado «conectando», como pasa de verdad.
        await Task.Delay(TimeSpan.FromMilliseconds(600), ct).ConfigureAwait(false);

        Cambiar(EstadoDeConexion.Conectado);
        LineaRecibida?.Invoke(this, $"Conectado como {_propio.Valor} al nodo simulado.");

        _relojDeSpots.Start();
        _relojDeCharla.Start();
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

    private void AlTocarSpot(object? origen, ElapsedEventArgs e)
    {
        if (_liberado) return;

        var (mhz, modo) = Huecos[_azar.Next(Huecos.Length)];

        // Se mueve unos hercios el dial: dos anuncios de la misma estacion nunca dan la misma
        // frecuencia exacta, y la lista tiene que soportarlo.
        var frecuencia = Frecuencia.DesdeMegahercios(
            decimal.Round(mhz + ((decimal)(_azar.NextDouble() - 0.5) * 0.004m), 5));

        // Una de cada tres veces el anuncio lo pone una estacion automatica de escucha, que
        // es mas o menos la proporcion de un cluster de verdad a media tarde.
        var automatico = _azar.Next(3) == 0;
        var referencia = _azar.Next(8) == 0
            ? new[] { new ReferenciaAnunciada(TipoDeReferencia.Pota, "EA8-0012") }
            : _azar.Next(11) == 0
                ? new[] { new ReferenciaAnunciada(TipoDeReferencia.Sota, "EA8/GC-001") }
                : [];

        var spot = new Spot(
            Indicativo.Crudo(Indicativos[_azar.Next(Indicativos.Length)]),
            frecuencia,
            Indicativo.Crudo(automatico ? "EA8URL-#" : Anunciantes[_azar.Next(Anunciantes.Length)]),
            _azar.Next(3) == 0 ? Comentarios[_azar.Next(Comentarios.Length)] : null,
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
