using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Aplicacion.Puertos;

/// <summary>Por que se cuenta un contacto para un diploma.</summary>
public enum ClaseDeDiploma
{
    /// <summary>Cuenta el indicativo: WPX, indicativos especiales.</summary>
    PorIndicativo,
    /// <summary>Cuenta un campo del contacto: DXCC, zonas, estados, cuadriculas.</summary>
    PorCampo,
    /// <summary>Cuenta una referencia de un programa: IOTA, SOTA, POTA, WWFF, castillos.</summary>
    PorReferencia,
}

/// <summary>Un diploma y sus reglas.</summary>
/// <param name="Codigo">Clave del diploma, por ejemplo <c>DXCC</c>.</param>
/// <param name="Nombre">Nombre para mostrar, en espanol si lo hay.</param>
/// <param name="Clase">Como cuenta.</param>
/// <param name="Gestor">Entidad que lo otorga.</param>
/// <param name="Web">Pagina del diploma.</param>
public sealed record Diploma(
    string Codigo,
    string Nombre,
    ClaseDeDiploma Clase,
    string? Gestor,
    Uri? Web);

/// <summary>
/// Familia de modos que exige una variante de diploma.
/// </summary>
/// <remarks>
/// Hace falta porque los reglamentos hablan de <b>fonia</b>, <b>telegrafia</b> y <b>digitales</b>,
/// no de modos ADIF sueltos. Una variante de fonia acepta SSB, AM y FM; exigir un modo concreto
/// dejaria fuera contactos que cuentan.
/// </remarks>
public enum ClaseDeModo
{
    /// <summary>Cualquier modo.</summary>
    Cualquiera,
    /// <summary>Telegrafia.</summary>
    Telegrafia,
    /// <summary>Fonia: SSB, AM, FM y voz digital cuando el diploma la admita.</summary>
    Fonia,
    /// <summary>Modos digitales.</summary>
    Digital,
}

/// <summary>Como hay que confirmar un contacto para que cuente.</summary>
public enum ExigenciaDeConfirmacion
{
    /// <summary>Basta con haberlo trabajado.</summary>
    Trabajado,
    /// <summary>Hace falta confirmacion por cualquier via.</summary>
    Confirmado,
    /// <summary>Solo cuenta la confirmacion verificada por el servicio.</summary>
    Verificado,
}

/// <summary>Una variante de un diploma: por banda, por modo, mixto…</summary>
/// <param name="Codigo">Clave del diploma al que pertenece.</param>
/// <param name="Variante">Nombre de la variante, por ejemplo <c>20m</c> o <c>CW</c>.</param>
/// <param name="Banda">Banda exigida, o vacia si vale cualquiera.</param>
/// <param name="Modo">
/// Modo ADIF exigido, o vacio si la variante no baja a ese detalle. Para las variantes que
/// hablan de familias —fonia, telegrafia, digitales— manda <see cref="Clase"/>.
/// </param>
/// <param name="Exigencia">Que confirmacion hace falta.</param>
/// <param name="MediosValidos">
/// Vias de confirmacion que acepta. Vacio significa que vale cualquiera. Importa: hay diplomas
/// que solo admiten LoTW y otros que solo admiten tarjeta en papel.
/// </param>
/// <param name="Objetivo">Cuantas referencias distintas hacen falta, si el diploma lo fija.</param>
public sealed record VarianteDeDiploma(
    string Codigo,
    string Variante,
    Banda Banda,
    Modo Modo,
    ExigenciaDeConfirmacion Exigencia,
    IReadOnlyList<MedioDeConfirmacion> MediosValidos,
    int? Objetivo)
{
    /// <summary>Familia de modos que exige la variante.</summary>
    public ClaseDeModo Clase { get; init; } = ClaseDeModo.Cualquiera;
}

/// <summary>Como va un diploma.</summary>
/// <param name="Codigo">Diploma.</param>
/// <param name="Variante">Variante.</param>
/// <param name="Trabajadas">Referencias distintas trabajadas.</param>
/// <param name="Confirmadas">De esas, cuantas estan confirmadas como el diploma exige.</param>
/// <param name="Objetivo">Cuantas hacen falta, si se sabe.</param>
/// <param name="CalculadoUtc">Cuando se calculo.</param>
public sealed record ProgresoDeDiploma(
    string Codigo,
    string Variante,
    int Trabajadas,
    int Confirmadas,
    int? Objetivo,
    DateTimeOffset CalculadoUtc)
{
    /// <summary>
    /// El progreso es una <b>cota inferior</b> o no se puede calcular, y esta es la razon.
    /// Nulo cuando la cifra es firme.
    /// </summary>
    /// <remarks>
    /// Es la traduccion al contrato de la regla del modulo: ante la duda se cuenta de menos y
    /// <b>se dice por que</b>. Hay diplomas que valida su gestor contra su propia base y otros
    /// que cuentan por campos que el cuaderno no guarda. Ensenar un numero sin ese aviso
    /// llevaria al operador a pedir un diploma que no tiene, o a no pedir el que si.
    /// </remarks>
    public string? PorQueNoEsFirme { get; init; }

    /// <summary>La cifra es firme y se puede usar para pedir el diploma.</summary>
    public bool EsFirme => PorQueNoEsFirme is null;

    /// <summary>Parte del objetivo cubierta, de 0 a 1. Nulo si el diploma no fija objetivo.</summary>
    public double? Fraccion => Objetivo is > 0 ? Math.Min(1.0, (double)Confirmadas / Objetivo.Value) : null;

    /// <summary>Cuantas faltan para el objetivo.</summary>
    public int? Faltan => Objetivo is > 0 ? Math.Max(0, Objetivo.Value - Confirmadas) : null;
}

