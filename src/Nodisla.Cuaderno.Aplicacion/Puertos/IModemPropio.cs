using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Idiomas;

namespace Nodisla.Cuaderno.Aplicacion.Puertos;

/// <summary>Modo digital que sabe hacer el modem propio.</summary>
public enum ModoDelModem
{
    /// <summary>FT8: ventanas de 15 segundos, 50 Hz de ancho.</summary>
    Ft8,
    /// <summary>FT4: ventanas de 7,5 segundos, mas rapido y menos sensible.</summary>
    Ft4,

    // Los modos que siguen los pidio Jose el 26-09-2026 con la regla de la casa: todo propio,
    // sin puentes con WSJT-X ni JTDX. Cada uno es un decodificador distinto y entra en el
    // modem por el marco de Nodisla.Cuaderno.Modos.Marco; mientras no tenga decodificador,
    // ParametrosDelModo.De lo rechaza y la pantalla no lo ofrece.

    /// <summary>WSPR: balizas de propagacion, ventanas de 2 minutos, 4-FSK, mensaje de 50 bits.</summary>
    Wspr,
    /// <summary>JT65: modo lento de HF y EME, ventanas de 1 minuto, 65 tonos, Reed-Solomon.</summary>
    Jt65,
    /// <summary>JT9: modo lento de HF, ventanas de 1 minuto, 9-FSK, muy estrecho.</summary>
    Jt9,
    /// <summary>Q65: sucesor de QRA64 para VHF/EME y dispersion, con varios submodos (A-E).</summary>
    Q65,
    /// <summary>MSK144: dispersion meteorica, tramas de 72 ms en ventanas de 15 s.</summary>
    Msk144,
    /// <summary>FST4: modo lento para bandas bajas (2200/630 m), varios periodos.</summary>
    Fst4,
    /// <summary>FST4W: la variante baliza de FST4, comparable a WSPR.</summary>
    Fst4w,
}

/// <summary>
/// Lo que el secuenciador de QSO ya sabe con certeza del contacto en curso, para que el
/// decodificador lo use como pista (decodificación AP, «a priori»).
/// </summary>
/// <remarks>
/// <para>
/// Mientras se está en QSO con un corresponsal conocido, quién llama y quién contesta ya no es
/// una incógnita: solo falta el informe de señal, que es justo lo único que el ruido puede
/// estropear. Darle esa certeza al decodificador reduce el problema a resolver únicamente el
/// campo que de verdad hace falta leer de la señal real, igual que hace JTDX (documentado en su
/// opción de «AP decoding», sin copiar ni una línea de su código: ver <c>TERCEROS.md</c>).
/// </para>
/// <para>
/// <b>No es adivinar.</b> El CRC de 14 bits y el campo del informe siguen sacándose de la señal
/// de verdad; la pista solo fija los bits de los indicativos, que ya se conocían de antes. Si la
/// pista fuera la del corresponsal equivocado, las ecuaciones de paridad no cuadrarían con la
/// señal real y el intento fallaría igual que si no hubiera pista: no hay manera de que esto
/// cuele un contacto falso, solo de que recupere uno de verdad que si no se perdería.
/// </para>
/// </remarks>
/// <param name="MiIndicativo">El propio, tal como viaja en el mensaje.</param>
/// <param name="DxCall">El corresponsal con el que se está en QSO. Vacío si no hay pista.</param>
public readonly record struct PistaDeQso(string MiIndicativo, string DxCall)
{
    /// <summary>Sin pista: el decodificador trabaja a ciegas, como siempre.</summary>
    public static PistaDeQso Ninguna => default;
}

/// <summary>Una columna de la cascada: el espectro de un instante.</summary>
/// <param name="Magnitudes">
/// Magnitud por cada casilla de frecuencia, ya en decibelios y lista para pintar.
/// </param>
/// <param name="HzPorCasilla">Anchura en hercios de cada casilla.</param>
/// <param name="InstanteUtc">Momento al que corresponde la columna.</param>
public sealed record ColumnaDeCascada(ReadOnlyMemory<float> Magnitudes, double HzPorCasilla, DateTimeOffset InstanteUtc);

