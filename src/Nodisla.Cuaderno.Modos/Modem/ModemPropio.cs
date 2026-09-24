using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Modos.Ft8;
using Nodisla.Cuaderno.Modos.Ldpc;
using Nodisla.Cuaderno.Modos.Senal;

namespace Nodisla.Cuaderno.Modos.Modem;

/// <summary>
/// El modem propio de FT8 y FT4: escucha, decodifica, pinta la cascada y emite.
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
    private readonly Decodificador _decodificador;
    private readonly Codificador _codificador;
    private readonly CatalogoDeIndicativos _catalogo = new();
    private readonly ILogger _registro;
    private readonly SemaphoreSlim _cerrojoDeEmision = new(1, 1);

    private Channel<BloqueDeAudio>? _cola;
    private Task? _tarea;
    private CancellationTokenSource? _paradaDeEscucha;
    private CancellationTokenSource? _paradaDeEmision;

    private readonly List<float> _acumuladoDeCascada = [];
    private int _frecuenciaDeLaCascada;

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
    public ModemPropio(
        TablasDelProtocolo tablas,
        IRelojDelModem reloj,
        IEntradaDeAudio? entrada = null,
        ISalidaDeAudio? salida = null,
        IVigilantePtt? vigilante = null,
        ILogger<ModemPropio>? registro = null)
    {
        ArgumentNullException.ThrowIfNull(tablas);
        ArgumentNullException.ThrowIfNull(reloj);
        _registro = registro ?? (ILogger)NullLogger.Instance;
        _reloj = reloj;
        _entrada = entrada;
        _salida = salida;
        _vigilante = vigilante;
        _decodificador = new Decodificador(tablas, _registro);
        _codificador = new Codificador(tablas);
        Tablas = tablas;

        if (!tablas.EsElCodigoReal)
            _registro.LogWarning(
                "El módem trabaja con el código corrector de pruebas: funciona consigo mismo pero no decodifica a otras estaciones. Falta la tabla del LDPC(174,91).");
    }

    /// <summary>Tablas del protocolo con las que trabaja.</summary>
    public TablasDelProtocolo Tablas { get; }

    /// <summary>Catalogo de indicativos que va aprendiendo mientras escucha.</summary>
    public CatalogoDeIndicativos Catalogo => _catalogo;

    /// <inheritdoc/>
    public ModoDelModem Modo { get; private set; } = ModoDelModem.Ft8;

    /// <inheritdoc/>
    public bool EstaEscuchando => _tarea is { IsCompleted: false };

    /// <inheritdoc/>
    public bool EstaEmitiendo { get; private set; }

    /// <inheritdoc/>
    public Frecuencia FrecuenciaDelDial { get; set; }

    /// <inheritdoc/>
    public event EventHandler<ColumnaDeCascada>? CascadaActualizada;

    /// <inheritdoc/>
    public event EventHandler<VentanaDecodificada>? VentanaLista;

    /// <inheritdoc/>
    public async Task EscucharAsync(ModoDelModem modo, CancellationToken ct = default)
    {
        if (_entrada is null)
            throw new InvalidOperationException("Este módem no tiene entrada de audio: solo puede decodificar ficheros.");
        await PararAsync(ct).ConfigureAwait(false);

        Modo = modo;
        _catalogo.Olvidar();
        _acumuladoDeCascada.Clear();
        _cola = Channel.CreateUnbounded<BloqueDeAudio>(new UnboundedChannelOptions { SingleReader = true });
        _paradaDeEscucha = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _entrada.BloqueCapturado += AlLlegarUnBloque;
        _tarea = Task.Run(() => MolerAsync(modo, _paradaDeEscucha.Token), CancellationToken.None);

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
    /// Se trabaja sobre una copia propia del audio y no sobre los bloques que llegan: quien
    /// captura puede reutilizar sus vectores en cuanto suelta el evento, y leer de ahi mas tarde
    /// daria audio revuelto sin que nada avisara.
    /// </remarks>
    private async Task MolerAsync(ModoDelModem modo, CancellationToken ct)
    {
        var p = ParametrosDelModo.De(modo);
        var periodo = TimeSpan.FromSeconds(p.PeriodoSegundos);
        var lector = _cola!.Reader;

        var audio = new List<float>();
        var frecuencia = 0;
        DateTimeOffset? instanteDelPrimero = null;
        DateTimeOffset? ventanaEnCurso = null;

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

                var ventana = VentanaDe(bloque.InstanteUtc, periodo);
                ventanaEnCurso ??= ventana;
                if (ventana == ventanaEnCurso) continue;

                // Cambio de ventana: lo acumulado ya contiene la anterior entera.
                await DecodificarLoAcumuladoAsync(modo, p, audio, frecuencia, instanteDelPrimero.Value, ventanaEnCurso.Value, ct)
                    .ConfigureAwait(false);

                // Se conserva el preludio de la ventana nueva y se tira lo demas.
                var conservar = (int)(SegundosDePreludio * frecuencia);
                var sobran = Math.Max(0, audio.Count - conservar);
                if (sobran > 0)
                {
                    audio.RemoveRange(0, sobran);
                    instanteDelPrimero = instanteDelPrimero.Value.AddSeconds((double)sobran / frecuencia);
                }
                ventanaEnCurso = ventana;
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

    private async Task DecodificarLoAcumuladoAsync(
        ModoDelModem modo, ParametrosDelModo p, List<float> audio, int frecuencia,
        DateTimeOffset instanteDelPrimero, DateTimeOffset ventana, CancellationToken ct)
    {
        var muestras = audio.ToArray();
        var desfase = (instanteDelPrimero - ventana).TotalSeconds;

        var resultado = await Task.Run(
            () => _decodificador.Decodificar(muestras, frecuencia, modo, ventana, _catalogo, desfase), ct)
            .ConfigureAwait(false);

        // Si la ventana costo mas que el propio periodo, la siguiente ya empezo tarde y se han
        // perdido decodificaciones. El operador tiene que verlo, no es una estadistica interna.
        var llegoTarde = resultado.Duracion.TotalSeconds > p.PeriodoSegundos;
        if (llegoTarde)
            _registro.LogWarning(
                "La ventana {Ventana} tardó {Segundos:0.0} s, más que el propio periodo: el ordenador no da abasto.",
                ventana, resultado.Duracion.TotalSeconds);

        VentanaLista?.Invoke(this, new VentanaDecodificada(
            ventana, resultado.Decodificaciones, resultado.Duracion, EnDecibelios(resultado))
        {
            LlegoTarde = llegoTarde,
        });
    }

    private static double EnDecibelios(ResultadoDeVentana resultado) =>
        resultado.Decodificaciones.Count == 0 ? -120 : resultado.Decodificaciones.Min(d => d.Decibelios) - 10;

    /// <summary>Comienzo de la ventana a la que pertenece un instante.</summary>
    private static DateTimeOffset VentanaDe(DateTimeOffset instante, TimeSpan periodo)
    {
        var desdeLaEpoca = instante.ToUniversalTime().UtcTicks;
        return new DateTimeOffset(desdeLaEpoca - (desdeLaEpoca % periodo.Ticks), TimeSpan.Zero);
    }

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
                "Este módem no puede emitir: no se le ha dado salida de audio ni vigilante de PTT.");
        if (!_codificador.TryCodificar(texto, Modo, out var tonos, out var motivo))
            throw new ArgumentException(motivo, nameof(texto));

        var p = ParametrosDelModo.De(Modo);
        var frecuenciaDeSalida = 48000;
        var senal = Modulador.Sintetizar(p, tonos, tonoHz, frecuenciaDeSalida, amplitud: 0.5);

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
        if (!_codificador.TryCodificar(texto, modo, out var tonos, out var motivo))
            throw new ArgumentException(motivo, nameof(texto));
        var p = ParametrosDelModo.De(modo);

        var ventana = new float[(int)Math.Round(p.PeriodoSegundos * frecuenciaDeMuestreo)];
        var senal = Modulador.Sintetizar(p, tonos, tonoHz, frecuenciaDeMuestreo, amplitud: 0.5);
        var comienzo = (int)Math.Round(p.ComienzoNominalSegundos * frecuenciaDeMuestreo);
        for (var i = 0; i < senal.Length && comienzo + i < ventana.Length; i++) ventana[comienzo + i] = senal[i];

        LectorWav.Escribir(ruta, ventana, frecuenciaDeMuestreo);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<DecodificacionPropia>> DecodificarFicheroAsync(
        string rutaWav, ModoDelModem modo, CancellationToken ct = default)
        => Task.Run<IReadOnlyList<DecodificacionPropia>>(() =>
        {
            var audio = LectorWav.Leer(rutaWav);
            var p = ParametrosDelModo.De(modo);
            var muestrasPorVentana = (int)Math.Round(p.PeriodoSegundos * audio.FrecuenciaDeMuestreo);
            var catalogo = new CatalogoDeIndicativos();
            var salida = new List<DecodificacionPropia>();

            // Se da por hecho que el fichero empieza en el comienzo de una ventana, que es como
            // los graban los programas del ramo. Si tiene mas de una, se recorren todas.
            var ventanas = Math.Max(1, audio.Muestras.Length / muestrasPorVentana);
            for (var v = 0; v < ventanas; v++)
            {
                ct.ThrowIfCancellationRequested();
                var desde = v * muestrasPorVentana;
                var cuantas = Math.Min(muestrasPorVentana, audio.Muestras.Length - desde);
                if (cuantas < p.MuestrasDeLaSenal / 4) break;

                var resultado = _decodificador.Decodificar(
                    audio.Muestras.AsSpan(desde, cuantas), audio.FrecuenciaDeMuestreo, modo,
                    DateTimeOffset.UnixEpoch.AddSeconds(v * p.PeriodoSegundos), catalogo);
                salida.AddRange(resultado.Decodificaciones);
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
