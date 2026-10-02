using Nodisla.Cuaderno.Idiomas;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Ui.Desarrollo;

/// <summary>
/// El modem propio, de mentira: pinta una cascada creible y saca decodificaciones inventadas.
/// </summary>
/// <remarks>
/// <para>
/// Sirve para ver y capturar la pestana Digital entera sin radio, sin antena y sin abrirle la
/// tarjeta de sonido a nadie. Lo que sale de aqui recorre <b>el mismo camino</b> que lo que
/// saldra del modem de verdad: los mismos eventos, los mismos registros y la misma pantalla.
/// </para>
/// <para>
/// <b>No emite.</b> <see cref="EmitirAsync"/> se niega en redondo, igual que hace el modem de
/// verdad cuando no tiene salida de audio ni vigilante. Un modem simulado que fingiera
/// transmitir seria justo el sitio donde se colaria una transmision de verdad el dia que
/// alguien confundiera el registro de servicios.
/// </para>
/// </remarks>
public sealed class ModemPropioSimulado : IModemPropio
{
    /// <summary>Cada cuanto sale una columna de cascada.</summary>
    private static readonly TimeSpan PasoDeLaCascada = TimeSpan.FromMilliseconds(170);

    /// <summary>Casillas de frecuencia que se pintan: las mismas que el modem de verdad.</summary>
    private const int Casillas = 1366;

    /// <summary>Anchura de cada casilla, con 16.384 muestras a 48.000 por segundo.</summary>
    private const double HzPorCasilla = 48000.0 / 16384.0;

    /// <summary>Ventanas pasadas que se sueltan al arrancar, para no ver una lista vacia.</summary>
    private const int VentanasDeArranque = 3;

    private readonly Random _azar = new(20260926);
    private readonly float[] _columna = new float[Casillas];

    private CancellationTokenSource? _parada;
    private Task _tarea = Task.CompletedTask;

    /// <summary>Senales que se ven en la cascada: tono en hercios y fuerza en decibelios.</summary>
    private static readonly (int TonoHz, double Fuerza)[] Senales =
    [
        (520, 26), (843, 18), (1240, 34), (1502, 12), (1890, 22), (2310, 8), (2640, 30),
    ];

    /// <summary>Lo que se «decodifica». Incluye una rescatada, que es lo que hay que ver.</summary>
    private static readonly (string Texto, int Db, double Desfase, int Tono, bool Profunda)[] Guion =
    [
        ("CQ EA5XYZ IM98", -7, 0.2, 520, false),
        ("EA8DLF IZ2ABC JN45", -12, 0.1, 843, false),
        ("CQ DX LU7HN GF05", -15, -0.3, 1240, false),
        ("K1ABC W9XYZ EN37", -18, 0.4, 1502, false),
        ("CQ VK3QQQ QF22", -21, 0.2, 1890, false),
        ("EA8DLF PY2ZZZ -14", -23, -0.2, 2310, true),
        ("CQ JA1AAA PM95", -24, 0.5, 2640, true),
    ];

    /// <inheritdoc />
    public ModoDelModem Modo { get; private set; } = ModoDelModem.Ft8;

    /// <inheritdoc />
    public bool EstaEscuchando => _parada is { IsCancellationRequested: false };

    /// <inheritdoc />
    public bool EstaEmitiendo => false;

    /// <inheritdoc />
    public Frecuencia FrecuenciaDelDial { get; set; } = Frecuencia.DesdeMegahercios(14.074m);

    /// <inheritdoc />
    public event EventHandler<ColumnaDeCascada>? CascadaActualizada;

    /// <inheritdoc />
    public event EventHandler<VentanaDecodificada>? VentanaLista;

    /// <inheritdoc />
    public async Task EscucharAsync(ModoDelModem modo, CancellationToken ct = default)
    {
        await PararAsync(ct).ConfigureAwait(false);

        Modo = modo;
        _parada = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _tarea = Task.Run(() => MolerAsync(_parada.Token), CancellationToken.None);
    }