/// <summary>Una decodificacion del modem propio.</summary>
/// <param name="Texto">Mensaje tal y como se decodifico.</param>
/// <param name="Decibelios">Relacion senal-ruido.</param>
/// <param name="DesfaseSegundos">Desfase del inicio de la senal respecto a la ventana.</param>
/// <param name="TonoHz">Tono dentro del ancho de banda de audio.</param>
/// <param name="Modo">Modo en que se decodifico.</param>
/// <param name="VentanaUtc">Ventana a la que pertenece.</param>
public sealed record DecodificacionPropia(
    string Texto,
    int Decibelios,
    double DesfaseSegundos,
    int TonoHz,
    ModoDelModem Modo,
    DateTimeOffset VentanaUtc)
{
    /// <summary>Indicativo que llama, si el mensaje permite sacarlo.</summary>
    public Indicativo Llamante { get; init; }

    /// <summary>Indicativo llamado. <c>CQ</c> no es un indicativo.</summary>
    public Indicativo Llamado { get; init; }

    /// <summary>Localizador que viaja en el mensaje.</summary>
    public Locator Locator { get; init; }

    /// <summary>Es una llamada general.</summary>
    public bool EsCq { get; init; }

    /// <summary>
    /// La decodificacion salio del bloque de correccion profunda y no de la pasada normal.
    /// </summary>
    /// <remarks>
    /// Las senales muy debiles se recuperan con mas esfuerzo y tienen algo mas de riesgo de ser
    /// falsas. Conviene poder distinguirlas, aunque sea solo para afinar el decodificador.
    /// </remarks>
    public bool EsRecuperacionProfunda { get; init; }

    /// <summary>
    /// La decodificacion salio usando la pista del QSO en curso (decodificacion AP) y no de la
    /// pasada a ciegas.
    /// </summary>
    /// <remarks>
    /// Como con <see cref="EsRecuperacionProfunda"/>, es para poder distinguirlas y medir si
    /// compensan; el CRC que las valida es el mismo para las dos.
    /// </remarks>
    public bool EsPorPista { get; init; }
}

/// <summary>Como fue una ventana completa.</summary>
/// <param name="VentanaUtc">Ventana a la que corresponde.</param>
/// <param name="Decodificaciones">Lo que se saco.</param>
/// <param name="DuracionDelProceso">Lo que costo decodificarla.</param>
/// <param name="RuidoDbm">Nivel de ruido de fondo medido.</param>
public sealed record VentanaDecodificada(
    DateTimeOffset VentanaUtc,
    IReadOnlyList<DecodificacionPropia> Decodificaciones,
    TimeSpan DuracionDelProceso,
    double RuidoDbm)
{
    /// <summary>
    /// El proceso tardo mas que la propia ventana, asi que la siguiente empezo tarde.
    /// </summary>
    /// <remarks>
    /// Si esto pasa a menudo, el ordenador no da abasto y se estan perdiendo decodificaciones.
    /// Es un dato que el operador tiene que ver, no una estadistica interna.
    /// </remarks>
    public bool LlegoTarde { get; init; }
}

/// <summary>
/// Si el modem trabaja con el codigo corrector real del protocolo o con uno de pruebas, y de
/// donde salieron las tablas.
/// </summary>
/// <remarks>
/// Se expone como estos dos datos sueltos, y no como el objeto de las tablas del modulo de los
/// modos, para que quien consulte el puerto no tenga que conocer nada del LDPC ni enlazar con
/// ese modulo: solo necesita saber si lo que se decodifica vale para hablar con el mundo.
/// </remarks>
/// <param name="EsElCodigoReal">
/// Falso mientras se use el codigo de pruebas: el modem funciona entero -escucha, decodifica,
/// mide- pero <b>solo se entiende consigo mismo</b>. Nada de lo que decodifique interoperara
/// con otra estacion, y es justo el dato que la pantalla tiene que poder mostrar sin que nadie
/// tenga que acordarse de leerlo de otro sitio.
/// </param>
/// <param name="Procedencia">
/// De donde salieron las tablas: la ruta del fichero cargado, o el texto que explique por que
/// no lo hay. Para mostrarlo al operador o dejarlo en un registro.
/// </param>
public readonly record struct EstadoDeLasTablas(bool EsElCodigoReal, string Procedencia);

