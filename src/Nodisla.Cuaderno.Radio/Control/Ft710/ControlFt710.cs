using System.ComponentModel;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Valores;
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
    /// Usar el codec de audio USB del equipo para saber si esta encendido antes de reintentar
    /// la conexion. Solo se aplica cuando se habla por su puerto serie.
    /// </summary>
    public bool ComprobarElCodecDeAudio { get; set; } = true;

    /// <summary>
    /// Lectura del medidor S a la que se considera que hay S9. Segun la captura el medidor va
    /// de 0 a 255; el reparto de esa escala en unidades S es aproximado y por eso se deja aqui.
    /// </summary>
    public int LecturaDeS9 { get; set; } = 128;
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
public sealed class ControlFt710 : IEquipoAvanzado, IPttDirecto, ISueltaDeEmergenciaPtt, IAvisaDePerdidaDeComunicacion
{
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
    private bool _pttPedido;
    private bool _desechado;
    private string? _modoDelEquipo;

    /// <summary>Crea el control sobre un canal CAT ya construido.</summary>
    /// <param name="canal">Canal por el que se habla con el equipo.</param>
    /// <param name="opciones">Ajustes.</param>
    /// <param name="registro">Donde anotar lo que pasa.</param>
    public ControlFt710(ICanalCat canal, OpcionesFt710? opciones = null, ILogger? registro = null)
    {
        ArgumentNullException.ThrowIfNull(canal);

        _canal = canal;
        _opciones = opciones ?? new OpcionesFt710();
        _registro = registro ?? NullLogger.Instance;

        ViasDeSuelta =
        [
            new ViaDeSuelta(
                "FT-710: TX0 por el canal abierto",
                async ct => await _canal.MandarAsync("TX0;", ct).ConfigureAwait(false),
                () => _canal.MandarSincrono("TX0;")),
            new ViaDeSuelta(
                "FT-710: reabrir el puerto y TX0",
                async ct =>
                {
                    await _canal.AbrirAsync(ct).ConfigureAwait(false);
                    await _canal.MandarAsync("TX0;", ct).ConfigureAwait(false);
                }),
        ];
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
                $"Por {_canal.Descripcion} contesta algo que no es un equipo Yaesu: «{identificador}».");
        }

        var codigo = identificador[2..].Trim();
        if (codigo != IdentificadorFt710)
        {
            _registro.LogWarning(
                "El equipo de {Canal} contesta ID{Codigo}, que no es un FT-710 (ID{Esperado}).",
                _canal.Descripcion,
                codigo,
                IdentificadorFt710);
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
        Actualizar(estado => estado with { Conectado = false, Transmitiendo = false });
    }

    /// <inheritdoc />
    public async Task PonerFrecuenciaAsync(Frecuencia frecuencia, CancellationToken ct = default)
    {
        var hercios = frecuencia.Hercios;
        if (hercios is < 0 or > 999_999_999)
        {
            throw new ArgumentOutOfRangeException(
                nameof(frecuencia),
                frecuencia,
                "El FT-710 solo admite frecuencias de nueve cifras en hercios.");
        }

        var vfoDeTrabajo = Estado.Vfo == "VFO B" ? "FB" : "FA";
        await MandarAsync(
            string.Create(CultureInfo.InvariantCulture, $"{vfoDeTrabajo}{hercios:D9};"),
            ct).ConfigureAwait(false);
        await LeerEstadoAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task PonerModoAsync(Modo modo, CancellationToken ct = default)
    {
        var nombre = _opciones.Traductor.AlEquipo(modo, Estado.Frecuencia)
            ?? throw new ArgumentException($"No sé cómo pedirle al FT-710 el modo {modo}.", nameof(modo));

        var codigo = ModosFt710.AlEquipo(nombre)
            ?? throw new ArgumentException($"El FT-710 no tiene el modo {nombre}.", nameof(modo));

        await MandarAsync($"MD0{codigo};", ct).ConfigureAwait(false);
        await LeerEstadoAsync(ct).ConfigureAwait(false);
    }

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
        await _canal.MandarAsync(transmitir ? "TX1;" : "TX0;", ct).ConfigureAwait(false);
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

        var descripcion = MandosFt710.Buscar(mando);
        if (descripcion is null)
        {
            return null;
        }

        return mando == MandoDeEquipo.AnchoDeFiltro
            ? RangoDelAnchoDeFiltro(descripcion.Rango)
            : descripcion.Rango;
    }

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
        return indice is null
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
        var descripcion = MandosFt710.Buscar(mando);
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
        var descripcion = MandosFt710.Buscar(mando);
        var rango = Rango(mando, vfo);
        if (descripcion is null || rango is null)
        {
            throw new NotSupportedException($"Este equipo no admite el mando {mando} en el VFO {vfo}.");
        }

        if (rango.SoloLectura)
        {
            throw new NotSupportedException($"El mando {mando} se puede leer pero no accionar.");
        }

        if (rango.TransmiteAlAccionar && !Volatile.Read(ref _pttPedido))
        {
            // Sintonizar el acoplador emite portadora. Si no hay una transmision pedida al
            // vigilante, no hay nadie que suelte el PTT si esto se atasca: no se acciona.
            throw new InvalidOperationException(
                $"El mando {mando} pone el equipo en antena: hay que pedir antes una transmisión a "
                + "IVigilantePtt y accionarlo dentro de ella.");
        }

        await MandarAsync(descripcion.OrdenDeEscritura(valor, vfo), ct).ConfigureAwait(false);
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
        foreach (var (orden, nombre) in MedidoresCrudos)
        {
            var respuesta = await PreguntarAsync($"{orden};", ct).ConfigureAwait(false);
            if (OrdenesFt710.DiceQueNoLoAdmite(respuesta) || !respuesta!.StartsWith(orden, StringComparison.Ordinal))
            {
                continue;
            }

            var cifras = respuesta[orden.Length..];
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

        for (var canal = 1; canal <= MemoriasDelEquipo; canal++)
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

            var etiqueta = await LeerRotuloDeMemoriaAsync(canal, ct).ConfigureAwait(false);
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
        if (!respuesta.StartsWith("MR", StringComparison.OrdinalIgnoreCase) || respuesta.Length < 22)
        {
            return false;
        }

        var cuerpo = respuesta[2..];
        if (!int.TryParse(cuerpo[..3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var canal)
            || !long.TryParse(cuerpo[3..12], NumberStyles.Integer, CultureInfo.InvariantCulture, out var hercios))
        {
            return false;
        }

        var modo = Modo.Vacio;
        if (cuerpo.Length > 19 && ModosFt710.DesdeElEquipo(cuerpo[19]) is { } nombre)
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
    public async Task IrAMemoriaAsync(int numero, CancellationToken ct = default)
    {
        if (numero is < 1 or > 999)
        {
            throw new ArgumentOutOfRangeException(nameof(numero), numero, "Las memorias del FT-710 van de 1 a 999.");
        }

        await MandarAsync(string.Create(CultureInfo.InvariantCulture, $"MC{numero:D3};"), ct).ConfigureAwait(false);
        await LeerEstadoAsync(ct).ConfigureAwait(false);
    }

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
    public async Task<EstadoDelEquipo> LeerEstadoAsync(CancellationToken ct = default)
    {
        var anterior = Estado;
        var vfoActivo = await PreguntarAsync("VS;", ct).ConfigureAwait(false);
        if (vfoActivo is null)
        {
            // El canal sigue abierto pero el equipo ha dejado de contestar: se ha apagado o
            // lo han desenchufado. Quien llama decide, pero enterarse es obligatorio.
            throw new EquipoNoContestaException(_canal.Descripcion);
        }

        var enB = vfoActivo is { Length: >= 3 } && vfoActivo[2] == '1';

        var frecuenciaActiva = await LeerFrecuenciaAsync(enB ? "FB" : "FA", ct).ConfigureAwait(false)
            ?? anterior.Frecuencia;

        var split = await PreguntarAsync("ST;", ct).ConfigureAwait(false);
        var haySplit = split is { Length: >= 3 } && split[2] != '0';

        Frecuencia? frecuenciaRx = null;
        var frecuenciaDeTrabajo = frecuenciaActiva;
        if (haySplit)
        {
            // Con dos frecuencias, el cuaderno apunta la de transmision y guarda la de escucha.
            var otra = await LeerFrecuenciaAsync(enB ? "FA" : "FB", ct).ConfigureAwait(false);
            if (otra is not null)
            {
                frecuenciaRx = frecuenciaActiva;
                frecuenciaDeTrabajo = otra.Value;
            }
        }

        var modo = anterior.Modo;
        var respuestaModo = await PreguntarAsync(enB ? "MD1;" : "MD0;", ct).ConfigureAwait(false);
        if (respuestaModo is { Length: >= 4 })
        {
            var nombre = ModosFt710.DesdeElEquipo(respuestaModo[3]);
            if (nombre is not null)
            {
                modo = _opciones.Traductor.DesdeElEquipo(nombre);

                // El ancho de filtro solo se puede dar en hercios sabiendo el modo.
                Volatile.Write(ref _modoDelEquipo, nombre);
            }
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
        });
    }

    private static readonly (string Orden, string Nombre)[] MedidoresCrudos =
    [
        ("RM1", "señal"),
        ("RM3", "compresión"),
        ("RM4", "control automático de nivel"),
        ("RM5", "potencia"),
        ("RM6", "relación de onda estacionaria"),
        ("RM7", "corriente"),
    ];

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

        foreach (var descripcion in MandosFt710.Todos)
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
            if (!OrdenesFt710.DiceQueNoLoAdmite(respuestaDelSegundo))
            {
                admitidosEnElSegundo.Add(descripcion.Mando);
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

    private async Task<Frecuencia?> LeerFrecuenciaAsync(string orden, CancellationToken ct)
    {
        var respuesta = await PreguntarAsync($"{orden};", ct).ConfigureAwait(false);
        if (OrdenesFt710.DiceQueNoLoAdmite(respuesta) || respuesta!.Length < 3)
        {
            return null;
        }

        var cifras = respuesta[2..];
        return long.TryParse(cifras, NumberStyles.Integer, CultureInfo.InvariantCulture, out var hercios)
            ? Frecuencia.DesdeHercios(hercios)
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
        if (!_opciones.ComprobarElCodecDeAudio || _canal is not CanalSerieCat)
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
    /// </remarks>
    private async Task BajarElPttComoSeaAsync(CancellationToken ct)
    {
        try
        {
            if (_canal.Abierto)
            {
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

    private async Task<string?> PreguntarAsync(string orden, CancellationToken ct)
    {
        // Una espera agotada deja el canal inservible: cancelar una lectura aborta el socket, y
        // en el puerto serie pasa otro tanto. Si no se vuelve a abrir, las preguntas siguientes
        // fallan solas sin llegar al equipo, y entonces el contador de fallos no cuenta intentos
        // de verdad: cuenta el mismo canal roto tres veces.
        if (!_canal.Abierto)
        {
            await _canal.AbrirAsync(ct).ConfigureAwait(false);
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

        if (avisar)
        {
            EstadoCambiado?.Invoke(this, nuevo);
        }

        return nuevo;
    }
}
