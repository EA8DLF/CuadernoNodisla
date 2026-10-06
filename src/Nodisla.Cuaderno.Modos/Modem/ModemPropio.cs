using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nodisla.Cuaderno.Idiomas;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Modos.Ft8;
using Nodisla.Cuaderno.Modos.Ldpc;
using Nodisla.Cuaderno.Modos.Fst4;
using Nodisla.Cuaderno.Modos.Marco;
using Nodisla.Cuaderno.Modos.Msk144;
using Nodisla.Cuaderno.Modos.Q65;
using Nodisla.Cuaderno.Modos.Senal;
using Nodisla.Cuaderno.Modos.Wspr;

namespace Nodisla.Cuaderno.Modos.Modem;

/// <summary>
/// El modem propio de modos digitales: escucha, decodifica, pinta la cascada y emite.
/// </summary>
/// <remarks>
/// <para>
/// Junta el decodificador con el audio del equipo y con el reloj corregido, y saca por el puerto
/// lo mismo que sacaba el puente con los programas de fuera, para que la pantalla no note la
/// diferencia.
/// </para>
/// <para>
/// <b>Transmitir es imposible por descuido.</b> Este modem no sabe abrir una tarjeta de sonido
/// ni accionar un PTT: le tienen que entregar hechos una salida de audio y un vigilante de PTT.
/// Si no se los dan, <see cref="EmitirAsync"/> se niega en redondo en vez de apanarselas. No es
/// un detalle de diseno: emitir un periodo de FT8 son trece segundos con el equipo en antena, y
/// un modem que pudiera hacerlo «por si acaso» acabaria haciendolo alguna vez.
/// </para>
/// <para>
/// <b>El reloj manda.</b> Las ventanas no se cuentan desde que arranco el programa sino desde el
/// reloj corregido, porque FT8 esta alineado al reloj universal. Cada bloque de audio trae su
/// instante y con el se sabe a que ventana pertenece; asi da igual que la captura se retrase o
/// que se pierda un bloque.
/// </para>
/// </remarks>
public sealed class ModemPropio : IModemPropio
{
    /// <summary>
    /// Segundos de audio anteriores al comienzo de la ventana que se guardan.
    /// </summary>
    /// <remarks>
    /// Hay quien transmite adelantado, por tener el reloj mal o por una propagacion larguisima.
    /// Sin este preludio, esas senales caerian fuera del margen de busqueda y se perderian sin
    /// que nada lo explicara.
    /// </remarks>
    public const double SegundosDePreludio = 1.0;

    /// <summary>
    /// Fraccion del periodo a partir de la que se lanza la decodificacion de adelanto, en los
    /// modos que la soportan (<see cref="IModoDigital.SoportaDecodificacionProgresiva"/>).
    /// </summary>
    /// <remarks>
    /// Ochenta y cinco por ciento, igual de orden de magnitud que las pasadas progresivas de
    /// JTDX (a los 11,8 s de una ventana de 15 s son el 79 %; a los 13,5 s, el 90 %): bastante
    /// tarde para que la señal ya este casi entera en el audio acumulado, y bastante pronto para
    /// que quede un hueco de verdad antes de que la ventana cierre.
    /// </remarks>
    public const double FraccionDelPrefijoProgresivo = 0.85;

    /// <summary>Muestras de cada columna de la cascada.</summary>
    private const int MuestrasDeCascada = 16384;

    /// <summary>Muestras que avanza la cascada entre columna y columna.</summary>
    private const int PasoDeCascada = 8192;

    /// <summary>Frecuencia mas alta que se pinta en la cascada, en hercios.</summary>
    private const double TopeDeLaCascadaHz = 4000;

    private readonly IEntradaDeAudio? _entrada;
    private readonly ISalidaDeAudio? _salida;
    private readonly IVigilantePtt? _vigilante;
    private readonly IRelojDelModem _reloj;
    private readonly RegistroDeModos _modos;
    private readonly ILogger _registro;
    private readonly SemaphoreSlim _cerrojoDeEmision = new(1, 1);

