using System.Globalization;
using Nodisla.Cuaderno.Idiomas;
using Nodisla.Cuaderno.Impresion.Qsl;

namespace Nodisla.Cuaderno.Impresion.Diplomas;

/// <summary>Una fila de la tabla que justifica el diploma: una referencia o un contacto.</summary>
public sealed class FilaDeJustificante
{
    /// <summary>Referencia (DXCC, DME, isla…). Vacia si la fila es un contacto sin mas.</summary>
    public string Referencia { get; set; } = string.Empty;

    /// <summary>Nombre de la referencia.</summary>
    public string? NombreDeReferencia { get; set; }

    /// <summary>Indicativo del contacto.</summary>
    public string Indicativo { get; set; } = string.Empty;

    /// <summary>Inicio del contacto, en UTC.</summary>
    public DateTimeOffset? FechaUtc { get; set; }

    /// <summary>Banda.</summary>
    public string Banda { get; set; } = string.Empty;

    /// <summary>Modo.</summary>
    public string Modo { get; set; } = string.Empty;

    /// <summary>Informe enviado.</summary>
    public string Rst { get; set; } = string.Empty;
}

/// <summary>
/// Lo que se imprime en un diploma concreto: a quien, que diploma, que numero y con que
/// justificantes.
/// </summary>
/// <remarks>
/// Se guarda tal cual (en JSON) en el historial de emitidos: volver a imprimir un diploma
/// emitido hace meses saca exactamente lo mismo aunque el cuaderno haya cambiado.
/// </remarks>
public sealed class DatosDeDiploma
{
    /// <summary>Indicativo del que recibe el diploma.</summary>
    public string Indicativo { get; set; } = string.Empty;

    /// <summary>Su nombre.</summary>
    public string? Nombre { get; set; }

    /// <summary>Nombre del diploma.</summary>
    public string NombreDelDiploma { get; set; } = string.Empty;

    /// <summary>Categoria, nivel o variante (Oro, 20m CW…).</summary>
    public string? Categoria { get; set; }

    /// <summary>Numero correlativo; nulo mientras no se emite.</summary>
    public int? Numero { get; set; }

    /// <summary>Fecha de emision.</summary>
    public DateTimeOffset Fecha { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Numero de referencias; nulo lo cuenta de las filas.</summary>
    public int? Referencias { get; set; }

    /// <summary>Numero de contactos; nulo lo cuenta de las filas.</summary>
    public int? Qsos { get; set; }

    /// <summary>Entidad que concede el diploma (el gestor del catalogo, un club…).</summary>
    public string? Entidad { get; set; }

    /// <summary>Correo del destinatario, si se conoce.</summary>
    public string? Correo { get; set; }

    /// <summary>Las filas de la tabla.</summary>
    public List<FilaDeJustificante> Filas { get; set; } = [];
}

/// <summary>
/// Las variables de los diplomas: las mismas llaves y la misma sustitucion que las QSL
/// (<see cref="VariablesDeQsl.Sustituir"/>), con los datos propios del diploma.
/// </summary>
public static class VariablesDeDiploma
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");

    /// <summary>Las variables del diploma, para la ayuda y el editor.</summary>
    public static IReadOnlyList<(string Nombre, string Descripcion)> Conocidas =>
    [
        ("indicativo", Textos.T("Servicios.Impresion.VariableDiploma.indicativo")),
        ("nombre", Textos.T("Servicios.Impresion.VariableDiploma.nombre")),
        ("diploma", Textos.T("Servicios.Impresion.VariableDiploma.diploma")),
        ("categoria", Textos.T("Servicios.Impresion.VariableDiploma.categoria")),
        ("numero", Textos.T("Servicios.Impresion.VariableDiploma.numero")),
        ("serie", Textos.T("Servicios.Impresion.VariableDiploma.serie")),
        ("fecha", Textos.T("Servicios.Impresion.VariableDiploma.fecha")),
        ("fechacorta", Textos.T("Servicios.Impresion.VariableDiploma.fechacorta")),
        ("fechaiso", Textos.T("Servicios.Impresion.VariableDiploma.fechaiso")),
        ("referencias", Textos.T("Servicios.Impresion.VariableDiploma.referencias")),
        ("qsos", Textos.T("Servicios.Impresion.VariableDiploma.qsos")),
        ("primerqso", Textos.T("Servicios.Impresion.VariableDiploma.primerqso")),
        ("ultimoqso", Textos.T("Servicios.Impresion.VariableDiploma.ultimoqso")),
        ("bandas", Textos.T("Servicios.Impresion.VariableDiploma.bandas")),
        ("modos", Textos.T("Servicios.Impresion.VariableDiploma.modos")),
        ("entidad", Textos.T("Servicios.Impresion.VariableDiploma.entidad")),
        ("gestor", Textos.T("Servicios.Impresion.VariableDiploma.gestor")),
        ("miindicativo", Textos.T("Servicios.Impresion.VariableDiploma.miindicativo")),
        ("minombre", Textos.T("Servicios.Impresion.VariableDiploma.minombre")),
        ("miqth", Textos.T("Servicios.Impresion.VariableDiploma.miqth")),
        ("milocalizador", Textos.T("Servicios.Impresion.VariableDiploma.milocalizador")),
    ];

