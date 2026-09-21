using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Aplicacion.Puertos;

/// <summary>Programa de modos digitales con el que se habla.</summary>
public enum DialectoDigital
{
    Desconocido,
    WsjtX,
    Jtdx,
    Mshv,
    Js8Call,
}

/// <summary>Una decodificacion recibida de un programa de modos digitales.</summary>
/// <param name="Identificador">
/// Instancia que la envio. Va en la decodificacion y no aparte porque para responder a una
/// llamada hay que saber a que instancia contestar, y puede haber varias abiertas.
/// </param>
/// <param name="Texto">El mensaje decodificado, tal y como viene.</param>
/// <param name="Decibelios">Relacion senal-ruido informada.</param>
/// <param name="DesfaseSegundos">Desfase temporal de la senal.</param>
/// <param name="TonoHz">Tono en hercios dentro del ancho de banda de audio.</param>
/// <param name="Modo">
/// Modo real en que se decodifico, para mostrarlo al operador y para armar el contacto.
/// Puede venir vacio si aun no se sabe.
/// </param>
/// <param name="InstanteUtc">Momento del periodo de decodificacion.</param>
/// <param name="Dialecto">Programa que la envio.</param>
public sealed record DecodificacionDigital(
    string Identificador,
    string Texto,
    int Decibelios,
    double DesfaseSegundos,
    int TonoHz,
    Modo Modo,
    DateTimeOffset InstanteUtc,
    DialectoDigital Dialecto)
{
    /// <summary>
    /// El campo que el programa llama «modo» en su aviso de decodificacion, tal y como vino.
    /// </summary>
    /// <remarks>
    /// <b>No es un modo</b>, aunque se llame asi: WSJT-X manda aqui el caracter que pinta en
    /// su ventana (<c>~</c> para FT8, <c>+</c> para FT4), mientras que MSHV manda el nombre.
    /// Y no es decorativo: el programa reconstruye con el la linea de su ventana para saber a
    /// que decodificacion estamos contestando, asi que hay que <b>devolverlo intacto</b>.
    /// Va en su propio campo para que no contamine <see cref="Modo"/>, que es el modo de
    /// verdad y es lo que ve el operador.
    /// </remarks>
    public string? IndicadorDelPrograma { get; init; }

    /// <summary>Indicativo de quien llama, cuando el mensaje permite extraerlo.</summary>
    public Indicativo Llamante { get; init; }

    /// <summary>Indicativo al que se llama, cuando lo hay. <c>CQ</c> no es un indicativo.</summary>
    public Indicativo Llamado { get; init; }

    /// <summary>Localizador que viaja en el mensaje, cuando lo hay.</summary>
    public Locator Locator { get; init; }

    /// <summary>El mensaje es una llamada general.</summary>
    public bool EsCq { get; init; }
}

/// <summary>Estado que informa el programa de modos digitales.</summary>
/// <param name="Dialecto">Programa del que viene.</param>
/// <param name="Identificador">Nombre de la instancia, que permite tener varias a la vez.</param>
/// <param name="Frecuencia">Frecuencia del dial.</param>
/// <param name="Modo">Modo en uso.</param>
/// <param name="Transmitiendo">Esta en transmision.</param>
/// <param name="Decodificando">Esta procesando el periodo recibido.</param>
/// <param name="Llamado">Indicativo con el que esta en QSO, si lo hay.</param>
/// <param name="RecibidoUtc">Momento del informe.</param>
public sealed record EstadoDigital(
    DialectoDigital Dialecto,
    string Identificador,
    Frecuencia Frecuencia,
    Modo Modo,
    bool Transmitiendo,
    bool Decodificando,
    Indicativo Llamado,
    DateTimeOffset RecibidoUtc)
{
    /// <summary>Tono de recepcion dentro del ancho de banda de audio, en hercios.</summary>
    public int? TonoRxHz { get; init; }

    /// <summary>Tono de transmision, en hercios.</summary>
    public int? TonoTxHz { get; init; }

    /// <summary>La transmision esta habilitada en el programa.</summary>
    public bool? TransmisionHabilitada { get; init; }

    /// <summary>Ha saltado el vigilante de transmision del programa.</summary>
    public bool? VigilanteDisparado { get; init; }

    /// <summary>Se transmite en el periodo par o en el impar.</summary>
    public bool? TransmiteElPrimero { get; init; }

    /// <summary>Modo especial de operacion, en los programas que lo tienen.</summary>
    public string? ModoEspecial { get; init; }

    /// <summary>Nombre de la configuracion activa en el programa.</summary>
    public string? Configuracion { get; init; }

    /// <summary>Mensaje que el programa esta transmitiendo ahora.</summary>
    public string? MensajeEnTransmision { get; init; }

    /// <summary>Localizador del corresponsal, cuando el programa lo informa.</summary>
    public Locator LocatorDelCorresponsal { get; init; }
}