    private Channel<BloqueDeAudio>? _cola;
    private Task? _tarea;
    private CancellationTokenSource? _paradaDeEscucha;
    private CancellationTokenSource? _paradaDeEmision;

    private readonly List<float> _acumuladoDeCascada = [];
    private int _frecuenciaDeLaCascada;
    private double _nivelDeSalida = IModoDigital.AmplitudDeSalidaPorDefecto;

    /// <summary>Crea el modem.</summary>
    /// <param name="tablas">Tablas del protocolo.</param>
    /// <param name="reloj">Reloj corregido que dice donde caen las ventanas.</param>
    /// <param name="entrada">Captura de audio del equipo; sin ella solo se pueden leer ficheros.</param>
    /// <param name="salida">
    /// Reproduccion hacia el equipo. <b>Sin ella el modem no puede emitir</b>, que es como debe
    /// estar mientras no se quiera transmitir de verdad.
    /// </param>
    /// <param name="vigilante">Vigilante de PTT; sin el tampoco se puede emitir.</param>
    /// <param name="registro">Para dejar constancia.</param>
    /// <param name="modos">Modos que sabe hacer. Si no se da, los de serie (<see cref="ModosDeSerie"/>).</param>
    public ModemPropio(
        TablasDelProtocolo tablas,
        IRelojDelModem reloj,
        IEntradaDeAudio? entrada = null,
        ISalidaDeAudio? salida = null,
        IVigilantePtt? vigilante = null,
        ILogger<ModemPropio>? registro = null,
        RegistroDeModos? modos = null)
    {
        ArgumentNullException.ThrowIfNull(tablas);
        ArgumentNullException.ThrowIfNull(reloj);
        _registro = registro ?? (ILogger)NullLogger.Instance;
        _reloj = reloj;
        _entrada = entrada;
        _salida = salida;
        _vigilante = vigilante;
        _modos = modos ?? ModosDeSerie(tablas, _registro);
        Tablas = tablas;

        if (!tablas.EsElCodigoReal)
            _registro.LogWarning(
                "El módem trabaja con el código corrector de pruebas: funciona consigo mismo pero no decodifica a otras estaciones. Falta la tabla del LDPC(174,91).");
    }

    /// <summary>Tablas del protocolo con las que trabaja.</summary>
    public TablasDelProtocolo Tablas { get; }

    /// <inheritdoc/>
    /// <remarks>
    /// No se guarda por modo: se fija al que este en uso justo antes de generar cada señal
    /// (<see cref="EmitirAsync"/>, <see cref="GuardarEmisionEnFichero"/>), asi que un cambio de
    /// nivel se nota en la siguiente emision sin tener que reiniciar nada.
    /// </remarks>
    public double NivelDeSalida
    {
        get => _nivelDeSalida;
        // Igual que exige Modulador.Sintetizar de cada modo: por encima de cero y hasta 1.
        set => _nivelDeSalida = Math.Clamp(value, 0.01, 1.0);
    }

    /// <inheritdoc/>
    public EstadoDeLasTablas EstadoDeLasTablas => new(Tablas.EsElCodigoReal, Tablas.Procedencia);

