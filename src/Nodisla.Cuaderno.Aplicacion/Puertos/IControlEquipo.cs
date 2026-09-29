using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Aplicacion.Puertos;

/// <summary>Como esta el equipo en un instante dado.</summary>
/// <param name="Conectado">Hay comunicacion con el equipo.</param>
/// <param name="Transmitiendo">El equipo esta en transmision.</param>
/// <param name="Frecuencia">Frecuencia del VFO activo.</param>
/// <param name="FrecuenciaRx">Frecuencia de recepcion si se opera en dos frecuencias.</param>
/// <param name="Modo">Modo que tiene puesto el equipo.</param>
/// <param name="Vfo">VFO activo, tal y como lo nombre el equipo.</param>
/// <param name="PotenciaVatios">Potencia de salida configurada, si el equipo la informa.</param>
/// <param name="SenalRecibida">Lectura del medidor S, en unidades S, si el equipo la informa.</param>
/// <param name="LeidoUtc">Momento de la lectura.</param>
public sealed record EstadoDelEquipo(
    bool Conectado,
    bool Transmitiendo,
    Frecuencia Frecuencia,
    Frecuencia? FrecuenciaRx,
    Modo Modo,
    string? Vfo,
    double? PotenciaVatios,
    double? SenalRecibida,
    DateTimeOffset LeidoUtc)
{
    /// <summary>Estado de partida: sin equipo y sin datos.</summary>
    public static EstadoDelEquipo Desconectado { get; } = new(
        Conectado: false,
        Transmitiendo: false,
        Frecuencia: Frecuencia.Cero,
        FrecuenciaRx: null,
        Modo: Modo.Vacio,
        Vfo: null,
        PotenciaVatios: null,
        SenalRecibida: null,
        LeidoUtc: DateTimeOffset.MinValue);

    /// <summary>Banda deducida de la frecuencia del VFO activo.</summary>
    public Banda Banda => Banda.DesdeFrecuencia(Frecuencia);
}

/// <summary>Por que via se controla el equipo.</summary>
public enum ViaDeControl
{
    /// <summary>
    /// Juego de ordenes propio del fabricante, hablado directamente por el puerto serie.
    /// Es la via que da acceso a todo lo que el equipo sabe hacer.
    /// </summary>
    CatNativo,
    /// <summary>
    /// Demonio <c>rigctld</c> de Hamlib por TCP. Vale para casi cualquier equipo, a cambio
    /// de quedarse en el minimo comun de todos ellos.
    /// </summary>
    Rigctld,
    /// <summary>
    /// OmniRig por COM.
    /// </summary>
    /// <remarks>
    /// OmniRig es de 32 bits, pero esta registrado como servidor <c>LocalServer32</c>, es
    /// decir <b>fuera de proceso</b>: COM cruza la frontera de arquitectura y se le puede
    /// hablar desde un proceso de 64 bits. Comprobado en vivo contra la instalacion del operador.
    /// Por eso la aplicacion no tiene que compilarse en 32 bits, lo que habria estrangulado
    /// al modem digital de la Fase 4.
    /// </remarks>
    OmniRig,
    /// <summary>Sin equipo: la frecuencia y el modo los pone el operador a mano.</summary>
    Ninguna,
}

/// <summary>
/// Control del equipo de radio: leer lo que tiene puesto y decirle lo que debe poner.
/// </summary>
/// <remarks>
/// La implementacion no debe suponer que el operador solo toca el programa: el dial fisico
/// manda tanto como nosotros, y el estado hay que leerlo, no recordarlo.
/// </remarks>
public interface IControlEquipo : IAsyncDisposable
{
    /// <summary>Via por la que se habla con el equipo.</summary>
    ViaDeControl Via { get; }

    /// <summary>Ultimo estado conocido. Nunca lanza ni bloquea.</summary>
    EstadoDelEquipo Estado { get; }

    /// <summary>Salta cada vez que el estado cambia.</summary>
    event EventHandler<EstadoDelEquipo>? EstadoCambiado;

    /// <summary>Abre la comunicacion con el equipo y arranca el sondeo.</summary>
    Task ConectarAsync(CancellationToken ct = default);

    /// <summary>Cierra la comunicacion. Debe soltar el PTT antes de cerrar.</summary>
    Task DesconectarAsync(CancellationToken ct = default);

    /// <summary>Pone el equipo en la frecuencia indicada.</summary>
    Task PonerFrecuenciaAsync(Frecuencia frecuencia, CancellationToken ct = default);

