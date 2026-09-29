namespace Nodisla.Cuaderno.Concursos.Catalogo;

/// <summary>
/// Los concursos que conoce el programa, buscables por identificador y por nombre.
/// </summary>
/// <remarks>
/// Se construye una sola vez y se comparte: son doscientos y pico registros pequenos y no
/// tiene sentido releer el recurso cada vez que alguien abre el desplegable de concursos.
/// </remarks>
public sealed class CatalogoDeConcursos
{
    private readonly Dictionary<string, ReglaDeConcurso> _porCodigo;
    private static readonly Lazy<CatalogoDeConcursos> Incrustado = new(LectorDelRecurso.Leer, isThreadSafe: true);

    /// <summary>Construye un catalogo a partir de una lista de concursos.</summary>
    /// <param name="cabecera">De cuando son los datos.</param>
    /// <param name="concursos">Los concursos, con sus reglas ya enganchadas.</param>
    public CatalogoDeConcursos(CabeceraDelCatalogo cabecera, IReadOnlyList<ReglaDeConcurso> concursos)
    {
        ArgumentNullException.ThrowIfNull(cabecera);
        ArgumentNullException.ThrowIfNull(concursos);
        Cabecera = cabecera;
        Concursos = concursos;
        _porCodigo = new Dictionary<string, ReglaDeConcurso>(concursos.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var concurso in concursos) _porCodigo[concurso.Codigo] = concurso;
    }

    /// <summary>El catalogo que viaja dentro del ensamblado. Se carga la primera vez que se pide.</summary>
    public static CatalogoDeConcursos Predeterminado => Incrustado.Value;

    /// <summary>De cuando son los datos y cuantos hay.</summary>
    public CabeceraDelCatalogo Cabecera { get; }

    /// <summary>Todos los concursos, ordenados por identificador.</summary>
    public IReadOnlyList<ReglaDeConcurso> Concursos { get; }

    /// <summary>Los que traen reglas escritas y por tanto el programa sabe puntuar.</summary>
    public IEnumerable<ReglaDeConcurso> ConReglas =>
        Concursos.Where(c => c.Estado != EstadoDeLasReglas.SoloCatalogo);

    /// <summary>Busca un concurso por su identificador Cabrillo.</summary>
    /// <param name="codigo">Identificador, por ejemplo <c>CQ-WW-CW</c>.</param>
    /// <returns>El concurso, o nulo si no esta en el catalogo.</returns>
    public ReglaDeConcurso? Buscar(string? codigo) =>
        codigo is { Length: > 0 } && _porCodigo.TryGetValue(codigo, out var regla) ? regla : null;

    /// <summary>
    /// Busca un concurso por su identificador y, si no esta, se inventa uno de catalogo.
    /// </summary>
    /// <remarks>
    /// Un concurso desconocido no puede impedir que el operador registre contactos. Se
    /// devuelve una regla vacia con ese identificador, que etiqueta bien el contacto y
    /// exporta Cabrillo, pero no puntua.
    /// </remarks>
    /// <param name="codigo">Identificador del concurso.</param>
    /// <returns>El concurso del catalogo o uno minimo con ese identificador.</returns>
    public ReglaDeConcurso BuscarOMinimo(string codigo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(codigo);
        return Buscar(codigo)
            ?? new ReglaDeConcurso(codigo.Trim().ToUpperInvariant(), codigo.Trim(), null, EstadoDeLasReglas.SoloCatalogo);
    }

    /// <summary>Busca concursos cuyo nombre o identificador contenga el texto.</summary>
    /// <param name="texto">Lo que ha escrito el operador.</param>
    /// <param name="maximo">Cuantos devolver como mucho.</param>
    /// <returns>
    /// Los que casan, primero los que empiezan por ese texto y despues los que solo lo
    /// contienen, que es el orden en que el operador espera verlos.
    /// </returns>
    public IReadOnlyList<ReglaDeConcurso> Buscar(string? texto, int maximo)
    {
        if (string.IsNullOrWhiteSpace(texto)) return Concursos.Take(maximo).ToArray();
        var aguja = texto.Trim();
        return Concursos
            .Select(c => (Concurso: c, Peso: Peso(c, aguja)))
            .Where(x => x.Peso > 0)
            .OrderByDescending(x => x.Peso)
            .ThenBy(x => x.Concurso.Codigo, StringComparer.OrdinalIgnoreCase)
            .Take(maximo)
            .Select(x => x.Concurso)
            .ToArray();
    }

    private static int Peso(ReglaDeConcurso concurso, string aguja)
    {
        if (concurso.Codigo.StartsWith(aguja, StringComparison.OrdinalIgnoreCase)) return 4;
        if (concurso.Nombre.StartsWith(aguja, StringComparison.OrdinalIgnoreCase)) return 3;
        if (concurso.Codigo.Contains(aguja, StringComparison.OrdinalIgnoreCase)) return 2;
        if (concurso.Nombre.Contains(aguja, StringComparison.OrdinalIgnoreCase)) return 1;
        return 0;
    }
}