    /// <summary>
    /// Los modos de serie, los nueve: FT8, FT4, WSPR, JT65 (A), JT9, Q65 (60A), MSK144, FST4 (60 s) y FST4W (120 s).
    /// </summary>
    /// <remarks>
    /// <para>
    /// FT8 y FT4 comparten catalogo de indicativos. Q65 carga sus propias tablas; si faltan,
    /// trabaja con un codigo de pruebas y lo dice por el registro, igual que FT8.
    /// </para>
    /// <para>
    /// <b>Un modo nuevo</b> se da de alta aqui con una linea, cuando su banco este en verde. Lo que
    /// no se registre no aparece en el desplegable.
    /// </para>
    /// </remarks>
    /// <param name="tablas">Tablas del LDPC de FT8 y FT4.</param>
    /// <param name="registro">Registro.</param>
    public static RegistroDeModos ModosDeSerie(TablasDelProtocolo tablas, ILogger? registro = null)
    {
        ArgumentNullException.ThrowIfNull(tablas);
        var catalogo = new CatalogoDeIndicativos();
        var modos = new RegistroDeModos()
            .Anadir(new ModoFt8(ModoDelModem.Ft8, tablas, catalogo, registro))
            .Anadir(new ModoFt8(ModoDelModem.Ft4, tablas, catalogo, registro))
            .Anadir(new ModoWspr(registro))
            .Anadir(new Jt65.ModoJt65(registro: registro))
            .Anadir(new Jt9.ModoJt9(registro))
            .Anadir(new ModoQ65(ParametrosDeQ65.De(60, SubmodoDeQ65.A), TablasDeQ65.Cargar(registro: registro), registro))
            // MSK144 decodifica aqui por ventana completa. Los pings en tiempo real
            // (ModoMsk144.DecodificarTrozo) aun no estan enganchados al modem: pendiente.
            .Anadir(new ModoMsk144(
                TablaLdpc.Cargar(ParametrosMsk144.FicheroDeTablas, 128, 90),
                TablaLdpc.Cargar(ParametrosMsk144.FicheroDeTablasCortas, 32, 16),
                registro))
            // FST4 a 60 s y FST4W a 120 s. El selector de periodo (ModoFst4.CambiarPeriodo)
            // aun no llega a la pantalla: pendiente.
            .Anadir(new ModoFst4(TablaLdpc.Cargar(ParametrosFst4.FicheroDeTablas, 240, 101), esFst4w: false, periodoSegundos: 60, registro))
            .Anadir(new ModoFst4(TablaLdpc.Cargar(ParametrosFst4.FicheroDeTablasFst4w, 240, 74), esFst4w: true, periodoSegundos: 120, registro));
        return modos;
    }

    /// <summary>Modos que sabe hacer este modem, tal y como estan en su registro.</summary>
    public RegistroDeModos Modos => _modos;

    /// <inheritdoc/>
    public IReadOnlyList<ModoDelModem> ModosDisponibles => _modos.Disponibles;

    /// <summary>Catalogo de indicativos que va aprendiendo FT8 y FT4 mientras escucha.</summary>
    public CatalogoDeIndicativos Catalogo =>
        _modos.TryObtener(ModoDelModem.Ft8, out var ft8) && ft8 is ModoFt8 m ? m.Catalogo : new CatalogoDeIndicativos();

    /// <inheritdoc/>
    public ModoDelModem Modo { get; private set; } = ModoDelModem.Ft8;

    /// <inheritdoc/>
    public bool EstaEscuchando => _tarea is { IsCompleted: false };

    /// <inheritdoc/>
    public bool EstaEmitiendo { get; private set; }

    /// <inheritdoc/>
    public Frecuencia FrecuenciaDelDial { get; set; }

    /// <inheritdoc/>
    public void FijarPistaDeQso(string miIndicativo, string dxCall)
    {
        if (_modos.TryObtener(Modo, out var implementacion)) implementacion.PistaDeQso = new PistaDeQso(miIndicativo, dxCall);
    }

    /// <inheritdoc/>
    public event EventHandler<ColumnaDeCascada>? CascadaActualizada;

    /// <inheritdoc/>
    public event EventHandler<VentanaDecodificada>? VentanaLista;

    /// <inheritdoc/>
    public async Task EscucharAsync(ModoDelModem modo, CancellationToken ct = default)
    {
        if (_entrada is null)
            throw new InvalidOperationException("Este módem no tiene entrada de audio: solo puede decodificar ficheros.");
        var implementacion = ModoRegistrado(modo);
        await PararAsync(ct).ConfigureAwait(false);

        Modo = modo;
        implementacion.Reiniciar();
        _acumuladoDeCascada.Clear();
        _cola = Channel.CreateUnbounded<BloqueDeAudio>(new UnboundedChannelOptions { SingleReader = true });
        _paradaDeEscucha = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _entrada.BloqueCapturado += AlLlegarUnBloque;
        _tarea = Task.Run(() => MolerAsync(implementacion, _paradaDeEscucha.Token), CancellationToken.None);

        _registro.LogInformation("Módem propio escuchando en {Modo}.", modo);
    }

