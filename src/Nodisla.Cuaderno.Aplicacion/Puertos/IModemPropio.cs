using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Aplicacion.Puertos;

/// <summary>Modo digital que sabe hacer el modem propio.</summary>
public enum ModoDelModem
{
    /// <summary>FT8: ventanas de 15 segundos, 50 Hz de ancho.</summary>
    Ft8,
    /// <summary>FT4: ventanas de 7,5 segundos, mas rapido y menos sensible.</summary>
    Ft4,
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

    /// <summary>Corta la emision en curso y suelta el PTT.</summary>
    Task AbortarEmisionAsync(CancellationToken ct = default);

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
