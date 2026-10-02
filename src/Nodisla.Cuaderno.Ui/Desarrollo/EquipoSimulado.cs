using System.Timers;
using Nodisla.Cuaderno.Aplicacion.CasosDeUso;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Valores;
using Temporizador = System.Timers.Timer;

namespace Nodisla.Cuaderno.Ui.Desarrollo;

/// <summary>
/// Un FT-710 de mentira que se comporta como el de verdad: el dial se mueve solo, el modo
/// cambia de vez en cuando, los medidores se menean y tiene los mandos que tiene.
/// </summary>
/// <remarks>
/// Existe para poder construir y probar la pantalla de operacion sin tener el equipo delante
/// ni el control CAT terminado. Imita lo que de verdad importa para la interfaz:
///
/// <list type="bullet">
///   <item>que el estado se lee y no se recuerda: el dial fisico manda tanto como nosotros;</item>
///   <item>que los mandos se <b>declaran</b>, y con los tres tipos —interruptor, posiciones y
///         escala continua— para que el panel se construya solo a partir de esa lista;</item>
///   <item>que el dial pasa de vez en cuando por <b>27.555 MHz</b>, que no esta en la tabla de
///         bandas de ADIF. Es la frecuencia en la que estaba el equipo de Jose el dia que se
///         capturo su CAT, y una interfaz que suponga que toda frecuencia tiene banda falla
///         el primer dia.</item>
/// </list>
///
/// Cuando llegue el control de verdad, esta clase se queda para las pruebas y en
/// <c>ConfiguracionDeServicios</c> solo cambia la linea que dice quien cubre el puerto.
/// </remarks>
public sealed class EquipoSimulado : IEquipoAvanzado, IEquipoConDosVfos, IEquipoConBotonera, Radio.Modelos.IEquipoDeModelo, IManipuladorCw, IAsyncDisposable
{
    /// <summary>Frecuencia sin banda de aficionado, la del dia de la captura del FT-710.</summary>
    public const decimal FrecuenciaSinBanda = 27.555m;

    private static readonly decimal[] Trozos =
    [
        3.650m, 7.120m, 10.130m, 14.210m, 18.130m, 21.250m, 28.480m, FrecuenciaSinBanda,
    ];

    private static readonly string[] Modos = ["SSB", "CW", "FT8", "RTTY"];

