using Nodisla.Cuaderno.Aplicacion.Puertos;

namespace Nodisla.Cuaderno.Ui.Desarrollo;

/// <summary>
/// Una entrada de audio de mentira: lista dispositivos inventados y no abre ninguna tarjeta.
/// </summary>
/// <remarks>
/// <para>
/// Existe para poder ver y capturar el apartado de audio sin abrirle el microfono a nadie.
/// Abrir aqui no toca la tarjeta de sonido: solo se apunta cual se habria abierto, y desde
/// entonces se inventa el audio de recepcion (ruido y dos tonos, bloques de 20 ms a 48 kHz)
/// para que el osciloscopio y el AF-FFT de MULTI se vean funcionando.
/// </para>
/// <para>
/// El nivel que devuelve es alto <b>a proposito</b>: 0,94, que es la zona en la que estaba el
/// codec de esta estacion. Asi el medidor y su aviso de saturacion se ven funcionando en vez
/// de quedarse en un caso bonito que no prueba nada.
/// </para>
/// </remarks>
public sealed class EntradaDeAudioSimulada : IEntradaDeAudio
{
    /// <summary>Nivel que devuelve mientras esta abierta: el que tenia esta estacion.</summary>
    public const double NivelSimulado = 0.94;

    private static readonly IReadOnlyList<DispositivoDeAudio> Inventados =
    [
        new("simulado-ft710-entrada", "Codec USB del FT-710 (simulado)", EsDeEntrada: true, EsDelEquipo: true),
        new("simulado-micro", "Micrófono de la placa base (simulado)", EsDeEntrada: true, EsDelEquipo: false),
        new("simulado-cable", "Cable virtual de audio (simulado)", EsDeEntrada: true, EsDelEquipo: false),
    ];

    /// <inheritdoc />
    public IReadOnlyList<DispositivoDeAudio> Dispositivos => Inventados;

    /// <inheritdoc />
    public DispositivoDeAudio? Abierto { get; private set; }

    /// <inheritdoc />
    public double Nivel => Abierto is null ? 0 : NivelSimulado;

    /// <inheritdoc />
    public event EventHandler<BloqueDeAudio>? BloqueCapturado;

    /// <inheritdoc />
    public event EventHandler<HuecoDeAudio>? MuestrasPerdidas;

    private readonly Random _azar = new(48_000);
    private Timer? _reloj;
    private long _muestra;
    private int _frecuencia = 48_000;

