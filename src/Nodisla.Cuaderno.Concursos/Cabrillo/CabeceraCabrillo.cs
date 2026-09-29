using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Concursos.Cabrillo;

/// <summary>Categoria por operadores, tal y como la nombra Cabrillo.</summary>
public enum CategoriaOperador
{
    /// <summary><c>SINGLE-OP</c>.</summary>
    Individual,
    /// <summary><c>MULTI-OP</c>.</summary>
    Multioperador,
    /// <summary><c>CHECKLOG</c>: el log se manda solo para comprobar los de los demas.</summary>
    Comprobacion,
}

/// <summary>Categoria por transmisores.</summary>
public enum CategoriaTransmisor
{
    /// <summary><c>ONE</c>.</summary>
    Uno,
    /// <summary><c>TWO</c>.</summary>
    Dos,
    /// <summary><c>LIMITED</c>.</summary>
    Limitado,
    /// <summary><c>UNLIMITED</c>.</summary>
    Ilimitado,
    /// <summary><c>SWL</c>.</summary>
    Escucha,
}

/// <summary>Categoria por potencia.</summary>
public enum CategoriaPotencia
{
    /// <summary><c>HIGH</c>.</summary>
    Alta,
    /// <summary><c>LOW</c>.</summary>
    Baja,
    /// <summary><c>QRP</c>.</summary>
    Qrp,
}

/// <summary>Si se han usado ayudas como el cluster.</summary>
public enum CategoriaAsistencia
{
    /// <summary><c>NON-ASSISTED</c>.</summary>
    SinAyuda,
    /// <summary><c>ASSISTED</c>.</summary>
    ConAyuda,
}

/// <summary>Categoria por tipo de estacion.</summary>
public enum CategoriaEstacion
{
    /// <summary><c>FIXED</c>.</summary>
    Fija,
    /// <summary><c>MOBILE</c>.</summary>
    Movil,
    /// <summary><c>PORTABLE</c>.</summary>
    Portable,
    /// <summary><c>ROVER</c>.</summary>
    Itinerante,
    /// <summary><c>EXPEDITION</c>.</summary>
    Expedicion,
    /// <summary><c>HQ</c>.</summary>
    Sede,
    /// <summary><c>SCHOOL</c>.</summary>
    Escuela,
    /// <summary><c>DISTRIBUTED</c>.</summary>
    Distribuida,
}

/// <summary>
/// La cabecera de un fichero Cabrillo version 3.
/// </summary>
/// <remarks>
/// <para>
/// Las etiquetas y sus valores admitidos estan tomados de la especificacion publica de la
/// WWROF, no de memoria. Lo que importa de esta clase es que <b>solo deja escribir valores
/// que existen</b>: las categorias son enumerados y no texto libre. Un
/// <c>CATEGORY-POWER: LOW POWER</c> en vez de <c>LOW</c> hace que el robot del concurso
/// rechace el log entero, y eso se descubre cuando ya es tarde.
/// </para>
/// <para>
/// <c>CATEGORY-BAND</c> y <c>CATEGORY-MODE</c> no se piden aqui: los deduce el exportador de
/// los contactos que realmente se envian, que es donde esta la verdad. Si el operador quiere
/// declarar otra cosa, la pone a mano.
/// </para>
/// </remarks>
public sealed record CabeceraCabrillo
{
    /// <summary>Identificador del concurso, tal y como lo pide el organizador.</summary>
    public required string Contest { get; init; }

    /// <summary>Indicativo con el que se participo.</summary>
    public required Indicativo Indicativo { get; init; }

    /// <summary>Categoria por operadores.</summary>
    public CategoriaOperador Operadores { get; init; } = CategoriaOperador.Individual;

    /// <summary>Categoria por transmisores. Solo hace falta en multioperador.</summary>
    public CategoriaTransmisor? Transmisores { get; init; }

    /// <summary>Categoria por potencia.</summary>
    public CategoriaPotencia Potencia { get; init; } = CategoriaPotencia.Baja;

    /// <summary>Si se usaron ayudas.</summary>
    public CategoriaAsistencia Asistencia { get; init; } = CategoriaAsistencia.SinAyuda;

    /// <summary>Tipo de estacion.</summary>
    public CategoriaEstacion Estacion { get; init; } = CategoriaEstacion.Fija;

    /// <summary>Banda declarada. Si no se pone, la deduce el exportador.</summary>
    public string? Banda { get; init; }

    /// <summary>Modo declarado. Si no se pone, lo deduce el exportador.</summary>
    public string? Modo { get; init; }

    /// <summary>Categoria de tiempo, para los concursos que la tienen.</summary>
    public string? Tiempo { get; init; }

    /// <summary>Categoria superpuesta: <c>CLASSIC</c>, <c>ROOKIE</c>, <c>YOUTH</c>…</summary>
    public string? Superpuesta { get; init; }

    /// <summary>Ubicacion: seccion ARRL o <c>DX</c> para el resto del mundo.</summary>
    public string? Ubicacion { get; init; }

    /// <summary>Puntuacion reclamada. Va sin puntos ni comas.</summary>
    public long? PuntuacionReclamada { get; init; }

    /// <summary>Club con el que se compite.</summary>
    public string? Club { get; init; }

    /// <summary>Nombre del titular.</summary>
    public string? Nombre { get; init; }

    /// <summary>Correo electronico.</summary>
    public string? Correo { get; init; }

    /// <summary>Localizador de la estacion.</summary>
    public Locator Locator { get; init; }

    /// <summary>Direccion postal, una linea por elemento.</summary>
    public IReadOnlyList<string> Direccion { get; init; } = [];

    /// <summary>Localidad.</summary>
    public string? Localidad { get; init; }

    /// <summary>Provincia o estado.</summary>
    public string? Provincia { get; init; }

    /// <summary>Codigo postal.</summary>
    public string? CodigoPostal { get; init; }

    /// <summary>Pais.</summary>
    public string? Pais { get; init; }

    /// <summary>Indicativos de los operadores, en multioperador.</summary>
    public IReadOnlyList<string> Operadoras { get; init; } = [];

    /// <summary>Comentarios libres para el organizador.</summary>
    public IReadOnlyList<string> Comentarios { get; init; } = [];

    /// <summary>Se quiere diploma de participacion.</summary>
    public bool? Certificado { get; init; }

    /// <summary>Programa que genero el fichero.</summary>
    public string CreadoPor { get; init; } = "Cuaderno NODISLA";

    /// <summary>
    /// Numero de transmisor que se escribe al final de cada linea <c>QSO:</c>.
    /// </summary>
    /// <remarks>
    /// La especificacion dice que no se usa en monooperador ni en multi-multi, pero casi todos
    /// los programas escriben un cero y todos los robots lo aceptan. Se deja en cero por
    /// omision y se puede quitar poniendo nulo.
    /// </remarks>
    public int? NumeroDeTransmisor { get; init; } = 0;
}
