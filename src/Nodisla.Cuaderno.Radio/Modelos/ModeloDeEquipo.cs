using Nodisla.Cuaderno.Idiomas;

namespace Nodisla.Cuaderno.Radio.Modelos;

/// <summary>Fabricante del equipo.</summary>
public enum Fabricante
{
    /// <summary>Yaesu.</summary>
    Yaesu,

    /// <summary>ICOM.</summary>
    Icom,
}

/// <summary>Juego de ordenes con el que se habla con el equipo.</summary>
public enum ProtocoloCat
{
    /// <summary>
    /// CAT nuevo de Yaesu: texto ASCII terminado en <c>;</c> (FT-710, FTDX10, FTDX101, FT-991,
    /// FT-891, FTDX3000, FTDX1200, FTDX5000). Se identifica con <c>ID;</c>.
    /// </summary>
    YaesuAscii,

    /// <summary>
    /// CAT antiguo de Yaesu: bloques binarios de 5 bytes (FT-817/818/857/897). No tiene orden
    /// de identificacion: el modelo lo elige el operador.
    /// </summary>
    YaesuBinario,

    /// <summary>
    /// CI-V de ICOM: tramas binarias <c>FE FE dir ctl ... FD</c>. Se identifica con la
    /// orden <c>19 00</c>, que devuelve la direccion CI-V del equipo.
    /// </summary>
    IcomCiv,
}

/// <summary>Lo que un modelo sabe hacer, segun su manual.</summary>
/// <remarks>
/// Es el tope de lo posible. Lo que el control ofrece de verdad lo dice el propio control al
/// conectar (<c>IEquipoAvanzado.Mandos</c>, <c>IEquipoConTeclas.Teclas</c>): si el equipo
/// contesta <c>?;</c> a una orden, ese mando no aparece aunque el manual lo prometa.
/// </remarks>
/// <param name="DosVfos">Tiene VFO A y B manejables por CAT.</param>
/// <param name="DosReceptores">Tiene doble recepcion real (FTDX101, FTDX5000, IC-7610...).</param>
/// <param name="Analizador">Tiene analizador de espectro accesible desde el ordenador.</param>
/// <param name="Sintonizador">Tiene acoplador de antena interno que se puede lanzar por CAT.</param>
/// <param name="Memorias">Canales de memoria legibles por CAT (0 si no se pueden leer).</param>
/// <param name="Mandos">Hay mandos (filtros, ruido, ganancias...) por CAT, no solo frecuencia y modo.</param>
/// <param name="Encendido">Se puede encender y apagar por CAT.</param>
/// <param name="HerciosMinimo">Frecuencia mas baja que se puede poner.</param>
/// <param name="HerciosMaximo">Frecuencia mas alta que se puede poner.</param>
public sealed record CapacidadesDelModelo(
    bool DosVfos,
    bool DosReceptores,
    bool Analizador,
    bool Sintonizador,
    int Memorias,
    bool Mandos,
    bool Encendido,
    long HerciosMinimo,
    long HerciosMaximo);

/// <summary>Un modelo concreto de equipo que el cuaderno sabe manejar.</summary>
/// <param name="Clave">
/// Identificador estable que se guarda en los ajustes: <c>fabricante-modelo</c> en minusculas,
/// sin espacios ni guiones internos del modelo (<c>yaesu-ft710</c>, <c>icom-ic7300</c>).
/// </param>
/// <param name="Fabricante">Fabricante.</param>
/// <param name="Nombre">Nombre para el operador (<c>FT-710</c>, <c>IC-7300</c>).</param>
/// <param name="Protocolo">Juego de ordenes.</param>
/// <param name="IdentificadorYaesu">
/// Lo que contesta a <c>ID;</c> (solo CAT nuevo de Yaesu), por ejemplo <c>0800</c>.
/// </param>
/// <param name="DireccionCiv">Direccion CI-V de fabrica (solo ICOM), por ejemplo <c>0x94</c>.</param>
/// <param name="Velocidades">Velocidades de puerto que admite, de la mas probable a la menos.</param>
/// <param name="Capacidades">Lo que sabe hacer segun el manual.</param>
/// <param name="Frontal">
/// Nombre de la clase de la vista del frontal dibujado (<c>FrontalFt710</c>,
/// <c>FrontalIc7300</c>), o nulo para el frontal generico. La interfaz busca esa clase en
/// <c>Nodisla.Cuaderno.Ui.Vistas.Frontales</c> y luego en <c>Nodisla.Cuaderno.Ui.Vistas</c>; si
/// no existe, pone el generico.
/// </param>
/// <param name="ProbadoConRadio">
/// Verdadero solo si se ha validado contra una radio real. Los demas estan programados segun
/// el manual y la interfaz lo dice.
/// </param>
/// <param name="Notas">Lo que hay que confirmar con la radio real, o avisos del manual.</param>
public sealed record ModeloDeEquipo(
    string Clave,
    Fabricante Fabricante,
    string Nombre,
    ProtocoloCat Protocolo,
    string? IdentificadorYaesu,
    byte? DireccionCiv,
    IReadOnlyList<int> Velocidades,
    CapacidadesDelModelo Capacidades,
    string? Frontal,
    bool ProbadoConRadio = false,
    string? Notas = null)
{
    /// <summary>Nombre completo: <c>Yaesu FT-710</c>.</summary>
    public string NombreCompleto => $"{Fabricante} {Nombre}";

    /// <summary>Velocidad que se prueba primero.</summary>
    public int VelocidadPorOmision => Velocidades.Count > 0 ? Velocidades[0] : 9600;

    /// <summary>Para el desplegable de Ajustes.</summary>
    public string ParaElDesplegable => ProbadoConRadio ? Nombre : Textos.F("Servicios.Radio.SinProbar", Nombre);

    /// <inheritdoc />
    public override string ToString() => NombreCompleto;
}