/// <summary>
/// Lo que sabe hacer una instancia concreta.
/// </summary>
/// <remarks>
/// La interfaz pregunta esto para <b>desactivar</b> los botones que el programa al otro lado
/// no admite, en vez de dejar que el operador los pulse y no pase nada. JTDX, por ejemplo,
/// no tiene resaltado de indicativos; WSJT-X si.
/// </remarks>
/// <param name="PuedeResponder">Admite que le pidamos llamar a una estacion decodificada.</param>
/// <param name="PuedeResaltar">Admite resaltar indicativos en su ventana.</param>
/// <param name="PuedeCambiarTonoTx">Admite que le cambiemos el tono de transmision.</param>
/// <param name="PuedeLlamarCq">Admite que le pidamos lanzar una llamada general.</param>
/// <param name="PuedeCambiarConfiguracion">Admite cambiar de configuracion.</param>
public sealed record CapacidadesDigitales(
    bool PuedeResponder,
    bool PuedeResaltar,
    bool PuedeCambiarTonoTx,
    bool PuedeLlamarCq,
    bool PuedeCambiarConfiguracion)
{
    /// <summary>Lo que se supone de una instancia de la que aun no se sabe nada.</summary>
    public static CapacidadesDigitales Desconocidas { get; } = new(false, false, false, false, false);
}

/// <summary>
/// Puente con un programa de modos digitales.
/// </summary>
/// <remarks>
/// WSJT-X, JTDX, MSHV y JS8Call hablan el mismo protocolo UDP, pero no exactamente: JTDX no
/// tiene los mensajes de resaltado ni de configuracion, tiene dos propios, y su mensaje de
/// estado diverge a partir de cierto campo. El lector debe detectar el dialecto, tolerar que
/// un datagrama termine antes de lo esperado y descartar sin romperse los tipos que no conoce.
/// Un programa que se cuelgue porque el otro lado saco version nueva no sirve.
///
/// Este puente es provisional por diseno: en la Fase 4 el modem propio implementara el mismo
/// papel desde dentro de la aplicacion. Por eso todo lo que la interfaz necesita pasa por
/// aqui y no por detalles del protocolo.
/// </remarks>
public interface IPuenteDigital : IAsyncDisposable
{
    /// <summary>Puerto UDP en el que se escucha.</summary>
    int Puerto { get; }

    /// <summary>Instancias que han dado senales de vida, por su identificador.</summary>
    IReadOnlyCollection<EstadoDigital> Instancias { get; }

    /// <summary>Salta con cada decodificacion.</summary>
    event EventHandler<DecodificacionDigital>? Decodificado;

    /// <summary>Salta con cada informe de estado.</summary>
    event EventHandler<EstadoDigital>? EstadoRecibido;

    /// <summary>
    /// Salta cuando el programa da un contacto por cerrado. El QSO llega ya armado y listo
    /// para guardarlo en el cuaderno.
    /// </summary>
    event EventHandler<Qso>? QsoRegistrado;

    /// <summary>Salta cuando una instancia se cierra o deja de responder.</summary>
    event EventHandler<string>? InstanciaPerdida;

    /// <summary>
    /// Salta con el ADIF que el programa envia al cerrar un contacto. Trae campos que el
    /// aviso binario de contacto no lleva, asi que conviene quedarse con los dos.
    /// </summary>
    event EventHandler<string>? AdifRecibido;

    /// <summary>Empieza a escuchar.</summary>
    Task ArrancarAsync(CancellationToken ct = default);

    Task PararAsync(CancellationToken ct = default);

    /// <summary>
    /// Que sabe hacer una instancia. Se consulta <b>antes</b> de ofrecer un boton, no despues
    /// de que falle.
    /// </summary>
    /// <param name="identificador">Instancia por la que se pregunta.</param>
    CapacidadesDigitales Capacidades(string identificador);

    /// <summary>
    /// Pide al programa que llame a la estacion de una decodificacion.
    /// </summary>
    /// <remarks>
    /// No todos los dialectos lo admiten. Si el programa al otro lado no sabe hacerlo, se
    /// devuelve falso; pero lo correcto es mirar antes <see cref="Capacidades"/> y desactivar
    /// el boton, en vez de dejar que el operador lo pulse y no pase nada.
    /// </remarks>
    Task<bool> ResponderAAsync(DecodificacionDigital decodificacion, CancellationToken ct = default);

    /// <summary>Resalta un indicativo en la ventana del programa, si el dialecto lo admite.</summary>
    Task<bool> ResaltarAsync(string identificador, Indicativo indicativo, bool esNuevo, CancellationToken ct = default);
}
