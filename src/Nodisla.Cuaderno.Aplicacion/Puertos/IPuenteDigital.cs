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
/// <param name="Texto">El mensaje decodificado, tal y como viene.</param>
/// <param name="Decibelios">Relacion senal-ruido informada.</param>
/// <param name="DesfaseSegundos">Desfase temporal de la senal.</param>
/// <param name="TonoHz">Tono en hercios dentro del ancho de banda de audio.</param>
/// <param name="InstanteUtc">Momento del periodo de decodificacion.</param>
/// <param name="Dialecto">Programa que la envio.</param>
public sealed record DecodificacionDigital(
    string Texto,
    int Decibelios,
    double DesfaseSegundos,
    int TonoHz,
    DateTimeOffset InstanteUtc,
    DialectoDigital Dialecto)
{
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
    DateTimeOffset RecibidoUtc);

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

    /// <summary>Empieza a escuchar.</summary>
    Task ArrancarAsync(CancellationToken ct = default);

    Task PararAsync(CancellationToken ct = default);

    /// <summary>
    /// Pide al programa que llame a un indicativo de los que ha decodificado.
    /// </summary>
    /// <remarks>
    /// No todos los dialectos lo admiten. Si el programa al otro lado no sabe hacerlo, se
    /// devuelve falso y la interfaz debe desactivar el boton en vez de fallar.
    /// </remarks>
    Task<bool> ResponderAAsync(string identificador, DecodificacionDigital decodificacion, CancellationToken ct = default);

    /// <summary>Resalta un indicativo en la ventana del programa, si el dialecto lo admite.</summary>
    Task<bool> ResaltarAsync(string identificador, Indicativo indicativo, bool esNuevo, CancellationToken ct = default);
}
