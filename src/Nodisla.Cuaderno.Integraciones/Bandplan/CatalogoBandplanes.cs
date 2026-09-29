using System.Globalization;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Integraciones.Bandplan;

/// <summary>
/// Todos los planes de bandas conocidos, ya cargados en memoria.
/// </summary>
/// <remarks>
/// El texto de origen es el recurso <c>recursos/bandplan-nodisla.tsv</c>, que el guion
/// <c>recursos/generar-bandplan.py</c> fabrica a partir de los ficheros XML de Log4OM. El
/// formato esta descrito en <c>recursos/LEEME-bandplan.md</c>. Se incrusta
/// como constante para que la consulta funcione igual en la aplicacion y en las pruebas,
/// sin depender de ningun fichero suelto.
/// </remarks>
public sealed class CatalogoBandplanes
{
    private readonly Dictionary<string, PlanDeBanda> _porIdentificador;
    private readonly Dictionary<int, PlanDeBanda> _porDxcc;
    private readonly Dictionary<int, PlanDeBanda> _porRegion;

    private CatalogoBandplanes(
        IReadOnlyList<PlanDeBanda> todos,
        DateOnly? fechaDeLosDatos,
        int version)
    {
        Todos = todos;
        FechaDeLosDatos = fechaDeLosDatos;
        Version = version;

        _porIdentificador = new Dictionary<string, PlanDeBanda>(StringComparer.OrdinalIgnoreCase);
        _porDxcc = [];
        _porRegion = [];
        foreach (var p in todos)
        {
            _porIdentificador[p.Identificador] = p;
            if (p.EsNacional) _porDxcc[p.Dxcc] = p;
            else _porRegion[p.Region] = p;
        }
    }

    /// <summary>Catalogo cargado del recurso incrustado. Se construye una sola vez.</summary>
    public static CatalogoBandplanes Predeterminado => Perezoso.Value;

    private static readonly Lazy<CatalogoBandplanes> Perezoso =
        new(() => Cargar(DatosBandplan.Texto), LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Todos los planes: primero los de region IARU, despues los nacionales.</summary>
    public IReadOnlyList<PlanDeBanda> Todos { get; }

    /// <summary>Fecha de edicion de los ficheros de origen.</summary>
    public DateOnly? FechaDeLosDatos { get; }

    /// <summary>Version del formato del recurso.</summary>
    public int Version { get; }

    /// <summary>Plan por su identificador, por ejemplo <c>r1</c>. Nulo si no existe.</summary>
    public PlanDeBanda? PorIdentificador(string identificador) =>
        _porIdentificador.GetValueOrDefault(identificador);

    /// <summary>Plan de una region IARU. Nulo si esa region no esta en el recurso.</summary>
    public PlanDeBanda? PorRegion(int region) => _porRegion.GetValueOrDefault(region);

    /// <summary>Plan nacional de una entidad DXCC, si lo hay.</summary>
    public PlanDeBanda? PorDxcc(int dxcc) => _porDxcc.GetValueOrDefault(dxcc);

    /// <summary>
    /// Plan que le toca a una estacion: el de su pais si esta publicado y, si no, el de su
    /// region IARU.
    /// </summary>
    /// <remarks>
    /// EA8 es Region 1, no Region 2, por mucho que las Canarias esten frente a America: el
    /// plan que manda ahi es el de la Region 1.
    /// </remarks>
    public PlanDeBanda? Para(int dxcc, int region) => PorDxcc(dxcc) ?? PorRegion(region);

    /// <summary>Lee un catalogo del formato descrito en <c>recursos/LEEME-bandplan.md</c>.</summary>
    public static CatalogoBandplanes Cargar(string texto)
    {
        ArgumentNullException.ThrowIfNull(texto);

        var planes = new List<PlanDeBanda>();
        var version = 0;
        DateOnly? fechaDatos = null;

        string? identificador = null;
        var region = 0;
        var dxcc = 0;
        var nombre = string.Empty;
        var segmentos = new List<SegmentoBandplan>();
        var senaladas = new List<FrecuenciaSenalada>();

        void CerrarPlan()
        {
            if (identificador is null) return;
            segmentos.Sort(CompararSegmentos);
            senaladas.Sort(static (a, b) => a.Frecuencia.CompareTo(b.Frecuencia));
            planes.Add(new PlanDeBanda(
                identificador, region, dxcc, nombre, [.. segmentos], [.. senaladas]));
            segmentos.Clear();
            senaladas.Clear();
        }

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

                case "P":
                    CerrarPlan();
                    identificador = Campo(c, 1);
                    region = Entero(Campo(c, 2)) ?? 0;
                    dxcc = Entero(Campo(c, 3)) ?? 0;
                    nombre = Campo(c, 4);
                    break;

                case "S":
                    if (identificador is null) break;
                    var s = LeerSegmento(c, identificador);
                    if (s is not null) segmentos.Add(s);
                    break;

                case "F":
                    if (identificador is null) break;
                    var f = LeerSenalada(c);
                    if (f is not null) senaladas.Add(f);
                    break;

                default:
                    break;
            }
        }
        CerrarPlan();