    private static readonly IReadOnlyDictionary<MandoDeEquipo, RangoDeMando> Catalogo =
        new Dictionary<MandoDeEquipo, RangoDeMando>
        {
            // ── Nivel y potencia ────────────────────────────────────────────
            [MandoDeEquipo.Potencia] = new(MandoDeEquipo.Potencia, 5, 100, 1, "W"),
            [MandoDeEquipo.GananciaRf] = new(MandoDeEquipo.GananciaRf, 0, 255, 1),
            [MandoDeEquipo.GananciaMicrofono] = new(MandoDeEquipo.GananciaMicrofono, 0, 100, 1),
            [MandoDeEquipo.Volumen] = new(MandoDeEquipo.Volumen, 0, 255, 1),
            [MandoDeEquipo.Monitor] = new(MandoDeEquipo.Monitor, 0, 100, 1),
            [MandoDeEquipo.Compresor] = new(MandoDeEquipo.Compresor, 0, 100, 1),

            // ── Recepcion ───────────────────────────────────────────────────
            [MandoDeEquipo.Atenuador] = new(
                MandoDeEquipo.Atenuador, 0, 3, 1, "dB", ["0 dB", "6 dB", "12 dB", "18 dB"]),
            [MandoDeEquipo.Preamplificador] = new(
                MandoDeEquipo.Preamplificador, 0, 2, 1, null, ["Directo (IPO)", "Amplificador 1", "Amplificador 2"]),
            [MandoDeEquipo.Agc] = new(
                MandoDeEquipo.Agc, 0, 4, 1, null, ["Desconectado", "Rápido", "Medio", "Lento", "Automático"]),
            [MandoDeEquipo.SupresorDeRuido] = new(MandoDeEquipo.SupresorDeRuido, 0, 1, 1),
            [MandoDeEquipo.NivelSupresorDeRuido] = new(MandoDeEquipo.NivelSupresorDeRuido, 0, 10, 1),
            [MandoDeEquipo.ReductorDeRuido] = new(MandoDeEquipo.ReductorDeRuido, 0, 1, 1),
            [MandoDeEquipo.NivelReductorDeRuido] = new(MandoDeEquipo.NivelReductorDeRuido, 1, 15, 1),
            [MandoDeEquipo.MuescaAutomatica] = new(MandoDeEquipo.MuescaAutomatica, 0, 1, 1),
            [MandoDeEquipo.MuescaManual] = new(MandoDeEquipo.MuescaManual, 0, 1, 1),
            [MandoDeEquipo.FrecuenciaDeMuesca] = new(MandoDeEquipo.FrecuenciaDeMuesca, 10, 3200, 10, "Hz"),
            [MandoDeEquipo.Contorno] = new(MandoDeEquipo.Contorno, 0, 1, 1),
            [MandoDeEquipo.FrecuenciaDeContorno] = new(MandoDeEquipo.FrecuenciaDeContorno, 10, 3200, 10, "Hz"),
            [MandoDeEquipo.DesplazamientoFi] = new(MandoDeEquipo.DesplazamientoFi, -1200, 1200, 20, "Hz"),
            [MandoDeEquipo.AnchoDeFiltro] = new(MandoDeEquipo.AnchoDeFiltro, 50, 3000, 50, "Hz"),
            [MandoDeEquipo.FiltroDeTejado] = new(
                MandoDeEquipo.FiltroDeTejado, 0, 2, 1, null, ["500 Hz", "3 kHz", "12 kHz"]),
            [MandoDeEquipo.Silenciador] = new(MandoDeEquipo.Silenciador, 0, 100, 1),

            // ── Telegrafia ──────────────────────────────────────────────────
            [MandoDeEquipo.TonoCw] = new(MandoDeEquipo.TonoCw, 300, 1050, 50, "Hz"),
            [MandoDeEquipo.VelocidadKeyer] = new(MandoDeEquipo.VelocidadKeyer, 4, 60, 1, "ppm"),
            [MandoDeEquipo.BreakIn] = new(MandoDeEquipo.BreakIn, 0, 1, 1),
            [MandoDeEquipo.RetardoBreakIn] = new(MandoDeEquipo.RetardoBreakIn, 30, 3000, 10, "ms"),

            // ── Transmision ─────────────────────────────────────────────────
            [MandoDeEquipo.Vox] = new(MandoDeEquipo.Vox, 0, 1, 1),
            [MandoDeEquipo.GananciaVox] = new(MandoDeEquipo.GananciaVox, 0, 100, 1),
            [MandoDeEquipo.RetardoVox] = new(MandoDeEquipo.RetardoVox, 30, 3000, 10, "ms"),
            [MandoDeEquipo.Sintonizador] = new(
                MandoDeEquipo.Sintonizador, 0, 2, 1, null, ["Apagado", "Encendido", "Sintonizando"]),
            [MandoDeEquipo.Antena] = new(MandoDeEquipo.Antena, 0, 2, 1, null, ["Antena 1", "Antena 2", "Antena 3"]),

            // ── Frecuencia ──────────────────────────────────────────────────
            [MandoDeEquipo.Split] = new(MandoDeEquipo.Split, 0, 1, 1),
            [MandoDeEquipo.Rit] = new(MandoDeEquipo.Rit, 0, 1, 1),
            [MandoDeEquipo.DesplazamientoRit] = new(MandoDeEquipo.DesplazamientoRit, -9990, 9990, 10, "Hz"),
            [MandoDeEquipo.Xit] = new(MandoDeEquipo.Xit, 0, 1, 1),
            [MandoDeEquipo.DesplazamientoXit] = new(MandoDeEquipo.DesplazamientoXit, -9990, 9990, 10, "Hz"),

            // ── Analizador (teclas de la pantalla; lo pinta AnalizadorSimulado) ──
            [MandoDeEquipo.EspectroVelocidad] = new(
                MandoDeEquipo.EspectroVelocidad, 0, 5, 1, null, ["SLOW1", "SLOW2", "FAST1", "FAST2", "FAST3", "STOP"]),
            [MandoDeEquipo.EspectroAncho] = new(
                MandoDeEquipo.EspectroAncho, 0, 9, 1, null, ["1 kHz", "2 kHz", "5 kHz", "10 kHz", "20 kHz", "50 kHz", "100 kHz", "200 kHz", "500 kHz", "1 MHz"]),
            [MandoDeEquipo.EspectroModo] = new(
                MandoDeEquipo.EspectroModo, 0, 8, 1, null, Radio.Control.Ft710.ModosDelAnalizadorFt710.Etiquetas()),
        };

