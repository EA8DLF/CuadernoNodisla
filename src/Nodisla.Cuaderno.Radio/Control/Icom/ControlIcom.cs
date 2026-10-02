using System.ComponentModel;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Idiomas;
using Nodisla.Cuaderno.Radio.Control.Ft710;
using Nodisla.Cuaderno.Radio.Modelos;
using Nodisla.Cuaderno.Radio.Ptt;

namespace Nodisla.Cuaderno.Radio.Control.Icom;

/// <summary>
/// Control nativo por CI-V de las radios ICOM (IC-7300, IC-705, IC-7610, IC-9700, IC-7100,
/// IC-7851). <b>Programado segun los manuales CI-V y probado solo contra un equipo simulado:
/// sin probar con radio real.</b>
/// </summary>
/// <remarks>
/// <para>
/// Frecuencia y modo se leen por sondeo (<c>03</c>, <c>04</c>, <c>1A 06</c>, <c>25</c>, <c>26</c>...) y
/// ademas se atienden los avisos del transceive (<c>00</c>/<c>01</c> a la direccion 00) en cuanto
/// llegan, para que el dial se vea moverse sin esperar a la siguiente pasada.
/// </para>
/// <para>
/// <b>El PTT se pide al vigilante.</b> <c>1C 00 01</c> y <c>1C 01 02</c> (sintonizar) solo salen por
/// <see cref="IPttDirecto"/>, dentro del ambito que abre <see cref="OrdenesIcom"/>; la orden en
/// crudo rechaza cualquier orden que transmita.
/// </para>
/// </remarks>
public sealed class ControlIcom
    : IEquipoAvanzado, IEquipoConDosVfos, IEquipoConTeclas, IEquipoConBotonera, IEquipoConEncendido,
      IEquipoDeModelo, IEquipoConSintonia, IPttDirecto, ISueltaDeEmergenciaPtt, IAvisaDePerdidaDeComunicacion
{
    /// <summary>Pasadas seguidas sin respuesta para dar el equipo por perdido.</summary>
    public const int FallosParaDarloPorPerdido = 3;

    private readonly CanalCiv _canal;
    private readonly PerfilIcom _perfil;
    private readonly OpcionesFt710 _opciones;
    private readonly ILogger _registro;
    private readonly int _baudios;
    private readonly object _candado = new();
    private readonly Dictionary<MandoDeEquipo, MandoIcom> _todos;
    private readonly HashSet<MandoDeEquipo> _mandos = [];
    private readonly SemaphoreSlim _exclusiva = new(1, 1);
    private static readonly AsyncLocal<bool> DentroDeLaExclusiva = new();

    private CancellationTokenSource? _ctsSondeo;
    private Task? _sondeo;
    private EstadoDelEquipo _estado = EstadoDelEquipo.Desconectado;
    private EstadoDeLosVfos _vfos = EstadoDeLosVfos.SinDatos;
    private bool _pttPedido;
    private bool _desechado;
    private bool _canalAbiertoAlgunaVez;
    private int _sintoniaPedida;
    private bool _sintonizando;
    private bool _enMemoria;
    private int _canalDeMemoria = 1;
    private bool _bEsElActivo;
    private bool _datos;

    /// <summary>Crea el control sobre un canal CI-V.</summary>
    /// <param name="canal">Canal (con la direccion de la radio ya puesta).</param>
    /// <param name="perfil">Perfil del modelo.</param>
    /// <param name="opciones">Ajustes CAT comunes (espera, sondeo, via del PTT, traductor).</param>
    /// <param name="registro">Donde anotar.</param>
    /// <param name="baudios">Velocidad del puerto, para el encendido (cuantos <c>FE</c> mandar).</param>
    public ControlIcom(CanalCiv canal, PerfilIcom perfil, OpcionesFt710? opciones = null, ILogger? registro = null, int baudios = 115200)
    {
        ArgumentNullException.ThrowIfNull(canal);
        ArgumentNullException.ThrowIfNull(perfil);
        _canal = canal;
        _perfil = perfil;
        _opciones = opciones ?? new OpcionesFt710();
        _registro = registro ?? NullLogger.Instance;
        _baudios = baudios;
        _todos = MandosIcom.De(perfil).ToDictionary(m => m.Mando);
        NombreDelEquipo = perfil.Modelo.NombreCompleto;
        _canal.TramaEspontanea += AlAvisoDelTransceive;

        var nombre = perfil.Modelo.Nombre;
        List<ViaDeSuelta> vias = [];
        if (_opciones.ViaDePtt != ViaDePtt.Cat && _canal.PuedeAccionarLineas)
        {
            vias.Add(new ViaDeSuelta(
                $"{nombre}: bajar la línea {_opciones.ViaDePtt} del puerto",
                async ct => await _canal.PonerLineaDePttAsync(false, ct).ConfigureAwait(false),
                () => _canal.PonerLineaDePttSincrono(false)));
        }

        vias.Add(new ViaDeSuelta(
            $"{nombre}: 1C 00 00 por el canal abierto",
            async ct => await BajarPorCivAsync(ct).ConfigureAwait(false),
            () => _canal.MandarSincrono([.. OrdenesIcom.BajarPtt])));
        vias.Add(new ViaDeSuelta(
            $"{nombre}: reabrir el puerto y 1C 00 00",
            async ct =>
            {
                await _canal.AbrirAsync(ct).ConfigureAwait(false);
                await BajarPorCivAsync(ct).ConfigureAwait(false);
            }));
        ViasDeSuelta = vias;
    }

    /// <inheritdoc />
    public ModeloDeEquipo Modelo => _perfil.Modelo;

    /// <summary>El perfil CI-V del modelo.</summary>
    public PerfilIcom Perfil => _perfil;

    /// <inheritdoc />
    public ViaDeControl Via => ViaDeControl.CatNativo;

    /// <inheritdoc />
    public string NombreDelEquipo { get; private set; }

    /// <inheritdoc />
    public EstadoDelEquipo Estado
    {
        get
        {
            lock (_candado) return _estado;
        }
    }

    /// <inheritdoc />
    public EstadoDeLosVfos Vfos
    {
        get
        {
            lock (_candado) return _vfos;
        }
    }

    /// <inheritdoc />
    public IReadOnlySet<MandoDeEquipo> Mandos
    {
        get
        {
            lock (_candado) return new HashSet<MandoDeEquipo>(_mandos);
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<ViaDeSuelta> ViasDeSuelta { get; }

    /// <inheritdoc />
    public event EventHandler<EstadoDelEquipo>? EstadoCambiado;

    /// <inheritdoc />
    public event EventHandler<string>? ComunicacionPerdida;

    /// <summary>Salta cuando vuelve un equipo que se habia perdido.</summary>
    public event EventHandler<string>? ComunicacionRecuperada;

    // ── Conexion ──────────────────────────────────────────────────────────────

    /// <inheritdoc />
    /// <exception cref="CanalNoDisponibleException">Si el puerto no existe o esta ocupado.</exception>
    /// <exception cref="EquipoNoContestaException">Si nadie contesta a <c>19 00</c>.</exception>
    public async Task ConectarAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_desechado, this);
        if (!_canal.Abierto) await _canal.AbrirAsync(ct).ConfigureAwait(false);
        Volatile.Write(ref _canalAbiertoAlgunaVez, true);

        await ComprobarQueSigueAhiAsync(ct).ConfigureAwait(false);
        await AveriguarMandosAsync(ct).ConfigureAwait(false);
        await LeerEstadoAsync(ct).ConfigureAwait(false);

        lock (_candado)
        {
            if (_sondeo is not null) return;
            _ctsSondeo = new CancellationTokenSource();
            var token = _ctsSondeo.Token;
            _sondeo = Task.Run(() => SondearSiempreAsync(token), CancellationToken.None);
        }
    }

    /// <inheritdoc />
    public async Task DesconectarAsync(CancellationToken ct = default)
    {
        CancellationTokenSource? cts;
        Task? sondeo;
        lock (_candado)
        {
            (cts, sondeo) = (_ctsSondeo, _sondeo);
            (_ctsSondeo, _sondeo) = (null, null);
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
                _registro.LogDebug(ex, "El sondeo del {Equipo} no terminó a tiempo.", NombreDelEquipo);
            }
        }

        cts?.Dispose();
        await BajarElPttComoSeaAsync(ct).ConfigureAwait(false);
        _canal.Cerrar();
        lock (_candado) _vfos = EstadoDeLosVfos.SinDatos;
        Actualizar(e => e with { Conectado = false, Transmitiendo = false });
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_desechado) return;
        try
        {
            await DesconectarAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _registro.LogDebug(ex, "Fallo al desconectar el {Equipo} al desecharlo.", NombreDelEquipo);
        }

        _desechado = true;
        _canal.TramaEspontanea -= AlAvisoDelTransceive;
        await _canal.DisposeAsync().ConfigureAwait(false);
        _exclusiva.Dispose();
    }

    // ── Encendido ────────────────────────────────────────────────────────────

    /// <inheritdoc />
    /// <remarks>El unico sitio por el que sale <c>18 00</c>: primero se baja el PTT.</remarks>
    public async Task ApagarAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_desechado, this);
        if (!_canal.Abierto) throw new InvalidOperationException(Textos.T("Servicios.Radio.NoApagaDesconectada"));
        await BajarElPttComoSeaAsync(ct).ConfigureAwait(false);
        await OrdenesIcom.ConApagadoAutorizadoAsync(
            async () => await _canal.PreguntarAsync([.. OrdenesIcom.Apagar], ct).ConfigureAwait(false)).ConfigureAwait(false);
        _registro.LogInformation("{Equipo} apagado desde el programa (confirmado por el operador).", NombreDelEquipo);
        await DesconectarAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Cuantos <c>FE</c> pide el manual antes de <c>18 01</c> a cada velocidad.</summary>
    /// <param name="baudios">Velocidad.</param>
    /// <returns>Repeticiones.</returns>
    public static int PreambulosDeEncendido(int baudios) => baudios switch
    {
        >= 115200 => 150,
        >= 57600 => 75,
        >= 38400 => 50,
        >= 19200 => 25,
        >= 9600 => 13,
        _ => 7,
    };

    /// <inheritdoc />
    public async Task<string?> EncenderAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_desechado, this);
        try
        {
            if (!_canal.Abierto) await _canal.AbrirAsync(ct).ConfigureAwait(false);
        }
        catch (CanalNoDisponibleException)
        {
            return Textos.F("Servicios.Radio.NoEnciendePuerto", _canal.Descripcion);
        }

        await _canal.DespertarAsync(PreambulosDeEncendido(_baudios), ct).ConfigureAwait(false);
        _registro.LogInformation("{Equipo}: orden de encendido enviada.", NombreDelEquipo);
        for (var intento = 0; intento < 10; intento++)
        {
            await _opciones.Esperar(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
            try
            {
                await ConectarAsync(ct).ConfigureAwait(false);
                return null;
            }
            catch (EquipoNoContestaException)
            {
                // Todavia arrancando.
            }
        }

        return Textos.T("Servicios.Radio.EncendidoSinRespuesta");
    }

    // ── Frecuencia y modo ────────────────────────────────────────────────────

    /// <inheritdoc />
    /// <remarks><c>05</c>: frecuencia del VFO (o banda) elegido en ese momento.</remarks>
    public Task PonerFrecuenciaAsync(Frecuencia frecuencia, CancellationToken ct = default)
    {
        var datos = BcdFrecuencia(frecuencia);
        return EnExclusivaAsync(async () =>
        {
            await OrdenAsync([0x05, .. datos], ct).ConfigureAwait(false);
            await LeerEstadoAsync(ct).ConfigureAwait(false);
        }, ct);
    }

    /// <inheritdoc />
    public Task PonerModoAsync(Modo modo, CancellationToken ct = default) =>
        EnExclusivaAsync(async () =>
        {
            var (codigo, datos) = ModoAlEquipo(modo, Estado.Frecuencia);
            await OrdenAsync([0x06, codigo], ct).ConfigureAwait(false);
            await PonerDatosAsync(datos, ct).ConfigureAwait(false);
            await LeerEstadoAsync(ct).ConfigureAwait(false);
        }, ct);

    private (byte Codigo, bool Datos) ModoAlEquipo(Modo modo, Frecuencia frecuencia)
    {
        var nombre = _opciones.Traductor.AlEquipo(modo, frecuencia)
                     ?? throw new ArgumentException(Textos.F("Servicios.Radio.ModoDesconocido", _perfil.Modelo.Nombre, modo), nameof(modo));
        return ModosIcom.AlEquipo(nombre)
               ?? throw new ArgumentException(Textos.F("Servicios.Radio.SinEseModo", _perfil.Modelo.Nombre, nombre), nameof(modo));
    }

    private async Task PonerDatosAsync(bool datos, CancellationToken ct)
    {
        // 1A 06: modo de datos y filtro (01 = FIL1). Con datos apagados el filtro va a 00.
        await OrdenAsync([0x1A, 0x06, (byte)(datos ? 0x01 : 0x00), (byte)(datos ? 0x01 : 0x00)], ct, tolerarNo: true).ConfigureAwait(false);
    }

    private byte[] BcdFrecuencia(Frecuencia frecuencia)
    {
        var hz = frecuencia.Hercios;
        var capacidades = _perfil.Modelo.Capacidades;
        if (hz < capacidades.HerciosMinimo || hz > capacidades.HerciosMaximo)
        {
            throw new ArgumentOutOfRangeException(nameof(frecuencia), frecuencia,
                Textos.F("Servicios.Radio.RangoDeFrecuencias", _perfil.Modelo.Nombre, capacidades.HerciosMinimo, capacidades.HerciosMaximo));
        }

        return BcdCiv.Frecuencia(hz);
    }

    // ── Dos VFO ──────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<EstadoDeLosVfos> LeerVfosAsync(CancellationToken ct = default)
    {
        await LeerEstadoAsync(ct).ConfigureAwait(false);
        return Vfos;
    }

    /// <inheritdoc />
    /// <remarks>
    /// IC-7610/IC-7851: <c>07 D0</c> (MAIN) / <c>07 D1</c> (SUB). Los demas: <c>07 00</c> / <c>07 01</c>.
    /// </remarks>
    public Task PonerVfoActivoAsync(NombreDeVfo vfo, CancellationToken ct = default) =>
        EnExclusivaAsync(async () =>
        {
            var b = vfo == NombreDeVfo.B;
            byte sub = _perfil.Reparto == RepartoDeVfos.PrincipalYSecundario ? (byte)(b ? 0xD1 : 0xD0) : (byte)(b ? 0x01 : 0x00);
            await OrdenAsync([0x07, sub], ct).ConfigureAwait(false);
            Volatile.Write(ref _bEsElActivo, b);
            Volatile.Write(ref _enMemoria, false);
            await LeerEstadoAsync(ct).ConfigureAwait(false);
        }, ct);

    /// <inheritdoc />
    /// <remarks>
    /// <c>07 B0</c>: intercambia A/B (o MAIN/SUB). En el IC-9700 <c>07 B0</c> intercambia las
    /// bandas MAIN y SUB, que no es lo que se pide: alli se leen A y B y se reescriben cruzados.
    /// </remarks>
    public Task IntercambiarVfosAsync(CancellationToken ct = default) =>
        EnExclusivaAsync(async () =>
        {
            if (_perfil.Reparto == RepartoDeVfos.VfoAyBDeLaPrincipal)
            {
                var elegido = await LeerFrecuenciaYModoAsync(0x00, ct).ConfigureAwait(false);
                var otro = await LeerFrecuenciaYModoAsync(0x01, ct).ConfigureAwait(false);
                if (elegido is null || otro is null) throw new InvalidOperationException(Textos.T("Servicios.Radio.NoLeeLosVfos"));
                await EscribirFrecuenciaYModoAsync(0x00, otro.Value, ct).ConfigureAwait(false);
                await EscribirFrecuenciaYModoAsync(0x01, elegido.Value, ct).ConfigureAwait(false);
            }
            else
            {
                await OrdenAsync([0x07, 0xB0], ct).ConfigureAwait(false);
            }

            await LeerEstadoAsync(ct).ConfigureAwait(false);
        }, ct);

    /// <inheritdoc />
    /// <remarks>
    /// Un receptor: <c>07 A0</c> (A = B). MAIN/SUB: se copia el elegido sobre el otro con <c>25</c>/<c>26</c>
    /// (<c>07 B1</c> copia siempre MAIN sobre SUB, y el activo puede ser SUB).
    /// </remarks>
    public Task IgualarVfosAsync(CancellationToken ct = default) =>
        EnExclusivaAsync(async () =>
        {
            if (_perfil.Reparto == RepartoDeVfos.PrincipalYSecundario)
            {
                var activoEsSub = await ElSecundarioEsElActivoAsync(ct).ConfigureAwait(false);
                byte de = activoEsSub ? (byte)0x01 : (byte)0x00;
                var datos = await LeerFrecuenciaYModoAsync(de, ct).ConfigureAwait(false)
                            ?? throw new InvalidOperationException(Textos.T("Servicios.Radio.NoLeeVfoActivo"));
                await EscribirFrecuenciaYModoAsync((byte)(de ^ 0x01), datos, ct).ConfigureAwait(false);
            }
            else
            {
                await OrdenAsync([0x07, 0xA0], ct).ConfigureAwait(false);
            }

            await LeerEstadoAsync(ct).ConfigureAwait(false);
        }, ct);

    /// <inheritdoc />
    public Task PonerFrecuenciaDeAsync(NombreDeVfo vfo, Frecuencia frecuencia, CancellationToken ct = default)
    {
        var datos = BcdFrecuencia(frecuencia);
        return EnExclusivaAsync(async () =>
        {
            var selector = await SelectorDeAsync(vfo, ct).ConfigureAwait(false);
            await OrdenAsync([0x25, selector, .. datos], ct).ConfigureAwait(false);
            await LeerEstadoAsync(ct).ConfigureAwait(false);
        }, ct);
    }

    /// <inheritdoc />
    public Task PonerModoDeAsync(NombreDeVfo vfo, Modo modo, CancellationToken ct = default) =>
        EnExclusivaAsync(async () =>
        {
            var selector = await SelectorDeAsync(vfo, ct).ConfigureAwait(false);
            var actual = await LeerFrecuenciaYModoAsync(selector, ct).ConfigureAwait(false);
            var (codigo, datos) = ModoAlEquipo(modo, actual?.Frecuencia ?? Estado.Frecuencia);
            var filtro = actual?.Filtro ?? 0x01;
            await OrdenAsync([0x26, selector, codigo, (byte)(datos ? 0x01 : 0x00), filtro], ct).ConfigureAwait(false);
            await LeerEstadoAsync(ct).ConfigureAwait(false);
        }, ct);

    /// <summary>
    /// El byte de <c>25</c>/<c>26</c> para un VFO: con MAIN/SUB es fijo (A = 00 = MAIN, B = 01 =
    /// SUB); con A/B es 00 si ese VFO es el elegido y 01 si es el otro.
    /// </summary>
    private Task<byte> SelectorDeAsync(NombreDeVfo vfo, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (_perfil.Reparto == RepartoDeVfos.PrincipalYSecundario) return Task.FromResult(vfo == NombreDeVfo.B ? (byte)0x01 : (byte)0x00);
        var esElActivo = (vfo == NombreDeVfo.B) == Volatile.Read(ref _bEsElActivo);
        return Task.FromResult(esElActivo ? (byte)0x00 : (byte)0x01);
    }

    private async Task<bool> ElSecundarioEsElActivoAsync(CancellationToken ct)
    {
        var r = await _canal.PreguntarAsync([0x07, 0xD2], ct).ConfigureAwait(false);
        return r is not null && r.EmpiezaPor([0x07, 0xD2]) && r.DatosTras(2) is [0x01, ..];
    }

    private readonly record struct FrecuenciaYModo(Frecuencia Frecuencia, byte Modo, bool Datos, byte Filtro);

    private async Task<FrecuenciaYModo?> LeerFrecuenciaYModoAsync(byte selector, CancellationToken ct)
    {
        var f = await _canal.PreguntarAsync([0x25, selector], ct).ConfigureAwait(false);
        var m = await _canal.PreguntarAsync([0x26, selector], ct).ConfigureAwait(false);
        if (f is null || !f.EmpiezaPor([0x25, selector]) || BcdCiv.Hercios(f.DatosTras(2)) is not { } hz) return null;
        var dm = m is not null && m.EmpiezaPor([0x26, selector]) ? m.DatosTras(2) : [];
        return new FrecuenciaYModo(
            Frecuencia.DesdeHercios(hz),
            dm.Length > 0 ? dm[0] : (byte)0x01,
            dm.Length > 1 && dm[1] == 0x01,
            dm.Length > 2 ? dm[2] : (byte)0x01);
    }

    private async Task EscribirFrecuenciaYModoAsync(byte selector, FrecuenciaYModo valor, CancellationToken ct)
    {
        await OrdenAsync([0x25, selector, .. BcdCiv.Frecuencia(valor.Frecuencia.Hercios)], ct).ConfigureAwait(false);
        await OrdenAsync([0x26, selector, valor.Modo, (byte)(valor.Datos ? 0x01 : 0x00), valor.Filtro], ct).ConfigureAwait(false);
    }

    // ── Teclas ────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    /// <remarks>
    /// Sin memoria rapida (QMB), ajuste a cero ni DSP RESET: no hay orden CI-V equivalente en los
    /// manuales. Van las demas.
    /// </remarks>
    public IReadOnlySet<TeclaDelEquipo> Teclas { get; } = new HashSet<TeclaDelEquipo>
    {
        TeclaDelEquipo.MemoriaAVfo,
        TeclaDelEquipo.VfoOMemoria,
        TeclaDelEquipo.BandaArriba,
        TeclaDelEquipo.BandaAbajo,
        TeclaDelEquipo.AlternarVfo,
        TeclaDelEquipo.BorrarClarificador,
    };

    /// <summary>Las tramas (cuerpos) que manda una tecla simple, para las pruebas y la ayuda.</summary>
    /// <param name="tecla">Tecla.</param>
    /// <returns>Los cuerpos, o vacio si la tecla depende del estado.</returns>
    public static IReadOnlyList<byte[]> OrdenesDeLaTecla(TeclaDelEquipo tecla) => tecla switch
    {
        TeclaDelEquipo.MemoriaAVfo => [[0x0A]],
        TeclaDelEquipo.BorrarClarificador => [[0x21, 0x00, 0x00, 0x00, 0x00]],
        _ => [],
    };

    /// <inheritdoc />
    public Task PulsarAsync(TeclaDelEquipo tecla, CancellationToken ct = default)
    {
        if (!Teclas.Contains(tecla)) throw new NotSupportedException(Textos.F("Servicios.Radio.SinTeclaCiv", _perfil.Modelo.Nombre, tecla));

        return tecla switch
        {
            TeclaDelEquipo.BandaArriba => SaltarDeBandaAsync(+1, ct),
            TeclaDelEquipo.BandaAbajo => SaltarDeBandaAsync(-1, ct),
            TeclaDelEquipo.AlternarVfo => AlternarVfoAsync(ct),
            _ => EnExclusivaAsync(async () =>
            {
                switch (tecla)
                {
                    case TeclaDelEquipo.MemoriaAVfo:
                        await OrdenAsync([0x0A], ct).ConfigureAwait(false);
                        break;
                    case TeclaDelEquipo.VfoOMemoria:
                        var aMemoria = !Volatile.Read(ref _enMemoria);
                        await OrdenAsync(aMemoria ? [0x08] : [0x07], ct).ConfigureAwait(false);
                        Volatile.Write(ref _enMemoria, aMemoria);
                        break;
                    case TeclaDelEquipo.BorrarClarificador:
                        await OrdenAsync([0x21, 0x00, 0x00, 0x00, 0x00], ct).ConfigureAwait(false);
                        break;
                }

                await LeerEstadoAsync(ct).ConfigureAwait(false);
            }, ct),
        };
    }

    private async Task AlternarVfoAsync(CancellationToken ct)
    {
        var bAhora = _perfil.Reparto == RepartoDeVfos.PrincipalYSecundario
            ? await ElSecundarioEsElActivoAsync(ct).ConfigureAwait(false)
            : Volatile.Read(ref _bEsElActivo);
        await PonerVfoActivoAsync(bAhora ? NombreDeVfo.A : NombreDeVfo.B, ct).ConfigureAwait(false);
    }

    private async Task SaltarDeBandaAsync(int sentido, CancellationToken ct)
    {
        var bandas = _perfil.Bandas;
        var actual = Estado.Frecuencia;
        var indice = -1;
        for (var i = 0; i < bandas.Count; i++)
        {
            if (!bandas[i].Tecla.EsCoberturaGeneral && bandas[i].Tecla.Banda.Contiene(actual)) indice = i;
        }

        var siguiente = indice < 0
            ? (sentido > 0 ? 0 : bandas.Count - 1)
            : (indice + sentido + bandas.Count) % bandas.Count;
        await IrABandaAsync(bandas[siguiente].Tecla, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>Paso del dial: el que tenga la radio en <c>10</c>; con el paso apagado, 10 Hz.</remarks>
    public Task GirarDialAsync(int muescas, CancellationToken ct = default) =>
        muescas == 0 ? Task.CompletedTask : EnExclusivaAsync(async () =>
        {
            var paso = 10;
            var r = await _canal.PreguntarAsync([0x10], ct).ConfigureAwait(false);
            if (r is not null && r.EmpiezaPor([0x10]) && BcdCiv.Numero(r.DatosTras(1).AsSpan(0, Math.Min(1, r.DatosTras(1).Length))) is { } codigo
                && codigo < _perfil.PasosDeSintonia.Count)
            {
                paso = _perfil.PasosDeSintonia[codigo];
            }

            await MoverAsync((long)muescas * paso, ct).ConfigureAwait(false);
        }, ct);

    /// <inheritdoc />
    /// <remarks>En memoria cambia de canal (<c>08 cc cc</c>); en VFO salta de kHz en kHz.</remarks>
    public Task GirarPasosAsync(int muescas, CancellationToken ct = default) =>
        muescas == 0 ? Task.CompletedTask : EnExclusivaAsync(async () =>
        {
            if (Volatile.Read(ref _enMemoria))
            {
                var canal = ((Volatile.Read(ref _canalDeMemoria) - 1 + muescas) % 99 + 99) % 99 + 1;
                await OrdenAsync([0x08, .. BcdCiv.Numero(canal, 2)], ct).ConfigureAwait(false);
                Volatile.Write(ref _canalDeMemoria, canal);
                await LeerEstadoAsync(ct).ConfigureAwait(false);
                return;
            }

            await MoverAsync(muescas * 1_000L, ct).ConfigureAwait(false);
        }, ct);

    private async Task MoverAsync(long hercios, CancellationToken ct)
    {
        var r = await _canal.PreguntarAsync([0x03], ct).ConfigureAwait(false);
        if (r is null || !r.EmpiezaPor([0x03]) || BcdCiv.Hercios(r.DatosTras(1)) is not { } actual)
        {
            throw new EquipoNoContestaException(_canal.Descripcion);
        }

        var capacidades = _perfil.Modelo.Capacidades;
        var nueva = Math.Clamp(actual + hercios, capacidades.HerciosMinimo, capacidades.HerciosMaximo);
        await OrdenAsync([0x05, .. BcdCiv.Frecuencia(nueva)], ct).ConfigureAwait(false);
        await LeerEstadoAsync(ct).ConfigureAwait(false);
    }

    // ── Botonera ─────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public IReadOnlyList<TeclaDeBanda> TeclasDeBanda => _perfil.Bandas.Select(b => b.Tecla).ToList();

    /// <inheritdoc />
    /// <remarks>
    /// ICOM no tiene una orden «ve a la banda» como <c>BS</c> de Yaesu. Se lee el registro mas
    /// reciente de la pila de banda (<c>1A 01 bb 01</c>, <b>solo lectura</b>) y se pone esa
    /// frecuencia (<c>05</c>), modo (<c>06</c>) y datos (<c>1A 06</c>). Si el modelo no tiene
    /// pila verificada o no contesta, se va a una frecuencia y modo fijos de la banda.
    /// </remarks>
    public Task IrABandaAsync(TeclaDeBanda tecla, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tecla);
        var banda = _perfil.Bandas.FirstOrDefault(b => b.Tecla.Codigo == tecla.Codigo)
                    ?? throw new ArgumentOutOfRangeException(nameof(tecla), tecla.Codigo, $"El {_perfil.Modelo.Nombre} no tiene esa tecla de banda.");

        return EnExclusivaAsync(async () =>
        {
            long hz = banda.HerciosPorOmision;
            var modo = ModosIcom.AlEquipo(banda.ModoPorOmision) ?? ((byte)0x01, false);
            byte? filtro = null;

            if (banda.CodigoDePila is { } codigo)
            {
                var pila = await _canal.PreguntarAsync([0x1A, 0x01, codigo, 0x01], ct).ConfigureAwait(false);
                var datos = pila is not null && pila.EmpiezaPor([0x1A, 0x01, codigo, 0x01]) ? pila.DatosTras(4) : [];
                if (datos.Length >= 6 && BcdCiv.Hercios(datos.AsSpan(0, 5)) is { } deLaPila and > 0)
                {
                    hz = deLaPila;
                    // Byte 8 del registro: nibble alto = modo de datos (como en las memorias).
                    modo = (datos[5], datos.Length >= 8 && (datos[7] >> 4) == 0x01);
                    filtro = datos.Length >= 7 ? datos[6] : null;
                }
                else
                {
                    _registro.LogDebug("{Equipo}: la pila de banda {Codigo:X2} no contesta; se va a la frecuencia fija.", NombreDelEquipo, codigo);
                }
            }

            await OrdenAsync([0x05, .. BcdCiv.Frecuencia(hz)], ct).ConfigureAwait(false);
            await OrdenAsync(filtro is { } f ? [0x06, modo.Item1, f] : [0x06, modo.Item1], ct).ConfigureAwait(false);
            await PonerDatosAsync(modo.Item2, ct).ConfigureAwait(false);
            await LeerEstadoAsync(ct).ConfigureAwait(false);
        }, ct);
    }

    /// <inheritdoc />
    public Task PonerModoDeTeclaAsync(TeclaDeModo modo, CancellationToken ct = default) =>
        EnExclusivaAsync(async () =>
        {
            var (codigo, datos) = OrdenDeModo(modo);
            await OrdenAsync([0x06, codigo], ct).ConfigureAwait(false);
            await PonerDatosAsync(datos, ct).ConfigureAwait(false);
            await LeerEstadoAsync(ct).ConfigureAwait(false);
        }, ct);

    /// <summary>Codigo CI-V y datos de una tecla de modo. DATA = USB con datos (USB-D), como se opera en digitales.</summary>
    /// <param name="modo">Tecla.</param>
    /// <returns>El codigo y si lleva datos.</returns>
    public static (byte Codigo, bool Datos) OrdenDeModo(TeclaDeModo modo) => modo switch
    {
        TeclaDeModo.Lsb => (0x00, false),
        TeclaDeModo.Usb => (0x01, false),
        TeclaDeModo.Am => (0x02, false),
        TeclaDeModo.Cw => (0x03, false),
        TeclaDeModo.Fm => (0x05, false),
        TeclaDeModo.Datos => (0x01, true),
        _ => throw new ArgumentOutOfRangeException(nameof(modo), modo, "Tecla de modo desconocida."),
    };

    // ── Mandos ───────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public RangoDeMando? Rango(MandoDeEquipo mando)
    {
        lock (_candado)
        {
            return _mandos.Contains(mando) && _todos.TryGetValue(mando, out var m) ? m.Rango : null;
        }
    }

    /// <inheritdoc />
    public async Task<double?> LeerMandoAsync(MandoDeEquipo mando, CancellationToken ct = default)
    {
        if (Rango(mando) is null || !_todos.TryGetValue(mando, out var descripcion)) return null;
        var r = await _canal.PreguntarAsync(descripcion.Orden, ct).ConfigureAwait(false);
        if (r is null || !r.EmpiezaPor(descripcion.Orden)) return null;
        return descripcion.Leer(r.DatosTras(descripcion.Orden.Length));
    }

    /// <inheritdoc />
    /// <exception cref="NotSupportedException">Si el equipo no tiene el mando.</exception>
    /// <exception cref="InvalidOperationException">Si el mando transmite y no hay transmision pedida al vigilante.</exception>
    public async Task EscribirMandoAsync(MandoDeEquipo mando, double valor, CancellationToken ct = default)
    {
        var rango = Rango(mando);
        if (rango is null || !_todos.TryGetValue(mando, out var descripcion))
        {
            throw new NotSupportedException(Textos.F("Servicios.Radio.SinMando", _perfil.Modelo.Nombre, mando));
        }

        var ajustado = rango.Ajustar(valor);
        var emite = rango.TransmiteAlAccionar && (!rango.EsDePosiciones || ajustado >= rango.Maximo);
        byte[] cuerpo = [.. descripcion.Orden, .. descripcion.Escribir(ajustado)];
        if (!emite)
        {
            await OrdenAsync(cuerpo, ct).ConfigureAwait(false);
            return;
        }

        if (!Volatile.Read(ref _pttPedido))
        {
            throw new InvalidOperationException(
                $"El mando {mando} pone el equipo en antena: hay que pedir antes una transmisión a "
                + "IVigilantePtt y accionarlo dentro de ella.");
        }

        await OrdenesIcom.ConTransmisionAutorizadaAsync(() => OrdenAsync(cuerpo, ct)).ConfigureAwait(false);
    }

    private async Task AveriguarMandosAsync(CancellationToken ct)
    {
        HashSet<MandoDeEquipo> admitidos = [];
        foreach (var m in _todos.Values)
        {
            var r = await _canal.PreguntarAsync(m.Orden, ct).ConfigureAwait(false);
            if (r is not null && r.EmpiezaPor(m.Orden))
            {
                admitidos.Add(m.Mando);
            }
            else
            {
                _registro.LogDebug("{Equipo}: {Orden} no contesta ({Respuesta}); {Mando} fuera.",
                    NombreDelEquipo, Hex.De(m.Orden), r?.ToString() ?? "nada", m.Mando);
            }
        }

        lock (_candado)
        {
            _mandos.Clear();
            _mandos.UnionWith(admitidos);
        }
    }

    // ── Medidores ────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<LecturaDeMedidores> LeerMedidoresAsync(CancellationToken ct = default)
    {
        var s = await LeerMedidorAsync(0x02, ct).ConfigureAwait(false);
        var po = await LeerMedidorAsync(0x11, ct).ConfigureAwait(false);
        var swr = await LeerMedidorAsync(0x12, ct).ConfigureAwait(false);
        var alc = await LeerMedidorAsync(0x13, ct).ConfigureAwait(false);
        var comp = await LeerMedidorAsync(0x14, ct).ConfigureAwait(false);
        var vd = await LeerMedidorAsync(0x15, ct).ConfigureAwait(false);
        var id = await LeerMedidorAsync(0x16, ct).ConfigureAwait(false);

        return new LecturaDeMedidores(
            s is { } cs ? AUnidadesS(cs) : null,
            po is { } cp ? Interpolar(cp, EscalaPo) * _perfil.PotenciaMaxima / 100d : null,
            swr is { } cw ? Interpolar(cw, EscalaRoe) : null,
            alc is { } ca ? ca * 100d / 120d : null,
            id is { } ci ? Interpolar(ci, _perfil.EscalaCorriente) : null,
            vd is { } cv ? Interpolar(cv, _perfil.EscalaTension) : null,
            comp is { } cc ? Interpolar(cc, EscalaCompresion) : null,
            DateTimeOffset.UtcNow);
    }

    private static readonly (int, double)[] EscalaPo = [(0, 0), (143, 50), (213, 100)];
    private static readonly (int, double)[] EscalaRoe = [(0, 1.0), (48, 1.5), (80, 2.0), (120, 3.0)];
    private static readonly (int, double)[] EscalaCompresion = [(0, 0), (130, 15), (241, 30)];

    /// <summary>
    /// Medidor S de 0 a 255 a unidades S: 0 = S0, 120 = S9, 241 = S9+60 dB (todos los manuales).
    /// Por encima de S9 cada unidad son 6 dB.
    /// </summary>
    /// <param name="crudo">Lectura.</param>
    /// <returns>Unidades S (de 0 a 19).</returns>
    public static double AUnidadesS(int crudo) =>
        crudo <= 120
            ? Math.Clamp(crudo * 9d / 120d, 0, 9)
            : Math.Clamp(9d + ((crudo - 120) * 60d / 121d / 6d), 9, 19);

    /// <summary>Interpolacion lineal a trozos, alargando el ultimo tramo.</summary>
    /// <param name="crudo">Lectura.</param>
    /// <param name="puntos">Puntos (crudo, valor) crecientes.</param>
    /// <returns>El valor.</returns>
    public static double Interpolar(int crudo, IReadOnlyList<(int Crudo, double Valor)> puntos)
    {
        if (puntos.Count == 0) return crudo;
        if (puntos.Count == 1 || crudo <= puntos[0].Crudo) return puntos[0].Valor;
        for (var i = 1; i < puntos.Count; i++)
        {
            if (crudo <= puntos[i].Crudo || i == puntos.Count - 1)
            {
                var (x0, y0) = puntos[i - 1];
                var (x1, y1) = puntos[i];
                return y0 + ((crudo - x0) * (y1 - y0) / (x1 - x0));
            }
        }

        return puntos[^1].Valor;
    }

    private async Task<int?> LeerMedidorAsync(byte sub, CancellationToken ct)
    {
        var r = await _canal.PreguntarAsync([0x15, sub], ct).ConfigureAwait(false);
        return r is not null && r.EmpiezaPor([0x15, sub]) && r.DatosTras(2) is { Length: >= 2 } d ? BcdCiv.Numero(d.AsSpan(0, 2)) : null;
    }

    // ── Memorias ─────────────────────────────────────────────────────────────

    /// <inheritdoc />
    /// <remarks>
    /// Solo lectura (<c>1A 00 cc cc</c>, sin datos). Formato del IC-7300/IC-7610/IC-7851: seleccion,
    /// frecuencia (5), modo, filtro, datos/tono, tonos y 10 letras de nombre. Una memoria vacia
    /// contesta <c>FF</c>. Los modelos con bancos (IC-705, IC-9700, IC-7100) no se leen.
    /// </remarks>
    public async Task<IReadOnlyList<MemoriaDeEquipo>> LeerMemoriasAsync(CancellationToken ct = default)
    {
        if (!_perfil.LeeMemorias) return [];
        List<MemoriaDeEquipo> memorias = [];
        for (var canal = 1; canal <= _perfil.Modelo.Capacidades.Memorias; canal++)
        {
            var numero = BcdCiv.Numero(canal, 2);
            byte[] orden = [0x1A, 0x00, .. numero];
            var r = await _canal.PreguntarAsync(orden, ct).ConfigureAwait(false);
            if (r is null || !r.EmpiezaPor(orden)) continue;
            var d = r.DatosTras(4);
            if (d.Length < 8 || d[0] == 0xFF || BcdCiv.Hercios(d.AsSpan(1, 5)) is not { } hz)
            {
                memorias.Add(new MemoriaDeEquipo(canal, Frecuencia.Cero, Modo.Vacio, null, false));
                continue;
            }

            var nombreDelModo = ModosIcom.DesdeElEquipo(d[6], d.Length > 8 && (d[8] >> 4) == 0x01);
            string? etiqueta = null;
            if (d.Length >= 10)
            {
                var texto = Encoding.ASCII.GetString(d, Math.Max(0, d.Length - 10), 10).Trim();
                etiqueta = texto.Length > 0 && texto.All(c => c >= 0x20 && c < 0x7F) ? texto : null;
            }

            memorias.Add(new MemoriaDeEquipo(canal, Frecuencia.DesdeHercios(hz), _opciones.Traductor.DesdeElEquipo(nombreDelModo), etiqueta, true));
        }

        return memorias;
    }

    /// <inheritdoc />
    public Task IrAMemoriaAsync(int numero, CancellationToken ct = default)
    {
        if (numero is < 1 or > 99) throw new ArgumentOutOfRangeException(nameof(numero), numero, "Las memorias van de 1 a 99.");
        return EnExclusivaAsync(async () =>
        {
            await OrdenAsync([0x08, .. BcdCiv.Numero(numero, 2)], ct).ConfigureAwait(false);
            Volatile.Write(ref _enMemoria, true);
            Volatile.Write(ref _canalDeMemoria, numero);
            await LeerEstadoAsync(ct).ConfigureAwait(false);
        }, ct);
    }

    // ── Orden en crudo ───────────────────────────────────────────────────────

    /// <inheritdoc />
    /// <remarks>
    /// Se escribe en hexadecimal: el cuerpo (<c>14 0A</c>) o la trama entera
    /// (<c>FE FE 94 E0 14 0A FD</c>, de la que se toma el cuerpo). Devuelve el cuerpo de la
    /// respuesta en hexadecimal (<c>FB</c>, <c>FA</c> o los datos). Rechaza lo que transmite, apaga,
    /// escribe memorias o toca el menu.
    /// </remarks>
    public async Task<string?> OrdenEnCrudoAsync(string orden, CancellationToken ct = default)
    {
        var bytes = Hex.ABytes(orden);
        if (bytes.Length >= 6 && bytes[0] == TramaCiv.Preambulo && bytes[1] == TramaCiv.Preambulo && bytes[^1] == TramaCiv.Fin)
        {
            bytes = bytes[4..^1];
        }

        OrdenesIcom.ComprobarQueValeEnCrudo(bytes);
        var r = await _canal.PreguntarAsync(bytes, ct).ConfigureAwait(false);
        return r is null ? null : Hex.De(r.Cuerpo);
    }

    // ── PTT ──────────────────────────────────────────────────────────────────

    /// <summary>Subir el PTT por aqui se salta el vigilante: se rechaza. Bajarlo, siempre.</summary>
    /// <param name="transmitir">Verdadero para subir.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <returns>La tarea.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    Task IControlEquipo.PonerPttAsync(bool transmitir, CancellationToken ct) =>
        GuardiaDelPtt.Rechazar(transmitir, () => ((IPttDirecto)this).PonerPttDirectoAsync(false, ct));

    /// <inheritdoc />
    async Task IPttDirecto.PonerPttDirectoAsync(bool transmitir, CancellationToken ct)
    {
        if (!transmitir && NoHayNadaQueBajar())
        {
            _registro.LogDebug(
                "{Canal} está cerrado y no hay PTT pedido: no hay nada que bajar.",
                _canal.Descripcion);
            return;
        }

        if (transmitir && Interlocked.Exchange(ref _sintoniaPedida, 0) == 1)
        {
            // TUNE vigilado: emite el acoplador (1C 01 02). Tope, latido y suelta como cualquier transmision.
            Volatile.Write(ref _sintonizando, true);
            Volatile.Write(ref _pttPedido, true);
            await OrdenesIcom.ConTransmisionAutorizadaAsync(
                async () => await _canal.PreguntarAsync([.. OrdenesIcom.Sintonizar], ct).ConfigureAwait(false)).ConfigureAwait(false);
            Actualizar(e => e with { Transmitiendo = true });
            return;
        }

        if (!transmitir && Volatile.Read(ref _sintonizando))
        {
            // Bajar una sintonia: 1C 00 00 siempre y, si sigue sintonizando (1C 01 = 02), pararla
            // dejando el acoplador en linea (1C 01 01).
            Volatile.Write(ref _sintonizando, false);
            await BajarPorCivAsync(ct).ConfigureAwait(false);
            var estado = await _canal.PreguntarAsync([0x1C, 0x01], ct).ConfigureAwait(false);
            if (estado is null || !estado.EmpiezaPor([0x1C, 0x01]) || estado.DatosTras(2) is [0x02, ..])
            {
                await _canal.PreguntarAsync([0x1C, 0x01, 0x01], ct).ConfigureAwait(false);
            }

            Volatile.Write(ref _pttPedido, false);
            Actualizar(e => e with { Transmitiendo = false });
            return;
        }

        if (_opciones.ViaDePtt != ViaDePtt.Cat && _canal.PuedeAccionarLineas)
        {
            await _canal.PonerLineaDePttAsync(transmitir, ct).ConfigureAwait(false);
        }
        else if (transmitir)
        {
            await OrdenesIcom.ConTransmisionAutorizadaAsync(
                async () => await _canal.PreguntarAsync([.. OrdenesIcom.SubirPtt], ct).ConfigureAwait(false)).ConfigureAwait(false);
        }
        else
        {
            await BajarPorCivAsync(ct).ConfigureAwait(false);
        }

        Volatile.Write(ref _pttPedido, transmitir);
        Actualizar(e => e with { Transmitiendo = transmitir });
    }

    /// <summary>Prepara la siguiente transmision vigilada para que sea una sintonia del acoplador (<c>1C 01 02</c>).</summary>
    public void PrepararSintonia() => Volatile.Write(ref _sintoniaPedida, 1);

    /// <summary>Espera a que el acoplador termine (<c>1C 01</c> deja de valer 02), latiendo al vigilante.</summary>
    /// <param name="latir">Latido al vigilante.</param>
    /// <param name="tope">Lo mas que se espera.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <returns>Verdadero si se le vio terminar dentro del tope.</returns>
    public async Task<bool> EsperarFinDeSintoniaAsync(Action latir, TimeSpan tope, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(latir);
        var intervalo = TimeSpan.FromMilliseconds(250);
        var vueltas = Math.Max(1, (int)(tope / intervalo));
        for (var i = 0; i < vueltas; i++)
        {
            if (i > 0) await _opciones.Esperar(intervalo, ct).ConfigureAwait(false);
            latir();
            var r = await _canal.PreguntarAsync([0x1C, 0x01], ct).ConfigureAwait(false);
            if (r is not null && r.EmpiezaPor([0x1C, 0x01]) && r.DatosTras(2) is [var valor, ..] && valor != 0x02)
            {
                return true;
            }
        }

        return false;
    }

    private async Task BajarPorCivAsync(CancellationToken ct)
    {
        if (!_canal.Abierto) throw new InvalidOperationException(Textos.F("Servicios.Radio.CanalCerrado", _canal.Descripcion));
        await _canal.PreguntarAsync([.. OrdenesIcom.BajarPtt], ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Dice si una orden de bajar el PTT no tiene a quien llegar ni nada que bajar: canal
    /// cerrado, sin PTT pedido y, o nunca abierto, o el control ya desechado (que al desecharse
    /// ya bajo el PTT por todas sus vias). Evita el «¡PTT PEGADO!» falso del vigilante que se
    /// cierra despues del control (01-10-2026). Con PTT pedido no se cumple nunca.
    /// </summary>
    private bool NoHayNadaQueBajar() =>
        !_canal.Abierto
        && !Volatile.Read(ref _pttPedido)
        && !Volatile.Read(ref _sintonizando)
        && (Volatile.Read(ref _desechado) || !Volatile.Read(ref _canalAbiertoAlgunaVez));

    private async Task BajarElPttComoSeaAsync(CancellationToken ct)
    {
        if (!_canal.Abierto && !Volatile.Read(ref _canalAbiertoAlgunaVez) && !Volatile.Read(ref _pttPedido))
        {
            return;
        }

        if (_canal.Abierto)
        {
            try
            {
                if (_opciones.ViaDePtt != ViaDePtt.Cat && _canal.PuedeAccionarLineas)
                {
                    await _canal.PonerLineaDePttAsync(false, ct).ConfigureAwait(false);
                }

                await BajarPorCivAsync(ct).ConfigureAwait(false);
                Volatile.Write(ref _pttPedido, false);
                return;
            }
            catch (Exception ex)
            {
                _registro.LogWarning(ex, "No se pudo bajar el PTT por la vía normal antes de desconectar.");
            }
        }

        foreach (var via in ViasDeSuelta)
        {
            try
            {
                using var espera = new CancellationTokenSource(_opciones.EsperaDeOrden);
                await via.SoltarAsync(espera.Token).ConfigureAwait(false);
                Volatile.Write(ref _pttPedido, false);
                return;
            }
            catch (Exception ex)
            {
                _registro.LogWarning(ex, "Falló la vía «{Via}» al bajar el PTT antes de desconectar.", via.Nombre);
            }
        }

        _registro.LogError("No se ha podido bajar el PTT antes de desconectar por ninguna vía.");
    }

    // ── Estado y sondeo ──────────────────────────────────────────────────────

    /// <summary>Lee del equipo frecuencia, modo, VFO, split, RIT/XIT, potencia y senal.</summary>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <returns>El estado nuevo.</returns>
    public Task<EstadoDelEquipo> LeerEstadoAsync(CancellationToken ct = default) =>
        EnExclusivaAsync(() => LeerEstadoSinEsperarAsync(ct), ct);

    private async Task<EstadoDelEquipo> LeerEstadoSinEsperarAsync(CancellationToken ct)
    {
        var anterior = Estado;
        var rf = await _canal.PreguntarAsync([0x03], ct).ConfigureAwait(false);
        if (rf is null) throw new EquipoNoContestaException(_canal.Descripcion);
        var hzActiva = rf.EmpiezaPor([0x03]) ? BcdCiv.Hercios(rf.DatosTras(1)) : null;
        var frecuenciaActiva = hzActiva is { } h ? Frecuencia.DesdeHercios(h) : anterior.Frecuencia;

        var rm = await _canal.PreguntarAsync([0x04], ct).ConfigureAwait(false);
        var codigoModo = rm is not null && rm.EmpiezaPor([0x04]) && rm.DatosTras(1) is { Length: >= 1 } dm ? dm[0] : (byte?)null;

        var rd = await _canal.PreguntarAsync([0x1A, 0x06], ct).ConfigureAwait(false);
        if (rd is not null && rd.EmpiezaPor([0x1A, 0x06]) && rd.DatosTras(2) is { Length: >= 1 } dd)
        {
            Volatile.Write(ref _datos, dd[0] == 0x01);
        }

        var nombreModo = codigoModo is { } cm ? ModosIcom.DesdeElEquipo(cm, Volatile.Read(ref _datos)) : null;
        var modoActivo = nombreModo is null ? anterior.Modo : _opciones.Traductor.DesdeElEquipo(nombreModo);

        var transmitiendo = Volatile.Read(ref _pttPedido);
        if (!transmitiendo)
        {
            // 1C 00 sin datos es una CONSULTA: dice si la radio esta en antena (por su PTT, por ejemplo).
            var rt = await _canal.PreguntarAsync([0x1C, 0x00], ct).ConfigureAwait(false);
            transmitiendo = rt is not null && rt.EmpiezaPor([0x1C, 0x00]) && rt.DatosTras(2) is [0x01, ..];
        }

        var anteriores = Vfos;
        var rs = await _canal.PreguntarAsync([0x0F], ct).ConfigureAwait(false);
        var split = rs is not null && rs.EmpiezaPor([0x0F]) && rs.DatosTras(1) is { Length: >= 1 } ds ? ds[0] == 0x01 : anteriores.Split;

        var activoEsB = _perfil.Reparto == RepartoDeVfos.PrincipalYSecundario
            ? await ElSecundarioEsElActivoAsync(ct).ConfigureAwait(false)
            : Volatile.Read(ref _bEsElActivo);

        var cero = await LeerFrecuenciaYModoAsync(0x00, ct).ConfigureAwait(false);
        var uno = await LeerFrecuenciaYModoAsync(0x01, ct).ConfigureAwait(false);
        FrecuenciaYModo? a, b;
        if (_perfil.Reparto == RepartoDeVfos.PrincipalYSecundario)
        {
            (a, b) = (cero, uno);
        }
        else
        {
            (a, b) = activoEsB ? (uno, cero) : (cero, uno);
        }

        var rit = anteriores.Rit;
        var xit = anteriores.Xit;
        var desplazamiento = anteriores.DesplazamientoRitHz;
        if (await LeerMandoAsync(MandoDeEquipo.Rit, ct).ConfigureAwait(false) is { } r) rit = r >= 1;
        if (await LeerMandoAsync(MandoDeEquipo.Xit, ct).ConfigureAwait(false) is { } x) xit = x >= 1;
        if (await LeerMandoAsync(MandoDeEquipo.DesplazamientoRit, ct).ConfigureAwait(false) is { } d)
        {
            desplazamiento = (int)d;
        }

        var transmiteEnB = split ? !activoEsB : activoEsB;
        EstadoDeUnVfo Vfo(NombreDeVfo nombre, FrecuenciaYModo? datos, EstadoDeUnVfo antes, bool esB) => new(
            nombre,
            datos?.Frecuencia ?? antes.Frecuencia,
            datos is { } v && ModosIcom.DesdeElEquipo(v.Modo, v.Datos) is { } n ? _opciones.Traductor.DesdeElEquipo(n) : antes.Modo,
            EsElActivo: activoEsB == esB,
            Transmite: transmiteEnB == esB,
            Recibe: activoEsB == esB,
            AnchoDeFiltroHz: null);

        var vfos = new EstadoDeLosVfos(
            Vfo(NombreDeVfo.A, a, anteriores.A, false),
            Vfo(NombreDeVfo.B, b, anteriores.B, true),
            split, rit, desplazamiento, xit, desplazamiento)
        {
            EnMemoria = Volatile.Read(ref _enMemoria),
        };

        bool vfosCambiaron;
        lock (_candado)
        {
            vfosCambiaron = _vfos != vfos;
            _vfos = vfos;
        }

        Frecuencia? frecuenciaRx = null;
        var frecuenciaDeTrabajo = frecuenciaActiva;
        if (split)
        {
            var otra = activoEsB ? vfos.A.Frecuencia : vfos.B.Frecuencia;
            if (!otra.EsCero)
            {
                frecuenciaRx = frecuenciaActiva;
                frecuenciaDeTrabajo = otra;
            }
        }

        var potencia = anterior.PotenciaVatios;
        if (await LeerMandoAsync(MandoDeEquipo.Potencia, ct).ConfigureAwait(false) is { } w)
        {
            potencia = w;
        }

        var senal = transmitiendo ? anterior.SenalRecibida : await LeerMedidorAsync(0x02, ct).ConfigureAwait(false) is { } s ? AUnidadesS(s) : anterior.SenalRecibida;

        var nombreVfo = Volatile.Read(ref _enMemoria)
            ? "MEM"
            : _perfil.Reparto == RepartoDeVfos.PrincipalYSecundario
                ? (activoEsB ? "SUB" : "MAIN")
                : (activoEsB ? "VFO B" : "VFO A");

        return Actualizar(e => e with
        {
            Conectado = true,
            Transmitiendo = transmitiendo,
            Frecuencia = frecuenciaDeTrabajo,
            FrecuenciaRx = frecuenciaRx,
            Modo = modoActivo,
            Vfo = nombreVfo,
            PotenciaVatios = potencia,
            SenalRecibida = senal,
        }, forzarAviso: vfosCambiaron);
    }

    /// <summary>
    /// Aviso del transceive: la radio dice sola que ha cambiado la frecuencia (<c>00</c>) o el
    /// modo (<c>01</c>) del VFO elegido. Se pinta en el acto; la siguiente pasada completa el resto.
    /// </summary>
    private void AlAvisoDelTransceive(object? sender, TramaCiv trama)
    {
        if (Volatile.Read(ref _pttPedido) || !Estado.Conectado) return;
        if (trama.Orden == 0x00 && BcdCiv.Hercios(trama.DatosTras(1)) is { } hz && !Vfos.Split)
        {
            Actualizar(e => e with { Frecuencia = Frecuencia.DesdeHercios(hz) });
        }
        else if (trama.Orden == 0x01 && trama.DatosTras(1) is { Length: >= 1 } d
                 && ModosIcom.DesdeElEquipo(d[0], Volatile.Read(ref _datos)) is { } nombre)
        {
            Actualizar(e => e with { Modo = _opciones.Traductor.DesdeElEquipo(nombre) });
        }
    }

    private async Task ComprobarQueSigueAhiAsync(CancellationToken ct)
    {
        var r = await _canal.PreguntarAsync([0x19, 0x00], ct).ConfigureAwait(false);
        if (r is null || !r.EmpiezaPor([0x19, 0x00]))
        {
            throw new EquipoNoContestaException(_canal.Descripcion);
        }

        if (r.DatosTras(2) is [var direccion, ..] && direccion != _canal.DireccionDelEquipo)
        {
            _registro.LogWarning("El equipo de {Canal} dice ser {Direccion:X2}.", _canal.Descripcion, direccion);
        }
    }

    private async Task SondearSiempreAsync(CancellationToken ct)
    {
        using var reloj = new PeriodicTimer(_opciones.IntervaloDeSondeo);
        var fallos = 0;
        var perdido = false;
        var espera = _opciones.EsperaDeReconexion;
        while (true)
        {
            try
            {
                if (!await reloj.WaitForNextTickAsync(ct).ConfigureAwait(false)) return;

                if (perdido)
                {
                    await Task.Delay(espera, ct).ConfigureAwait(false);
                    if (!_canal.Abierto) await _canal.AbrirAsync(ct).ConfigureAwait(false);
                    await ComprobarQueSigueAhiAsync(ct).ConfigureAwait(false);
                    await LeerEstadoAsync(ct).ConfigureAwait(false);
                    (perdido, fallos, espera) = (false, 0, _opciones.EsperaDeReconexion);
                    _registro.LogInformation("El {Equipo} ha vuelto.", NombreDelEquipo);
                    ComunicacionRecuperada?.Invoke(this, _canal.Descripcion);
                    continue;
                }

                if (Volatile.Read(ref _pttPedido))
                {
                    // En antena solo se comprueba que sigue ahi, con la orden mas inocua (19 00).
                    await ComprobarQueSigueAhiAsync(ct).ConfigureAwait(false);
                    fallos = 0;
                    continue;
                }

                await LeerEstadoAsync(ct).ConfigureAwait(false);
                fallos = 0;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                if (perdido)
                {
                    espera = TimeSpan.FromTicks(Math.Min(espera.Ticks * 2, _opciones.EsperaMaximaDeReconexion.Ticks));
                    continue;
                }

                fallos++;
                _registro.LogDebug(ex, "El {Equipo} no contesta ({Fallos} de {Tope}).", NombreDelEquipo, fallos, FallosParaDarloPorPerdido);
                if (fallos < FallosParaDarloPorPerdido) continue;

                perdido = true;
                var porque = ex is EquipoNoContestaException ? "el equipo ha dejado de contestar; puede que se haya apagado" : ex.Message;
                _registro.LogWarning("Se ha perdido el {Equipo}: {Porque}", NombreDelEquipo, porque);
                Actualizar(e => e with { Conectado = false, Transmitiendo = false });
                Volatile.Write(ref _pttPedido, false);
                ComunicacionPerdida?.Invoke(this, porque);
            }
        }
    }

    // ── Utilidades ───────────────────────────────────────────────────────────

    /// <summary>Manda una orden y exige <c>FB</c> (o la propia orden de vuelta).</summary>
    private async Task OrdenAsync(byte[] cuerpo, CancellationToken ct, bool tolerarNo = false)
    {
        var r = await _canal.PreguntarAsync(cuerpo, ct).ConfigureAwait(false);
        if (r is null) throw new EquipoNoContestaException(_canal.Descripcion);
        if (r.EsNoAdmitido && !tolerarNo)
        {
            throw new InvalidOperationException(Textos.F("Servicios.Radio.OrdenNoAdmitida", _perfil.Modelo.Nombre, Hex.De(cuerpo)));
        }
    }

    private async Task EnExclusivaAsync(Func<Task> accion, CancellationToken ct) =>
        await EnExclusivaAsync(async () =>
        {
            await accion().ConfigureAwait(false);
            return true;
        }, ct).ConfigureAwait(false);

    private async Task<T> EnExclusivaAsync<T>(Func<Task<T>> accion, CancellationToken ct)
    {
        if (DentroDeLaExclusiva.Value) return await accion().ConfigureAwait(false);
        await _exclusiva.WaitAsync(ct).ConfigureAwait(false);
        DentroDeLaExclusiva.Value = true;
        try
        {
            return await accion().ConfigureAwait(false);
        }
        finally
        {
            DentroDeLaExclusiva.Value = false;
            _exclusiva.Release();
        }
    }

    private EstadoDelEquipo Actualizar(Func<EstadoDelEquipo, EstadoDelEquipo> cambio, bool forzarAviso = false)
    {
        EstadoDelEquipo nuevo;
        bool avisar;
        lock (_candado)
        {
            var anterior = _estado;
            nuevo = cambio(anterior) with { LeidoUtc = DateTimeOffset.UtcNow };
            _estado = nuevo;
            avisar = forzarAviso || anterior with { LeidoUtc = default } != nuevo with { LeidoUtc = default };
        }

        if (avisar) EstadoCambiado?.Invoke(this, nuevo);
        return nuevo;
    }
}