        return new CatalogoBandplanes(planes, fechaDatos, version);
    }

    private static int CompararSegmentos(SegmentoBandplan a, SegmentoBandplan b)
    {
        var porDesde = a.Desde.CompareTo(b.Desde);
        return porDesde != 0 ? porDesde : a.Hasta.CompareTo(b.Hasta);
    }

    private static SegmentoBandplan? LeerSegmento(string[] c, string plan)
    {
        var desde = Kilohercios(Campo(c, 3));
        var hasta = Kilohercios(Campo(c, 4));
        if (desde is null || hasta is null) return null;

        var nombreBanda = Campo(c, 2);
        if (!Banda.TryParse(nombreBanda, out var banda))
        {
            // El plan nombra una banda que ADIF no conoce: se deduce de la frecuencia para
            // no perder el tramo.
            banda = Banda.DesdeFrecuencia(desde.Value);
        }

        return new SegmentoBandplan
        {
            Plan = plan,
            Banda = banda,
            Desde = desde.Value,
            Hasta = hasta.Value,
            Uso = LeerUso(Campo(c, 5)),
            Modulacion = Texto(Campo(c, 6)),
            Clases = LeerClases(Campo(c, 7)),
        };
    }

    private static FrecuenciaSenalada? LeerSenalada(string[] c)
    {
        var khz = Kilohercios(Campo(c, 2));
        var modo = Campo(c, 3);
        if (khz is null || modo.Length == 0) return null;
        return new FrecuenciaSenalada(khz.Value, modo, Texto(Campo(c, 4)));
    }

    /// <summary>
    /// Traduce el nombre de uso del recurso al del contrato.
    /// </summary>
    /// <remarks>
    /// Log4OM solo distingue tres usos. Su <c>DIGITAL</c> se traduce por
    /// <see cref="UsoDelTramo.DigitalEstrecho"/>, que es lo que hay en esos tramos en la
    /// practica; los planes de la IARU separan ademas los modos de banda ancha, pero esa
    /// division no viene en la fuente y no se inventa aqui.
    /// </remarks>
    private static UsoDelTramo LeerUso(string valor) => valor.ToUpperInvariant() switch
    {
        "CW" => UsoDelTramo.Cw,
        "DIGITAL" or "DATA" => UsoDelTramo.DigitalEstrecho,
        "DIGITALANCHO" => UsoDelTramo.DigitalAncho,
        "PHONE" or "FONIA" or "SSB" => UsoDelTramo.Fonia,
        "IMAGEN" => UsoDelTramo.FoniaEImagen,
        "BALIZA" => UsoDelTramo.Baliza,
        "RESERVADO" => UsoDelTramo.Reservado,
        _ => UsoDelTramo.Todos,
    };

    private static IReadOnlyList<string> LeerClases(string valor) => valor.Length == 0
        ? []
        : valor.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string Campo(string[] c, int i) => i < c.Length ? c[i] : string.Empty;

    private static string? Texto(string v) => v.Length == 0 ? null : v;

    private static int? Entero(string v) =>
        int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;

    private static DateOnly? Fecha(string v) =>
        DateOnly.TryParse(v, CultureInfo.InvariantCulture, out var f) ? f : null;

    private static Frecuencia? Kilohercios(string v) =>
        decimal.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var khz) && khz >= 0
            ? Frecuencia.DesdeKilohercios(khz)
            : null;
}