    /// <summary>Las variables de cada fila de la tabla.</summary>
    public static IReadOnlyList<(string Nombre, string Descripcion)> DeLaFila =>
    [
        ("referencia", Textos.T("Servicios.Impresion.VariableFila.referencia")),
        ("nombrereferencia", Textos.T("Servicios.Impresion.VariableFila.nombrereferencia")),
        ("indicativo", Textos.T("Servicios.Impresion.VariableFila.indicativo")),
        ("fecha", Textos.T("Servicios.Impresion.VariableFila.fecha")),
        ("hora", Textos.T("Servicios.Impresion.VariableFila.hora")),
        ("banda", Textos.T("Servicios.Impresion.VariableFila.banda")),
        ("modo", Textos.T("Servicios.Impresion.VariableFila.modo")),
        ("rst", Textos.T("Servicios.Impresion.VariableFila.rst")),
        ("n", Textos.T("Servicios.Impresion.VariableFila.n")),
    ];

    /// <summary>Los valores de las variables de un diploma.</summary>
    /// <param name="datos">Los datos del diploma.</param>
    /// <param name="diseno">La plantilla (numeracion y gestor).</param>
    /// <param name="yo">Mi estacion.</param>
    /// <returns>Nombre de variable y valor.</returns>
    public static IReadOnlyDictionary<string, string> Para(DatosDeDiploma datos, DisenoDeDiploma diseno, DatosDeMiEstacion? yo)
    {
        ArgumentNullException.ThrowIfNull(datos);
        ArgumentNullException.ThrowIfNull(diseno);
        yo ??= DatosDeMiEstacion.Vacios;
        var inv = CultureInfo.InvariantCulture;
        var fecha = datos.Fecha;
        var fechas = datos.Filas.Where(f => f.FechaUtc is not null).Select(f => f.FechaUtc!.Value.UtcDateTime).OrderBy(f => f).ToList();
        var referencias = datos.Referencias
            ?? datos.Filas.Select(f => f.Referencia).Where(r => !string.IsNullOrWhiteSpace(r)).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var qsos = datos.Qsos ?? datos.Filas.Count(f => f.FechaUtc is not null || f.Indicativo.Length > 0);

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["indicativo"] = datos.Indicativo.Trim().ToUpperInvariant(),
            ["nombre"] = datos.Nombre?.Trim() ?? string.Empty,
            ["diploma"] = datos.NombreDelDiploma.Trim(),
            ["categoria"] = datos.Categoria?.Trim() ?? string.Empty,
            ["numero"] = diseno.FormatearNumero(datos.Numero),
            ["serie"] = diseno.Serie,
            ["fecha"] = fecha.ToString("d 'de' MMMM 'de' yyyy", Es),
            ["fechacorta"] = fecha.ToString("dd/MM/yyyy", inv),
            ["fechaiso"] = fecha.ToString("yyyy-MM-dd", inv),
            ["referencias"] = referencias.ToString(inv),
            ["qsos"] = qsos.ToString(inv),
            ["primerqso"] = fechas.Count > 0 ? fechas[0].ToString("yyyy-MM-dd", inv) : string.Empty,
            ["ultimoqso"] = fechas.Count > 0 ? fechas[^1].ToString("yyyy-MM-dd", inv) : string.Empty,
            ["bandas"] = Lista(datos.Filas.Select(f => f.Banda)),
            ["modos"] = Lista(datos.Filas.Select(f => f.Modo)),
            ["entidad"] = datos.Entidad?.Trim() ?? string.Empty,
            ["gestor"] = !string.IsNullOrWhiteSpace(diseno.NombreDelGestor) ? diseno.NombreDelGestor.Trim() : datos.Entidad?.Trim() ?? string.Empty,
            ["miindicativo"] = yo.Indicativo,
            ["minombre"] = yo.Nombre ?? string.Empty,
            ["miqth"] = yo.Qth ?? string.Empty,
            ["milocalizador"] = yo.Localizador,
        };
    }

    /// <summary>Los valores de las variables de una fila.</summary>
    /// <param name="fila">La fila.</param>
    /// <param name="n">Su numero, desde 1.</param>
    /// <returns>Nombre de variable y valor.</returns>
    public static IReadOnlyDictionary<string, string> DeFila(FilaDeJustificante fila, int n)
    {
        ArgumentNullException.ThrowIfNull(fila);
        var inv = CultureInfo.InvariantCulture;
        var utc = fila.FechaUtc?.UtcDateTime;
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["referencia"] = fila.Referencia,
            ["nombrereferencia"] = fila.NombreDeReferencia ?? string.Empty,
            ["indicativo"] = fila.Indicativo,
            ["fecha"] = utc?.ToString("yyyy-MM-dd", inv) ?? string.Empty,
            ["hora"] = utc?.ToString("HH:mm", inv) ?? string.Empty,
            ["banda"] = fila.Banda,
            ["modo"] = fila.Modo,
            ["rst"] = fila.Rst,
            ["n"] = n.ToString(inv),
        };
    }

    /// <summary>Texto con variables ya relleno (la sustitucion de las QSL).</summary>
    /// <param name="plantilla">El texto.</param>
    /// <param name="valores">Los valores.</param>
    /// <returns>El texto relleno.</returns>
    public static string Sustituir(string? plantilla, IReadOnlyDictionary<string, string> valores) =>
        VariablesDeQsl.Sustituir(plantilla, valores);

    private static string Lista(IEnumerable<string> valores) =>
        string.Join(", ", valores.Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.OrdinalIgnoreCase));
}