/// <summary>
/// El modem propio de modos digitales.
/// </summary>
/// <remarks>
/// <para>
/// Es la pieza que hace que Cuaderno NODISLA no dependa de WSJT-X ni de JTDX. El audio entra
/// por el codec USB del equipo, la cascada se calcula aqui y las decodificaciones salen por
/// este puerto igual que salian las del puente externo, para que la pantalla no distinga de
/// donde vienen.
/// </para>
/// <para>
/// Dos reglas que valen mas que la sensibilidad:
/// </para>
/// <para>
/// <b>Transmitir solo por el vigilante.</b> Emitir un periodo de FT8 son trece segundos con el
/// equipo en antena. Cualquier cuelgue aqui tiene que acabar con el PTT abajo.
/// </para>
/// <para>
/// <b>No mentir sobre lo que se decodifica.</b> Un decodificador que se inventa un indicativo
/// mete un contacto falso en el cuaderno, y eso contamina los diplomas para siempre. Ante la
/// duda, no decodificar.
/// </para>
/// </remarks>
public interface IModemPropio : IAsyncDisposable
{
    /// <summary>Modo en el que trabaja.</summary>
    ModoDelModem Modo { get; }

    /// <summary>El modem esta escuchando.</summary>
    bool EstaEscuchando { get; }

    /// <summary>El modem esta emitiendo.</summary>
    bool EstaEmitiendo { get; }

    /// <summary>Frecuencia del dial, para poder componer el contacto.</summary>
    Frecuencia FrecuenciaDelDial { get; set; }

    /// <summary>
    /// Amplitud de pico (0 a 1) con la que se sintetiza la señal digital al emitir: el «Pwr» de
    /// JTDX. Se aplica al modo en curso justo antes de generar cada señal, así que cambiarlo
    /// surte efecto en la siguiente emisión, sin reiniciar el programa ni reabrir la salida de
    /// audio. Por omision, el mismo valor conservador de todos los modos
    /// (<c>IModoDigital.AmplitudDeSalidaPorDefecto</c>).
    /// </summary>
    double NivelDeSalida { get; set; }

    /// <summary>
    /// Como esta el codigo corrector con el que trabaja este modem.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Se expone aqui, en el puerto, y no solo en como se ensamblo el programa, porque es un
    /// dato del modem que esta en marcha, no de configuracion: si el fichero de tablas se pierde
    /// o se corrompe entre un arranque y otro, la pantalla tiene que poder reflejarlo leyendo
    /// directamente al modem, sin depender de que alguien se acuerde de leer tambien el registro
    /// de servicios.
    /// </para>
    /// <para>
    /// Por omision vale codigo real y «no procede»: es lo que le corresponde a un modem que no
    /// trabaja con tablas del protocolo -por ejemplo, uno simulado para pruebas de pantalla-, y
    /// asi no hace falta que cada implementacion que no toque el LDPC se acuerde de declararlo.
    /// El modem de verdad lo sobreescribe con lo que diga <c>TablasDelProtocolo</c>.
    /// </para>
    /// </remarks>
    EstadoDeLasTablas EstadoDeLasTablas => new(EsElCodigoReal: true, Procedencia: Textos.T("Servicios.Aplicacion.NoProcede"));

    /// <summary>
    /// Modos que este modem sabe hacer de verdad, en el orden en que se ofrecen.
    /// </summary>
    /// <remarks>
    /// Tiene implementacion por omision —FT8 y FT4— por la misma razon que
    /// <see cref="EstadoDeLasTablas"/>: los modems que no hayan incorporado aun los
    /// decodificadores nuevos no tienen que declararlo, y la pantalla rellena el selector con
    /// lo que diga esto en vez de con dos botones fijos. Un modo que no este aqui no se ofrece.
    /// </remarks>
    IReadOnlyList<ModoDelModem> ModosDisponibles => [ModoDelModem.Ft8, ModoDelModem.Ft4];

