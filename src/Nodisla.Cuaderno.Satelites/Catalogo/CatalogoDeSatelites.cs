using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Satelites.Catalogo;

/// <summary>En que situacion esta el satelite.</summary>
public enum EstadoDelSatelite
{
    /// <summary>Funciona y se puede contar con el.</summary>
    Activo = 0,

    /// <summary>Va y viene: campanas anunciadas, temporizadores o baterias gastadas.</summary>
    Intermitente,

    /// <summary>Ya no transmite. Se conserva para poder registrar contactos antiguos.</summary>
    Inactivo,
}

/// <summary>Que clase de carga util es el transpondedor.</summary>
public enum ClaseDeTranspondedor
{
    /// <summary>No consta.</summary>
    Desconocida = 0,

    /// <summary>Repetidor de FM: una conversacion cada vez.</summary>
    Fm,

    /// <summary>Transpondedor lineal: un trozo de banda entero, para CW y SSB.</summary>
    Lineal,

    /// <summary>Paquete, digipetidor o telemetria con datos.</summary>
    Digital,

    /// <summary>Solo baliza.</summary>
    Baliza,
}

/// <summary>
/// Un transpondedor concreto de un satelite, con sus frecuencias de subida y de bajada.
/// </summary>
/// <param name="Abreviatura">Abreviatura del satelite (<c>SO-50</c>), la que va en ADIF.</param>
/// <param name="Nombre">Nombre completo.</param>
/// <param name="NumeroCatalogo">Numero NORAD, o cero si no consta.</param>
/// <param name="Estado">Situacion del satelite.</param>
/// <param name="NombreDelTranspondedor">Como se llama este transpondedor.</param>
/// <param name="Clase">Que clase de carga util es.</param>
/// <param name="Modo">Modo en la notacion de ADIF (<c>VU</c>, <c>UV</c>, <c>A</c>, <c>SX</c>…).</param>
/// <param name="SubidaInicio">Extremo bajo del margen de subida, o nulo si no tiene subida.</param>
/// <param name="SubidaFin">Extremo alto del margen de subida.</param>
/// <param name="BajadaInicio">Extremo bajo del margen de bajada.</param>
/// <param name="BajadaFin">Extremo alto del margen de bajada.</param>
/// <param name="Invertido">
/// El transpondedor da la vuelta a la banda: lo que sube por abajo baja por arriba, y la banda
/// lateral cambia. Hace falta saberlo para contestar en el sitio correcto.
/// </param>
/// <param name="SubtonoHz">Subtono CTCSS que exige el repetidor, si exige alguno.</param>
/// <param name="Notas">Lo que conviene saber antes de intentarlo.</param>
public sealed record Transpondedor(
    string Abreviatura,
    string Nombre,
    int NumeroCatalogo,
    EstadoDelSatelite Estado,
    string NombreDelTranspondedor,
    ClaseDeTranspondedor Clase,
    string Modo,
    Frecuencia? SubidaInicio,
    Frecuencia? SubidaFin,
    Frecuencia? BajadaInicio,
    Frecuencia? BajadaFin,
    bool Invertido,
    double? SubtonoHz,
    string Notas)
{
    /// <summary>Centro del margen de subida, que es por donde se empieza a llamar.</summary>
    public Frecuencia? SubidaCentral => Centro(SubidaInicio, SubidaFin);

    /// <summary>Centro del margen de bajada.</summary>
    public Frecuencia? BajadaCentral => Centro(BajadaInicio, BajadaFin);

    private static Frecuencia? Centro(Frecuencia? a, Frecuencia? b) => (a, b) switch
    {
        ({ } x, { } y) => Frecuencia.DesdeMegahercios((x.Megahercios + y.Megahercios) / 2m),
        ({ } x, null) => x,
        (null, { } y) => y,
        _ => null,
    };
}

/// <summary>Un satelite con todos sus transpondedores.</summary>
/// <param name="Abreviatura">Abreviatura, la que va en el campo <c>SAT_NAME</c> de ADIF.</param>
/// <param name="Nombre">Nombre completo.</param>
/// <param name="NumeroCatalogo">Numero NORAD, o cero si no consta.</param>
/// <param name="Estado">Situacion del satelite.</param>
/// <param name="Transpondedores">Sus transpondedores, en el orden del catalogo.</param>
public sealed record SateliteDeAficionado(
    string Abreviatura,
    string Nombre,
    int NumeroCatalogo,
    EstadoDelSatelite Estado,
    IReadOnlyList<Transpondedor> Transpondedores);

/// <summary>
/// Catalogo de satelites de aficionado con sus frecuencias, incrustado en el ensamblado.
/// </summary>
/// <remarks>
/// El catalogo se genera con <c>recursos\satelites\generar-satelites.py</c> y viaja comprimido
/// dentro del ensamblado. No se lee de Log4OM en ejecucion: el programa tiene que funcionar en
/// una maquina donde Log4OM no este instalado.
/// <para>
/// <b>Las frecuencias envejecen.</b> Los satelites cambian de plan de frecuencias, se apagan y
/// se reactivan. Este catalogo es un punto de partida mantenido a mano, no una fuente de
/// autoridad: antes de una actividad conviene mirar el estado en AMSAT.
/// </para>
/// </remarks>
public sealed class CatalogoDeSatelites
{
    private const string NombreDelRecurso =
        "Nodisla.Cuaderno.Satelites.Catalogo.Recursos.catalogo-satelites.tsv.gz";