    /// <summary>Pone el equipo en el modo indicado.</summary>
    Task PonerModoAsync(Modo modo, CancellationToken ct = default);

    /// <summary>
    /// Pide o suelta el PTT.
    /// </summary>
    /// <remarks>
    /// <b>Nunca se llama directamente desde la aplicacion.</b> Se usa a traves de
    /// <see cref="IVigilantePtt"/>, que garantiza que el PTT se suelta pase lo que pase.
    /// Un PTT que se queda pegado quema la etapa final del equipo o el amplificador: es el
    /// unico fallo de este programa que destroza cosas fisicas.
    /// </remarks>
    Task PonerPttAsync(bool transmitir, CancellationToken ct = default);
}

/// <summary>Por que motivo se ha soltado el PTT.</summary>
public enum MotivoDeSuelta
{
    /// <summary>La transmision termino como estaba previsto.</summary>
    Normal,
    /// <summary>Se agoto el tiempo maximo de transmision.</summary>
    TiempoAgotado,
    /// <summary>Quien transmitia dejo de dar senales de vida.</summary>
    SinLatido,
    /// <summary>Salto una excepcion durante la transmision.</summary>
    Excepcion,
    /// <summary>La aplicacion se esta cerrando.</summary>
    Cierre,
    /// <summary>Se cancelo la transmision.</summary>
    Cancelado,
    /// <summary>
    /// Se perdio la comunicacion con el equipo en plena transmision. Es el caso peligroso:
    /// si el equipo desaparece con el PTT puesto, nadie va a soltarlo salvo nosotros.
    /// </summary>
    EquipoPerdido,
    /// <summary>El operador ha pedido parar.</summary>
    Panico,
}

/// <summary>Una transmision en curso. Soltar el PTT es liberar este objeto.</summary>
public interface ITransmisionEnCurso : IAsyncDisposable
{
    /// <summary>
    /// Avisa de que quien transmite sigue vivo. Si deja de llamarse durante mas tiempo del
    /// tolerado, el vigilante suelta el PTT por su cuenta.
    /// </summary>
    void Latir();

    /// <summary>El PTT sigue pedido.</summary>
    bool EnAntena { get; }
}

/// <summary>
/// Unico camino permitido para transmitir.
/// </summary>
/// <remarks>
/// Existe para que sea <b>imposible</b> dejar el PTT pegado. El vigilante suelta el PTT
/// cuando se libera la transmision, cuando se agota el tiempo maximo, cuando quien transmite
/// deja de latir, cuando salta una excepcion y cuando la aplicacion se cierra —incluido el
/// cierre por fallo—. Cualquier codigo que quiera poner el equipo en antena pasa por aqui;
/// llamar a <see cref="IControlEquipo.PonerPttAsync"/> por fuera es un error.
///
/// El motivo no es la elegancia: un PTT que no se suelta quema la etapa final del equipo o el
/// amplificador. Este vigilante tiene que existir y estar probado <b>antes</b> de la primera
/// transmision real, no despues.
/// </remarks>
public interface IVigilantePtt
{
    /// <summary>Tiempo maximo que se permite estar en antena de una vez.</summary>
    TimeSpan TiempoMaximo { get; }

    /// <summary>Tiempo sin latido tras el cual se suelta el PTT.</summary>
    TimeSpan TiempoSinLatido { get; }

    /// <summary>Hay una transmision en curso.</summary>
    bool EnAntena { get; }

    /// <summary>Salta cada vez que se suelta el PTT, diciendo por que.</summary>
    event EventHandler<MotivoDeSuelta>? PttSoltado;

    /// <summary>
    /// Pide el PTT y devuelve el objeto que lo mantiene. Liberarlo suelta el PTT.
    /// </summary>
    /// <param name="motivo">Para que se transmite, para el registro.</param>
    /// <param name="ct">Testigo de cancelacion; al cancelarse, se suelta el PTT.</param>
    Task<ITransmisionEnCurso> PedirAntenaAsync(string motivo, CancellationToken ct = default);

    /// <summary>
    /// Suelta el PTT ahora mismo, pase lo que pase. Es el boton de panico del operador y
    /// tambien lo que se llama al cerrar la aplicacion.
    /// </summary>
    Task SoltarYaAsync(MotivoDeSuelta motivo = MotivoDeSuelta.Panico);
}
