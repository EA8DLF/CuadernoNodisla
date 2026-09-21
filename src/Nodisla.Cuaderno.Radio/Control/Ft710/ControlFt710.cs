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

    /// <summary>Tope de la espera entre reintentos, para no machacar el puerto si el equipo no vuelve.</summary>
    public TimeSpan EsperaMaximaDeReconexion { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Lo que se espera a que el equipo conteste cada orden.</summary>
    public TimeSpan EsperaDeOrden { get; set; } = TimeSpan.FromMilliseconds(350);

    /// <summary>Como traducir los modos entre el equipo y el cuaderno.</summary>
    public TraductorDeModos Traductor { get; set; } = TraductorDeModos.PorOmision;

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

    private CancellationTokenSource? _ctsSondeo;
    private Task? _sondeo;
    private EstadoDelEquipo _estado = EstadoDelEquipo.Desconectado;
    private bool _pttPedido;
    private bool _desechado;

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

        try
        {
            if (_canal.Abierto)
            {
                await _canal.MandarAsync("TX0;", ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _registro.LogWarning(ex, "No se pudo bajar el PTT antes de desconectar.");
        }

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

    /// <inheritdoc />
    public RangoDeMando? Rango(MandoDeEquipo mando)
    {
        lock (_candado)
        {
            if (!_mandos.Contains(mando))
            {
                return null;
            }
        }

        return MandosFt710.Buscar(mando)?.Rango;
    }

    /// <inheritdoc />
    public async Task<double?> LeerMandoAsync(MandoDeEquipo mando, CancellationToken ct = default)
    {
        var descripcion = MandosFt710.Buscar(mando);
        if (descripcion is null || Rango(mando) is null)
        {
            return null;
        }

        var respuesta = await PreguntarAsync(descripcion.OrdenDeLectura, ct).ConfigureAwait(false);
        return descripcion.Interpretar(respuesta);
    }

    /// <inheritdoc />
    /// <exception cref="NotSupportedException">
    /// Si este equipo no admite el mando o si el mando es de solo lectura.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Si el mando pone el equipo en antena y no hay una transmision en curso pedida al
    /// vigilante del PTT.
    /// </exception>
    public async Task EscribirMandoAsync(MandoDeEquipo mando, double valor, CancellationToken ct = default)
    {
        var descripcion = MandosFt710.Buscar(mando);
        var rango = Rango(mando);
        if (descripcion is null || rango is null)
        {
            throw new NotSupportedException($"Este equipo no admite el mando {mando}.");
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

        await MandarAsync(descripcion.OrdenDeEscritura(valor), ct).ConfigureAwait(false);
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

    /// <inheritdoc />
    /// <remarks>
    /// De la captura del equipo solo sale <c>MC;</c>, que dice en que memoria esta, no como leer
    /// el banco entero. Hasta tener la orden de lectura de memorias confirmada, esto devuelve la
    /// memoria en la que esta el equipo y nada mas: inventarse el formato del banco seria peor.
    /// </remarks>
    public async Task<IReadOnlyList<MemoriaDeEquipo>> LeerMemoriasAsync(CancellationToken ct = default)
    {
        var respuesta = await PreguntarAsync("MC;", ct).ConfigureAwait(false);
        if (OrdenesFt710.DiceQueNoLoAdmite(respuesta) || !respuesta!.StartsWith("MC", StringComparison.Ordinal))
        {
            return [];
        }

        if (!int.TryParse(respuesta[2..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var numero))
        {
            return [];
        }

        var estado = Estado;
        return [new MemoriaDeEquipo(numero, estado.Frecuencia, estado.Modo, null, Ocupada: true)];
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

    private async Task AveriguarCapacidadesAsync(CancellationToken ct)
    {
        var admitidos = new List<MandoDeEquipo>();
        var rechazados = new List<MandoDeEquipo>();

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
        }

        lock (_candado)
        {
            _mandos.Clear();
            foreach (var mando in admitidos)
            {
                _mandos.Add(mando);
            }
        }

        _registro.LogInformation(
            "El equipo admite {Admitidos} mandos y rechaza {Rechazados}: {Lista}.",
            admitidos.Count,
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
                    await Task.Delay(espera, ct).ConfigureAwait(false);
                    await ReconectarAsync(ct).ConfigureAwait(false);
                    perdido = false;
                    fallosSeguidos = 0;
                    espera = _opciones.EsperaDeReconexion;
                    _registro.LogInformation("El equipo ha vuelto.");
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
                    espera = espera + espera > _opciones.EsperaMaximaDeReconexion
                        ? _opciones.EsperaMaximaDeReconexion
                        : espera + espera;
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
    private const int FallosParaDarloPorPerdido = 3;

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
        var identificador = await _canal.PreguntarAsync("ID;", ct).ConfigureAwait(false);
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

    private async Task<string?> PreguntarAsync(string orden, CancellationToken ct)
    {
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