    /// <inheritdoc />
    public Task AbrirAsync(string idDispositivo, int frecuenciaDeMuestreo = 48000, CancellationToken ct = default)
    {
        Abierto = Inventados.FirstOrDefault(d => d.Id == idDispositivo) ?? Inventados[0];
        _frecuencia = frecuenciaDeMuestreo > 0 ? frecuenciaDeMuestreo : 48_000;
        _llave700 = _llave940 = null;

        // Nunca hay huecos: no hay tarjeta que se atasque.
        _ = MuestrasPerdidas;
        if (!SinReloj) _reloj ??= new Timer(_ => Inventar(), null, TimeSpan.Zero, TimeSpan.FromMilliseconds(20));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task CerrarAsync(CancellationToken ct = default)
    {
        Parar();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        Parar();
        return ValueTask.CompletedTask;
    }

    private void Parar()
    {
        Abierto = null;
        _reloj?.Dispose();
        _reloj = null;
    }

    /// <summary>Lo que se oye en telegrafía a 700 Hz, en bucle (22 WPM).</summary>
    public const string TextoCw = "CQ CQ CQ DE EA5XYZ EA5XYZ K   EA5XYZ DE EA8DLF EA8DLF <KN>   EA8DLF DE EA5XYZ GM UR RST 599 5NN <BT> NAME PACO QTH VALENCIA HW? <KN>   ";

    /// <summary>Otra señal más débil a 940 Hz, en bucle (28 WPM), para que se vea el skimmer.</summary>
    public const string OtraCw = "TEST DL1ABC DL1ABC TEST   ";

    /// <summary>Abrir no arranca el reloj: el audio solo llega con <see cref="Adelantar"/> (capturas y pruebas).</summary>
    public bool SinReloj { get; init; }

    private float[]? _llave700;
    private float[]? _llave940;

    /// <summary>
    /// Inventa <paramref name="segundos"/> de audio de golpe y lo entrega en bloques de 20 ms, sin
    /// esperar al reloj: para capturas y pruebas fuera de tiempo real.
    /// </summary>
    public void Adelantar(double segundos)
    {
        var bloques = (int)Math.Ceiling(segundos * 50);
        for (var i = 0; i < bloques; i++) Inventar();
    }

    /// <summary>
    /// 20 ms de audio de recepcion inventado: ruido, telegrafía a 700 Hz (y otra más floja a
    /// 940) y un tono de 1850 Hz que va y viene.
    /// </summary>
    private float[] Llave(string texto, double wpm) =>
        Modos.Cw.SintetizadorCw.Envolvente(
            Modos.Cw.SintetizadorCw.Tramos(texto, new Modos.Cw.ManeraDeManipular(wpm)), _frecuencia, 5, 0.8, 0.4);

    private void Inventar()
    {
        var bloque = new float[_frecuencia / 50];
        _llave700 ??= Llave(TextoCw, 22);
        _llave940 ??= Llave(OtraCw, 28);
        for (var i = 0; i < bloque.Length; i++)
        {
            var n = _muestra + i;
            var t = n / (double)_frecuencia;
            var vaiviene = 0.5 + (0.5 * Math.Sin(2 * Math.PI * 0.7 * t));
            bloque[i] = (float)((0.25 * _llave700[n % _llave700.Length] * Math.Sin(2 * Math.PI * 700 * t))
                + (0.08 * _llave940[n % _llave940.Length] * Math.Sin(2 * Math.PI * 940 * t))
                + (0.15 * vaiviene * Math.Sin(2 * Math.PI * 1850 * t))
                + (0.05 * ((_azar.NextDouble() * 2) - 1)));
        }

        _muestra += bloque.Length;
        BloqueCapturado?.Invoke(this, new BloqueDeAudio(bloque, _frecuencia, DateTimeOffset.UtcNow));
    }
}

/// <summary>Una salida de audio de mentira: no reproduce nada y no pone nada en antena.</summary>
public sealed class SalidaDeAudioSimulada : ISalidaDeAudio
{
    private static readonly IReadOnlyList<DispositivoDeAudio> Inventados =
    [
        new("simulado-ft710-salida", "Codec USB del FT-710 (simulado)", EsDeEntrada: false, EsDelEquipo: true),
        new("simulado-altavoces", "Altavoces del monitor (simulado)", EsDeEntrada: false, EsDelEquipo: false),
    ];

    /// <inheritdoc />
    public DateTimeOffset? UltimoAvanceUtc => null;

    /// <inheritdoc />
    public IReadOnlyList<DispositivoDeAudio> Dispositivos => Inventados;

    /// <inheritdoc />
    public DispositivoDeAudio? Abierto { get; private set; }

    /// <inheritdoc />
    public int FrecuenciaDeMuestreo { get; private set; } = 48000;

    /// <inheritdoc />
    public Task AbrirAsync(string idDispositivo, int frecuenciaDeMuestreo = 48000, CancellationToken ct = default)
    {
        Abierto = Inventados.FirstOrDefault(d => d.Id == idDispositivo) ?? Inventados[0];
        FrecuenciaDeMuestreo = frecuenciaDeMuestreo;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task CerrarAsync(CancellationToken ct = default)
    {
        Abierto = null;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ReproducirAsync(ReadOnlyMemory<float> muestras, CancellationToken ct = default) =>
        Task.CompletedTask;

    /// <inheritdoc />
    public Task SilenciarAsync(CancellationToken ct = default) => Task.CompletedTask;

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        Abierto = null;
        return ValueTask.CompletedTask;
    }
}
