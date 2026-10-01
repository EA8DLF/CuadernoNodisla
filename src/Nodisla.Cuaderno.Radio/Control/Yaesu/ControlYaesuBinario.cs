using System.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Radio.Control.Ft710;
using Nodisla.Cuaderno.Radio.Modelos;
using Nodisla.Cuaderno.Radio.Ptt;

namespace Nodisla.Cuaderno.Radio.Control.Yaesu;

/// <summary>
/// CAT antiguo de Yaesu (FT-817, FT-818, FT-857, FT-897): bloques de 5 bytes, el ultimo es la
/// orden. Programado segun el manual de operacion (capitulo «CAT System Programming») y sin
/// probar con la radio.
/// </summary>
/// <remarks>
/// <para>
/// Lo que da el CAT de estos equipos, y nada mas: frecuencia y modo (orden 03), poner
/// frecuencia (01), modo (07), PTT (08/88), VFO A/B (81, alterna), split (02/82), clarificador
/// (05/85), bloqueo (00/80) y los medidores de estado (E7 en recepcion, F7 en transmision).
/// No hay forma de leer el split, el bloqueo ni el clarificador: sus mandos se accionan pero no
/// se sabe como estan, y asi se dice.
/// </para>
/// <para>
/// <b>El PTT sigue pidiendose al vigilante.</b> Subirlo por <c>PonerPttAsync</c> esta vetado.
/// </para>
/// </remarks>
public sealed class ControlYaesuBinario
    : IEquipoAvanzado, IEquipoConTeclas, IEquipoConBotonera, IPttDirecto, ISueltaDeEmergenciaPtt, IAvisaDePerdidaDeComunicacion, IEquipoDeModelo
{
    /// <summary>Ordenes (el quinto byte).</summary>
    public static class Orden
    {
        /// <summary>Bloqueo encendido.</summary>
        public const byte BloqueoSi = 0x00;

        /// <summary>Bloqueo apagado.</summary>
        public const byte BloqueoNo = 0x80;

        /// <summary>PTT arriba.</summary>
        public const byte PttSi = 0x08;

        /// <summary>PTT abajo.</summary>
        public const byte PttNo = 0x88;

        /// <summary>Poner frecuencia (4 bytes BCD en decenas de hercio).</summary>
        public const byte Frecuencia = 0x01;

        /// <summary>Poner modo.</summary>
        public const byte Modo = 0x07;

        /// <summary>Clarificador encendido.</summary>
        public const byte ClarificadorSi = 0x05;

        /// <summary>Clarificador apagado.</summary>
        public const byte ClarificadorNo = 0x85;

        /// <summary>Alterna VFO A/B.</summary>
        public const byte AlternarVfo = 0x81;

        /// <summary>Split encendido.</summary>
        public const byte SplitSi = 0x02;

        /// <summary>Split apagado.</summary>
        public const byte SplitNo = 0x82;

        /// <summary>Leer frecuencia y modo (contesta 5 bytes).</summary>
        public const byte LeerFrecuenciaYModo = 0x03;

        /// <summary>Estado de recepcion (1 byte: medidor S en los 4 bits bajos).</summary>
        public const byte EstadoDeRecepcion = 0xE7;

        /// <summary>Estado de transmision (1 byte: bit 7 a 0 = PTT puesto; 4 bits bajos = potencia).</summary>
        public const byte EstadoDeTransmision = 0xF7;
    }

    private static readonly Dictionary<byte, string> ModosDelEquipo = new()
    {
        [0x00] = "LSB",
        [0x01] = "USB",
        [0x02] = "CW",
        [0x03] = "CW",
        [0x04] = "AM",
        [0x06] = "FM",
        [0x08] = "FM",
        [0x88] = "FM",
        [0x0A] = "PKTUSB",
        [0x0C] = "PKTFM",
    };

    private static readonly Dictionary<string, byte> ModosAlEquipo = new(StringComparer.OrdinalIgnoreCase)
    {
        ["LSB"] = 0x00,
        ["USB"] = 0x01,
        ["CW"] = 0x02,
        ["AM"] = 0x04,
        ["FM"] = 0x08,
        ["RTTY"] = 0x0A,
        ["PSK"] = 0x0A,
        ["PKTUSB"] = 0x0A,
        ["PKTLSB"] = 0x0A,
        ["PKTFM"] = 0x0C,
    };

    private readonly ICanalBinario _canal;
    private readonly OpcionesFt710 _opciones;
    private readonly ILogger _registro;
    private readonly object _candado = new();
    private readonly SemaphoreSlim _exclusiva = new(1, 1);
    private readonly Dictionary<MandoDeEquipo, double> _ultimoPuesto = [];

    private CancellationTokenSource? _ctsSondeo;
    private Task? _sondeo;
    private EstadoDelEquipo _estado = EstadoDelEquipo.Desconectado;
    private bool _pttPedido;
    private bool _desechado;
    private bool _abiertoAlgunaVez;
    private int _fallosSeguidos;
    private LecturaDeMedidores _medidores = new(null, null, null, null, null, null, null, DateTimeOffset.MinValue);

    /// <summary>Crea el control sobre un canal ya construido.</summary>
    /// <param name="canal">Canal binario.</param>
    /// <param name="modelo">Modelo (FT-817/818/857/897).</param>
    /// <param name="opciones">Ajustes de la conexion.</param>
    /// <param name="registro">Donde anotar.</param>
    public ControlYaesuBinario(ICanalBinario canal, ModeloDeEquipo modelo, OpcionesFt710? opciones = null, ILogger? registro = null)
    {
        ArgumentNullException.ThrowIfNull(canal);
        ArgumentNullException.ThrowIfNull(modelo);
        _canal = canal;
        Modelo = modelo;
        _opciones = opciones ?? new OpcionesFt710();
        _registro = registro ?? NullLogger.Instance;

        List<ViaDeSuelta> vias = [];
        if (_opciones.ViaDePtt != ViaDePtt.Cat && _canal.PuedeAccionarLineas)
        {
            vias.Add(new ViaDeSuelta(
                $"{modelo.Nombre}: bajar la línea {_opciones.ViaDePtt} del puerto",
                async ct => await _canal.PonerLineaDePttAsync(false, ct).ConfigureAwait(false),
                () => _canal.PonerLineaDePttSincrono(false)));
        }

        vias.Add(new ViaDeSuelta(
            $"{modelo.Nombre}: PTT OFF (88) por el canal abierto",
            async ct => await _canal.MandarAsync(Bloque(Orden.PttNo), ct).ConfigureAwait(false),
            () => _canal.MandarSincrono(Bloque(Orden.PttNo))));
        vias.Add(new ViaDeSuelta(
            $"{modelo.Nombre}: reabrir el puerto y PTT OFF (88)",
            async ct =>
            {
                await _canal.AbrirAsync(ct).ConfigureAwait(false);
                await _canal.MandarAsync(Bloque(Orden.PttNo), ct).ConfigureAwait(false);
            }));
        ViasDeSuelta = vias;
    }

    /// <inheritdoc />
    public ModeloDeEquipo Modelo { get; }

    /// <inheritdoc />
    public ViaDeControl Via => ViaDeControl.CatNativo;

    /// <inheritdoc />
    public string NombreDelEquipo => Modelo.NombreCompleto;

    /// <inheritdoc />
    public EstadoDelEquipo Estado
    {
        get
        {
            lock (_candado) return _estado;
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<ViaDeSuelta> ViasDeSuelta { get; }

    /// <inheritdoc />
    public event EventHandler<EstadoDelEquipo>? EstadoCambiado;

    /// <inheritdoc />
    public event EventHandler<string>? ComunicacionPerdida;

    /// <summary>
    /// Mandos del CAT antiguo. Son de solo escritura: el equipo no dice como estan, asi que se
    /// ensena lo ultimo que se puso desde el programa (nulo si no se ha tocado).
    /// </summary>
    public IReadOnlySet<MandoDeEquipo> Mandos { get; } = new HashSet<MandoDeEquipo>
    {
        MandoDeEquipo.Split, MandoDeEquipo.Rit, MandoDeEquipo.Bloqueo,
    };

    /// <inheritdoc />
    public IReadOnlySet<TeclaDelEquipo> Teclas { get; } = new HashSet<TeclaDelEquipo> { TeclaDelEquipo.AlternarVfo };

    /// <summary>
    /// Bandas: este CAT no tiene la tecla BAND, asi que ir a una banda es poner su frecuencia de
    /// partida (la de la tabla), no la ultima usada como hace la radio.
    /// </summary>
    public IReadOnlyList<TeclaDeBanda> TeclasDeBanda { get; } =
    [
        PerfilYaesu.Tecla(1_840_000, "1.8", "160m", "160 m: 1,840 MHz"),
        PerfilYaesu.Tecla(3_700_000, "3.5", "80m", "80 m: 3,700 MHz"),
        PerfilYaesu.Tecla(7_100_000, "7", "40m", "40 m: 7,100 MHz"),
        PerfilYaesu.Tecla(10_120_000, "10", "30m", "30 m: 10,120 MHz"),
        PerfilYaesu.Tecla(14_200_000, "14", "20m", "20 m: 14,200 MHz"),
        PerfilYaesu.Tecla(18_120_000, "18", "17m", "17 m: 18,120 MHz"),
        PerfilYaesu.Tecla(21_200_000, "21", "15m", "15 m: 21,200 MHz"),
        PerfilYaesu.Tecla(24_940_000, "24", "12m", "12 m: 24,940 MHz"),
        PerfilYaesu.Tecla(28_500_000, "28", "10m", "10 m: 28,500 MHz"),
        PerfilYaesu.Tecla(50_150_000, "50", "6m", "6 m: 50,150 MHz"),
        PerfilYaesu.Tecla(145_500_000, "144", "2m", "2 m: 145,500 MHz"),
        PerfilYaesu.Tecla(433_500_000, "430", "70cm", "70 cm: 433,500 MHz"),
    ];

    /// <summary>Monta un bloque de 5 bytes: cuatro de parametros y la orden.</summary>
    /// <param name="orden">Quinto byte.</param>
    /// <param name="p1">Parametro 1.</param>
    /// <param name="p2">Parametro 2.</param>
    /// <param name="p3">Parametro 3.</param>
    /// <param name="p4">Parametro 4.</param>
    /// <returns>El bloque.</returns>
    public static byte[] Bloque(byte orden, byte p1 = 0, byte p2 = 0, byte p3 = 0, byte p4 = 0) => [p1, p2, p3, p4, orden];

    /// <summary>La frecuencia en 4 bytes BCD, en decenas de hercio (14.234.560 Hz = 01 42 34 56).</summary>
    /// <param name="hercios">Hercios.</param>
    /// <returns>Los cuatro bytes.</returns>
    public static byte[] ABcd(long hercios)
    {
        var decenas = Math.Clamp(hercios / 10, 0, 99_999_999);
        var cifras = decenas.ToString("D8", System.Globalization.CultureInfo.InvariantCulture);
        var bytes = new byte[4];
        for (var i = 0; i < 4; i++)
        {
            bytes[i] = (byte)(((cifras[i * 2] - '0') << 4) | (cifras[(i * 2) + 1] - '0'));
        }

        return bytes;
    }

    /// <summary>Lee 4 bytes BCD como hercios.</summary>
    /// <param name="bcd">Los bytes.</param>
    /// <returns>Los hercios, o nulo si no son BCD.</returns>
    public static long? DesdeBcd(ReadOnlySpan<byte> bcd)
    {
        long decenas = 0;
        foreach (var b in bcd[..4])
        {
            int alto = b >> 4, bajo = b & 0x0F;
            if (alto > 9 || bajo > 9) return null;
            decenas = (decenas * 100) + (alto * 10) + bajo;
        }

        return decenas * 10;
    }

    /// <summary>Medidor S del estado de recepcion (0-15) a unidades S: 0-9 son S0-S9 y cada paso de mas son 10 dB.</summary>
    /// <param name="crudo">Los 4 bits bajos.</param>
    /// <returns>Unidades S (10 dB por encima de S9 cuentan como 10/6 de unidad).</returns>
    public static double AUnidadesS(int crudo) => crudo <= 9 ? crudo : 9 + ((crudo - 9) * 10d / 6d);

    /// <inheritdoc />
    public async Task ConectarAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_desechado, this);
        if (!_canal.Abierto) await _canal.AbrirAsync(ct).ConfigureAwait(false);
        Volatile.Write(ref _abiertoAlgunaVez, true);

        var leido = await _canal.PreguntarAsync(Bloque(Orden.LeerFrecuenciaYModo), 5, ct).ConfigureAwait(false);
        if (leido is null || DesdeBcd(leido) is null)
        {
            throw new EquipoNoContestaException(_canal.Descripcion);
        }

        await LeerEstadoAsync(ct).ConfigureAwait(false);
        lock (_candado)
        {
            if (_sondeo is not null) return;
            _ctsSondeo = new CancellationTokenSource();
            _sondeo = Task.Run(() => SondearAsync(_ctsSondeo.Token), CancellationToken.None);
        }
    }

    /// <inheritdoc />
    public async Task DesconectarAsync(CancellationToken ct = default)
    {
        CancellationTokenSource? cts;
        Task? sondeo;
        lock (_candado)
        {
            cts = _ctsSondeo;
            sondeo = _sondeo;
            _ctsSondeo = null;
            _sondeo = null;
        }

        if (cts is not null) await cts.CancelAsync().ConfigureAwait(false);
        if (sondeo is not null)
        {
            try
            {
                await sondeo.WaitAsync(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _registro.LogDebug(ex, "El sondeo del {Modelo} no terminó a tiempo.", Modelo.Nombre);
            }
        }

        cts?.Dispose();
        await BajarElPttComoSeaAsync(ct).ConfigureAwait(false);
        _canal.Cerrar();
        Actualizar(e => e with { Conectado = false, Transmitiendo = false });
    }

    /// <inheritdoc />
    public Task PonerFrecuenciaAsync(Frecuencia frecuencia, CancellationToken ct = default)
    {
        var hercios = frecuencia.Hercios;
        if (hercios < Modelo.Capacidades.HerciosMinimo || hercios > Modelo.Capacidades.HerciosMaximo)
        {
            throw new ArgumentOutOfRangeException(nameof(frecuencia), frecuencia, $"El {Modelo.Nombre} no admite esa frecuencia.");
        }

        var bcd = ABcd(hercios);
        return EnExclusivaAsync(async () =>
        {
            await _canal.MandarAsync(Bloque(Orden.Frecuencia, bcd[0], bcd[1], bcd[2], bcd[3]), ct).ConfigureAwait(false);
            await LeerEstadoSinEsperarAsync(ct).ConfigureAwait(false);
        }, ct);
    }

    /// <inheritdoc />
    public Task PonerModoAsync(Modo modo, CancellationToken ct = default)
    {
        var nombre = _opciones.Traductor.AlEquipo(modo, Estado.Frecuencia)
            ?? throw new ArgumentException($"No sé cómo pedirle al {Modelo.Nombre} el modo {modo}.", nameof(modo));
        if (!ModosAlEquipo.TryGetValue(nombre, out var codigo))
        {
            throw new ArgumentException($"El {Modelo.Nombre} no tiene el modo {nombre}.", nameof(modo));
        }

        return PonerCodigoDeModoAsync(codigo, ct);
    }

    private Task PonerCodigoDeModoAsync(byte codigo, CancellationToken ct) =>
        EnExclusivaAsync(async () =>
        {
            await _canal.MandarAsync(Bloque(Orden.Modo, codigo), ct).ConfigureAwait(false);
            await LeerEstadoSinEsperarAsync(ct).ConfigureAwait(false);
        }, ct);

    /// <summary>Subir el PTT por aqui se salta el vigilante: se rechaza. Bajarlo, siempre.</summary>
    /// <param name="transmitir">Subir.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <returns>La tarea.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    Task IControlEquipo.PonerPttAsync(bool transmitir, CancellationToken ct) =>
        GuardiaDelPtt.Rechazar(transmitir, () => ((IPttDirecto)this).PonerPttDirectoAsync(false, ct));

    /// <inheritdoc />
    async Task IPttDirecto.PonerPttDirectoAsync(bool transmitir, CancellationToken ct)
    {
        if (_opciones.ViaDePtt != ViaDePtt.Cat && _canal.PuedeAccionarLineas)
        {
            await _canal.PonerLineaDePttAsync(transmitir, ct).ConfigureAwait(false);
        }
        else
        {
            await _canal.MandarAsync(Bloque(transmitir ? Orden.PttSi : Orden.PttNo), ct).ConfigureAwait(false);
        }

        Volatile.Write(ref _pttPedido, transmitir);
        Actualizar(e => e with { Transmitiendo = transmitir });
    }

    /// <inheritdoc />
    public RangoDeMando? Rango(MandoDeEquipo mando) => mando switch
    {
        MandoDeEquipo.Split => new RangoDeMando(mando, 0, 1, 1),
        MandoDeEquipo.Rit => new RangoDeMando(mando, 0, 1, 1),
        MandoDeEquipo.Bloqueo => new RangoDeMando(mando, 0, 1, 1),
        _ => null,
    };

    /// <inheritdoc />
    /// <remarks>El CAT antiguo no deja leer estos mandos: se devuelve lo ultimo que se puso desde aqui.</remarks>
    public Task<double?> LeerMandoAsync(MandoDeEquipo mando, CancellationToken ct = default)
    {
        lock (_candado)
        {
            return Task.FromResult<double?>(_ultimoPuesto.TryGetValue(mando, out var v) ? v : null);
        }
    }

    /// <inheritdoc />
    public Task EscribirMandoAsync(MandoDeEquipo mando, double valor, CancellationToken ct = default)
    {
        var si = valor >= 0.5;
        var orden = mando switch
        {
            MandoDeEquipo.Split => si ? Orden.SplitSi : Orden.SplitNo,
            MandoDeEquipo.Rit => si ? Orden.ClarificadorSi : Orden.ClarificadorNo,
            MandoDeEquipo.Bloqueo => si ? Orden.BloqueoSi : Orden.BloqueoNo,
            _ => throw new NotSupportedException($"El {Modelo.Nombre} no admite el mando {mando} por CAT."),
        };

        return EnExclusivaAsync(async () =>
        {
            await _canal.MandarAsync(Bloque(orden), ct).ConfigureAwait(false);
            lock (_candado) _ultimoPuesto[mando] = si ? 1 : 0;
        }, ct);
    }

    /// <inheritdoc />
    public Task<LecturaDeMedidores> LeerMedidoresAsync(CancellationToken ct = default)
    {
        lock (_candado) return Task.FromResult(_medidores);
    }

    /// <inheritdoc />
    /// <remarks>El CAT antiguo no deja leer las memorias.</remarks>
    public Task<IReadOnlyList<MemoriaDeEquipo>> LeerMemoriasAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<MemoriaDeEquipo>>([]);

    /// <inheritdoc />
    public Task IrAMemoriaAsync(int numero, CancellationToken ct = default) =>
        throw new NotSupportedException($"El CAT del {Modelo.Nombre} no tiene orden para ir a una memoria.");

    /// <inheritdoc />
    /// <remarks>El CAT antiguo es binario: no hay ordenes en texto que mandar.</remarks>
    public Task<string?> OrdenEnCrudoAsync(string orden, CancellationToken ct = default) =>
        throw new NotSupportedException($"El CAT del {Modelo.Nombre} es binario (bloques de 5 bytes): no admite órdenes en texto.");

    /// <inheritdoc />
    public Task PulsarAsync(TeclaDelEquipo tecla, CancellationToken ct = default)
    {
        if (tecla != TeclaDelEquipo.AlternarVfo)
        {
            throw new NotSupportedException($"El {Modelo.Nombre} no tiene la tecla {tecla} por CAT.");
        }

        return EnExclusivaAsync(async () =>
        {
            await _canal.MandarAsync(Bloque(Orden.AlternarVfo), ct).ConfigureAwait(false);
            await LeerEstadoSinEsperarAsync(ct).ConfigureAwait(false);
        }, ct);
    }

    /// <inheritdoc />
    /// <remarks>Diez hercios por muesca (el CAT va en decenas de hercio).</remarks>
    public Task GirarDialAsync(int muescas, CancellationToken ct = default) => MoverAsync(muescas * 10L, 0, ct);

    /// <inheritdoc />
    /// <remarks>Un kilohercio por muesca, cayendo en la rejilla.</remarks>
    public Task GirarPasosAsync(int muescas, CancellationToken ct = default) => MoverAsync(muescas * 1000L, 1000, ct);

    private Task MoverAsync(long hercios, int rejilla, CancellationToken ct)
    {
        if (hercios == 0) return Task.CompletedTask;
        return EnExclusivaAsync(async () =>
        {
            await LeerEstadoSinEsperarAsync(ct).ConfigureAwait(false);
            var ahora = Estado.Frecuencia.Hercios;
            var nueva = ahora + hercios;
            if (rejilla > 0) nueva = (long)Math.Round(nueva / (double)rejilla) * rejilla;
            nueva = Math.Clamp(nueva, Modelo.Capacidades.HerciosMinimo, Modelo.Capacidades.HerciosMaximo);
            var bcd = ABcd(nueva);
            await _canal.MandarAsync(Bloque(Orden.Frecuencia, bcd[0], bcd[1], bcd[2], bcd[3]), ct).ConfigureAwait(false);
            await LeerEstadoSinEsperarAsync(ct).ConfigureAwait(false);
        }, ct);
    }

    /// <inheritdoc />
    public Task IrABandaAsync(TeclaDeBanda tecla, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tecla);
        return PonerFrecuenciaAsync(Frecuencia.DesdeHercios(tecla.Codigo), ct);
    }

    /// <inheritdoc />
    public Task PonerModoDeTeclaAsync(TeclaDeModo modo, CancellationToken ct = default)
    {
        byte codigo = modo switch
        {
            TeclaDeModo.Lsb => 0x00,
            TeclaDeModo.Usb => 0x01,
            TeclaDeModo.Cw => 0x02,
            TeclaDeModo.Am => 0x04,
            TeclaDeModo.Fm => 0x08,
            TeclaDeModo.Datos => 0x0A,
            _ => throw new ArgumentOutOfRangeException(nameof(modo), modo, "Tecla de modo desconocida."),
        };
        return PonerCodigoDeModoAsync(codigo, ct);
    }

    /// <summary>Lee frecuencia, modo y medidor.</summary>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <returns>El estado.</returns>
    public async Task<EstadoDelEquipo> LeerEstadoAsync(CancellationToken ct = default)
    {
        await _exclusiva.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await LeerEstadoSinEsperarAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _exclusiva.Release();
        }
    }

    private async Task<EstadoDelEquipo> LeerEstadoSinEsperarAsync(CancellationToken ct)
    {
        if (!_canal.Abierto)
        {
            await _canal.AbrirAsync(ct).ConfigureAwait(false);
            Volatile.Write(ref _abiertoAlgunaVez, true);
        }

        var anterior = Estado;
        var leido = await _canal.PreguntarAsync(Bloque(Orden.LeerFrecuenciaYModo), 5, ct).ConfigureAwait(false);
        if (leido is null)
        {
            throw new EquipoNoContestaException(_canal.Descripcion);
        }

        var hercios = DesdeBcd(leido);
        var modo = ModosDelEquipo.TryGetValue(leido[4], out var nombre) ? _opciones.Traductor.DesdeElEquipo(nombre) : anterior.Modo;

        var transmitiendo = Volatile.Read(ref _pttPedido);
        double? senal = anterior.SenalRecibida;
        if (transmitiendo)
        {
            // En antena no se lee el medidor S. F7 (estado de transmision, solo consulta) sirve de
            // latido: si deja de contestar, el equipo se ha ido. Sus bits 0-3 son potencia
            // relativa (0-15), no vatios, y no se ensenan como vatios.
            _ = await _canal.PreguntarAsync(Bloque(Orden.EstadoDeTransmision), 1, ct).ConfigureAwait(false);
        }
        else
        {
            var rx = await _canal.PreguntarAsync(Bloque(Orden.EstadoDeRecepcion), 1, ct).ConfigureAwait(false);
            if (rx is { Length: 1 }) senal = AUnidadesS(rx[0] & 0x0F);
        }

        lock (_candado)
        {
            _medidores = new LecturaDeMedidores(senal, null, null, null, null, null, null, DateTimeOffset.UtcNow);
        }

        return Actualizar(e => e with
        {
            Conectado = true,
            Transmitiendo = transmitiendo,
            Frecuencia = hercios is { } h ? Frecuencia.DesdeHercios(h) : anterior.Frecuencia,
            Modo = modo,
            SenalRecibida = senal,
            PotenciaVatios = anterior.PotenciaVatios,
        });
    }

    private async Task SondearAsync(CancellationToken ct)
    {
        using var reloj = new PeriodicTimer(_opciones.IntervaloDeSondeo);
        var perdido = false;
        while (true)
        {
            try
            {
                if (!await reloj.WaitForNextTickAsync(ct).ConfigureAwait(false)) return;
                if (perdido)
                {
                    await Task.Delay(_opciones.EsperaDeReconexion, ct).ConfigureAwait(false);
                    await _canal.AbrirAsync(ct).ConfigureAwait(false);
                }

                await LeerEstadoAsync(ct).ConfigureAwait(false);
                if (perdido) _registro.LogInformation("El {Modelo} ha vuelto.", Modelo.Nombre);
                perdido = false;
                _fallosSeguidos = 0;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                if (perdido) continue;
                if (++_fallosSeguidos < ControlFt710.FallosParaDarloPorPerdido) continue;
                perdido = true;
                var porque = ex is EquipoNoContestaException ? "el equipo ha dejado de contestar; puede que se haya apagado" : ex.Message;
                _registro.LogWarning("Se ha perdido el {Modelo}: {Porque}", Modelo.Nombre, porque);
                Volatile.Write(ref _pttPedido, false);
                Actualizar(e => e with { Conectado = false, Transmitiendo = false });
                ComunicacionPerdida?.Invoke(this, porque);
            }
        }
    }

    private async Task BajarElPttComoSeaAsync(CancellationToken ct)
    {
        if (!_canal.Abierto && !Volatile.Read(ref _abiertoAlgunaVez) && !Volatile.Read(ref _pttPedido)) return;

        foreach (var via in ViasDeSuelta)
        {
            try
            {
                using var espera = new CancellationTokenSource(_opciones.EsperaDeOrden + TimeSpan.FromSeconds(1));
                await via.SoltarAsync(espera.Token).ConfigureAwait(false);
                return;
            }
            catch (Exception ex)
            {
                _registro.LogWarning(ex, "Falló la vía «{Via}» al bajar el PTT antes de desconectar.", via.Nombre);
            }
        }
    }

    private async Task EnExclusivaAsync(Func<Task> accion, CancellationToken ct)
    {
        await _exclusiva.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await accion().ConfigureAwait(false);
        }
        finally
        {
            _exclusiva.Release();
        }
    }

    private EstadoDelEquipo Actualizar(Func<EstadoDelEquipo, EstadoDelEquipo> cambio)
    {
        EstadoDelEquipo nuevo;
        bool avisar;
        lock (_candado)
        {
            var anterior = _estado;
            nuevo = cambio(anterior) with { LeidoUtc = DateTimeOffset.UtcNow };
            _estado = nuevo;
            avisar = anterior with { LeidoUtc = default } != nuevo with { LeidoUtc = default };
        }

        if (avisar) EstadoCambiado?.Invoke(this, nuevo);
        return nuevo;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_desechado) return;
        _desechado = true;
        try
        {
            await DesconectarAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _registro.LogWarning(ex, "Fallo al desconectar del {Modelo}.", Modelo.Nombre);
        }

        await _canal.DisposeAsync().ConfigureAwait(false);
        _exclusiva.Dispose();
    }
}