    /// <inheritdoc />
    public async Task PararAsync(CancellationToken ct = default)
    {
        if (_parada is not null) await _parada.CancelAsync().ConfigureAwait(false);

        try { await _tarea.ConfigureAwait(false); }
        catch (OperationCanceledException) { /* Se estaba parando; es lo esperado. */ }

        _parada?.Dispose();
        _parada = null;
        _tarea = Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task EmitirAsync(string texto, int tonoHz, CancellationToken ct = default) =>
        throw new InvalidOperationException(Textos.T("Dialogos.Simulado.ModemNoTransmite"));

    /// <inheritdoc />
    public Task AbortarEmisionAsync(CancellationToken ct = default) => Task.CompletedTask;

    /// <inheritdoc />
    public Task<IReadOnlyList<DecodificacionPropia>> DecodificarFicheroAsync(
        string rutaWav, ModoDelModem modo, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<DecodificacionPropia>>([]);

    /// <inheritdoc />
    public async ValueTask DisposeAsync() => await PararAsync().ConfigureAwait(false);

    private async Task MolerAsync(CancellationToken ct)
    {
        var periodo = TimeSpan.FromSeconds(Modo == ModoDelModem.Ft8 ? 15 : 7.5);
        var ahora = DateTimeOffset.UtcNow;

        // Unas cuantas ventanas de hace un rato, para que la lista no empiece en blanco. En
        // el modem de verdad esto no existe: hay que esperar al primer periodo.
        for (var i = VentanasDeArranque; i > 0; i--)
        {
            SoltarVentana(Alinear(ahora - (periodo * i), periodo), semilla: i);
        }

        var siguiente = Alinear(ahora, periodo) + periodo;

        using var latido = new PeriodicTimer(PasoDeLaCascada);
        var vuelta = 0;

        while (await latido.WaitForNextTickAsync(ct).ConfigureAwait(false))
        {
            SoltarColumna(vuelta++);

            var instante = DateTimeOffset.UtcNow;
            if (instante < siguiente) continue;

            SoltarVentana(siguiente - periodo, semilla: vuelta);
            siguiente += periodo;
        }
    }

    private static DateTimeOffset Alinear(DateTimeOffset instante, TimeSpan periodo)
    {
        var pulsos = instante.ToUniversalTime().UtcTicks;
        return new DateTimeOffset(pulsos - (pulsos % periodo.Ticks), TimeSpan.Zero);
    }

    private void SoltarColumna(int vuelta)
    {
        if (CascadaActualizada is null) return;

        for (var i = 0; i < Casillas; i++)
        {
            _columna[i] = -78f + (float)(_azar.NextDouble() * 5.0);
        }

        foreach (var (tono, fuerza) in Senales)
        {
            // Las senales van y vienen: cada una respira a su ritmo, que es lo que hace que
            // una cascada parezca una cascada y no un dibujo fijo.
            var respiracion = 0.5 + (0.5 * Math.Sin((vuelta * 0.07) + (tono * 0.01)));
            var centro = (int)(tono / HzPorCasilla);
            var ancho = (int)(50 / HzPorCasilla);

            for (var i = Math.Max(0, centro - ancho); i < Math.Min(Casillas, centro + ancho); i++)
            {
                var caida = 1.0 - (Math.Abs(i - centro) / (double)ancho);
                _columna[i] += (float)(fuerza * respiracion * caida * caida);
            }
        }

        CascadaActualizada.Invoke(
            this,
            new ColumnaDeCascada(_columna.AsMemory().ToArray(), HzPorCasilla, DateTimeOffset.UtcNow));
    }

    private void SoltarVentana(DateTimeOffset ventana, int semilla)
    {
        if (VentanaLista is null) return;

        var cuantas = 4 + (Math.Abs(semilla) % (Guion.Length - 3));
        var lista = new List<DecodificacionPropia>(cuantas);

        for (var i = 0; i < cuantas; i++)
        {
            var (texto, db, desfase, tono, profunda) = Guion[(i + semilla) % Guion.Length];
            lista.Add(Componer(texto, db, desfase, tono, profunda, ventana));
        }

        VentanaLista.Invoke(this, new VentanaDecodificada(
            ventana,
            lista,
            TimeSpan.FromMilliseconds(900 + (semilla % 5 * 120)),
            RuidoDbm: -118)
        {
            LlegoTarde = false,
        });
    }

    private DecodificacionPropia Componer(
        string texto, int db, double desfase, int tono, bool profunda, DateTimeOffset ventana)
    {
        var partes = texto.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var esCq = partes.Length > 0 && string.Equals(partes[0], "CQ", StringComparison.Ordinal);

        var llamado = !esCq && partes.Length > 0 && Indicativo.TryParse(partes[0], out var quien)
            ? quien
            : Indicativo.Vacio;

        var indiceDelLlamante = esCq
            ? (partes.Length > 2 && partes[1] == "DX" ? 2 : 1)
            : 1;

        var llamante = partes.Length > indiceDelLlamante
                       && Indicativo.TryParse(partes[indiceDelLlamante], out var quienLlama)
            ? quienLlama
            : Indicativo.Vacio;

        var locator = Locator.TryParse(partes[^1], out var rejilla) ? rejilla : Locator.Vacio;

        return new DecodificacionPropia(texto, db, desfase, tono, Modo, ventana)
        {
            Llamante = llamante,
            Llamado = llamado,
            Locator = locator,
            EsCq = esCq,
            EsRecuperacionProfunda = profunda,
        };
    }
}