/// <summary>Una referencia concreta y como esta.</summary>
/// <param name="Referencia">Codigo de la referencia: una entidad DXCC, una isla, un parque.</param>
/// <param name="Nombre">Nombre para mostrar.</param>
/// <param name="Trabajada">Se ha trabajado al menos una vez.</param>
/// <param name="Confirmada">Esta confirmada como el diploma exige.</param>
/// <param name="PrimerQsoId">Contacto que la aporto, para poder ir a el.</param>
public sealed record EstadoDeReferencia(
    string Referencia,
    string? Nombre,
    bool Trabajada,
    bool Confirmada,
    long? PrimerQsoId);

/// <summary>
/// El motor de diplomas.
/// </summary>
/// <remarks>
/// <para>
/// Es el modulo mas pesado del programa: el original lleva 87 diplomas, 315 variantes y
/// <b>553.064 referencias</b>. Por eso el calculo no puede hacerse recorriendo el cuaderno en
/// memoria, que es lo que hace Log4OM por haber guardado las confirmaciones dentro de un JSON.
/// Aqui las confirmaciones y las referencias son tablas indexadas: el progreso se resuelve con
/// consultas, y lo caro se cachea.
/// </para>
/// <para>
/// Un diploma mal calculado es peor que no calcularlo: lleva al operador a pedir un diploma que
/// no tiene, o a no pedir el que si. Ante la duda, <b>se cuenta de menos y se dice por que</b>.
/// </para>
/// </remarks>
public interface IDiplomas
{
    /// <summary>Todos los diplomas conocidos.</summary>
    Task<IReadOnlyList<Diploma>> CatalogoAsync(CancellationToken ct = default);

    /// <summary>Variantes de un diploma.</summary>
    Task<IReadOnlyList<VarianteDeDiploma>> VariantesAsync(string codigo, CancellationToken ct = default);

    /// <summary>Progreso de una variante.</summary>
    Task<ProgresoDeDiploma> ProgresoAsync(string codigo, string variante, CancellationToken ct = default);

    /// <summary>Diplomas que el operador ha marcado como suyos.</summary>
    Task<IReadOnlyList<string>> MisDiplomasAsync(CancellationToken ct = default);

    /// <summary>
    /// Fija que diplomas sigue el operador.
    /// </summary>
    /// <param name="codigos">Claves de los diplomas elegidos. Lista vacia = ninguno.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <remarks>
    /// La eleccion es del operador y la guarda el motor, no la pantalla. Si viviera en la
    /// interfaz, cualquier otra cosa que quisiera saber que diplomas sigue —un aviso al teclear
    /// un indicativo, un resumen al arrancar— tendria que preguntarselo a la ventana, que es
    /// justo al reves de como deben ir las dependencias.
    ///
    /// Con la lista vacia, el motor <b>no calcula nada</b>: computar los 87 diplomas por si
    /// acaso es trabajo tirado y ademas ensena cifras que el operador no ha pedido.
    /// </remarks>
    Task FijarMisDiplomasAsync(IReadOnlyList<string> codigos, CancellationToken ct = default);

    /// <summary>Progreso de todos los diplomas que el operador tenga marcados como propios.</summary>
    Task<IReadOnlyList<ProgresoDeDiploma>> ProgresoDeMisDiplomasAsync(CancellationToken ct = default);

    /// <summary>
    /// Estado de cada referencia de una variante: lo trabajado, lo confirmado y lo que falta.
    /// </summary>
    /// <param name="codigo">Diploma.</param>
    /// <param name="variante">Variante.</param>
    /// <param name="desplazamiento">Cuantas referencias saltar.</param>
    /// <param name="limite">Cuantas devolver.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <remarks>
    /// Va paginado porque hay diplomas enormes: SOTA tiene mas de 182.000 referencias. Un tope
    /// escondido en la implementacion dejaria al operador creyendo que ha visto la lista entera.
    /// </remarks>
    Task<Pagina<EstadoDeReferencia>> DetalleAsync(
        string codigo,
        string variante,
        int desplazamiento,
        int limite,
        CancellationToken ct = default);

    /// <summary>
    /// Que aportaria este contacto a los diplomas del operador.
    /// </summary>
    /// <remarks>
    /// Es la consulta que se dispara al teclear un indicativo o al llegar un anuncio del
    /// cluster, asi que tiene que ser inmediata. De ella sale el aviso de «entidad nueva» y el
    /// resaltado de los spots.
    /// </remarks>
    Task<IReadOnlyList<string>> QueAportaAsync(
        Indicativo indicativo,
        Banda banda,
        Modo modo,
        CancellationToken ct = default);

    /// <summary>Recalcula el progreso desde cero y renueva la cache.</summary>
    Task RecalcularAsync(IProgress<ProgresoDeSincronizacion>? progreso = null, CancellationToken ct = default);
}