    /// <summary>Valores de partida, los que devolvio el equipo de Jose en la captura.</summary>
    private static readonly IReadOnlyDictionary<MandoDeEquipo, double> DePartida =
        new Dictionary<MandoDeEquipo, double>
        {
            [MandoDeEquipo.Potencia] = 100,
            [MandoDeEquipo.GananciaRf] = 255,
            [MandoDeEquipo.GananciaMicrofono] = 80,
            [MandoDeEquipo.Volumen] = 89,
            [MandoDeEquipo.Monitor] = 30,
            [MandoDeEquipo.Compresor] = 0,
            [MandoDeEquipo.Atenuador] = 0,
            [MandoDeEquipo.Preamplificador] = 0,
            [MandoDeEquipo.Agc] = 3,
            [MandoDeEquipo.SupresorDeRuido] = 0,
            [MandoDeEquipo.NivelSupresorDeRuido] = 0,
            [MandoDeEquipo.ReductorDeRuido] = 0,
            [MandoDeEquipo.NivelReductorDeRuido] = 1,
            [MandoDeEquipo.MuescaAutomatica] = 0,
            [MandoDeEquipo.MuescaManual] = 0,
            [MandoDeEquipo.FrecuenciaDeMuesca] = 1000,
            [MandoDeEquipo.Contorno] = 0,
            [MandoDeEquipo.FrecuenciaDeContorno] = 1000,
            [MandoDeEquipo.DesplazamientoFi] = 0,
            [MandoDeEquipo.AnchoDeFiltro] = 2400,
            [MandoDeEquipo.FiltroDeTejado] = 1,
            [MandoDeEquipo.Silenciador] = 0,
            [MandoDeEquipo.TonoCw] = 700,
            [MandoDeEquipo.VelocidadKeyer] = 20,
            [MandoDeEquipo.BreakIn] = 0,
            [MandoDeEquipo.RetardoBreakIn] = 300,
            [MandoDeEquipo.Vox] = 0,
            [MandoDeEquipo.GananciaVox] = 70,
            [MandoDeEquipo.RetardoVox] = 800,
            [MandoDeEquipo.Sintonizador] = 0,
            [MandoDeEquipo.Antena] = 0,
            [MandoDeEquipo.Split] = 0,
            [MandoDeEquipo.Rit] = 0,
            [MandoDeEquipo.DesplazamientoRit] = 0,
            [MandoDeEquipo.Xit] = 0,
            [MandoDeEquipo.DesplazamientoXit] = 0,
            [MandoDeEquipo.EspectroVelocidad] = 2,
            [MandoDeEquipo.EspectroAncho] = 7,
            [MandoDeEquipo.EspectroModo] = 4,
        };

    private readonly Temporizador _reloj = new(400) { AutoReset = true };
    private readonly Random _azar = new(8_1968);
    private readonly object _cerrojo = new();
    private readonly Dictionary<MandoDeEquipo, double> _valores = new(DePartida);

    private decimal _dial = 14.210m;
    private string _modo = "SSB";

    // El VFO B lleva su propia frecuencia y su propio modo: es lo que permite trabajar en
    // dos frecuencias, y lo que la pantalla de operacion tiene que ensenar a la vez que A.
    private decimal _dialB = 14.195m;
    private string _modoB = "USB";
    private NombreDeVfo _activo = NombreDeVfo.A;

