using System.Globalization;

namespace Nodisla.Cuaderno.Dominio.Dxcc;

/// <summary>
/// Tabla de paises ya cargada en memoria: entidades, excepciones nominales y arbol de
/// prefijos con sus ventanas temporales.
/// </summary>
/// <remarks>
/// El texto de origen es el recurso <c>recursos/paises-nodisla.tsv</c>, que el guion
/// <c>recursos/generar-paises.py</c> fabrica a partir del fichero de paises de Log4OM
/// (que aporta el historico) y de <c>cty.dat</c> de AD1C (que aporta las zonas finas y
/// las excepciones nominales). Ver <c>recursos/LEEME.md</c>.
/// </remarks>
public sealed class CatalogoDxcc
{
    private readonly Dictionary<int, EntidadDxcc> _porNumero;
    private readonly Dictionary<string, ReglaDxcc> _excepciones;
    private readonly ArbolPrefijos _arbol;

    private CatalogoDxcc(
        Dictionary<int, EntidadDxcc> porNumero,
        Dictionary<string, ReglaDxcc> excepciones,
        ArbolPrefijos arbol,
        IReadOnlyList<EntidadDxcc> todas,
        DateOnly? fechaDeLosDatos,
        int version)
    {
        _porNumero = porNumero;
        _excepciones = excepciones;
        _arbol = arbol;
        Todas = todas;
        FechaDeLosDatos = fechaDeLosDatos;
        Version = version;
    }

    /// <summary>Catalogo cargado del recurso incrustado. Se construye una sola vez.</summary>
    public static CatalogoDxcc Predeterminado => Perezoso.Value;

    private static readonly Lazy<CatalogoDxcc> Perezoso =
        new(() => Cargar(DatosPaises.Texto), LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Todas las entidades, vigentes y borradas, ordenadas por numero DXCC.</summary>
    public IReadOnlyList<EntidadDxcc> Todas { get; }

    /// <summary>Fecha de edicion de los datos de origen.</summary>
    public DateOnly? FechaDeLosDatos { get; }

    /// <summary>Version del formato del recurso.</summary>
    public int Version { get; }

    /// <summary>Numero de excepciones nominales cargadas.</summary>
    public int NumeroDeExcepciones => _excepciones.Count;

    /// <summary>Numero de nodos del arbol de prefijos.</summary>
    public int NodosDelArbol => _arbol.Nodos;

    /// <summary>Busca una entidad por su numero DXCC.</summary>
    public EntidadDxcc? PorNumero(int numero) => _porNumero.GetValueOrDefault(numero);

    /// <summary>Excepcion nominal para un indicativo completo, si la hay.</summary>
    internal ReglaDxcc? Excepcion(string indicativo) => _excepciones.GetValueOrDefault(indicativo);

    /// <summary>Arbol de prefijos para la busqueda por prefijo mas largo.</summary>
    internal ArbolPrefijos Arbol => _arbol;

    /// <summary>Lee un catalogo del formato de texto descrito en <c>recursos/LEEME.md</c>.</summary>
    public static CatalogoDxcc Cargar(string texto)
    {
        ArgumentNullException.ThrowIfNull(texto);

        var entidades = new Dictionary<int, EntidadDxcc>();
        var excepciones = new Dictionary<string, ReglaDxcc>(StringComparer.Ordinal);
        var prefijos = new Dictionary<string, List<ReglaDxcc>>(StringComparer.Ordinal);
        DateOnly? fechaDatos = null;
        var version = 0;

        foreach (var linea in texto.Split('\n'))
        {
            var l = linea.EndsWith('\r') ? linea[..^1] : linea;
            if (l.Length == 0 || l[0] == '#') continue;
            var c = l.Split('\t');

            switch (c[0])
            {
                case "V":
                    version = Entero(Campo(c, 1)) ?? 0;
                    fechaDatos = Fecha(Campo(c, 2));
                    break;

                case "E":
                    var e = LeerEntidad(c);
                    entidades[e.Numero] = e;
                    break;

                case "X":
                    var x = LeerRegla(c, OrigenRegla.Excepcion);
                    excepciones.TryAdd(x.Clave, x);
                    break;

                case "P":
                case "H":
                    var r = LeerRegla(c, c[0] == "P" ? OrigenRegla.Prefijo : OrigenRegla.Historica);
                    if (!prefijos.TryGetValue(r.Clave, out var lista))
                    {
                        lista = [];
                        prefijos[r.Clave] = lista;
                    }
                    lista.Add(r);
                    break;

                default:
                    break;
            }
        }

        var todas = entidades.Values.OrderBy(v => v.Numero).ToArray();
        var arbol = ArbolPrefijos.Construir(prefijos);
        return new CatalogoDxcc(entidades, excepciones, arbol, todas, fechaDatos, version);
    }

    private static EntidadDxcc LeerEntidad(string[] c) => new()
    {
        Numero = Entero(Campo(c, 1)) ?? 0,
        Nombre = Campo(c, 2),
        NombreEspanol = Texto(Campo(c, 3)),
        PrefijoPrincipal = Campo(c, 4),
        Continente = Campo(c, 5),
        ZonaCq = Entero(Campo(c, 6)) ?? 0,
        ZonaItu = Entero(Campo(c, 7)) ?? 0,
        Latitud = Real(Campo(c, 8)) ?? 0,
        Longitud = Real(Campo(c, 9)) ?? 0,
        DesfaseUtc = Real(Campo(c, 10)) ?? 0,
        ValidaDesde = Fecha(Campo(c, 11)),
        ValidaHasta = Fecha(Campo(c, 12)),
    };

    private static ReglaDxcc LeerRegla(string[] c, OrigenRegla origen) => new()
    {
        Clave = Campo(c, 1),
        Dxcc = Entero(Campo(c, 2)) ?? 0,
        ZonaCq = Entero(Campo(c, 3)),
        ZonaItu = Entero(Campo(c, 4)),
        Continente = Texto(Campo(c, 5)),
        Latitud = Real(Campo(c, 6)),
        Longitud = Real(Campo(c, 7)),
        Desde = Fecha(Campo(c, 8)),
        Hasta = Fecha(Campo(c, 9)),
        Origen = origen,
    };

    /// <summary>Campo del registro, o cadena vacia si la linea venia recortada.</summary>
    private static string Campo(string[] c, int i) => i < c.Length ? c[i] : string.Empty;

    private static string? Texto(string v) => v.Length == 0 ? null : v;

    private static int? Entero(string v) =>
        int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;

    private static double? Real(string v) =>
        double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : null;

    private static DateOnly? Fecha(string v) =>
        DateOnly.TryParseExact(v, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var f) ? f : null;
}
