using System.ComponentModel;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Idiomas;
using Nodisla.Cuaderno.Radio.Ptt;

namespace Nodisla.Cuaderno.Radio.Control.Ft710;

/// <summary>Ajustes del control nativo del FT-710.</summary>
public sealed class OpcionesFt710
{
    /// <summary>
    /// Puerto serie del equipo, por ejemplo <c>COM3</c>. Si se deja vacio, se busca el equipo
    /// por todos los puertos identificandolo con <c>ID;</c>.
    /// </summary>
    public string? Puerto { get; set; }

    /// <summary>Velocidad del puerto: 115200 en el puerto <i>Enhanced</i>, 4800 en el <i>Standard</i>.</summary>
    public int Baudios { get; set; } = 115200;

    /// <summary>Cada cuanto se pregunta al equipo como esta.</summary>
    public TimeSpan IntervaloDeSondeo { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Lo que se espera antes de volver a intentar hablar con un equipo que se ha perdido.</summary>
    public TimeSpan EsperaDeReconexion { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Tope de la espera entre reintentos, para no machacar el puerto si el equipo no vuelve.
    /// </summary>
    /// <remarks>
    /// Cinco segundos y no mas: esto no es un servidor remoto al que convenga dejar en paz, es
    /// un puerto serie local que no le cuesta nada a nadie. Un tope largo se traduce en que el
    /// operador enciende la radio y el cuaderno tarda en enterarse, que es justo lo que no
    /// queremos.
    /// </remarks>
    public TimeSpan EsperaMaximaDeReconexion { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Lo que se espera a que el equipo conteste cada orden.</summary>
    public TimeSpan EsperaDeOrden { get; set; } = TimeSpan.FromMilliseconds(350);

    /// <summary>Como traducir los modos entre el equipo y el cuaderno.</summary>
    public TraductorDeModos Traductor { get; set; } = TraductorDeModos.PorOmision;

    /// <summary>
    /// Por donde se sube el PTT: por CAT, o levantando <c>RTS</c> o <c>DTR</c> del puerto serie.
    /// </summary>
    /// <remarks>
    /// De partida, por CAT, que es lo que hace el FT-710 sin nada mas conectado. Las lineas del
    /// puerto hacen falta para los montajes con interfaz de audio y optoacoplador. Un canal que
    /// no tenga lineas —por TCP, por ejemplo— se queda con el CAT aunque aqui se pida otra cosa.
    /// </remarks>
    public ViaDePtt ViaDePtt { get; set; } = ViaDePtt.Cat;

    /// <summary>
    /// Usar el codec de audio USB del equipo para saber si esta encendido antes de reintentar
    /// la conexion. Solo se aplica cuando se habla por su puerto serie.
    /// </summary>
    public bool ComprobarElCodecDeAudio { get; set; } = true;

    /// <summary>
    /// Lectura del medidor S a la que se considera que hay S9. Segun la captura el medidor va
    /// de 0 a 255; el reparto de esa escala en unidades S es aproximado y por eso se deja aqui.
    /// </summary>
    public int LecturaDeS9 { get; set; } = 128;

    /// <summary>
    /// Como se espera entre dos miradas mientras el acoplador sintoniza. Las pruebas lo cambian
    /// por una espera nula para no depender del reloj.
    /// </summary>
    public Func<TimeSpan, CancellationToken, Task> Esperar { get; set; } = Task.Delay;
}

/// <summary>
/// Control nativo del Yaesu FT-710 por CAT.
/// </summary>
/// <remarks>
/// <para>
/// Hamlib da frecuencia, modo y PTT; este control va mas alla porque el equipo de Jose tiene
/// filtros, ruido, contorno, muesca, telegrafia y medidores accesibles por CAT, y los quiere
/// manejar desde el ordenador.
/// </para>
/// <para>
/// Las capacidades no se suponen: al conectar se pregunta al equipo uno a uno por los mandos de
/// <see cref="MandosFt710.Todos"/> y el que conteste <c>?;</c> se queda fuera de
/// <see cref="Mandos"/>. Asi es el propio equipo el que dice lo que sabe hacer, y la interfaz
/// no enseña mandos que no existen.
/// </para>
/// <para>
/// <b>El PTT sigue pidiendose al vigilante.</b> Subirlo por <c>PonerPttAsync</c> esta vetado, y
/// la valvula de escape <see cref="OrdenEnCrudoAsync"/> rechaza cualquier <c>TX</c>.
/// </para>
/// </remarks>
public sealed class ControlFt710
    : IEquipoAvanzado, IEquipoConDosVfos, IEquipoConTeclas, IEquipoConBotonera, IEquipoConEncendido, IPttDirecto, ISueltaDeEmergenciaPtt, IAvisaDePerdidaDeComunicacion,
      Modelos.IEquipoDeModelo, IEquipoConSintonia, IManipuladorCw, IMedidorDeRoe
{
    /// <summary>
    /// Diferencias del modelo respecto del FT-710 (ver <see cref="Yaesu.PerfilYaesu"/>). Con el
    /// perfil del FT-710 este control se porta exactamente como antes de haber mas modelos.
    /// </summary>
    public Yaesu.PerfilYaesu Perfil => _perfil;

    /// <inheritdoc />
    public Modelos.ModeloDeEquipo Modelo => _perfil.Modelo;

    private readonly Yaesu.PerfilYaesu _perfil;

    /// <inheritdoc />
    /// <remarks>
    /// El unico sitio del programa por el que sale <c>PS0;</c> (ver
    /// <see cref="OrdenesFt710.ConApagadoAutorizadoAsync"/>). Primero se baja el PTT, luego se
    /// apaga, y al final se cierra la comunicacion como en una desconexion normal.
    /// </remarks>
    public async Task ApagarAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_desechado, this);
        if (!_canal.Abierto)
        {
            throw new InvalidOperationException(Textos.T("Servicios.Radio.NoApagaDesconectada"));
        }

        await BajarElPttComoSeaAsync(ct).ConfigureAwait(false);
        await OrdenesFt710.ConApagadoAutorizadoAsync(
            () => _canal.MandarAsync("PS0;", ct)).ConfigureAwait(false);
        _registro.LogInformation("FT-710 apagado desde el programa (LOCK, confirmado por el operador).");

        await DesconectarAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Procedimiento del manual CAT: una orden de despertar, y entre uno y dos segundos
    /// despues, <c>PS1;</c>. Luego la radio tarda unos segundos en arrancar; se reintenta
    /// conectar hasta que conteste.
    /// </remarks>
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

        await _canal.MandarAsync("PS1;", ct).ConfigureAwait(false);
        await Task.Delay(TimeSpan.FromMilliseconds(1300), ct).ConfigureAwait(false);
        await _canal.MandarAsync("PS1;", ct).ConfigureAwait(false);
        _registro.LogInformation("FT-710: orden de encendido enviada.");

        // La radio tarda en arrancar. Se prueba a conectar durante unos 20 s.
        for (var intento = 0; intento < 10; intento++)
        {
            await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
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

    /// <summary>Lo que contesta el FT-710 a <c>ID;</c>.</summary>
    public const string IdentificadorFt710 = "0800";

    private readonly ICanalCat _canal;
    private readonly OpcionesFt710 _opciones;
    private readonly ILogger _registro;
    private readonly object _candado = new();
    private readonly HashSet<MandoDeEquipo> _mandos = [];
    private readonly HashSet<MandoDeEquipo> _mandosDelSegundoVfo = [];

    private CancellationTokenSource? _ctsSondeo;
    private Task? _sondeo;
    private EstadoDelEquipo _estado = EstadoDelEquipo.Desconectado;
    private EstadoDeLosVfos _vfos = EstadoDeLosVfos.SinDatos;
    private bool _pttPedido;
    private bool _desechado;
    private bool _canalAbiertoAlgunaVez;
    private string? _modoDelEquipo;
    private int _sintoniaPedida;
    private bool _sintonizando;
    private bool _enMemoria;

    /// <summary>Crea el control sobre un canal CAT ya construido.</summary>
    /// <param name="canal">Canal por el que se habla con el equipo.</param>
    /// <param name="opciones">Ajustes.</param>
    /// <param name="registro">Donde anotar lo que pasa.</param>
    /// <param name="perfil">Modelo Yaesu de CAT nuevo; nulo = FT-710.</param>
    public ControlFt710(ICanalCat canal, OpcionesFt710? opciones = null, ILogger? registro = null, Yaesu.PerfilYaesu? perfil = null)
    {
        ArgumentNullException.ThrowIfNull(canal);

        _perfil = perfil ?? Yaesu.PerfilesYaesu.Ft710;
        NombreDelEquipo = _perfil.Modelo.NombreCompleto;
        _canal = canal;
        _opciones = opciones ?? new OpcionesFt710();
        _registro = registro ?? NullLogger.Instance;

        // Si el PTT va por una linea del puerto, bajar esa linea es LO PRIMERO que hay que
        // probar: mandar TX0; por CAT no baja un PTT que subio por RTS.
        List<ViaDeSuelta> vias = [];
        if (_opciones.ViaDePtt != ViaDePtt.Cat && _canal.PuedeAccionarLineas)
        {
            vias.Add(new ViaDeSuelta(
                $"FT-710: bajar la línea {_opciones.ViaDePtt} del puerto",
                async ct => await _canal.PonerLineaDePttAsync(false, ct).ConfigureAwait(false),
                () => _canal.PonerLineaDePttSincrono(false)));
        }

        // Con el manipulador en marcha, cada via para antes la telegrafia (KY00;): con el
        // break-in puesto, el equipo manipularia solo aunque el PTT ya estuviera abajo.
        vias.AddRange(
        [
            new ViaDeSuelta(
                "FT-710: TX0 por el canal abierto",
                async ct =>
                {
                    await PararElManipuladorSiHaceFaltaAsync(ct).ConfigureAwait(false);
                    await _canal.MandarAsync("TX0;", ct).ConfigureAwait(false);
                },
                () =>
                {
                    PararElManipuladorSincronoSiHaceFalta();
                    _canal.MandarSincrono("TX0;");
                }),
            new ViaDeSuelta(
                "FT-710: reabrir el puerto y TX0",
                async ct =>
                {
                    await _canal.AbrirAsync(ct).ConfigureAwait(false);
                    await PararElManipuladorSiHaceFaltaAsync(ct).ConfigureAwait(false);
                    await _canal.MandarAsync("TX0;", ct).ConfigureAwait(false);
                }),
        ]);

        ViasDeSuelta = vias;
    }

    /// <inheritdoc />
    public ViaDeControl Via => ViaDeControl.CatNativo;

    /// <inheritdoc />
    public string NombreDelEquipo { get; private set; } = "Yaesu FT-710";

    /// <inheritdoc />
    public EstadoDelEquipo Estado
    {
        get
        {
            lock (_candado)
            {
                return _estado;
            }
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Se rellena en cada pasada del sondeo con <c>FA;</c>, <c>FB;</c>, <c>MD0;</c>, <c>MD1;</c>,
    /// <c>VS;</c>, <c>ST;</c> y <c>FT;</c>: todas son consultas, ninguna cambia nada. Sin esto el
    /// visor del frontal no tenia de donde sacar el VFO B —ni el A— y ensenaba guiones con el
    /// equipo contestando (27-09-2026).
    /// </remarks>
    public EstadoDeLosVfos Vfos
    {
        get
        {
            lock (_candado)
            {
                return _vfos;
            }
        }
    }

    /// <inheritdoc />
    public IReadOnlySet<MandoDeEquipo> Mandos
    {
        get
        {
            lock (_candado)
            {
                return new HashSet<MandoDeEquipo>(_mandos);
            }
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<ViaDeSuelta> ViasDeSuelta { get; }

    /// <inheritdoc />
    public event EventHandler<EstadoDelEquipo>? EstadoCambiado;

    /// <inheritdoc />
    public event EventHandler<string>? ComunicacionPerdida;

    /// <summary>
    /// Salta cuando se recupera la comunicacion con un equipo que se habia perdido.
    /// </summary>
    /// <remarks>
    /// La interfaz lo necesita para dejar de avisar de que no hay radio, y sirve ademas para
    /// que nadie tenga que esperar «un rato a ver si vuelve»: hay una senal concreta.
    /// </remarks>
    public event EventHandler<string>? ComunicacionRecuperada;

    /// <inheritdoc />
    /// <exception cref="CanalNoDisponibleException">Si el puerto no existe o esta ocupado.</exception>
    /// <exception cref="EquipoNoContestaException">
    /// Si el puerto se abre pero no contesta nadie, que es lo que pasa con el equipo apagado.
    /// </exception>
    public async Task ConectarAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_desechado, this);

        if (!_canal.Abierto)
        {
            await _canal.AbrirAsync(ct).ConfigureAwait(false);
        }

        Volatile.Write(ref _canalAbiertoAlgunaVez, true);

        var identificador = await PreguntarAsync("ID;", ct).ConfigureAwait(false);
        if (identificador is null)
        {
            // El puerto existe y se ha abierto, pero al otro lado no hay nadie: el equipo esta
            // apagado o desenchufado. No es un fallo del puerto y hay que decirlo de otra manera.
            throw new EquipoNoContestaException(_canal.Descripcion);
        }

        if (!identificador.StartsWith("ID", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                Textos.F("Servicios.Radio.NoEsYaesu", _canal.Descripcion, identificador));
        }

        var codigo = identificador[2..].Trim();
        if (codigo != _perfil.Modelo.IdentificadorYaesu && Yaesu.PerfilesYaesu.PorIdentificador(codigo) != _perfil)
        {
            _registro.LogWarning(
                "El equipo de {Canal} contesta ID{Codigo}, que no es un {Modelo} (ID{Esperado}).",
                _canal.Descripcion,
                codigo,
                _perfil.Nombre,
                _perfil.Modelo.IdentificadorYaesu);
            NombreDelEquipo = $"Yaesu (ID{codigo})";
        }

        await AveriguarCapacidadesAsync(ct).ConfigureAwait(false);
        await LeerEstadoAsync(ct).ConfigureAwait(false);

        lock (_candado)
        {
            if (_sondeo is not null)
            {
                return;
            }

            _ctsSondeo = new CancellationTokenSource();
            _sondeo = Task.Run(() => SondearSiempreAsync(_ctsSondeo.Token), CancellationToken.None);
        }
    }

    /// <inheritdoc />
    /// <remarks>Se baja el PTT antes de cerrar nada.</remarks>
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

        if (cts is not null)
        {
            await cts.CancelAsync().ConfigureAwait(false);
        }

        if (sondeo is not null)
        {
            try
            {
                await sondeo.WaitAsync(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _registro.LogDebug(ex, "El sondeo del FT-710 no terminó a tiempo.");
            }
        }

        cts?.Dispose();

        await BajarElPttComoSeaAsync(ct).ConfigureAwait(false);

        _canal.Cerrar();
        lock (_candado)
        {
            _vfos = EstadoDeLosVfos.SinDatos;
        }

        Actualizar(estado => estado with { Conectado = false, Transmitiendo = false });
    }

    /// <inheritdoc />
    /// <remarks>
    /// Va al VFO <b>activo</b>, preguntado a la radio en ese momento (<c>VS;</c>) y no al que se
    /// recordaba del ultimo sondeo: si el operador acaba de pulsar A/B en el equipo, el spot iria
    /// al otro. Con split tambien va al activo, que es el de recepcion.
    /// </remarks>
    public Task PonerFrecuenciaAsync(Frecuencia frecuencia, CancellationToken ct = default)
    {
        var hercios = frecuencia.Hercios;
        ComprobarFrecuencia(hercios, frecuencia);

        return EnExclusivaAsync(
            async () =>
            {
                var enB = await ElActivoEsBAsync(ct).ConfigureAwait(false);
                await MandarAsync($"{(enB ? "FB" : "FA")}{_perfil.Cifras(hercios)};", ct).ConfigureAwait(false);
                await LeerEstadoAsync(ct).ConfigureAwait(false);
            },
            ct);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <c>MD0</c> es el modo de la banda <b>principal</b>, que en el FT-710 es el VFO elegido con
    /// <c>VS</c>, no siempre el A (manual CAT: «MD P1 0: MAIN Band»; comprobado en la radio). Asi
    /// que el modo del VFO activo es siempre <c>MD0</c>.
    /// </remarks>
    public Task PonerModoAsync(Modo modo, CancellationToken ct = default) =>
        EnExclusivaAsync(
            async () =>
            {
                var enB = await ElActivoEsBAsync(ct).ConfigureAwait(false);
                var frecuencia = enB ? Vfos.B.Frecuencia : Vfos.A.Frecuencia;
                var nombre = _opciones.Traductor.AlEquipo(modo, frecuencia.EsCero ? Estado.Frecuencia : frecuencia)
                    ?? throw new ArgumentException(Textos.F("Servicios.Radio.ModoDesconocido", "FT-710", modo), nameof(modo));

                var codigo = ModosFt710.AlEquipo(nombre)
                    ?? throw new ArgumentException(Textos.F("Servicios.Radio.SinEseModo", "FT-710", nombre), nameof(modo));

                await MandarAsync($"{OrdenDeModoDelActivo(enB)}{codigo};", ct).ConfigureAwait(false);
                await LeerEstadoAsync(ct).ConfigureAwait(false);
            },
            ct);

    /// <summary>
    /// <c>MD0</c> o <c>MD1</c> para el VFO activo: en el FT-710 y casi toda la familia es siempre
    /// <c>MD0</c>; en los de doble receptor (FTDX101, FTDX5000) <c>MD0</c> es el A y <c>MD1</c> el B.
    /// </summary>
    private string OrdenDeModoDelActivo(bool enB) =>
        !_perfil.ModoPrincipalEsElActivo && enB ? "MD1" : "MD0";

    private void ComprobarFrecuencia(long hercios, Frecuencia frecuencia)
    {
        if (hercios < 0 || hercios > _perfil.HerciosMaximo)
        {
            throw new ArgumentOutOfRangeException(
                nameof(frecuencia),
                frecuencia,
                _perfil.CifrasDeFrecuencia == 9 && _perfil == Yaesu.PerfilesYaesu.Ft710
                    ? Textos.T("Servicios.Radio.Ft710NueveCifras")
                    : Textos.F("Servicios.Radio.FrecuenciaFueraDelPerfil", _perfil.Nombre, _perfil.HerciosMaximo, _perfil.CifrasDeFrecuencia));
        }
    }

    /// <summary>Pregunta a la radio que VFO manda ahora (VS0 = A, VS1 = B).</summary>
    private async Task<bool> ElActivoEsBAsync(CancellationToken ct)
    {
        var vs = await PreguntarAsync("VS;", ct).ConfigureAwait(false);
        return vs is { Length: >= 3 } && vs.StartsWith("VS", StringComparison.OrdinalIgnoreCase)
            ? vs[2] == '1'
            : Estado.Vfo == "VFO B";
    }

    /// <summary>
    /// Cada lectura del estado y cada «escribir y releer» van de una en una. Sin esto el sondeo,
    /// que corre en otro hilo, podia haber leido FA/FB justo ANTES de que se escribiera el spot y
    /// publicar su lectura vieja justo DESPUES: el formulario volvia a la frecuencia anterior o
    /// se quedaba con la del otro VFO.
    /// </summary>
    private readonly SemaphoreSlim _exclusiva = new(1, 1);

    private static readonly AsyncLocal<bool> DentroDeLaExclusiva = new();

    private async Task EnExclusivaAsync(Func<Task> accion, CancellationToken ct)
    {
        if (DentroDeLaExclusiva.Value)
        {
            await accion().ConfigureAwait(false);
            return;
        }

        await _exclusiva.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            DentroDeLaExclusiva.Value = true;
            await accion().ConfigureAwait(false);
        }
        finally
        {
            DentroDeLaExclusiva.Value = false;
            _exclusiva.Release();
        }
    }

    private async Task<T> EnExclusivaAsync<T>(Func<Task<T>> accion, CancellationToken ct)
    {
        T resultado = default!;
        // Con cuerpo de bloque: la lambda devuelve Task y no Task<T>, asi no se llama a si misma.
        await EnExclusivaAsync(async () => { resultado = await accion().ConfigureAwait(false); }, ct).ConfigureAwait(false);
        return resultado;
    }

    /// <inheritdoc />
    public async Task<EstadoDeLosVfos> LeerVfosAsync(CancellationToken ct = default)
    {
        await LeerEstadoAsync(ct).ConfigureAwait(false);
        return Vfos;
    }

    /// <inheritdoc />
    /// <remarks><c>VS0;</c> o <c>VS1;</c>: lo mismo que la tecla del equipo que elige el VFO.</remarks>
    public Task PonerVfoActivoAsync(NombreDeVfo vfo, CancellationToken ct = default)
        => EnExclusivaAsync(async () =>
    {
        await MandarAsync(vfo == NombreDeVfo.B ? "VS1;" : "VS0;", ct).ConfigureAwait(false);
        await LeerEstadoAsync(ct).ConfigureAwait(false);
    }, ct);

    /// <inheritdoc />
    /// <remarks>
    /// <c>SV;</c> <b>no es una consulta</b>: intercambia de verdad el contenido de los dos VFO.
    /// Solo se manda cuando el operador pulsa A/B y lo confirma; nada lo manda solo.
    /// </remarks>
    public Task IntercambiarVfosAsync(CancellationToken ct = default)
        => EnExclusivaAsync(async () =>
    {
        // En el FT-710 SV NO intercambia el contenido: comprobado en la radio el 28-09-2026,
        // SV deja FA y FB como estaban y solo cambia el VFO con el que se opera (VS0 <-> VS1),
        // que es lo que hace la tecla A/B del equipo. Intercambiar de verdad es escribir cada
        // uno en el otro.
        if (!_perfil.TieneModoDelSegundoVfo)
        {
            // Sin MD1 no se puede copiar el modo del otro a mano: SV es la tecla A/B de la radio
            // (manual CAT: «SWAP VFO»). Sin probar con la radio.
            await MandarAsync("SV;", ct).ConfigureAwait(false);
            await LeerEstadoAsync(ct).ConfigureAwait(false);
            return;
        }

        var largo = 2 + _perfil.CifrasDeFrecuencia;
        var fa = await PreguntarAsync("FA;", ct).ConfigureAwait(false);
        var fb = await PreguntarAsync("FB;", ct).ConfigureAwait(false);
        var ma = await PreguntarAsync("MD0;", ct).ConfigureAwait(false);
        var mb = await PreguntarAsync("MD1;", ct).ConfigureAwait(false);
        if (fa?.Length != largo || fb?.Length != largo || ma is not { Length: 4 } || mb is not { Length: 4 })
        {
            throw new InvalidOperationException(
                Textos.F("Servicios.Radio.NoIntercambiaVfos", $"{fa}, {fb}, {ma}, {mb}"));
        }

        // Primero el modo y luego la frecuencia: al cambiar de LSB a USB el FT-710 corre el
        // dial (visto en la radio: 14.155.000 pasa a 14.153.600), asi que la frecuencia va la
        // ultima para que quede la que toca.
        await MandarAsync($"MD0{mb[3]};", ct).ConfigureAwait(false);
        await MandarAsync($"MD1{ma[3]};", ct).ConfigureAwait(false);
        await MandarAsync($"FA{fb[2..]};", ct).ConfigureAwait(false);
        await MandarAsync($"FB{fa[2..]};", ct).ConfigureAwait(false);
        await LeerEstadoAsync(ct).ConfigureAwait(false);
    }, ct);

    /// <inheritdoc />
    public IReadOnlySet<TeclaDelEquipo> Teclas => _perfil.Teclas;

    /// <summary>Las ordenes de una tecla en este modelo.</summary>
    /// <param name="tecla">Tecla.</param>
    /// <returns>Las ordenes.</returns>
    /// <exception cref="NotSupportedException">Si el modelo no tiene esa tecla por CAT.</exception>
    public IReadOnlyList<string> OrdenesDeLaTeclaEnEsteModelo(TeclaDelEquipo tecla)
    {
        if (!_perfil.Teclas.Contains(tecla))
        {
            throw new NotSupportedException(Textos.F("Servicios.Radio.SinTeclaCat", _perfil.Nombre, tecla));
        }

        return tecla == TeclaDelEquipo.BorrarClarificador
            ? [_perfil.OrdenDeBorrarClarificador]
            : OrdenesDeLaTecla(tecla);
    }

    /// <summary>La orden CAT que manda cada tecla. Ninguna transmite.</summary>
    /// <param name="tecla">Tecla pulsada.</param>
    /// <returns>Las ordenes, en orden.</returns>
    public static IReadOnlyList<string> OrdenesDeLaTecla(TeclaDelEquipo tecla) => tecla switch
    {
        TeclaDelEquipo.MemoriaAVfo => ["MA;"],
        TeclaDelEquipo.VfoOMemoria => ["VM;"],
        TeclaDelEquipo.RecuperarMemoriaRapida => ["QR;"],
        TeclaDelEquipo.GuardarMemoriaRapida => ["QI;"],
        TeclaDelEquipo.BandaArriba => ["BU0;"],
        TeclaDelEquipo.BandaAbajo => ["BD0;"],
        TeclaDelEquipo.AjusteACero => ["ZI0;"],
        TeclaDelEquipo.AlternarVfo => ["SV;"],
        TeclaDelEquipo.BorrarClarificador => ["CF001+0000;"],

        // DSP RESET no tiene orden CAT. Se hace lo que hace la tecla, a mano: desplazamiento de
        // FI a cero, ancho al de omision del modo, muesca, contorno y APF apagados.
        TeclaDelEquipo.RestablecerDsp => ["IS00+0000;", "SH0000;", "BP00000;", "CO000000;", "CO020000;"],
        _ => throw new ArgumentOutOfRangeException(nameof(tecla), tecla, "Tecla desconocida."),
    };

    /// <inheritdoc />
    public Task PulsarAsync(TeclaDelEquipo tecla, CancellationToken ct = default)
        => EnExclusivaAsync(async () =>
    {
        foreach (var orden in OrdenesDeLaTeclaEnEsteModelo(tecla))
        {
            await MandarAsync(orden, ct).ConfigureAwait(false);
        }

        await LeerEstadoAsync(ct).ConfigureAwait(false);
    }, ct);

    /// <summary>
    /// Las teclas de banda del FT-710, con el numero que lleva cada una en <c>BS</c> (manual CAT
    /// 2306-C: 00 = 1,8 MHz … 10 = 50 MHz, 11 = «70 MHz/GEN»). El FT-710 no tiene 70 MHz, asi que
    /// la 11 es la cobertura general.
    /// </summary>
    public static IReadOnlyList<TeclaDeBanda> BandasFt710 { get; } =
    [
        Tecla(0, "1.8", "160m", "160 m (1,8 MHz)"),
        Tecla(1, "3.5", "80m", "80 m (3,5 MHz)"),
        Tecla(2, "5", "60m", "60 m (5 MHz)"),
        Tecla(3, "7", "40m", "40 m (7 MHz)"),
        Tecla(4, "10", "30m", "30 m (10 MHz)"),
        Tecla(5, "14", "20m", "20 m (14 MHz)"),
        Tecla(6, "18", "17m", "17 m (18 MHz)"),
        Tecla(7, "21", "15m", "15 m (21 MHz)"),
        Tecla(8, "24", "12m", "12 m (24,9 MHz)"),
        Tecla(9, "28", "10m", "10 m (28 MHz)"),
        Tecla(10, "50", "6m", "6 m (50 MHz)"),
        new TeclaDeBanda(11, "GEN", Banda.Vacia, Textos.T("Servicios.Radio.CoberturaGeneralLarga")),
    ];

    private static TeclaDeBanda Tecla(int codigo, string rotulo, string banda, string descripcion) =>
        new(codigo, rotulo, Banda.TryParse(banda, out var b) ? b : Banda.Vacia, descripcion);

    /// <inheritdoc />
    public IReadOnlyList<TeclaDeBanda> TeclasDeBanda => _perfil.Bandas;

    /// <summary>La orden de la tecla de banda: <c>BS</c> y el numero de dos cifras.</summary>
    /// <param name="tecla">Tecla pulsada.</param>
    /// <returns>La orden, por ejemplo <c>BS03;</c> para 40 m.</returns>
    public static string OrdenDeBanda(TeclaDeBanda tecla)
    {
        ArgumentNullException.ThrowIfNull(tecla);
        if (tecla.Codigo is < 0 or > 11)
        {
            throw new ArgumentOutOfRangeException(nameof(tecla), tecla.Codigo, "El FT-710 tiene las bandas 00 a 11.");
        }

        return string.Create(CultureInfo.InvariantCulture, $"BS{tecla.Codigo:D2};");
    }

    /// <summary>
    /// La orden de una tecla de modo. <c>MD0</c> es el VFO activo en el FT-710 (ver
    /// <see cref="PonerModoAsync"/>). CW va a CW-U (3), que es lo que pone la tecla MODE de la
    /// radio; DATA va a DATA-L (8) por debajo de 10 MHz y a DATA-U (C) por encima, como la radio.
    /// </summary>
    /// <param name="modo">Tecla pulsada.</param>
    /// <param name="frecuencia">Frecuencia del VFO activo, para elegir la banda lateral de datos.</param>
    /// <returns>La orden, por ejemplo <c>MD02;</c>.</returns>
    public static string OrdenDeModo(TeclaDeModo modo, Frecuencia frecuencia)
    {
        var codigo = modo switch
        {
            TeclaDeModo.Lsb => '1',
            TeclaDeModo.Usb => '2',
            TeclaDeModo.Cw => '3',
            TeclaDeModo.Fm => '4',
            TeclaDeModo.Am => '5',
            TeclaDeModo.Datos => frecuencia.Hercios < 10_000_000 ? '8' : 'C',
            _ => throw new ArgumentOutOfRangeException(nameof(modo), modo, "Tecla de modo desconocida."),
        };
        return $"MD0{codigo};";
    }

    /// <inheritdoc />
    /// <remarks>
    /// <c>BS</c> hace lo mismo que la tecla BAND de la radio: el VFO activo pasa a la ultima
    /// frecuencia y modo usados en esa banda. No lleva VFO: va siempre al activo.
    /// </remarks>
    public Task IrABandaAsync(TeclaDeBanda tecla, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tecla);
        string orden;
        if (_perfil == Yaesu.PerfilesYaesu.Ft710)
        {
            orden = OrdenDeBanda(tecla);
        }
        else
        {
            if (!_perfil.Bandas.Any(b => b.Codigo == tecla.Codigo))
            {
                throw new ArgumentOutOfRangeException(nameof(tecla), tecla.Codigo, $"El {_perfil.Nombre} no tiene esa tecla de banda.");
            }

            orden = string.Create(CultureInfo.InvariantCulture, $"BS{tecla.Codigo:D2};");
        }

        return EnExclusivaAsync(async () =>
        {
            await MandarAsync(orden, ct).ConfigureAwait(false);
            await LeerEstadoAsync(ct).ConfigureAwait(false);
        }, ct);
    }

    /// <inheritdoc />
    public Task PonerModoDeTeclaAsync(TeclaDeModo modo, CancellationToken ct = default)
        => EnExclusivaAsync(async () =>
    {
        // La frecuencia del VFO activo, preguntada ahora (VS;), para DATA-L/DATA-U.
        var enB = await ElActivoEsBAsync(ct).ConfigureAwait(false);
        var frecuencia = enB ? Vfos.B.Frecuencia : Vfos.A.Frecuencia;
        if (frecuencia.EsCero) frecuencia = Estado.Frecuencia;

        var orden = OrdenDeModo(modo, frecuencia);
        if (!_perfil.ModoPrincipalEsElActivo && enB)
        {
            orden = "MD1" + orden[3..];
        }

        await MandarAsync(orden, ct).ConfigureAwait(false);
        await LeerEstadoAsync(ct).ConfigureAwait(false);
    }, ct);

    /// <inheritdoc />
    /// <remarks>
    /// El paso sale del menu del propio equipo (03-05-01 en banda lateral y telegrafia, 03-05-02
    /// en datos; 5, 10 o 20 Hz). Una muesca de rueda son diez pasos —la rueda da unas 24 muescas
    /// por vuelta y el dial 200 pasos—; con FINE un paso y con FAST cien. Con LOCK puesto no se
    /// mueve, como el dial de verdad. Se parte de la frecuencia sin clarificador (IF/OI), que es
    /// la que se escribe con FA/FB.
    /// </remarks>
    public Task GirarDialAsync(int muescas, CancellationToken ct = default)
        => EnExclusivaAsync(async () =>
    {
        if (muescas == 0 || await EstaBloqueadoAsync(ct).ConfigureAwait(false))
        {
            return;
        }

        var modo = Volatile.Read(ref _modoDelEquipo) ?? "USB";
        var menu = modo is "RTTY" or "PSK" or "PKTLSB" or "PKTUSB" ? "030502" : "030501";
        var paso = modo is "AM" or "FM" or "PKTFM"
            ? 100
            : !_perfil.PasosDelMenuFt710
                ? 10
            : await LeerDelMenuAsync(menu, ct).ConfigureAwait(false) is { Valor: var v } && int.TryParse(v, out var i)
                ? i switch { 0 => 5, 1 => 10, _ => 20 }
                : 10;

        var fina = await PreguntarAsync("FN;", ct).ConfigureAwait(false);
        var factor = fina switch { "FN1" => 1, "FN2" => 100, _ => 10 };
        await MoverAsync((long)muescas * paso * factor, ajustarA: 0, ct).ConfigureAwait(false);
    }, ct);

    /// <inheritdoc />
    /// <remarks>
    /// En memorias, cambia de canal (CH0 sube, CH1 baja). En VFO, salta de canal en canal con el
    /// paso del menu 03-05-03 (1, 2,5, 5 o 10 kHz; 03-05-04 en AM y 03-05-05 en FM), cayendo en la
    /// rejilla como el mando del equipo.
    /// </remarks>
    public Task GirarPasosAsync(int muescas, CancellationToken ct = default)
        => EnExclusivaAsync(async () =>
    {
        if (muescas == 0)
        {
            return;
        }

        await LeerEstadoAsync(ct).ConfigureAwait(false);
        if (Volatile.Read(ref _enMemoria))
        {
            for (var i = 0; i < Math.Abs(muescas); i++)
            {
                await MandarAsync(muescas > 0 ? "CH0;" : "CH1;", ct).ConfigureAwait(false);
            }

            await LeerEstadoAsync(ct).ConfigureAwait(false);
            return;
        }

        if (await EstaBloqueadoAsync(ct).ConfigureAwait(false))
        {
            return;
        }

        var modo = Volatile.Read(ref _modoDelEquipo) ?? "USB";
        var (menu, pasos) = modo switch
        {
            "AM" => ("030504", new[] { 2500, 5000, 9000, 10000, 12500, 25000 }),
            "FM" or "PKTFM" => ("030505", new[] { 5000, 6250, 10000, 12500, 20000, 25000 }),
            _ => ("030503", new[] { 1000, 2500, 5000, 10000 }),
        };
        // Fuera del FT-710 el menu tiene otra numeracion: se usa el primer paso de la tabla.
        var leido = _perfil.PasosDelMenuFt710 ? await LeerDelMenuAsync(menu, ct).ConfigureAwait(false) : null;
        var indice = leido is { Valor: var v } && int.TryParse(v, out var n) && n >= 0 && n < pasos.Length ? n : 0;
        var paso = pasos[indice];
        await MoverAsync((long)muescas * paso, ajustarA: paso, ct).ConfigureAwait(false);
    }, ct);

    /// <summary>
    /// Prepara la siguiente transmision vigilada para que sea una sintonia del acoplador.
    /// </summary>
    /// <remarks>
    /// Se llama justo antes de pedir antena al vigilante: en vez de <c>TX1;</c> se manda
    /// <c>AC003;</c> (el TUNE mantenido del equipo) y al soltar se manda <c>TX0;</c> y, si el
    /// acoplador sigue sintonizando, se para.
    /// </remarks>
    public void PrepararSintonia() => Volatile.Write(ref _sintoniaPedida, 1);

    /// <summary>
    /// Lo que se deja sintonizando un modelo que no dice por CAT cuando acaba (todos menos el
    /// FT-710): el acoplador interno de estos equipos tarda unos segundos como mucho.
    /// </summary>
    public static TimeSpan SintoniaSinIndicador { get; } = TimeSpan.FromSeconds(5);

    /// <summary>El modelo dice por CAT cuando termina de sintonizar el acoplador.</summary>
    public bool VeElFinDeLaSintonia => _perfil.PosicionDeSintoniaEnRi is not null;

    /// <summary>
    /// Espera a que el acoplador termine de sintonizar, latiendo al vigilante mientras tanto.
    /// </summary>
    /// <param name="latir">Latido al vigilante.</param>
    /// <param name="tope">Lo mas que se espera.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <returns>Verdadero si se le vio empezar y terminar dentro del tope.</returns>
    public async Task<bool> EsperarFinDeSintoniaAsync(Action latir, TimeSpan tope, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(latir);
        var intervalo = TimeSpan.FromMilliseconds(250);
        var vueltas = Math.Max(1, (int)(tope / intervalo));
        var empezo = false;

        if (_perfil.PosicionDeSintoniaEnRi is not { } posicion)
        {
            // Este modelo no dice por CAT cuando sintoniza: se deja el tiempo fijo de
            // SintoniaSinIndicador (o el tope, si es menor), latiendo, y se suelta.
            var fijas = Math.Max(1, (int)(TimeSpan.FromTicks(Math.Min(tope.Ticks, SintoniaSinIndicador.Ticks)) / intervalo));
            for (var i = 0; i < fijas; i++)
            {
                latir();
                await _opciones.Esperar(intervalo, ct).ConfigureAwait(false);
            }

            return false;
        }
        for (var i = 0; i < vueltas; i++)
        {
            // La primera mirada, en el acto: con el acoplador ya ajustado para esa frecuencia la
            // sintonia dura menos de medio segundo.
            if (i > 0)
            {
                await _opciones.Esperar(intervalo, ct).ConfigureAwait(false);
            }

            latir();

            // RI P1…P8 van de la posicion 2 a la 9: P6 (posicion 7) es 1 mientras el acoplador
            // sintoniza. Visto en la radio: «RI01010100» (Hi-SWR, TX, sintonizando) y luego ceros.
            var indicadores = await _canal.PreguntarAsync("RI0;", ct).ConfigureAwait(false);
            _registro.LogDebug("CAT RI0; -> {Respuesta} (sintonia)", indicadores ?? "(sin respuesta)");
            if (indicadores is null || indicadores.Length < posicion + 2 || !indicadores.StartsWith("RI", StringComparison.Ordinal))
            {
                continue;
            }

            if (indicadores[posicion] == '1')
            {
                empezo = true;
            }
            else if (empezo)
            {
                return true;
            }
        }

        return false;
    }

    private async Task<bool> EstaBloqueadoAsync(CancellationToken ct) =>
        await PreguntarAsync("LK;", ct).ConfigureAwait(false) == "LK1";

    private async Task MoverAsync(long hercios, int ajustarA, CancellationToken ct)
    {
        // IF da el VFO A y OI el B, los dos sin el clarificador sumado (FA si lo lleva).
        var enB = Estado.Vfo == "VFO B";
        var informe = await PreguntarAsync(enB ? "OI;" : "IF;", ct).ConfigureAwait(false);
        var n = _perfil.CifrasDeFrecuencia;
        if (informe is null || informe.Length < 5 + n || !long.TryParse(informe.AsSpan(5, n), NumberStyles.None, CultureInfo.InvariantCulture, out var ahora))
        {
            throw new InvalidOperationException(Textos.F("Servicios.Radio.FrecuenciaDesconocida", informe));
        }

        var nueva = ahora + hercios;
        if (ajustarA > 0)
        {
            // Como el mando de canales: se cae en la rejilla del paso.
            nueva = hercios > 0
                ? ((ahora / ajustarA) + (hercios / ajustarA)) * ajustarA
                : (((ahora + ajustarA - 1) / ajustarA) + (hercios / ajustarA)) * ajustarA;
        }

        nueva = Math.Clamp(nueva, 30_000, _perfil.TopeDelDial);
        await MandarAsync($"{(enB ? "FB" : "FA")}{_perfil.Cifras(nueva)};", ct).ConfigureAwait(false);
        await LeerEstadoAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <c>AB;</c> copia el A sobre el B y <c>BA;</c> el B sobre el A. Pisa el otro VFO, asi que
    /// tambien es solo cosa del operador.
    /// </remarks>
    public Task IgualarVfosAsync(CancellationToken ct = default)
        => EnExclusivaAsync(async () =>
    {
        await MandarAsync(Estado.Vfo == "VFO B" ? "BA;" : "AB;", ct).ConfigureAwait(false);
        await LeerEstadoAsync(ct).ConfigureAwait(false);
    }, ct);

    /// <inheritdoc />
    public Task PonerFrecuenciaDeAsync(NombreDeVfo vfo, Frecuencia frecuencia, CancellationToken ct = default)
        => EnExclusivaAsync(async () =>
    {
        var hercios = frecuencia.Hercios;
        ComprobarFrecuencia(hercios, frecuencia);

        var orden = vfo == NombreDeVfo.B ? "FB" : "FA";
        await MandarAsync($"{orden}{_perfil.Cifras(hercios)};", ct).ConfigureAwait(false);
        await LeerEstadoAsync(ct).ConfigureAwait(false);
    }, ct);

    /// <inheritdoc />
    public Task PonerModoDeAsync(NombreDeVfo vfo, Modo modo, CancellationToken ct = default)
        => EnExclusivaAsync(async () =>
    {
        var frecuencia = vfo == NombreDeVfo.B ? Vfos.B.Frecuencia : Vfos.A.Frecuencia;
        var nombre = _opciones.Traductor.AlEquipo(modo, frecuencia)
            ?? throw new ArgumentException(Textos.F("Servicios.Radio.ModoDesconocido", "FT-710", modo), nameof(modo));

        var codigo = ModosFt710.AlEquipo(nombre)
            ?? throw new ArgumentException(Textos.F("Servicios.Radio.SinEseModo", "FT-710", nombre), nameof(modo));

        // MD0 es el VFO activo y MD1 el otro, sea A o B (FT-710). En los de doble receptor MD0
        // es siempre el A y MD1 el B.
        var esElActivo = (vfo == NombreDeVfo.B) == await ElActivoEsBAsync(ct).ConfigureAwait(false);
        if (!esElActivo && !_perfil.TieneModoDelSegundoVfo)
        {
            throw new NotSupportedException(
                Textos.F("Servicios.Radio.SinMd1", _perfil.Nombre));
        }

        var orden = _perfil.ModoPrincipalEsElActivo
            ? (esElActivo ? "MD0" : "MD1")
            : (vfo == NombreDeVfo.B ? "MD1" : "MD0");
        await MandarAsync($"{orden}{codigo};", ct).ConfigureAwait(false);
        await LeerEstadoAsync(ct).ConfigureAwait(false);
    }, ct);

    /// <summary>
    /// Subir el PTT por aqui se salta el vigilante, asi que se rechaza; bajarlo se permite siempre.
    /// </summary>
    /// <param name="transmitir">Verdadero para subir el PTT.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <returns>La tarea de la orden.</returns>
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
            // La transmision vigilada de TUNE: el que emite es el propio acoplador («Tuning
            // Start», AC003), no el CAT. El vigilante la trata como cualquier otra: tope de
            // tiempo, latido y suelta por todas sus vias.
            Volatile.Write(ref _sintonizando, true);
            Volatile.Write(ref _pttPedido, true);
            await _canal.MandarAsync(_perfil.OrdenDeSintonia, ct).ConfigureAwait(false);
            Actualizar(estado => estado with { Transmitiendo = true });
            return;
        }

        if (!transmitir)
        {
            // Telegrafia en marcha: se para el manipulador ANTES de bajar el PTT, y por
            // cualquier camino que baje el PTT (fin normal, tope, latido, panico, cierre).
            await PararElManipuladorSiHaceFaltaAsync(ct).ConfigureAwait(false);
        }

        if (!transmitir && Volatile.Read(ref _sintonizando))
        {
            // Bajar una sintonia: TX0 siempre y, si el acoplador sigue en ello, pararlo
            // («Tuner OFF (Tuning Stop)», AC000) y volverlo a dejar en linea (AC001).
            Volatile.Write(ref _sintonizando, false);
            await _canal.MandarAsync("TX0;", ct).ConfigureAwait(false);
            if (_perfil.PosicionDeSintoniaEnRi is { } posicion)
            {
                var indicadores = await _canal.PreguntarAsync("RI0;", ct).ConfigureAwait(false);
                if (indicadores is null || !indicadores.StartsWith("RI", StringComparison.Ordinal)
                    || indicadores.Length < posicion + 2 || indicadores[posicion] != '0')
                {
                    await _canal.MandarAsync("AC000;", ct).ConfigureAwait(false);
                }
            }

            // Como la tecla del equipo: tras sintonizar, el acoplador queda en linea.
            await _canal.MandarAsync("AC001;", ct).ConfigureAwait(false);

            Volatile.Write(ref _pttPedido, false);
            Actualizar(estado => estado with { Transmitiendo = false });
            return;
        }

        if (_opciones.ViaDePtt != ViaDePtt.Cat && _canal.PuedeAccionarLineas)
        {
            await _canal.PonerLineaDePttAsync(transmitir, ct).ConfigureAwait(false);
        }
        else
        {
            await _canal.MandarAsync(transmitir ? "TX1;" : "TX0;", ct).ConfigureAwait(false);
        }

        Volatile.Write(ref _pttPedido, transmitir);
        Actualizar(estado => estado with { Transmitiendo = transmitir });
    }

    /// <summary>
    /// Mandos que admite un VFO concreto.
    /// </summary>
    /// <remarks>
    /// <see cref="Mandos"/> son los del VFO principal, que es lo que la interfaz ensena por
    /// omision. El segundo receptor tiene menos, y esta es la forma de saber cuales.
    /// </remarks>
    /// <param name="vfo">VFO por el que se pregunta.</param>
    /// <returns>Los mandos de ese VFO.</returns>
    public IReadOnlySet<MandoDeEquipo> MandosDe(VfoDelEquipo vfo)
    {
        lock (_candado)
        {
            return new HashSet<MandoDeEquipo>(vfo == VfoDelEquipo.Principal ? _mandos : _mandosDelSegundoVfo);
        }
    }

    /// <inheritdoc />
    public RangoDeMando? Rango(MandoDeEquipo mando) => Rango(mando, VfoDelEquipo.Principal);

    /// <summary>Describe el rango de un mando en un VFO. Nulo si ese VFO no lo tiene.</summary>
    /// <param name="mando">Mando buscado.</param>
    /// <param name="vfo">VFO por el que se pregunta.</param>
    /// <returns>El rango, o nulo.</returns>
    public RangoDeMando? Rango(MandoDeEquipo mando, VfoDelEquipo vfo)
    {
        lock (_candado)
        {
            var admitidos = vfo == VfoDelEquipo.Principal ? _mandos : _mandosDelSegundoVfo;
            if (!admitidos.Contains(mando))
            {
                return null;
            }
        }

        var descripcion = BuscarMando(mando);
        if (descripcion is null)
        {
            return null;
        }

        // La tabla de anchos en hercios es la del FT-710: en los demas se deja el indice.
        return mando == MandoDeEquipo.AnchoDeFiltro && _perfil == Yaesu.PerfilesYaesu.Ft710
            ? RangoDelAnchoDeFiltro(descripcion.Rango)
            : descripcion.Rango;
    }

    /// <summary>La descripcion de un mando en la tabla de este modelo.</summary>
    private MandoFt710? BuscarMando(MandoDeEquipo mando) =>
        _perfil.Mandos.FirstOrDefault(descripcion => descripcion.Mando == mando);

    /// <summary>
    /// Arma el rango del ancho de filtro con el modo que tenga puesto el equipo.
    /// </summary>
    /// <remarks>
    /// El indice de <c>SH</c> no significa lo mismo en banda lateral que en telegrafia, asi que
    /// las etiquetas se calculan con el modo de ahora: el operador ve «2400 Hz», no «13». Si
    /// todavia no se sabe en que modo esta el equipo, se deja el indice, que es lo honesto.
    /// </remarks>
    private RangoDeMando RangoDelAnchoDeFiltro(RangoDeMando deReserva)
    {
        var modo = Volatile.Read(ref _modoDelEquipo);
        var etiquetas = AnchosDeFiltroFt710.Etiquetas(modo);
        if (etiquetas is null)
        {
            return deReserva;
        }

        return new RangoDeMando(
            MandoDeEquipo.AnchoDeFiltro,
            0,
            AnchosDeFiltroFt710.IndiceMaximo(modo),
            1,
            "Hz",
            etiquetas);
    }

    /// <summary>
    /// Ancho del filtro en hercios, si se sabe.
    /// </summary>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <returns>
    /// Los hercios, o nulo si el equipo esta en «por omision», si no se conoce el modo o si el
    /// modo no tiene tabla de anchos.
    /// </returns>
    public async Task<int?> LeerAnchoDeFiltroEnHerciosAsync(CancellationToken ct = default)
    {
        var indice = await LeerMandoAsync(MandoDeEquipo.AnchoDeFiltro, ct).ConfigureAwait(false);
        return indice is null || _perfil != Yaesu.PerfilesYaesu.Ft710
            ? null
            : AnchosDeFiltroFt710.Hercios((int)indice.Value, Volatile.Read(ref _modoDelEquipo));
    }

    /// <inheritdoc />
    public Task<double?> LeerMandoAsync(MandoDeEquipo mando, CancellationToken ct = default) =>
        LeerMandoAsync(mando, VfoDelEquipo.Principal, ct);

    /// <summary>Lee el valor de un mando en el VFO indicado.</summary>
    /// <param name="mando">Mando a leer.</param>
    /// <param name="vfo">VFO del que se lee.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <returns>El valor, o nulo si ese VFO no tiene el mando.</returns>
    public async Task<double?> LeerMandoAsync(
        MandoDeEquipo mando,
        VfoDelEquipo vfo,
        CancellationToken ct = default)
    {
        var descripcion = BuscarMando(mando);
        if (descripcion is null || Rango(mando, vfo) is null || descripcion.ConsultaDe(vfo) is not { } consulta)
        {
            return null;
        }

        var respuesta = await PreguntarAsync(consulta + Cat.Fin, ct).ConfigureAwait(false);
        return descripcion.Interpretar(respuesta, vfo);
    }

    /// <inheritdoc />
    /// <exception cref="NotSupportedException">
    /// Si este equipo no admite el mando o si el mando es de solo lectura.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Si el mando pone el equipo en antena y no hay una transmision en curso pedida al
    /// vigilante del PTT.
    /// </exception>
    public Task EscribirMandoAsync(MandoDeEquipo mando, double valor, CancellationToken ct = default) =>
        EscribirMandoAsync(mando, valor, VfoDelEquipo.Principal, ct);

    /// <summary>Acciona un mando en el VFO indicado.</summary>
    /// <param name="mando">Mando a accionar.</param>
    /// <param name="valor">Valor en unidades del operador.</param>
    /// <param name="vfo">VFO al que se le manda.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <returns>La tarea de la orden.</returns>
    /// <exception cref="NotSupportedException">
    /// Si ese VFO no admite el mando o si el mando es de solo lectura.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Si el mando pone el equipo en antena y no hay una transmision en curso pedida al
    /// vigilante del PTT.
    /// </exception>
    public async Task EscribirMandoAsync(
        MandoDeEquipo mando,
        double valor,
        VfoDelEquipo vfo,
        CancellationToken ct = default)
    {
        var descripcion = BuscarMando(mando);
        var rango = Rango(mando, vfo);
        if (descripcion is null || rango is null)
        {
            throw new NotSupportedException(Textos.F("Servicios.Radio.MandoNoEnVfo", mando, vfo));
        }

        if (rango.SoloLectura)
        {
            throw new NotSupportedException(Textos.F("Servicios.Radio.MandoSoloLectura", mando));
        }

        // En el acoplador solo emite la ultima posicion («Sintonizar», AC002); encenderlo o
        // apagarlo (AC001/AC000) no pone nada en el aire y no necesita PTT.
        var emite = rango.TransmiteAlAccionar
                    && (!rango.EsDePosiciones || rango.Ajustar(valor) >= rango.Maximo);
        if (emite && !Volatile.Read(ref _pttPedido))
        {
            // Sintonizar el acoplador emite portadora. Si no hay una transmision pedida al
            // vigilante, no hay nadie que suelte el PTT si esto se atasca: no se acciona.
            throw new InvalidOperationException(
                $"El mando {mando} pone el equipo en antena: hay que pedir antes una transmisión a "
                + "IVigilantePtt y accionarlo dentro de ella.");
        }

        string? loQueHay = null;
        if (descripcion.NecesitaLoQueHay)
        {
            loQueHay = await PreguntarAsync(descripcion.OrdenDeLectura, ct).ConfigureAwait(false);
            if (OrdenesFt710.DiceQueNoLoAdmite(loQueHay)
                || !loQueHay!.StartsWith(descripcion.Consulta, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    Textos.F("Servicios.Radio.MandoSinEstado", mando, loQueHay));
            }
        }

        await MandarAsync(descripcion.OrdenDeEscritura(valor, vfo, loQueHay), ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Del medidor S se sacan unidades S. De los demas medidores el equipo da un numero de 0 a
    /// 255 sin curva de calibracion, asi que se devuelven en crudo por
    /// <see cref="LeerMedidoresCrudosAsync"/> y aqui se dejan sin rellenar antes que inventar
    /// vatios, amperios o una relacion de onda estacionaria que no son.
    /// </remarks>
    public async Task<LecturaDeMedidores> LeerMedidoresAsync(CancellationToken ct = default)
    {
        var senal = await LeerSenalAsync(ct).ConfigureAwait(false);
        return new LecturaDeMedidores(
            UnidadesS: senal,
            PotenciaVatios: null,
            Roe: null,
            Alc: null,
            CorrienteAmperios: null,
            TensionVoltios: null,
            Compresion: null,
            LeidoUtc: DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Lee los medidores tal y como los da el equipo, de 0 a 255.
    /// </summary>
    /// <remarks>
    /// <c>RM1</c> es el medidor S, <c>RM3</c> la compresion, <c>RM4</c> el control automatico de
    /// nivel, <c>RM5</c> la potencia, <c>RM6</c> la relacion de onda estacionaria y <c>RM7</c>
    /// la corriente. <c>RM2</c> contesta <c>?;</c> en este firmware, que encaja con que ese
    /// numero no se use. Pasar estos numeros a vatios, amperios o relacion de onda hace falta
    /// la curva del manual: mientras no la haya, la interfaz debe enseñarlos como barras
    /// relativas y no como unidades.
    /// </remarks>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <returns>Los medidores que conteste el equipo, con su valor de 0 a 255.</returns>
    public async Task<IReadOnlyDictionary<string, int>> LeerMedidoresCrudosAsync(CancellationToken ct = default)
    {
        var medidores = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (orden, nombre) in _perfil.Medidores)
        {
            var respuesta = await PreguntarAsync($"{orden};", ct).ConfigureAwait(false);
            if (OrdenesFt710.DiceQueNoLoAdmite(respuesta) || !respuesta!.StartsWith(orden, StringComparison.Ordinal))
            {
                continue;
            }

            // La trama es RM + medidor + TRES cifras de valor + tres de relleno: «RM1008000» es
            // el medidor S marcando 8. Leer las seis cifras de golpe daba 8000 con el equipo
            // real (27-09-2026), fuera de la escala de 0 a 255.
            var cifras = respuesta[orden.Length..];
            if (cifras.Length > 3)
            {
                cifras = cifras[..3];
            }

            if (int.TryParse(cifras, NumberStyles.Integer, CultureInfo.InvariantCulture, out var valor))
            {
                medidores[nombre] = valor;
            }
        }

        return medidores;
    }

    /// <summary>Cuantos canales de memoria se recorren al leer el banco.</summary>
    public const int MemoriasDelEquipo = 99;

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// Se recorren los canales con <c>MR</c>, <b>y el canal son tres cifras</b>. Aqui se dio por
    /// imposible leer memorias porque se probo <c>MT00;</c>, con dos: el equipo contestaba
    /// <c>?;</c> y se tomo por «no lo sabe hacer». No era eso.
    /// </para>
    /// <para>
    /// <b>Regla que conviene no olvidar:</b> un <c>?;</c> quiere decir «no lo admito <i>tal y
    /// como lo has escrito</i>», no que la orden no exista. Antes de dar una capacidad por
    /// ausente hay que mirar el manual de ordenes —el de CAT, no el de operacion—. Aqui, de
    /// hecho, el <c>?;</c> de un canal significa justo lo contrario de un fallo: <b>esa memoria
    /// esta vacia</b>.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyList<MemoriaDeEquipo>> LeerMemoriasAsync(CancellationToken ct = default)
    {
        var memorias = new List<MemoriaDeEquipo>();

        for (var canal = 1; canal <= _perfil.Memorias; canal++)
        {
            ct.ThrowIfCancellationRequested();

            var respuesta = await PreguntarAsync(
                string.Create(CultureInfo.InvariantCulture, $"MR{canal:D3}{Cat.Fin}"),
                ct).ConfigureAwait(false);

            if (OrdenesFt710.DiceQueNoLoAdmite(respuesta))
            {
                // Memoria vacia: no hay nada que apuntar y no es ningun error.
                continue;
            }

            if (!TryLeerMemoria(respuesta!, out var memoria))
            {
                continue;
            }

            var etiqueta = _perfil.RotulosDeMemoria ? await LeerRotuloDeMemoriaAsync(canal, ct).ConfigureAwait(false) : null;
            memorias.Add(memoria! with { Etiqueta = etiqueta });
        }

        return memorias;
    }

    /// <summary>
    /// Lee una trama de memoria: canal, frecuencia y modo.
    /// </summary>
    /// <remarks>
    /// La trama es <c>MR</c> + canal (3) + frecuencia en hercios (9) + clarificador (5) + dos
    /// cifras + el modo (1) + el resto. La posicion del modo esta sacada de comparar esta trama
    /// con la de <c>IF</c>, que tiene la misma forma: en la captura del equipo, <c>IF</c> traia
    /// un <c>2</c> con el equipo en banda lateral superior, y la memoria 001, en 7 MHz, trae un
    /// <c>1</c>, que es banda lateral inferior —lo que corresponde en 40 metros—. Si algun dia
    /// se ve un modo que no cuadra, es este desplazamiento lo que hay que revisar.
    /// </remarks>
    private bool TryLeerMemoria(string respuesta, out MemoriaDeEquipo? memoria)
    {
        memoria = null;
        var n = _perfil.CifrasDeFrecuencia;
        if (!respuesta.StartsWith("MR", StringComparison.OrdinalIgnoreCase) || respuesta.Length < 13 + n)
        {
            return false;
        }

        var cuerpo = respuesta[2..];
        if (!int.TryParse(cuerpo[..3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var canal)
            || !long.TryParse(cuerpo[3..(3 + n)], NumberStyles.Integer, CultureInfo.InvariantCulture, out var hercios))
        {
            return false;
        }

        var modo = Modo.Vacio;
        var posicionDelModo = 10 + n;
        if (cuerpo.Length > posicionDelModo && ModosFt710.DesdeElEquipo(cuerpo[posicionDelModo]) is { } nombre)
        {
            modo = _opciones.Traductor.DesdeElEquipo(nombre);
        }

        memoria = new MemoriaDeEquipo(
            canal,
            Frecuencia.DesdeHercios(hercios),
            modo,
            Etiqueta: null,
            Ocupada: true);
        return true;
    }

    /// <summary>
    /// Lee el rotulo de una memoria.
    /// </summary>
    /// <remarks>
    /// <c>MT001;</c> contesta <c>MT0010</c> mas el rotulo con relleno de espacios; la cifra que
    /// va antes del texto dice si el equipo lo ensena.
    /// </remarks>
    private async Task<string?> LeerRotuloDeMemoriaAsync(int canal, CancellationToken ct)
    {
        var respuesta = await PreguntarAsync(
            string.Create(CultureInfo.InvariantCulture, $"MT{canal:D3}{Cat.Fin}"),
            ct).ConfigureAwait(false);

        if (OrdenesFt710.DiceQueNoLoAdmite(respuesta) || respuesta!.Length <= 6)
        {
            return null;
        }

        var rotulo = respuesta[6..].Trim();
        return rotulo.Length == 0 ? null : rotulo;
    }

    /// <summary>
    /// Lee la version del firmware de cada procesador del equipo.
    /// </summary>
    /// <remarks>
    /// Con <c>VE</c>: el 0 es la unidad principal, el 1 la de pantalla, el 2 el receptor por
    /// muestreo directo y el 3 el procesador de senal. Contesta cuatro cifras, que son la
    /// version con dos decimales: <c>VE00112</c> es la 01.12.
    /// </remarks>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <returns>Cada procesador con su version.</returns>
    public async Task<IReadOnlyDictionary<string, string>> LeerVersionesDeFirmwareAsync(
        CancellationToken ct = default)
    {
        var versiones = new Dictionary<string, string>(StringComparer.Ordinal);
        var nombres = new[] { "unidad principal", "unidad de pantalla", "receptor SDR", "procesador de señal" };

        for (var i = 0; i < nombres.Length; i++)
        {
            var respuesta = await PreguntarAsync(
                string.Create(CultureInfo.InvariantCulture, $"VE{i}{Cat.Fin}"),
                ct).ConfigureAwait(false);

            if (OrdenesFt710.DiceQueNoLoAdmite(respuesta) || respuesta!.Length < 7)
            {
                continue;
            }

            var cifras = respuesta[3..7];
            versiones[nombres[i]] = $"{cifras[..2]}.{cifras[2..]}";
        }

        return versiones;
    }

    /// <inheritdoc />
    public Task IrAMemoriaAsync(int numero, CancellationToken ct = default)
        => EnExclusivaAsync(async () =>
    {
        if (numero is < 1 or > 999)
        {
            throw new ArgumentOutOfRangeException(nameof(numero), numero, "Las memorias del FT-710 van de 1 a 999.");
        }

        await MandarAsync(string.Create(CultureInfo.InvariantCulture, $"MC{numero:D3};"), ct).ConfigureAwait(false);
        await LeerEstadoAsync(ct).ConfigureAwait(false);
    }, ct);

    /// <inheritdoc />
    /// <exception cref="OrdenPeligrosaException">
    /// Si la orden apaga el equipo, escribe memorias, manipula telegrafia o pone el equipo en
    /// antena. Para transmitir se pide antena al vigilante del PTT.
    /// </exception>
    public async Task<string?> OrdenEnCrudoAsync(string orden, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orden);

        var limpia = orden.Trim();
        if (!limpia.EndsWith(Cat.Fin))
        {
            limpia += Cat.Fin;
        }

        OrdenesFt710.ComprobarQueValeEnCrudo(limpia);
        return await _canal.PreguntarAsync(limpia, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// El audio que trae el equipo por USB, si esta encendido.
    /// </summary>
    /// <remarks>
    /// Se mira cada vez, no se recuerda: el operador puede apagar la radio en cualquier momento.
    /// Sirve para dos cosas: saber si la radio esta viva sin abrir el puerto serie, y decirle al
    /// modem de la Fase 4 cual de todos los dispositivos de audio del ordenador es la radio.
    /// </remarks>
    /// <returns>El audio del equipo, o nulo si no esta.</returns>
    public static AudioDelEquipo? BuscarElAudioDelEquipo() => AudioDelFt710.Buscar();

    /// <summary>
    /// Lee la fecha y la hora que tiene puestas el equipo.
    /// </summary>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <returns>La fecha y hora del equipo, o nulo si no las da.</returns>
    public async Task<DateTimeOffset?> LeerRelojDelEquipoAsync(CancellationToken ct = default)
    {
        // DT0 da la fecha (DT020260921 = 2026-09-21) y DT1 la hora (DT1071700 = 07:17:00).
        var fecha = await PreguntarAsync("DT0;", ct).ConfigureAwait(false);
        var hora = await PreguntarAsync("DT1;", ct).ConfigureAwait(false);
        if (OrdenesFt710.DiceQueNoLoAdmite(fecha) || OrdenesFt710.DiceQueNoLoAdmite(hora))
        {
            return null;
        }

        var cifrasDeFecha = fecha!.Length >= 11 ? fecha[3..11] : null;
        var cifrasDeHora = hora!.Length >= 9 ? hora[3..9] : null;
        if (cifrasDeFecha is null || cifrasDeHora is null)
        {
            return null;
        }

        return DateTimeOffset.TryParseExact(
            cifrasDeFecha + cifrasDeHora,
            "yyyyMMddHHmmss",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeLocal,
            out var momento)
            ? momento
            : null;
    }

    /// <summary>Lee el tono CTCSS que tiene puesto el equipo, como indice de su tabla.</summary>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <returns>El indice del tono, o nulo si el equipo no lo da.</returns>
    /// <remarks>
    /// Se da el indice y no los hercios: la tabla de tonos de Yaesu no esta confirmada contra
    /// este equipo, y un tono equivocado es un repetidor que no abre.
    /// </remarks>
    public async Task<int?> LeerIndiceDeTonoAsync(CancellationToken ct = default) =>
        await LeerCifrasAsync("CN00;", "CN00", ct).ConfigureAwait(false);

    /// <summary>Lee el desplazamiento de repetidor que tiene puesto el equipo.</summary>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <returns>El valor del equipo, o nulo si no lo da.</returns>
    public async Task<int?> LeerDesplazamientoDeRepetidorAsync(CancellationToken ct = default) =>
        await LeerCifrasAsync("OS0;", "OS0", ct).ConfigureAwait(false);

    /// <summary>Lee los indicadores de estado del equipo, tal cual los da.</summary>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <returns>La cadena de indicadores, o nulo.</returns>
    /// <remarks>
    /// El equipo contesta ocho cifras (<c>RI00000000</c>) y no consta que significa cada una,
    /// asi que se devuelven sin interpretar.
    /// </remarks>
    public async Task<string?> LeerIndicadoresAsync(CancellationToken ct = default)
    {
        var respuesta = await PreguntarAsync("RI0;", ct).ConfigureAwait(false);
        return OrdenesFt710.DiceQueNoLoAdmite(respuesta) || respuesta!.Length <= 3
            ? null
            : respuesta[3..];
    }

    private async Task<int?> LeerCifrasAsync(string orden, string prefijo, CancellationToken ct)
    {
        var respuesta = await PreguntarAsync(orden, ct).ConfigureAwait(false);
        if (OrdenesFt710.DiceQueNoLoAdmite(respuesta)
            || !respuesta!.StartsWith(prefijo, StringComparison.OrdinalIgnoreCase)
            || respuesta.Length <= prefijo.Length)
        {
            return null;
        }

        return int.TryParse(
            respuesta[prefijo.Length..],
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var valor)
            ? valor
            : null;
    }

    /// <summary>Lee un ajuste del menu interno.</summary>
    /// <param name="indice">Indice de seis cifras, por ejemplo <c>040101</c>.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <returns>Lo leido, o nulo si el equipo no tiene ese ajuste.</returns>
    public async Task<LecturaDeMenu?> LeerDelMenuAsync(string indice, CancellationToken ct = default)
    {
        var respuesta = await PreguntarAsync(MenuFt710.OrdenDeLectura(indice), ct).ConfigureAwait(false);
        return MenuFt710.TryAnalizar(respuesta, out var lectura) ? lectura : null;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_desechado)
        {
            return;
        }

        _desechado = true;

        try
        {
            await DesconectarAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _registro.LogWarning(ex, "Fallo al desconectar del FT-710.");
        }

        await _canal.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>Lee del equipo lo que hace falta para el cuaderno.</summary>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <returns>El estado recien leido.</returns>
    public Task<EstadoDelEquipo> LeerEstadoAsync(CancellationToken ct = default) =>
        EnExclusivaAsync(() => LeerEstadoSinEsperarAsync(ct), ct);

    private async Task<EstadoDelEquipo> LeerEstadoSinEsperarAsync(CancellationToken ct)
    {
        var anterior = Estado;
        var vfoActivo = await PreguntarAsync("VS;", ct).ConfigureAwait(false);
        if (vfoActivo is null)
        {
            // El canal sigue abierto pero el equipo ha dejado de contestar: se ha apagado o
            // lo han desenchufado. Quien llama decide, pero enterarse es obligatorio.
            throw new EquipoNoContestaException(_canal.Descripcion);
        }

        // Si VS contesta «?;» o algo que no es VS, se sigue con el VFO que se tenia.
        var enB = vfoActivo is { Length: >= 3 } && vfoActivo.StartsWith("VS", StringComparison.OrdinalIgnoreCase)
            ? vfoActivo[2] == '1'
            : anterior.Vfo == "VFO B";

        // Los dos VFO en cada pasada: el frontal los ensena a la vez, como la radio.
        var anteriores = Vfos;
        var frecuenciaA = await LeerFrecuenciaAsync("FA", ct).ConfigureAwait(false);
        var frecuenciaB = await LeerFrecuenciaAsync("FB", ct).ConfigureAwait(false);
        var frecuenciaActiva = (enB ? frecuenciaB : frecuenciaA) ?? anterior.Frecuencia;

        var split = await PreguntarAsync("ST;", ct).ConfigureAwait(false);
        var stContesta = split is { Length: >= 3 } && split.StartsWith("ST", StringComparison.OrdinalIgnoreCase);
        var haySplit = stContesta
            ? split![2] != '0'
            : Vfos.Split;

        // Por donde se transmite. Si el equipo no contesta a FT;, se deduce: sin split se
        // transmite por el activo, con split por el otro.
        var respuestaFt = await PreguntarAsync("FT;", ct).ConfigureAwait(false);
        var ftContesta = respuestaFt is { Length: >= 3 }
                         && respuestaFt.StartsWith("FT", StringComparison.Ordinal)
                         && respuestaFt[2] is '0' or '1';
        var transmiteEnB = ftContesta
            // FT0 = transmite la banda PRINCIPAL (el VFO elegido con VS), FT1 = la otra. En los
            // modelos con FT absoluto (FT0 = VFO A, FT1 = VFO B) no se mira el activo.
            ? _perfil.TransmisionRelativaAlActivo ? (respuestaFt![2] == '1') != enB : respuestaFt![2] == '1'
            : enB != haySplit;

        if (!stContesta && ftContesta)
        {
            // Sin ST (FT-991, FTDX1200/3000/5000): hay split si se transmite por el otro VFO.
            haySplit = transmiteEnB != enB;
        }

        Frecuencia? frecuenciaRx = null;
        var frecuenciaDeTrabajo = frecuenciaActiva;
        if (haySplit)
        {
            // Con dos frecuencias, el cuaderno apunta la de transmision y guarda la de escucha.
            var otra = enB ? frecuenciaA : frecuenciaB;
            if (otra is not null)
            {
                frecuenciaRx = frecuenciaActiva;
                frecuenciaDeTrabajo = otra.Value;
            }
        }

        // IF: P3 desplazamiento del clarificador (+0120), P4 clarificador de recepcion, P5 de
        // transmision y P7 si se esta en VFO (0), memoria (1), sintonia de memoria (2) o QMB (3).
        // Con esto el visor ensena lo que se cambia desde las teclas del propio equipo.
        var informe = await PreguntarAsync("IF;", ct).ConfigureAwait(false);
        var anterioresVfos = Vfos;
        var rit = anterioresVfos.Rit;
        var xit = anterioresVfos.Xit;
        var desplazamiento = anterioresVfos.DesplazamientoRitHz;
        // IF + memoria (3) + frecuencia (9, u 8 en los antiguos) + clarificador (5) + P4 P5 P6 P7.
        var baseIf = 5 + _perfil.CifrasDeFrecuencia;
        if (informe is not null && informe.Length >= baseIf + 9 && informe.StartsWith("IF", StringComparison.Ordinal)
            && int.TryParse(informe.AsSpan(baseIf, 5), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var hz))
        {
            desplazamiento = hz;
            rit = informe[baseIf + 5] == '1';
            xit = informe[baseIf + 6] == '1';
            Volatile.Write(ref _enMemoria, informe[baseIf + 8] is '1' or '2');
        }

        // MD0 es la banda principal (el VFO activo) y MD1 la otra: con VS1, MD0 es el B. En los
        // de doble receptor MD0 es siempre el A. Sin MD1, el modo del otro no se sabe.
        var modoPrincipal = await LeerModoAsync("MD0", ct).ConfigureAwait(false);
        var modoOtro = _perfil.TieneModoDelSegundoVfo ? await LeerModoAsync("MD1", ct).ConfigureAwait(false) : null;
        var modoA = _perfil.ModoPrincipalEsElActivo ? (enB ? modoOtro : modoPrincipal) : modoPrincipal;
        var modoB = _perfil.ModoPrincipalEsElActivo ? (enB ? modoPrincipal : modoOtro) : modoOtro;
        var nombreActivo = enB ? modoB : modoA;

        var modo = anterior.Modo;
        if (nombreActivo is not null)
        {
            modo = _opciones.Traductor.DesdeElEquipo(nombreActivo);

            // El ancho de filtro solo se puede dar en hercios sabiendo el modo.
            Volatile.Write(ref _modoDelEquipo, nombreActivo);
        }

        var vfos = new EstadoDeLosVfos(
            new EstadoDeUnVfo(
                NombreDeVfo.A,
                frecuenciaA ?? anteriores.A.Frecuencia,
                modoA is null ? anteriores.A.Modo : _opciones.Traductor.DesdeElEquipo(modoA),
                EsElActivo: !enB,
                Transmite: !transmiteEnB,
                Recibe: !enB,
                AnchoDeFiltroHz: null),
            new EstadoDeUnVfo(
                NombreDeVfo.B,
                frecuenciaB ?? anteriores.B.Frecuencia,
                modoB is null ? anteriores.B.Modo : _opciones.Traductor.DesdeElEquipo(modoB),
                EsElActivo: enB,
                Transmite: transmiteEnB,
                Recibe: enB,
                AnchoDeFiltroHz: null),
            Split: haySplit,
            Rit: rit,
            DesplazamientoRitHz: desplazamiento,
            Xit: xit,
            DesplazamientoXitHz: desplazamiento)
        {
            EnMemoria = Volatile.Read(ref _enMemoria),
        };

        var vfosCambiaron = false;
        lock (_candado)
        {
            vfosCambiaron = _vfos != vfos;
            _vfos = vfos;
        }

        double? potencia = anterior.PotenciaVatios;
        var respuestaPotencia = await PreguntarAsync("PC;", ct).ConfigureAwait(false);
        if (!OrdenesFt710.DiceQueNoLoAdmite(respuestaPotencia)
            && respuestaPotencia!.Length > 2
            && int.TryParse(respuestaPotencia[2..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var vatios))
        {
            potencia = vatios;
        }

        var senal = Volatile.Read(ref _pttPedido)
            ? anterior.SenalRecibida
            : await LeerSenalAsync(ct).ConfigureAwait(false) ?? anterior.SenalRecibida;

        return Actualizar(estado => estado with
        {
            Conectado = true,
            Transmitiendo = Volatile.Read(ref _pttPedido),
            Frecuencia = frecuenciaDeTrabajo,
            FrecuenciaRx = frecuenciaRx,
            Modo = modo,
            Vfo = enB ? "VFO B" : "VFO A",
            PotenciaVatios = potencia,
            SenalRecibida = senal,
        }, forzarAviso: vfosCambiaron);
    }



    /// <summary>
    /// Pregunta al equipo, mando a mando y VFO a VFO, que sabe hacer.
    /// </summary>
    /// <remarks>
    /// Se pregunta por los dos VFO porque <b>varios mandos existen solo para el principal</b>:
    /// el ancho de filtro, el filtro estrecho, la ganancia de radiofrecuencia, el control
    /// automatico de ganancia y el atenuador contestan <c>?;</c> cuando se les pregunta por el
    /// segundo. Una interfaz que ofreciera esos mandos para el segundo VFO estaria ofreciendo
    /// algo que no existe.
    /// </remarks>
    private async Task AveriguarCapacidadesAsync(CancellationToken ct)
    {
        var admitidos = new List<MandoDeEquipo>();
        var rechazados = new List<MandoDeEquipo>();
        var admitidosEnElSegundo = new List<MandoDeEquipo>();

        foreach (var descripcion in _perfil.Mandos)
        {
            var respuesta = await PreguntarAsync(descripcion.OrdenDeLectura, ct).ConfigureAwait(false);
            if (OrdenesFt710.DiceQueNoLoAdmite(respuesta))
            {
                // Un «?;» no es un fallo de comunicacion: es el equipo diciendo que eso no lo tiene.
                rechazados.Add(descripcion.Mando);
                continue;
            }

            admitidos.Add(descripcion.Mando);

            if (descripcion.OrdenDeLecturaDelSegundoVfo is not { } ordenDelSegundo)
            {
                continue;
            }

            var respuestaDelSegundo = await PreguntarAsync(ordenDelSegundo, ct).ConfigureAwait(false);
            if (EsDelSegundoVfo(respuestaDelSegundo, descripcion.ConsultaDelSegundoVfo!))
            {
                admitidosEnElSegundo.Add(descripcion.Mando);
            }
            else if (!OrdenesFt710.DiceQueNoLoAdmite(respuestaDelSegundo))
            {
                _registro.LogDebug(
                    "{Orden} contesta «{Respuesta}», que es el valor del VFO principal: {Mando} no existe aparte en el segundo.",
                    ordenDelSegundo,
                    respuestaDelSegundo,
                    descripcion.Mando);
            }
        }

        lock (_candado)
        {
            _mandos.Clear();
            _mandosDelSegundoVfo.Clear();
            foreach (var mando in admitidos)
            {
                _mandos.Add(mando);
            }

            foreach (var mando in admitidosEnElSegundo)
            {
                _mandosDelSegundoVfo.Add(mando);
            }
        }

        _registro.LogInformation(
            "El equipo admite {Admitidos} mandos ({DelSegundo} también en el segundo VFO) y rechaza "
            + "{Rechazados}: {Lista}.",
            admitidos.Count,
            admitidosEnElSegundo.Count,
            rechazados.Count,
            rechazados.Count > 0 ? string.Join(", ", rechazados) : "ninguno");
    }

    /// <summary>
    /// La respuesta a la consulta del segundo VFO trae de verdad un valor de ese VFO.
    /// </summary>
    /// <remarks>
    /// Con el equipo real (27-09-2026), <c>SQ1;</c>, <c>PA1;</c> e <c>IS1;</c> contestan
    /// <c>SQ0000</c>, <c>PA00</c> e <c>IS00+0000</c>: con el indice del principal. El equipo no
    /// dice «no lo admito», pero tampoco da un valor aparte; ofrecer ese mando en el VFO B seria
    /// ensenar el del A con otra etiqueta. <c>AG1;</c> y <c>NB1;</c> si contestan con su indice.
    /// </remarks>
    private static bool EsDelSegundoVfo(string? respuesta, string consulta) =>
        !OrdenesFt710.DiceQueNoLoAdmite(respuesta)
        && respuesta!.Trim().StartsWith(consulta, StringComparison.OrdinalIgnoreCase);

    private async Task<Frecuencia?> LeerFrecuenciaAsync(string orden, CancellationToken ct)
    {
        var respuesta = await PreguntarAsync($"{orden};", ct).ConfigureAwait(false);
        // Sin comprobar el prefijo, una respuesta atrasada de otra orden («SM0003») se leia
        // como una frecuencia de 3 Hz.
        if (OrdenesFt710.DiceQueNoLoAdmite(respuesta)
            || respuesta!.Length != orden.Length + _perfil.CifrasDeFrecuencia
            || !respuesta.StartsWith(orden, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var cifras = respuesta[orden.Length..];
        return long.TryParse(cifras, NumberStyles.Integer, CultureInfo.InvariantCulture, out var hercios)
            ? Frecuencia.DesdeHercios(hercios)
            : null;
    }

    /// <summary>Lee el modo de un VFO (<c>MD0</c> o <c>MD1</c>) con el nombre que le da el equipo.</summary>
    private async Task<string?> LeerModoAsync(string orden, CancellationToken ct)
    {
        var respuesta = await PreguntarAsync($"{orden};", ct).ConfigureAwait(false);
        return respuesta is { Length: >= 4 } && respuesta.StartsWith(orden, StringComparison.Ordinal)
            ? ModosFt710.DesdeElEquipo(respuesta[3])
            : null;
    }

    private async Task<double?> LeerSenalAsync(CancellationToken ct)
    {
        var respuesta = await PreguntarAsync("SM0;", ct).ConfigureAwait(false);
        if (OrdenesFt710.DiceQueNoLoAdmite(respuesta) || respuesta!.Length < 4)
        {
            return null;
        }

        return int.TryParse(respuesta[3..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var crudo)
            ? AUnidadesS(crudo)
            : null;
    }

    /// <summary>
    /// Pasa la lectura del medidor S, de 0 a 255, a unidades S.
    /// </summary>
    /// <remarks>
    /// Hasta S9 el reparto es lineal; por encima, cada unidad son seis decibelios, que es como
    /// se lee un medidor de verdad. Donde cae el S9 en la escala del equipo es un ajuste
    /// (<see cref="OpcionesFt710.LecturaDeS9"/>) porque la captura no lo dice: es la mejor
    /// aproximacion disponible, no una medida calibrada.
    /// </remarks>
    /// <param name="crudo">Lectura del equipo, de 0 a 255.</param>
    /// <returns>Unidades S.</returns>
    public double AUnidadesS(int crudo)
    {
        var s9 = Math.Clamp(_opciones.LecturaDeS9, 1, 254);
        if (crudo <= s9)
        {
            return Math.Clamp(crudo / (double)s9 * 9d, 0d, 9d);
        }

        var porEncima = (crudo - s9) / (double)(255 - s9);
        return Math.Clamp(9d + (porEncima * 10d), 9d, 19d);
    }

    private async Task SondearSiempreAsync(CancellationToken ct)
    {
        using var reloj = new PeriodicTimer(_opciones.IntervaloDeSondeo);
        var fallosSeguidos = 0;
        var perdido = false;
        var espera = _opciones.EsperaDeReconexion;

        while (true)
        {
            try
            {
                if (!await reloj.WaitForNextTickAsync(ct).ConfigureAwait(false))
                {
                    return;
                }

                if (perdido)
                {
                    var seVeElCodec = ElCodecEstaPresente();
                    if (seVeElCodec == false)
                    {
                        // El códec de audio del equipo no está: la radio sigue apagada. No se
                        // abre el puerto para nada; se espera a que vuelva.
                        await Task.Delay(espera, ct).ConfigureAwait(false);
                        espera = Espaciar(espera);
                        continue;
                    }

                    if (seVeElCodec == true && espera > _opciones.EsperaDeReconexion)
                    {
                        // El códec ha vuelto a aparecer: la radio está otra vez encendida. No
                        // tiene ningún sentido seguir esperando lo que tocaba por el retroceso:
                        // se vuelve a intentar de inmediato.
                        _registro.LogDebug("El códec de audio del equipo ha vuelto; se reintenta ya.");
                        espera = _opciones.EsperaDeReconexion;
                    }

                    await Task.Delay(espera, ct).ConfigureAwait(false);
                    await ReconectarAsync(ct).ConfigureAwait(false);
                    perdido = false;
                    fallosSeguidos = 0;
                    espera = _opciones.EsperaDeReconexion;
                    _registro.LogInformation("El equipo ha vuelto.");
                    ComunicacionRecuperada?.Invoke(this, _canal.Descripcion);
                    continue;
                }

                if (Volatile.Read(ref _pttPedido))
                {
                    // En antena no se pregunta por el dial —no se mueve— ni por el PTT, porque
                    // en este equipo preguntar por el PTT es ponerlo. Pero hay que seguir
                    // comprobando que el equipo sigue ahi: si se apaga en mitad de una
                    // transmision hay que enterarse, y para eso vale `ID;`, que es inocuo.
                    await ComprobarQueSigueAhiAsync(ct).ConfigureAwait(false);
                    fallosSeguidos = 0;
                    continue;
                }

                await LeerEstadoAsync(ct).ConfigureAwait(false);
                fallosSeguidos = 0;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                if (perdido)
                {
                    // Sigue sin estar; se espacia el reintento para no machacar el puerto.
                    espera = Espaciar(espera);
                    continue;
                }

                fallosSeguidos++;
                _registro.LogDebug(
                    ex,
                    "El FT-710 no contesta ({Fallos} de {Tope}).",
                    fallosSeguidos,
                    FallosParaDarloPorPerdido);

                if (fallosSeguidos < FallosParaDarloPorPerdido)
                {
                    continue;
                }

                perdido = true;
                DarPorPerdido(ex);
            }
        }
    }

    /// <summary>
    /// Cuantas pasadas seguidas sin respuesta hacen falta para dar el equipo por perdido.
    /// </summary>
    /// <remarks>Una sola pasada fallida puede ser un byte perdido; tres seguidas, no.</remarks>
    public const int FallosParaDarloPorPerdido = 3;

    /// <summary>
    /// Lo que puede tardar, como mucho, en darse cuenta de que el equipo ha desaparecido.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Esta cifra importa: es la ventana en la que el programa todavia se cree en antena
    /// despues de que el equipo se apague o se desenchufe. Sale de que cada pasada de sondeo
    /// gasta, en el peor caso, el intervalo mas la espera completa de una orden que no va a
    /// contestar nadie, y de que hacen falta <see cref="FallosParaDarloPorPerdido"/> pasadas
    /// seguidas asi; se suma una pasada mas porque el equipo puede desaparecer justo despues
    /// de empezar una.
    /// </para>
    /// <para>
    /// Con los valores de partida (sondeo cada 500 ms, espera de orden 350 ms) son <b>3,4
    /// segundos</b>. Detras de esta cota estan las otras dos redes: el tiempo maximo de
    /// transmision del vigilante, que suelta pase lo que pase con la deteccion, y el cierre
    /// del proceso.
    /// </para>
    /// </remarks>
    /// <param name="opciones">Ajustes con los que corre el control.</param>
    /// <returns>La cota de tiempo hasta que se avisa de que el equipo no esta.</returns>
    public static TimeSpan TiempoMaximoDeDeteccion(OpcionesFt710 opciones)
    {
        ArgumentNullException.ThrowIfNull(opciones);

        var teorica = (FallosParaDarloPorPerdido + 1) * (opciones.IntervaloDeSondeo + opciones.EsperaDeOrden);
        return teorica * MargenDelPlanificador;
    }

    /// <summary>
    /// Lo que puede tardar, como mucho, en volver a conectarse con un equipo que reaparece.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Mientras el equipo no esta, el reintento se va espaciando hasta
    /// <see cref="OpcionesFt710.EsperaMaximaDeReconexion"/>; en el peor caso hay que esperar ese
    /// ciclo entero mas lo que cueste un intento. Por eso el tope de espera importa: <b>es lo
    /// que tarda el cuaderno en enterarse de que el operador ha encendido la radio</b>. Medido
    /// con sondeo de 50 ms y espera de orden de 500 ms: 336 ms tras medio segundo apagada, 861
    /// ms tras cinco segundos y 4,9 s tras diez.
    /// </para>
    /// <para>
    /// Cuando se habla por el puerto serie del propio equipo hay un atajo que se salta todo
    /// esto: en cuanto reaparece el codec de audio USB, el retroceso se descarta y se reintenta
    /// en la siguiente pasada.
    /// </para>
    /// </remarks>
    /// <param name="opciones">Ajustes con los que corre el control.</param>
    /// <returns>La cota de tiempo hasta volver a estar conectado.</returns>
    public static TimeSpan TiempoMaximoDeReconexion(OpcionesFt710 opciones)
    {
        ArgumentNullException.ThrowIfNull(opciones);

        var teorica = opciones.EsperaMaximaDeReconexion + opciones.IntervaloDeSondeo + opciones.EsperaDeOrden;
        return teorica * MargenDelPlanificador;
    }

    /// <summary>
    /// Holgura sobre el tiempo teorico, porque el sondeo espera con el repartidor de tareas y
    /// con la maquina cargada las esperas se pasan de largo.
    /// </summary>
    /// <remarks>
    /// Medido en esta maquina con sondeo de 50 ms y espera de orden de 500 ms —teoricas 2,2 s—:
    /// entre 1,55 s y 1,72 s con la maquina descansada, y entre 1,97 s y 2,19 s con el doble de
    /// hilos que nucleos moliendo. La mitad de holgura cubre eso con sitio de sobra.
    /// </remarks>
    private const double MargenDelPlanificador = 1.5;

    private TimeSpan Espaciar(TimeSpan espera) =>
        espera + espera > _opciones.EsperaMaximaDeReconexion
            ? _opciones.EsperaMaximaDeReconexion
            : espera + espera;

    /// <summary>
    /// Dice si tiene sentido volver a intentar hablar con el equipo.
    /// </summary>
    /// <remarks>
    /// Si el equipo va por su cable USB y su codec de audio no aparece, la radio esta apagada:
    /// abrir el puerto una y otra vez no la va a encender. Solo se mira cuando el canal es el
    /// puerto serie del propio equipo; con un puente por red esto no dice nada.
    /// </remarks>
    private bool? ElCodecEstaPresente()
    {
        // Nulo quiere decir «aquí el códec no dice nada»: con un puente por red o con el aviso
        // desactivado, hay que reintentar a la manera clásica.
        if (!_opciones.ComprobarElCodecDeAudio || !_perfil.CodecDelFt710 || _canal is not CanalSerieCat)
        {
            return null;
        }

        if (AudioDelFt710.EquipoEncendido())
        {
            return true;
        }

        _registro.LogDebug("El códec de audio del equipo no está: la radio sigue apagada.");
        return false;
    }

    private void DarPorPerdido(Exception causa)
    {
        var porque = causa is EquipoNoContestaException
            ? "el equipo ha dejado de contestar; puede que se haya apagado"
            : causa.Message;

        _registro.LogWarning("Se ha perdido el FT-710: {Porque}", porque);
        Actualizar(estado => estado with { Conectado = false, Transmitiendo = false });
        Volatile.Write(ref _pttPedido, false);

        // Que el equipo desaparezca no puede dejar al vigilante creyendose en antena.
        ComunicacionPerdida?.Invoke(this, porque);
    }

    /// <summary>
    /// Comprueba que el equipo sigue al otro lado sin tocar nada.
    /// </summary>
    /// <remarks>
    /// Se usa mientras se transmite. <c>ID;</c> es la orden mas inofensiva que tiene el equipo y
    /// sirve de latido: si deja de contestar, es que se ha apagado o lo han desenchufado.
    /// </remarks>
    private async Task ComprobarQueSigueAhiAsync(CancellationToken ct)
    {
        var identificador = await PreguntarAsync("ID;", ct).ConfigureAwait(false);
        if (identificador is null || !identificador.StartsWith("ID", StringComparison.OrdinalIgnoreCase))
        {
            throw new EquipoNoContestaException(_canal.Descripcion);
        }
    }

    private async Task ReconectarAsync(CancellationToken ct)
    {
        await _canal.AbrirAsync(ct).ConfigureAwait(false);
        Volatile.Write(ref _canalAbiertoAlgunaVez, true);
        var identificador = await PreguntarAsync("ID;", ct).ConfigureAwait(false);
        if (identificador is null || !identificador.StartsWith("ID", StringComparison.OrdinalIgnoreCase))
        {
            throw new EquipoNoContestaException(_canal.Descripcion);
        }

        await LeerEstadoAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Baja el PTT antes de cerrar, pase lo que pase con el canal.
    /// </summary>
    /// <remarks>
    /// Si el canal se ha roto no basta con intentarlo por la via normal y anotar el fallo:
    /// cerrar con el equipo en antena es justo lo que no puede pasar. Se prueban las vias de
    /// emergencia, las mismas que usa el vigilante.
    /// <para>
    /// Pero si este control <b>nunca llego a abrir el canal</b> y no hay PTT pedido, no hay nada
    /// que bajar: recorrer las vias abriria un puerto que nadie abrio —quiza de otro programa o
    /// de otra radio— solo para mandarle <c>TX0;</c>. Pasaba al cerrar el programa sin haber
    /// pulsado «Conectar» (27-09-2026). Con PTT pedido se recorren siempre, pase lo que pase.
    /// </para>
    /// </remarks>
    /// <summary>
    /// Dice si una orden de bajar el PTT no tiene a quien llegar ni nada que bajar.
    /// </summary>
    /// <remarks>
    /// Es asi cuando el canal esta cerrado, no hay PTT pedido y, ademas, o el canal no se llego
    /// a abrir nunca, o este control ya esta desechado —y al desecharse ya bajo el PTT por todas
    /// sus vias—. Sin esto, el vigilante que se cierra despues del control chocaba con el
    /// semaforo ya liberado del canal y daba un «¡PTT PEGADO!» falso (01-10-2026). Con PTT
    /// pedido nunca se cumple: entonces se intenta bajar siempre, y si falla, se avisa.
    /// </remarks>
    private bool NoHayNadaQueBajar() =>
        !_canal.Abierto
        && !Volatile.Read(ref _pttPedido)
        && !Volatile.Read(ref _sintonizando)
        && (Volatile.Read(ref _desechado) || !Volatile.Read(ref _canalAbiertoAlgunaVez));

    private async Task BajarElPttComoSeaAsync(CancellationToken ct)
    {
        if (!_canal.Abierto
            && !Volatile.Read(ref _canalAbiertoAlgunaVez)
            && !Volatile.Read(ref _pttPedido))
        {
            _registro.LogDebug(
                "{Canal} no se llegó a abrir y no hay PTT pedido: no se toca el puerto al desconectar.",
                _canal.Descripcion);
            return;
        }

        try
        {
            if (_canal.Abierto)
            {
                await PararElManipuladorSiHaceFaltaAsync(ct).ConfigureAwait(false);
                await _canal.MandarAsync("TX0;", ct).ConfigureAwait(false);
                return;
            }
        }
        catch (Exception ex)
        {
            _registro.LogWarning(ex, "No se pudo bajar el PTT por la vía normal antes de desconectar.");
        }

        foreach (var via in ViasDeSuelta)
        {
            try
            {
                using var espera = new CancellationTokenSource(_opciones.EsperaDeOrden);
                await via.SoltarAsync(espera.Token).ConfigureAwait(false);
                _registro.LogInformation("PTT abajo antes de desconectar por «{Via}».", via.Nombre);
                return;
            }
            catch (Exception ex)
            {
                _registro.LogWarning(ex, "Falló la vía «{Via}» al bajar el PTT antes de desconectar.", via.Nombre);
            }
        }

        _registro.LogError("No se ha podido bajar el PTT antes de desconectar por ninguna vía.");
    }

    // ── ROE durante la transmision (IMedidorDeRoe) ───────────────────────────────────────────

    /// <summary>
    /// RM6 (0-255) a ROE. El manual CAT (pag. 19) da la escala cruda sin curva; esta es la de
    /// Hamlib para FTDX10/FTDX101, de la misma familia. <b>Sin calibrar en el FT-710</b>: hay que
    /// comprobarla con una carga artificial y Jose delante.
    /// </summary>
    public static IReadOnlyList<(int Crudo, double Valor)> EscalaRoe { get; } =
        [(0, 1.0), (26, 1.2), (52, 1.5), (89, 2.0), (126, 3.0), (173, 4.0), (236, 5.0), (255, 10.0)];

    /// <summary>Lectura de RM5 (potencia, 0-255) por encima de la cual se considera que hay portadora.</summary>
    public const int PotenciaCrudaConPortadora = 10;

    /// <inheritdoc />
    /// <remarks>
    /// <c>RM5;</c> (potencia) y <c>RM6;</c> (ROE), las dos «RM P1 P2P2P2 P3P3P3», y
    /// <c>RI0;</c>, cuyo P2 (cuarto caracter) es 1 con «Hi-SWR»: la alarma de ROE del propio
    /// equipo (manual CAT, pag. 19). La alarma corta aunque la escala de RM6 no este calibrada.
    /// </remarks>
    public async Task<LecturaDeRoe?> LeerRoeAsync(CancellationToken ct = default)
    {
        if (!_canal.Abierto) return null;
        var po = Medidor(await PreguntarAsync("RM5;", ct).ConfigureAwait(false), "RM5");
        var swr = Medidor(await PreguntarAsync("RM6;", ct).ConfigureAwait(false), "RM6");
        var ri = await PreguntarAsync("RI0;", ct).ConfigureAwait(false);
        var alarma = ri is { Length: > 3 } r && r.StartsWith("RI0", StringComparison.Ordinal) && r[3] == '1';
        if (po is null && swr is null && !alarma) return null;
        return new LecturaDeRoe(
            swr is { } s ? Icom.ControlIcom.Interpolar(s, [.. EscalaRoe]) : null,
            HayPotencia: po is >= PotenciaCrudaConPortadora || alarma,
            AlarmaDelEquipo: alarma);

        static int? Medidor(string? respuesta, string prefijo) =>
            respuesta is { Length: >= 6 } t && t.StartsWith(prefijo, StringComparison.Ordinal)
            && int.TryParse(t.AsSpan(3, 3), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)
                ? v
                : null;
    }

    // ── Telegrafia por el manipulador interno (IManipuladorCw) ───────────────────────────────

    /// <summary>
    /// Memoria de texto del manipulador que usa el programa para la telegrafia. Su contenido se
    /// lee antes de usarla y se vuelve a escribir al terminar cada transmision.
    /// </summary>
    public const int MemoriaDelManipulador = 5;

    /// <summary>Uno mientras el manipulador puede estar manipulando algo mandado por el programa.</summary>
    private int _manipulando;

    /// <summary>Lo que tenia la memoria del manipulador antes de usarla, o nulo si no se pudo leer.</summary>
    private string? _textoOriginalDeLaMemoria;

    /// <summary>Ya se leyo la memoria en esta transmision.</summary>
    private bool _memoriaLeida;

    /// <inheritdoc />
    public string? PorQueNoManipula =>
        !_perfil.ManipulaPorMemoriaDeTexto
            ? Textos.F("Servicios.Radio.Cw.ModeloSinManipulador", _perfil.Nombre)
            : !Estado.Conectado
                ? Textos.T("Servicios.Radio.Cw.Desconectado")
                : null;

    /// <inheritdoc />
    public int LetrasPorOrden => OrdenesFt710.LetrasPorMemoriaDelManipulador;

    /// <inheritdoc />
    public int WpmMinima => OrdenesFt710.WpmMinima;

    /// <inheritdoc />
    public int WpmMaxima => OrdenesFt710.WpmMaxima;

    /// <inheritdoc />
    public async Task PonerVelocidadAsync(int wpm, CancellationToken ct = default)
    {
        ComprobarQuePuedeManipular();
        await MandarAsync(OrdenesFt710.OrdenDeVelocidad(wpm), ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Escribe el texto en la memoria de texto <see cref="MemoriaDelManipulador"/> (<c>KM5…;</c>)
    /// y la reproduce (<c>KY05;</c>). Solo con el PTT pedido al vigilante: sin eso, se rechaza.
    /// </remarks>
    public async Task ManipularAsync(string texto, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(texto);
        ComprobarQuePuedeManipular();
        if (!Volatile.Read(ref _pttPedido) || Volatile.Read(ref _sintonizando))
        {
            throw new InvalidOperationException(Textos.T("Servicios.Radio.Cw.SinAntena"));
        }

        var limpio = OrdenesFt710.TextoParaElManipulador(texto);
        if (limpio.Length == 0) return;
        if (limpio.Length > LetrasPorOrden)
        {
            throw new ArgumentOutOfRangeException(nameof(texto), limpio.Length, $"El manipulador del FT-710 admite {LetrasPorOrden} caracteres por orden.");
        }

        await LeerLaMemoriaSiHaceFaltaAsync(ct).ConfigureAwait(false);
        Volatile.Write(ref _manipulando, 1);
        await OrdenesFt710.ConManipulacionAutorizadaAsync(async () =>
        {
            await MandarAsync(OrdenesFt710.OrdenDeEscribirTexto(MemoriaDelManipulador, limpio), ct).ConfigureAwait(false);
            await MandarAsync(OrdenesFt710.OrdenDeReproducirTexto(MemoriaDelManipulador), ct).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task PararManipuladorAsync(CancellationToken ct = default)
    {
        if (_canal.Abierto) await MandarAsync(OrdenesFt710.PararElManipulador, ct).ConfigureAwait(false);
        Volatile.Write(ref _manipulando, 0);
    }

    /// <inheritdoc />
    /// <remarks>Vuelve a dejar en la memoria del manipulador lo que tenia antes de usarla.</remarks>
    public async Task TerminarAsync(CancellationToken ct = default)
    {
        Volatile.Write(ref _manipulando, 0);
        if (!_memoriaLeida) return;
        _memoriaLeida = false;
        var original = _textoOriginalDeLaMemoria;
        _textoOriginalDeLaMemoria = null;
        if (string.IsNullOrEmpty(original) || original.Length > OrdenesFt710.LetrasPorMemoriaDelManipulador
            || original.Contains(Cat.Fin) || !_canal.Abierto)
        {
            return;
        }

        try
        {
            await OrdenesFt710.ConManipulacionAutorizadaAsync(
                () => MandarAsync(OrdenesFt710.OrdenDeEscribirTexto(MemoriaDelManipulador, original), ct)).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _registro.LogWarning(ex, "No se ha podido devolver su texto a la memoria {Memoria} del manipulador.", MemoriaDelManipulador);
        }
    }

    private void ComprobarQuePuedeManipular()
    {
        ObjectDisposedException.ThrowIf(_desechado, this);
        if (PorQueNoManipula is { } porque) throw new InvalidOperationException(porque);
    }

    /// <summary>Lee la memoria del manipulador antes de escribirla, para devolverla al terminar.</summary>
    private async Task LeerLaMemoriaSiHaceFaltaAsync(CancellationToken ct)
    {
        if (_memoriaLeida) return;
        _memoriaLeida = true;
        var consulta = string.Create(CultureInfo.InvariantCulture, $"KM{MemoriaDelManipulador};");
        var respuesta = await PreguntarAsync(consulta, ct).ConfigureAwait(false);
        var prefijo = consulta[..^1];
        _textoOriginalDeLaMemoria = respuesta is not null && respuesta.StartsWith(prefijo, StringComparison.Ordinal) && respuesta.Length > prefijo.Length
            ? respuesta[prefijo.Length..]
            : null;
    }

    /// <summary>Si el manipulador puede estar en marcha, lo para. Nunca lanza: lo que sigue es bajar el PTT.</summary>
    private async Task PararElManipuladorSiHaceFaltaAsync(CancellationToken ct)
    {
        if (Volatile.Read(ref _manipulando) == 0) return;
        try
        {
            await _canal.MandarAsync(OrdenesFt710.PararElManipulador, ct).ConfigureAwait(false);
            Volatile.Write(ref _manipulando, 0);
            _registro.LogInformation("FT-710: manipulador parado (KY00) antes de bajar el PTT.");
        }
        catch (Exception ex)
        {
            _registro.LogWarning(ex, "No se ha podido parar el manipulador (KY00) antes de bajar el PTT.");
        }
    }

    /// <summary>Lo mismo, bloqueando, para las vias sincronas de suelta.</summary>
    private void PararElManipuladorSincronoSiHaceFalta()
    {
        if (Volatile.Read(ref _manipulando) == 0) return;
        try
        {
            _canal.MandarSincrono(OrdenesFt710.PararElManipulador);
            Volatile.Write(ref _manipulando, 0);
        }
        catch (Exception ex)
        {
            _registro.LogWarning(ex, "No se ha podido parar el manipulador (KY00) por la vía síncrona.");
        }
    }

    private async Task<string?> PreguntarAsync(string orden, CancellationToken ct)
    {
        // Una espera agotada deja el canal inservible: cancelar una lectura aborta el socket, y
        // en el puerto serie pasa otro tanto. Si no se vuelve a abrir, las preguntas siguientes
        // fallan solas sin llegar al equipo, y entonces el contador de fallos no cuenta intentos
        // de verdad: cuenta el mismo canal roto tres veces.
        if (!_canal.Abierto)
        {
            await _canal.AbrirAsync(ct).ConfigureAwait(false);
            Volatile.Write(ref _canalAbiertoAlgunaVez, true);
            _registro.LogDebug("Se ha vuelto a abrir {Canal} antes de preguntar.", _canal.Descripcion);
        }

        var respuesta = await _canal.PreguntarAsync(orden, ct).ConfigureAwait(false);
        _registro.LogDebug("CAT {Orden} -> {Respuesta}", orden, respuesta ?? "(sin respuesta)");
        return respuesta;
    }

    private async Task MandarAsync(string orden, CancellationToken ct)
    {
        await _canal.MandarAsync(orden, ct).ConfigureAwait(false);
        _registro.LogDebug("CAT {Orden}", orden);
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
            // Tambien se avisa si solo ha cambiado el VFO que no se usa: el estado resumido no
            // lo recoge, pero el visor lo pinta.
            avisar = forzarAviso || anterior with { LeidoUtc = default } != nuevo with { LeidoUtc = default };
        }

        if (avisar)
        {
            EstadoCambiado?.Invoke(this, nuevo);
        }

        return nuevo;
    }
}