    private int _vueltas;
    private bool _liberado;

    /// <summary>Monta el equipo simulado, todavia sin conectar.</summary>
    public EquipoSimulado()
        : this(ModeloSimulado.DelEntorno())
    {
    }

    /// <summary>Monta el equipo simulado presentándose como otro modelo (docs/18-frontales.md).</summary>
    /// <param name="modelo">Modelo con el que se presenta.</param>
    public EquipoSimulado(Radio.Modelos.ModeloDeEquipo modelo)
    {
        ArgumentNullException.ThrowIfNull(modelo);
        Modelo = modelo;

        // Lo que el modelo no tiene no se declara, para que su tecla salga apagada.
        var mandos = Catalogo.Keys.ToHashSet();
        if (!modelo.Capacidades.Sintonizador) mandos.Remove(MandoDeEquipo.Sintonizador);
        if (!modelo.Capacidades.Analizador)
        {
            mandos.ExceptWith([MandoDeEquipo.EspectroAncho, MandoDeEquipo.EspectroModo, MandoDeEquipo.EspectroVelocidad]);
        }

        Mandos = mandos;
        _reloj.Elapsed += AlPasarElTiempo;
    }

    /// <summary>El modelo con el que se presenta: el FT-710, o el de <c>CUADERNO_MODELO</c>.</summary>
    public Radio.Modelos.ModeloDeEquipo Modelo { get; }

    /// <inheritdoc />
    public event EventHandler<EstadoDelEquipo>? EstadoCambiado;

    /// <inheritdoc />
    public ViaDeControl Via => ViaDeControl.Rigctld;

    /// <inheritdoc />
    public string NombreDelEquipo => $"{Modelo.NombreCompleto} (simulado)";

    /// <inheritdoc />
    public IReadOnlySet<MandoDeEquipo> Mandos { get; }

    /// <inheritdoc />
    public EstadoDelEquipo Estado { get; private set; } = EstadoDelEquipo.Desconectado;

    /// <inheritdoc />
    public RangoDeMando? Rango(MandoDeEquipo mando) => Mandos.Contains(mando) ? Catalogo.GetValueOrDefault(mando) : null;

    /// <inheritdoc />
    public EstadoDeLosVfos Vfos { get; private set; } = EstadoDeLosVfos.SinDatos;