    private readonly Dictionary<string, SateliteDeAficionado> _porAbreviatura;
    private readonly Dictionary<int, SateliteDeAficionado> _porCatalogo;

    private CatalogoDeSatelites(IReadOnlyList<SateliteDeAficionado> satelites)
    {
        Satelites = satelites;
        _porAbreviatura = satelites.ToDictionary(s => s.Abreviatura, StringComparer.OrdinalIgnoreCase);
        _porCatalogo = satelites
            .Where(s => s.NumeroCatalogo > 0)
            .GroupBy(s => s.NumeroCatalogo)
            .ToDictionary(g => g.Key, g => g.First());
    }

    /// <summary>El catalogo compartido; se lee una sola vez.</summary>
    public static CatalogoDeSatelites Instancia { get; } = Cargar();

    /// <summary>Todos los satelites del catalogo.</summary>
    public IReadOnlyList<SateliteDeAficionado> Satelites { get; }

    /// <summary>Los que se pueden usar hoy, activos o intermitentes.</summary>
    public IEnumerable<SateliteDeAficionado> EnServicio =>
        Satelites.Where(s => s.Estado != EstadoDelSatelite.Inactivo);

    /// <summary>Busca por la abreviatura que se escribe en el cuaderno.</summary>
    /// <param name="abreviatura">Abreviatura, sin distinguir mayusculas.</param>
    /// <returns>El satelite, o <c>null</c> si no esta.</returns>
    public SateliteDeAficionado? PorAbreviatura(string? abreviatura) =>
        !string.IsNullOrWhiteSpace(abreviatura)
        && _porAbreviatura.TryGetValue(abreviatura.Trim(), out var s)
            ? s
            : null;

    /// <summary>Busca por numero NORAD, que es lo que trae el fichero de elementos.</summary>
    /// <param name="numeroCatalogo">Numero NORAD.</param>
    /// <returns>El satelite, o <c>null</c> si no esta.</returns>
    public SateliteDeAficionado? PorNumeroDeCatalogo(int numeroCatalogo) =>
        _porCatalogo.TryGetValue(numeroCatalogo, out var s) ? s : null;

    private static CatalogoDeSatelites Cargar()
    {
        var ensamblado = Assembly.GetExecutingAssembly();
        using var recurso = ensamblado.GetManifestResourceStream(NombreDelRecurso)
            ?? throw new InvalidOperationException(
                $"Falta el recurso «{NombreDelRecurso}». Hay que generarlo con "
                + "recursos\\satelites\\generar-satelites.py.");

        using var descomprimido = new GZipStream(recurso, CompressionMode.Decompress);
        using var lector = new StreamReader(descomprimido);

        var transpondedores = new List<Transpondedor>();
        var primera = true;

        while (lector.ReadLine() is { } linea)
        {
            if (primera)
            {
                primera = false;
                continue;
            }

            if (linea.Length == 0)
            {
                continue;
            }

            var c = linea.Split('\t');
            if (c.Length < 14)
            {
                c = [.. c, .. Enumerable.Repeat(string.Empty, 14 - c.Length)];
            }

            transpondedores.Add(new Transpondedor(
                c[0],
                c[1],
                Entero(c[2]),
                Estado(c[3]),
                c[4],
                Clase(c[5]),
                c[6],
                Mhz(c[7]),
                Mhz(c[8]),
                Mhz(c[9]),
                Mhz(c[10]),
                string.Equals(c[11], "si", StringComparison.OrdinalIgnoreCase),
                Real(c[12]),
                c[13]));
        }

        var satelites = transpondedores
            .GroupBy(t => t.Abreviatura, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var cabeza = g.First();
                var reales = g.Where(t => t.Clase != ClaseDeTranspondedor.Desconocida).ToList();
                return new SateliteDeAficionado(
                    cabeza.Abreviatura, cabeza.Nombre, cabeza.NumeroCatalogo, cabeza.Estado, reales);
            })
            .ToList();

        return new CatalogoDeSatelites(satelites);
    }

    private static int Entero(string s) =>
        int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;

    private static double? Real(string s) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;

    private static Frecuencia? Mhz(string s) =>
        decimal.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v > 0
            ? Frecuencia.DesdeMegahercios(v)
            : null;

    private static EstadoDelSatelite Estado(string s) => s switch
    {
        "activo" => EstadoDelSatelite.Activo,
        "intermitente" => EstadoDelSatelite.Intermitente,
        _ => EstadoDelSatelite.Inactivo,
    };

    private static ClaseDeTranspondedor Clase(string s) => s switch
    {
        "FM" => ClaseDeTranspondedor.Fm,
        "LINEAL" => ClaseDeTranspondedor.Lineal,
        "DIGITAL" => ClaseDeTranspondedor.Digital,
        "BALIZA" => ClaseDeTranspondedor.Baliza,
        _ => ClaseDeTranspondedor.Desconocida,
    };
}