    /// <summary>Salta con cada columna de cascada, varias veces por segundo.</summary>
    event EventHandler<ColumnaDeCascada>? CascadaActualizada;

    /// <summary>Salta al terminar de decodificar cada ventana.</summary>
    event EventHandler<VentanaDecodificada>? VentanaLista;

    /// <summary>Empieza a escuchar en el modo indicado.</summary>
    Task EscucharAsync(ModoDelModem modo, CancellationToken ct = default);

    Task PararAsync(CancellationToken ct = default);

    /// <summary>
    /// Emite un mensaje en la siguiente ventana que toque.
    /// </summary>
    /// <param name="texto">Mensaje a emitir, con la gramatica del modo.</param>
    /// <param name="tonoHz">Tono dentro del ancho de banda de audio.</param>
    /// <param name="ct">Testigo de cancelacion; al cancelarse, se corta y se suelta el PTT.</param>
    /// <remarks>
    /// <b>Esto pone el equipo en antena.</b> La implementacion pide la transmision a
    /// <see cref="IVigilantePtt"/>, late mientras suena y la suelta al terminar.
    /// </remarks>
    Task EmitirAsync(string texto, int tonoHz, CancellationToken ct = default);

    /// <summary>
    /// Emite varios mensajes a la vez, cada uno en su propio tono, mezclados en una sola señal de
    /// audio dentro de la misma ventana de transmisión.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Es lo que hace falta para el lado fox de fox/hound: varios cazadores en curso, cada uno con
    /// su propio tono, y el equipo solo puede emitir un audio. Pone el equipo en antena igual que
    /// <see cref="EmitirAsync"/> —mismo vigilante, mismo latido, misma salida compartida— solo que
    /// el audio que suena es la mezcla de todos los mensajes.
    /// </para>
    /// <para>
    /// Por omisión no lo sabe hacer ningún módem: solo tiene sentido en los modos que lo declaren
    /// (<c>IModoDigital.GenerarMezcla</c>, en <c>Nodisla.Cuaderno.Modos</c>), que hoy son FT8 y FT4.
    /// </para>
    /// </remarks>
    /// <param name="mensajes">Mensaje y tono de cada señal a mezclar; al menos una.</param>
    /// <param name="ct">Testigo de cancelación; al cancelarse, se corta y se suelta el PTT.</param>
    /// <exception cref="NotSupportedException">Si el modo en curso no sabe mezclar señales.</exception>
    Task EmitirVariosAsync(IReadOnlyList<(string Texto, int TonoHz)> mensajes, CancellationToken ct = default) =>
        throw new NotSupportedException("Este módem no sabe emitir varias señales a la vez.");

    /// <summary>Corta la emision en curso y suelta el PTT.</summary>
    Task AbortarEmisionAsync(CancellationToken ct = default);

    /// <summary>
    /// Le dice al modo que esta escuchando lo que el secuenciador de QSO ya sabe, para la
    /// decodificacion AP. Por omision no hace nada: solo lo aprovechan los modos que lo declaren.
    /// </summary>
    /// <param name="miIndicativo">El propio.</param>
    /// <param name="dxCall">El corresponsal del QSO en curso; vacio si no hay ninguno fijado.</param>
    void FijarPistaDeQso(string miIndicativo, string dxCall)
    {
    }

    /// <summary>
    /// Decodifica un fichero de audio, para probar el decodificador contra grabaciones.
    /// </summary>
    /// <remarks>
    /// Es lo que permite medir la calidad sin radio: se pasan las mismas grabaciones por el
    /// nuestro y por el de referencia y se comparan por franjas de relacion senal-ruido. Sin
    /// esto, «decodifica bien» es una opinion.
    /// </remarks>
    Task<IReadOnlyList<DecodificacionPropia>> DecodificarFicheroAsync(
        string rutaWav,
        ModoDelModem modo,
        CancellationToken ct = default);
}