    /// <inheritdoc/>
    public async Task PararAsync(CancellationToken ct = default)
    {
        if (_entrada is not null) _entrada.BloqueCapturado -= AlLlegarUnBloque;
        _cola?.Writer.TryComplete();
        if (_paradaDeEscucha is not null) await _paradaDeEscucha.CancelAsync().ConfigureAwait(false);

        if (_tarea is not null)
        {
            try { await _tarea.WaitAsync(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { /* Se estaba parando; es lo esperado. */ }
            catch (TimeoutException) { _registro.LogWarning("La tarea de decodificación no terminó a tiempo."); }
        }

        _paradaDeEscucha?.Dispose();
        _paradaDeEscucha = null;
        _tarea = null;
        _cola = null;
    }

    private void AlLlegarUnBloque(object? origen, BloqueDeAudio bloque) => _cola?.Writer.TryWrite(bloque);

    /// <summary>
    /// Va juntando los bloques de audio, corta por ventanas y decodifica cada una.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Se trabaja sobre una copia propia del audio y no sobre los bloques que llegan: quien
    /// captura puede reutilizar sus vectores en cuanto suelta el evento, y leer de ahi mas tarde
    /// daria audio revuelto sin que nada avisara.
    /// </para>
    /// <para>
    /// <b>Dos adelantos sobre el disparo de antes, los dos conservadores.</b> El primero: antes,
    /// la ventana no se daba por cerrada hasta que llegaba un bloque de la <i>siguiente</i>, asi
    /// que si el bloque dura, pongamos, cien milisegundos, esos cien milisegundos se perdian sin
    /// motivo entre que la ventana termina de verdad y que se nota. Ahora se nota en cuanto lo
    /// acumulado ya cubre el periodo entero, sin esperar a que la pinte un bloque de mas alla.
    /// </para>
    /// <para>
    /// El segundo, solo para los modos que lo piden (<see cref="IModoDigital.SoportaDecodificacionProgresiva"/>,
    /// hoy FT8 y FT4): al llegar a <see cref="FraccionDelPrefijoProgresivo"/> de la ventana se
    /// lanza, en segundo plano y sin esperarla, una decodificacion de adelanto sobre lo
    /// acumulado hasta ese instante. No decide nada por su cuenta ni se anuncia por
    /// <see cref="VentanaLista"/> —eso lo sigue haciendo, una sola vez por ventana, la
    /// decodificacion completa de siempre, que es la unica que cuenta para la fiabilidad y para
    /// quien escucha el evento—; lo que hace es adelantar el trabajo caro (demodular y corregir
    /// con el LDPC) al hueco de CPU que, si no, se queda ocioso mientras llegan los ultimos
    /// segundos de la ventana, y de paso le deja al catalogo de indicativos lo que haya podido
    /// resolver antes de que le toque el turno a la decodificacion completa. Por eso se espera a
    /// que termine (si no ha terminado ya) justo antes de esa decodificacion completa: las dos
    /// comparten el mismo decodificador del modo y no pueden correr a la vez.
    /// </para>
    /// </remarks>
    private async Task MolerAsync(IModoDigital modo, CancellationToken ct)
    {
        var periodo = modo.Periodo;
        var lector = _cola!.Reader;

        var audio = new List<float>();
        var frecuencia = 0;
        DateTimeOffset? instanteDelPrimero = null;
        DateTimeOffset? ventanaEnCurso = null;
        Task? progresivaEnCurso = null;
        var progresivaYaLanzada = false;

        try
        {
            await foreach (var bloque in lector.ReadAllAsync(ct).ConfigureAwait(false))
            {
                if (frecuencia == 0)
                {
                    frecuencia = bloque.FrecuenciaDeMuestreo;
                    _frecuenciaDeLaCascada = frecuencia;
                }
                instanteDelPrimero ??= bloque.InstanteUtc;
                audio.AddRange(bloque.Muestras.Span);
                PintarCascada(bloque);

                var ventana = IModoDigital.ComienzoDeVentana(bloque.InstanteUtc, periodo, modo.ArranqueDentroDelPeriodo);
                ventanaEnCurso ??= ventana;

                // Cuanto se lleva ya de la ventana en curso, contando desde su comienzo: el
                // preludio que se conservo de la ventana anterior no cuenta como suyo.
                var finDeLoAcumulado = instanteDelPrimero.Value.AddSeconds((double)audio.Count / frecuencia);
                var acumuladoDeEstaVentana = finDeLoAcumulado - ventanaEnCurso.Value;

                if (modo.SoportaDecodificacionProgresiva && !progresivaYaLanzada && progresivaEnCurso is null
                    && acumuladoDeEstaVentana.Ticks >= periodo.Ticks * FraccionDelPrefijoProgresivo)
                {
                    progresivaYaLanzada = true;
                    progresivaEnCurso = LanzarDecodificacionProgresiva(modo, audio, frecuencia, instanteDelPrimero.Value, ventanaEnCurso.Value, ct);
                }

                // Cambio de ventana, o lo acumulado ya cubre el periodo entero sin necesitar un
                // bloque de la siguiente para notarlo.
                var cambioDeVentana = ventana != ventanaEnCurso.Value;
                if (!cambioDeVentana && acumuladoDeEstaVentana < periodo) continue;

                var ventanaQueCierra = ventanaEnCurso.Value;
                // Si hubo un hueco de audio tan grande como para saltarse una ventana entera, se
                // salta a la que diga el bloque que acaba de llegar; si no, es sencillamente la
                // siguiente de la que se estaba llenando.
                var proximaVentana = cambioDeVentana ? ventana : ventanaQueCierra + periodo;

                // La decodificacion de adelanto y la completa comparten el decodificador del
                // modo: no pueden correr a la vez, asi que si la de adelanto sigue viva se espera
                // aqui, justo antes de necesitarlo de verdad.
                if (progresivaEnCurso is not null)
                {
                    await progresivaEnCurso.ConfigureAwait(false);
                    progresivaEnCurso = null;
                }

                // Lo acumulado ya contiene la ventana que cierra entera.
                await DecodificarLoAcumuladoAsync(modo, audio, frecuencia, instanteDelPrimero.Value, ventanaQueCierra, ct)
                    .ConfigureAwait(false);

                // Se conserva el preludio de la ventana nueva y se tira lo demas.
                var conservar = (int)(SegundosDePreludio * frecuencia);
                var sobran = Math.Max(0, audio.Count - conservar);
                if (sobran > 0)
                {
                    audio.RemoveRange(0, sobran);
                    instanteDelPrimero = instanteDelPrimero.Value.AddSeconds((double)sobran / frecuencia);
                }
                ventanaEnCurso = proximaVentana;
                progresivaYaLanzada = false;
            }
        }
        catch (OperationCanceledException)
        {
            // Se pidio parar. No es un error.
        }
        catch (Exception ex)
        {
            _registro.LogError(ex, "La decodificación se paró por un fallo inesperado.");
        }
    }

    /// <summary>
    /// Lanza en segundo plano una decodificacion de adelanto sobre el audio acumulado hasta
    /// ahora, sin que nadie la espere todavia.
    /// </summary>
    /// <remarks>
    /// Se le pasa una copia del audio acumulado (<see cref="List{T}.ToArray"/>): la lista sigue
    /// creciendo en el hilo de <see cref="MolerAsync"/> mientras esta tarea trabaja, y leer de
    /// ella a la vez que se escribe daria, en el mejor de los casos, una excepcion, y en el peor,
    /// audio revuelto. No se anuncia nada con lo que salga de aqui: su unico efecto util, aparte
    /// de adelantar el trabajo caro, es lo que <see cref="IModoDigital.DecodificarVentana"/> deje
    /// aprendido en el catalogo de indicativos del modo, que si es compartido y si le sirve
    /// luego a la decodificacion completa.
    /// </remarks>
    private Task LanzarDecodificacionProgresiva(
        IModoDigital modo, List<float> audio, int frecuencia,
        DateTimeOffset instanteDelPrimero, DateTimeOffset ventana, CancellationToken ct)
    {
        var prefijo = audio.ToArray();
        var desfase = (instanteDelPrimero - ventana).TotalSeconds;
        return Task.Run(() =>
        {
            if (ct.IsCancellationRequested) return;
            try
            {
                modo.DecodificarVentana(prefijo, frecuencia, ventana, desfase, ct);
            }
            catch (OperationCanceledException)
            {
                // Se pidio parar mientras adelantaba. No es un error: la completa no llegara a
                // lanzarse si la parada ya esta en marcha.
            }
            catch (Exception ex)
            {
                _registro.LogDebug(
                    ex, "La decodificación de adelanto de la ventana {Ventana} falló; no afecta a la decodificación completa.", ventana);
            }
        }, CancellationToken.None);
    }

    private async Task DecodificarLoAcumuladoAsync(
        IModoDigital modo, List<float> audio, int frecuencia,
        DateTimeOffset instanteDelPrimero, DateTimeOffset ventana, CancellationToken ct)
    {
        var muestras = audio.ToArray();
        var desfase = (instanteDelPrimero - ventana).TotalSeconds;

        var (decodificaciones, duracion) = await Task.Run(() =>
        {
            var reloj = System.Diagnostics.Stopwatch.StartNew();
            var lista = modo.DecodificarVentana(muestras, frecuencia, ventana, desfase, ct);
            return (lista, reloj.Elapsed);
        }, ct).ConfigureAwait(false);

        // Si la ventana costo mas que el propio periodo, la siguiente ya empezo tarde y se han
        // perdido decodificaciones. El operador tiene que verlo, no es una estadistica interna.
        var llegoTarde = duracion > modo.Periodo;
        if (llegoTarde)
            _registro.LogWarning(
                "La ventana {Ventana} tardó {Segundos:0.0} s, más que el propio periodo: el ordenador no da abasto.",
                ventana, duracion.TotalSeconds);

        VentanaLista?.Invoke(this, new VentanaDecodificada(
            ventana, decodificaciones, duracion, EnDecibelios(decodificaciones))
        {
            LlegoTarde = llegoTarde,
        });
    }

    private static double EnDecibelios(IReadOnlyList<DecodificacionPropia> decodificaciones) =>
        decodificaciones.Count == 0 ? -120 : decodificaciones.Min(d => d.Decibelios) - 10;

    /// <summary>El modo pedido, o un error claro si no esta registrado.</summary>
    private IModoDigital ModoRegistrado(ModoDelModem modo) =>
        _modos.TryObtener(modo, out var implementacion)
            ? implementacion
            : throw new ArgumentOutOfRangeException(nameof(modo), modo, $"El módem propio no sabe hacer {modo}: no está registrado.");

    /// <summary>Calcula y saca las columnas de cascada que quepan con lo que hay acumulado.</summary>
    private void PintarCascada(BloqueDeAudio bloque)
    {
        if (CascadaActualizada is null) return;
        _acumuladoDeCascada.AddRange(bloque.Muestras.Span);

        while (_acumuladoDeCascada.Count >= MuestrasDeCascada)
        {
            var trozo = CollectionsMarshalSpan(_acumuladoDeCascada)[..MuestrasDeCascada];
            var casillas = (MuestrasDeCascada / 2) + 1;
            var magnitudes = new float[casillas];
            Fft.MagnitudesDeSenalReal(trozo, MuestrasDeCascada, magnitudes);

            var hzPorCasilla = (double)_frecuenciaDeLaCascada / MuestrasDeCascada;
            var utiles = Math.Min(casillas, (int)(TopeDeLaCascadaHz / hzPorCasilla) + 1);
            var enDecibelios = new float[utiles];
            for (var i = 0; i < utiles; i++)
                enDecibelios[i] = 20f * MathF.Log10(magnitudes[i] + 1e-12f);

            CascadaActualizada.Invoke(this, new ColumnaDeCascada(enDecibelios, hzPorCasilla, bloque.InstanteUtc));
            _acumuladoDeCascada.RemoveRange(0, PasoDeCascada);
        }
    }

    private static Span<float> CollectionsMarshalSpan(List<float> lista) =>
        System.Runtime.InteropServices.CollectionsMarshal.AsSpan(lista);

    /// <inheritdoc/>
    public async Task EmitirAsync(string texto, int tonoHz, CancellationToken ct = default)
    {
        // Sin salida de audio y sin vigilante no se emite. No se busca una alternativa ni se
        // avisa con un registro y se sigue: se para aqui.
        if (_salida is null || _vigilante is null)
            throw new InvalidOperationException(
                Textos.T("Servicios.Modos.NoPuedeEmitir"));
        var modo = ModoRegistrado(Modo);

        // La señal se sintetiza a la misma frecuencia con la que se abrió la salida —casi
        // siempre 48.000, pero el operador puede haber puesto otra en los ajustes—. Antes aquí
        // había un 48.000 fijo: si la salida se abría a otra frecuencia (por ejemplo 96.000), la
        // salida remuestreaba esta señal como si fuera de esa frecuencia cuando en realidad era
        // de 48.000, y el resultado era un destrozo de aliasing —el equipo transmitía, pero en
        // vez de un tono limpio salían golpes de ruido—.
        var frecuenciaDeSalida = _salida.FrecuenciaDeMuestreo;
        // Igual que la frecuencia de salida, el nivel se lee en el momento de generar: si el
        // operador lo ha tocado en los ajustes desde la ultima emision, esta ya sale con el nuevo.
        modo.AmplitudDeSalida = _nivelDeSalida;
        float[] mensaje;
        try
        {
            mensaje = modo.Generar(texto, tonoHz, frecuenciaDeSalida);
        }
        catch (FormatException ex)
        {
            throw new ArgumentException(ex.Message, nameof(texto), ex);
        }

        // La senal empieza por convenio un rato despues del comienzo de la ventana: medio
        // segundo en FT8 y FT4, uno en WSPR y en Q65 largo. Si se llama al principio de la
        // ventana, que es lo que hace la pantalla, se antepone ese silencio para que la primera
        // muestra salga donde el otro lado la espera. Si se llama tarde, no se inventa nada: sale
        // en cuanto se puede.
        var ventana = IModoDigital.ComienzoDeVentana(_reloj.Ahora, modo.Periodo, modo.ArranqueDentroDelPeriodo);
        var espera = ventana + modo.ComienzoDeLaSenal - _reloj.Ahora;
        var silencio = espera > TimeSpan.Zero ? (int)Math.Round(espera.TotalSeconds * frecuenciaDeSalida) : 0;
        var senal = new float[silencio + mensaje.Length];
        mensaje.CopyTo(senal, silencio);

        await _cerrojoDeEmision.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _paradaDeEmision = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var testigo = _paradaDeEmision.Token;
            EstaEmitiendo = true;

            await using var transmision = await _vigilante.PedirAntenaAsync($"{Modo}: {texto}", testigo).ConfigureAwait(false);
            using var latido = new Timer(_ => transmision.Latir(), null, TimeSpan.Zero, TimeSpan.FromMilliseconds(500));
            try
            {
                await _salida.ReproducirAsync(senal, testigo).ConfigureAwait(false);
            }
            finally
            {
                // Pase lo que pase, lo que quede sonando se corta antes de soltar el PTT.
                await _salida.SilenciarAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
        finally
        {
            EstaEmitiendo = false;
            _paradaDeEmision?.Dispose();
            _paradaDeEmision = null;
            _cerrojoDeEmision.Release();
        }
    }

    /// <inheritdoc/>
    public async Task AbortarEmisionAsync(CancellationToken ct = default)
    {
        if (_paradaDeEmision is not null) await _paradaDeEmision.CancelAsync().ConfigureAwait(false);
        if (_salida is not null) await _salida.SilenciarAsync(ct).ConfigureAwait(false);
        if (_vigilante is not null) await _vigilante.SoltarYaAsync(MotivoDeSuelta.Cancelado).ConfigureAwait(false);
    }

    /// <summary>
    /// Sintetiza lo que se emitiria y lo guarda en un fichero, <b>sin poner el equipo en antena</b>.
    /// </summary>
    /// <param name="ruta">Fichero WAV a crear.</param>
    /// <param name="texto">Mensaje a sintetizar.</param>
    /// <param name="tonoHz">Tono dentro del ancho de banda de audio.</param>
    /// <param name="modo">FT8 o FT4.</param>
    /// <param name="frecuenciaDeMuestreo">Muestras por segundo del fichero.</param>
    /// <remarks>
    /// Es como se prueba la transmision sin transmitir: se guarda la senal, se mira con un
    /// programa de audio si hace falta, y se le vuelve a meter al decodificador para comprobar
    /// que el mensaje vuelve igual.
    /// </remarks>
    public void GuardarEmisionEnFichero(string ruta, string texto, int tonoHz, ModoDelModem modo, int frecuenciaDeMuestreo = 48000)
    {
        var implementacion = ModoRegistrado(modo);
        implementacion.AmplitudDeSalida = _nivelDeSalida;
        float[] senal;
        try
        {
            senal = implementacion.Generar(texto, tonoHz, frecuenciaDeMuestreo);
        }
        catch (FormatException ex)
        {
            throw new ArgumentException(ex.Message, nameof(texto), ex);
        }

        var ventana = new float[(int)Math.Round(implementacion.Periodo.TotalSeconds * frecuenciaDeMuestreo)];
        var comienzo = (int)Math.Round(implementacion.ComienzoDeLaSenal.TotalSeconds * frecuenciaDeMuestreo);
        for (var i = 0; i < senal.Length && comienzo + i < ventana.Length; i++) ventana[comienzo + i] = senal[i];

        LectorWav.Escribir(ruta, ventana, frecuenciaDeMuestreo);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<DecodificacionPropia>> DecodificarFicheroAsync(
        string rutaWav, ModoDelModem modo, CancellationToken ct = default)
        => Task.Run<IReadOnlyList<DecodificacionPropia>>(() =>
        {
            var implementacion = ModoRegistrado(modo);
            var audio = LectorWav.Leer(rutaWav);
            var periodo = implementacion.Periodo.TotalSeconds;
            var muestrasPorVentana = (int)Math.Round(periodo * audio.FrecuenciaDeMuestreo);
            var salida = new List<DecodificacionPropia>();

            // Se da por hecho que el fichero empieza en el comienzo de una ventana, que es como
            // los graban los programas del ramo. Si tiene mas de una, se recorren todas.
            var ventanas = Math.Max(1, audio.Muestras.Length / muestrasPorVentana);
            for (var v = 0; v < ventanas; v++)
            {
                ct.ThrowIfCancellationRequested();
                var desde = v * muestrasPorVentana;
                var cuantas = Math.Min(muestrasPorVentana, audio.Muestras.Length - desde);
                if (cuantas < muestrasPorVentana / 4) break;

                salida.AddRange(implementacion.DecodificarVentana(
                    audio.Muestras.AsSpan(desde, cuantas), audio.FrecuenciaDeMuestreo,
                    DateTimeOffset.UnixEpoch.AddSeconds(v * periodo), 0, ct));
            }
            return salida;
        }, ct);

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await PararAsync().ConfigureAwait(false);
        if (EstaEmitiendo) await AbortarEmisionAsync().ConfigureAwait(false);
        _cerrojoDeEmision.Dispose();
    }
}