    /// <inheritdoc />
    public Task<EstadoDeLosVfos> LeerVfosAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(Vfos);
    }

    /// <inheritdoc />
    public Task PonerVfoActivoAsync(NombreDeVfo vfo, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_cerrojo) _activo = vfo;
        Publicar(Estado.Conectado, Estado.Transmitiendo);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task IntercambiarVfosAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        lock (_cerrojo)
        {
            (_dial, _dialB) = (_dialB, _dial);
            (_modo, _modoB) = (_modoB, _modo);
        }

        Publicar(Estado.Conectado, Estado.Transmitiendo);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task IgualarVfosAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        lock (_cerrojo)
        {
            if (_activo == NombreDeVfo.A)
            {
                _dialB = _dial;
                _modoB = _modo;
            }
            else
            {
                _dial = _dialB;
                _modo = _modoB;
            }
        }

        Publicar(Estado.Conectado, Estado.Transmitiendo);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task PonerFrecuenciaDeAsync(NombreDeVfo vfo, Frecuencia frecuencia, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        lock (_cerrojo)
        {
            if (vfo == NombreDeVfo.A) _dial = frecuencia.Megahercios;
            else _dialB = frecuencia.Megahercios;
        }

        Publicar(Estado.Conectado, Estado.Transmitiendo);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task PonerModoDeAsync(NombreDeVfo vfo, Modo modo, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var nombre = modo.EsVacio ? "SSB" : modo.NombreUsual;
        lock (_cerrojo)
        {
            if (vfo == NombreDeVfo.A) _modo = nombre;
            else _modoB = nombre;
        }

        Publicar(Estado.Conectado, Estado.Transmitiendo);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ConectarAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        Publicar(conectado: true, transmitiendo: false);
        _reloj.Start();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DesconectarAsync(CancellationToken ct = default)
    {
        _reloj.Stop();

        // Soltar el PTT antes de cerrar es parte del contrato del puerto, y aqui se cumple
        // igual que lo hara el equipo de verdad.
        Publicar(conectado: false, transmitiendo: false);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task PonerFrecuenciaAsync(Frecuencia frecuencia, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        lock (_cerrojo) _dial = frecuencia.Megahercios;
        Publicar(Estado.Conectado, Estado.Transmitiendo);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task PonerModoAsync(Modo modo, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        lock (_cerrojo) _modo = modo.EsVacio ? "SSB" : modo.NombreUsual;
        Publicar(Estado.Conectado, Estado.Transmitiendo);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public IReadOnlyList<TeclaDeBanda> TeclasDeBanda => Radio.Control.Ft710.ControlFt710.BandasFt710;

    /// <inheritdoc />
    /// <remarks>Sin pila de banda: se va a un sitio fijo de cada banda.</remarks>
    public Task IrABandaAsync(TeclaDeBanda tecla, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tecla);
        double[] sitios = [1.840, 3.700, 5.360, 7.100, 10.136, 14.200, 18.100, 21.200, 24.940, 28.500, 50.150, 27.555];
        return PonerFrecuenciaAsync(Frecuencia.DesdeMegahercios((decimal)sitios[Math.Clamp(tecla.Codigo, 0, sitios.Length - 1)]), ct);
    }

    /// <inheritdoc />
    public Task PonerModoDeTeclaAsync(TeclaDeModo modo, CancellationToken ct = default) =>
        PonerModoAsync(Modo.Parse(modo switch
        {
            TeclaDeModo.Lsb or TeclaDeModo.Usb => "SSB",
            TeclaDeModo.Cw => "CW",
            TeclaDeModo.Am => "AM",
            TeclaDeModo.Fm => "FM",
            _ => "FT8",
        }), ct);

    /// <inheritdoc />
    public Task PonerPttAsync(bool transmitir, CancellationToken ct = default)
    {
        Publicar(Estado.Conectado, transmitir);
        return Task.CompletedTask;
    }

    // ── Telegrafia de mentira: solo apunta lo que mandaria el manipulador ──────────────────

    /// <summary>Lo que se ha «manipulado», para las pruebas y el registro.</summary>
    public List<string> Manipulado { get; } = [];

    /// <inheritdoc />
    public string? PorQueNoManipula => null;

    /// <inheritdoc />
    public int LetrasPorOrden => 50;

    /// <inheritdoc />
    public int WpmMinima => 4;

    /// <inheritdoc />
    public int WpmMaxima => 60;

    /// <inheritdoc />
    public Task PonerVelocidadAsync(int wpm, CancellationToken ct = default) => Task.CompletedTask;

    /// <inheritdoc />
    public Task ManipularAsync(string texto, CancellationToken ct = default)
    {
        if (!Estado.Transmitiendo) throw new InvalidOperationException("El manipulador simulado solo manipula con el PTT pedido.");
        lock (Manipulado) Manipulado.Add(texto);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task PararManipuladorAsync(CancellationToken ct = default) => Task.CompletedTask;

    /// <inheritdoc />
    public Task<double?> LeerMandoAsync(MandoDeEquipo mando, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        lock (_cerrojo)
        {
            return Task.FromResult(_valores.TryGetValue(mando, out var valor) ? valor : (double?)null);
        }
    }

    /// <inheritdoc />
    public Task EscribirMandoAsync(MandoDeEquipo mando, double valor, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (Rango(mando) is not { } rango) return Task.CompletedTask;

        // El rango ajusta antes de enviar: un valor fuera de sitio no llega nunca al equipo.
        lock (_cerrojo) _valores[mando] = rango.Ajustar(valor);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<LecturaDeMedidores> LeerMedidoresAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (!Estado.Conectado)
        {
            return Task.FromResult(new LecturaDeMedidores(
                null, null, null, null, null, null, null, DateTimeOffset.UtcNow));
        }

        var transmitiendo = Estado.Transmitiendo;
        double potencia;
        lock (_cerrojo) potencia = _valores[MandoDeEquipo.Potencia];

        return Task.FromResult(new LecturaDeMedidores(
            // En transmision el medidor S no marca: lo que se lee es la potencia.
            UnidadesS: transmitiendo ? null : Math.Round(2 + (_azar.NextDouble() * 7.5), 1),
            PotenciaVatios: transmitiendo ? Math.Round(potencia * (0.85 + (_azar.NextDouble() * 0.15)), 0) : null,
            Roe: transmitiendo ? Math.Round(1.1 + (_azar.NextDouble() * 0.5), 2) : null,
            Alc: transmitiendo ? Math.Round(_azar.NextDouble() * 0.4, 2) : null,
            CorrienteAmperios: transmitiendo ? Math.Round(8 + (_azar.NextDouble() * 10), 1) : Math.Round(1.2 + (_azar.NextDouble() * 0.3), 1),
            TensionVoltios: Math.Round(13.8 - (transmitiendo ? _azar.NextDouble() * 0.6 : 0), 1),
            Compresion: transmitiendo ? Math.Round(_azar.NextDouble() * 6, 1) : null,
            LeidoUtc: DateTimeOffset.UtcNow));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<MemoriaDeEquipo>> LeerMemoriasAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        IReadOnlyList<MemoriaDeEquipo> memorias =
        [
            new(1, Frecuencia.DesdeMegahercios(14.195m), Modo.Parse("SSB"), "Llamada 20 m", true),
            new(2, Frecuencia.DesdeMegahercios(7.074m), Modo.Parse("FT8"), "FT8 40 m", true),
            new(3, Frecuencia.DesdeMegahercios(14.074m), Modo.Parse("FT8"), "FT8 20 m", true),
            new(4, Frecuencia.DesdeMegahercios(FrecuenciaSinBanda), Modo.Parse("SSB"), "Fuera de banda", true),
            new(5, Frecuencia.Cero, Modo.Vacio, null, false),
        ];

        return Task.FromResult(memorias);
    }

    /// <inheritdoc />
    public Task IrAMemoriaAsync(int numero, CancellationToken ct = default) =>
        LeerMemoriasAsync(ct).ContinueWith(
            async t =>
            {
                var memoria = t.Result.FirstOrDefault(m => m.Numero == numero && m.Ocupada);
                if (memoria is null) return;

                await PonerFrecuenciaAsync(memoria.Frecuencia, ct).ConfigureAwait(false);
                await PonerModoAsync(memoria.Modo, ct).ConfigureAwait(false);
            },
            ct,
            TaskContinuationOptions.OnlyOnRanToCompletion,
            TaskScheduler.Default).Unwrap();

    /// <inheritdoc />
    public Task<string?> OrdenEnCrudoAsync(string orden, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        // Solo se responde a lo que se capturo del equipo de verdad; lo demas, silencio, que
        // es exactamente lo que hace el FT-710 con una orden que no entiende.
        decimal dial;
        lock (_cerrojo) dial = _dial;

        return Task.FromResult<string?>(orden.Trim() switch
        {
            "ID;" => "ID0800;",
            "PS;" => "PS1;",
            "FA;" => $"FA{(long)(dial * 1_000_000m):000000000};",
            "PC;" => "PC100;",
            "SM0;" => $"SM0{_azar.Next(0, 255):000};",
            _ => null,
        });
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (_liberado) return ValueTask.CompletedTask;
        _liberado = true;

        _reloj.Elapsed -= AlPasarElTiempo;
        _reloj.Stop();
        _reloj.Dispose();
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Mueve el dial un poco, como si alguien estuviera buscando estaciones, y de tarde en
    /// tarde salta de banda y de modo.
    /// </summary>
    private void AlPasarElTiempo(object? origen, ElapsedEventArgs e)
    {
        if (_liberado) return;

        lock (_cerrojo)
        {
            _vueltas++;

            // Cada veinte segundos largos, cambio de banda y de modo: es lo que hace un
            // operador que va mirando donde hay algo.
            if (_vueltas % 55 == 0)
            {
                _dial = Trozos[_azar.Next(Trozos.Length)];
                _modo = Modos[_azar.Next(Modos.Length)];
            }
            else
            {
                _dial = decimal.Round(_dial + ((decimal)(_azar.NextDouble() - 0.5) * 0.0025m), 5);
                _dialB = decimal.Round(_dialB + ((decimal)(_azar.NextDouble() - 0.5) * 0.0008m), 5);
            }
        }

        Publicar(conectado: true, Estado.Transmitiendo);
    }

    private void Publicar(bool conectado, bool transmitiendo)
    {
        decimal dial;
        decimal dialB;
        string modo;
        string modoB;
        NombreDeVfo activo;
        double potencia;
        double split;
        double rit;
        double desplazamientoRit;
        double xit;
        double desplazamientoXit;
        double ancho;

        lock (_cerrojo)
        {
            dial = _dial;
            dialB = _dialB;
            modo = _modo;
            modoB = _modoB;
            activo = _activo;
            potencia = _valores[MandoDeEquipo.Potencia];
            split = _valores[MandoDeEquipo.Split];
            rit = _valores[MandoDeEquipo.Rit];
            desplazamientoRit = _valores[MandoDeEquipo.DesplazamientoRit];
            xit = _valores[MandoDeEquipo.Xit];
            desplazamientoXit = _valores[MandoDeEquipo.DesplazamientoXit];
            ancho = _valores[MandoDeEquipo.AnchoDeFiltro];
        }

        if (!conectado)
        {
            Estado = EstadoDelEquipo.Desconectado;
            Vfos = EstadoDeLosVfos.SinDatos;
            EstadoCambiado?.Invoke(this, Estado);
            return;
        }

        var haySplit = split >= 0.5;
        var frecuenciaActiva = activo == NombreDeVfo.A ? dial : dialB;
        var modoActivo = activo == NombreDeVfo.A ? modo : modoB;

        // Con split, se transmite por el VFO inactivo y se recibe por el activo. Sin split,
        // los dos papeles caen en el mismo.
        var transmiteA = haySplit ? activo == NombreDeVfo.B : activo == NombreDeVfo.A;
        var recibeA = activo == NombreDeVfo.A;

        Vfos = new EstadoDeLosVfos(
            new EstadoDeUnVfo(
                NombreDeVfo.A,
                Frecuencia.DesdeMegahercios(dial),
                Modo.Parse(modo),
                EsElActivo: activo == NombreDeVfo.A,
                Transmite: transmiteA,
                Recibe: recibeA,
                AnchoDeFiltroHz: (int)ancho),
            new EstadoDeUnVfo(
                NombreDeVfo.B,
                Frecuencia.DesdeMegahercios(dialB),
                Modo.Parse(modoB),
                EsElActivo: activo == NombreDeVfo.B,
                Transmite: !transmiteA,
                Recibe: !recibeA,
                AnchoDeFiltroHz: (int)ancho),
            Split: haySplit,
            Rit: rit >= 0.5,
            DesplazamientoRitHz: (int)desplazamientoRit,
            Xit: xit >= 0.5,
            DesplazamientoXitHz: (int)desplazamientoXit);

        Estado = new EstadoDelEquipo(
            Conectado: true,
            Transmitiendo: transmitiendo,
            Frecuencia: Frecuencia.DesdeMegahercios(frecuenciaActiva),
            FrecuenciaRx: haySplit ? Vfos.DeRecepcion.Frecuencia : null,
            Modo: Modo.Parse(modoActivo),
            Vfo: activo == NombreDeVfo.A ? "VFO A" : "VFO B",
            PotenciaVatios: potencia,
            SenalRecibida: transmitiendo ? null : Math.Round(3 + (_azar.NextDouble() * 6), 1),
            LeidoUtc: DateTimeOffset.UtcNow);

        EstadoCambiado?.Invoke(this, Estado);
    }
}
