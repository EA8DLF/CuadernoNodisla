using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Entidades;

namespace Nodisla.Cuaderno.Diplomas.Catalogo;

/// <summary>
/// Lee el catalogo de diplomas incrustado en el ensamblado.
/// </summary>
/// <remarks>
/// <para>
/// El recurso es el mismo texto que vive en <c>recursos\diplomas\</c>, concatenado y
/// comprimido: cabecera, diplomas, variantes, informes y despues las referencias, una linea
/// por registro y la primera columna diciendo de que tipo es. Las lineas que empiezan por
/// <c>#</c> son comentarios y los campos vacios del final se recortan, asi que hay que leer
/// tolerando lineas cortas.
/// </para>
/// <para>
/// Las 553.064 referencias no se materializan nunca en memoria: se recorren en flujo y el
/// constructor de la base las va insertando. La parte pequena (87 diplomas y 390 variantes)
/// si cabe y se devuelve entera.
/// </para>
/// </remarks>
public static class LectorDelRecurso
{
    /// <summary>Nombre del recurso incrustado.</summary>
    public const string NombreDelRecurso = "Nodisla.Cuaderno.Diplomas.Recursos.catalogo-diplomas.tsv.gz";

    /// <summary>Lee la cabecera, los diplomas, las variantes y los informes.</summary>
    /// <returns>La parte del catalogo que cabe en memoria.</returns>
    /// <exception cref="InvalidOperationException">El recurso falta o no trae cabecera.</exception>
    public static CatalogoDeDiplomas LeerCatalogo()
    {
        CabeceraDelCatalogo? cabecera = null;
        var diplomas = new Dictionary<string, PremioDelCatalogo>(StringComparer.OrdinalIgnoreCase);
        var variantes = new Dictionary<string, List<VarianteDelCatalogo>>(StringComparer.OrdinalIgnoreCase);
        var informes = new List<InformeDelCatalogo>();

        foreach (var campos in Lineas())
        {
            switch (campos[0])
            {
                case "V":
                    cabecera = LeerCabecera(campos);
                    break;
                case "D":
                    var premio = LeerPremio(campos);
                    diplomas[premio.Codigo] = premio;
                    break;
                case "C":
                    var variante = LeerVariante(campos);
                    if (!variantes.TryGetValue(variante.Codigo, out var lista))
                    {
                        lista = [];
                        variantes[variante.Codigo] = lista;
                    }
                    lista.Add(variante);
                    break;
                case "I":
                    informes.Add(LeerInforme(campos));
                    break;
                case "R":
                    // Las referencias van al final y no se cargan aqui.
                    return Construir(cabecera, diplomas, variantes, informes);
                default:
                    break;
            }
        }

        return Construir(cabecera, diplomas, variantes, informes);
    }

    /// <summary>Recorre las referencias del catalogo sin cargarlas todas en memoria.</summary>
    /// <param name="codigosIncluidos">
    /// Diplomas que interesan. Vacio o nulo significa todos. Sirve para construir catalogos
    /// parciales, que es lo que hacen las pruebas para no montar 1,5 millones de filas.
    /// </param>
    /// <returns>Las referencias, en el orden en que estan en el recurso.</returns>
    public static IEnumerable<ReferenciaDelCatalogo> LeerReferencias(
        IReadOnlySet<string>? codigosIncluidos = null)
    {
        foreach (var campos in Lineas())
        {
            if (campos[0] != "R") continue;
            var codigo = Campo(campos, 1);
            if (codigosIncluidos is { Count: > 0 } && !codigosIncluidos.Contains(codigo)) continue;
            yield return LeerReferencia(campos, codigo);
        }
    }

    private static CatalogoDeDiplomas Construir(
        CabeceraDelCatalogo? cabecera,
        Dictionary<string, PremioDelCatalogo> diplomas,
        Dictionary<string, List<VarianteDelCatalogo>> variantes,
        List<InformeDelCatalogo> informes)
    {
        if (cabecera is null)
        {
            throw new InvalidOperationException(
                "El recurso de diplomas no trae cabecera. Hay que regenerarlo con " +
                "recursos\\diplomas\\generar-diplomas.py.");
        }

        var porCodigo = variantes.ToDictionary(
            p => p.Key,
            p => (IReadOnlyList<VarianteDelCatalogo>)p.Value,
            StringComparer.OrdinalIgnoreCase);

        return new CatalogoDeDiplomas(cabecera, diplomas, porCodigo, informes);
    }

    private static IEnumerable<string[]> Lineas()
    {
        using var bruto = Abrir();
        using var descomprimido = new GZipStream(bruto, CompressionMode.Decompress);
        using var lector = new StreamReader(descomprimido, System.Text.Encoding.UTF8);

        while (lector.ReadLine() is { } linea)
        {
            if (linea.Length == 0 || linea[0] == '#') continue;
            yield return linea.Split('\t');
        }
    }

    private static Stream Abrir()
    {
        var ensamblado = typeof(LectorDelRecurso).GetTypeInfo().Assembly;
        return ensamblado.GetManifestResourceStream(NombreDelRecurso)
            ?? throw new InvalidOperationException(
                $"Falta el recurso incrustado «{NombreDelRecurso}». Hay que generarlo con " +
                "recursos\\diplomas\\generar-diplomas.py antes de compilar.");
    }

    // ── lectura de cada tipo de linea ────────────────────────────────────────

    private static CabeceraDelCatalogo LeerCabecera(string[] c) => new(
        Entero(Campo(c, 1)) ?? 0,
        Campo(c, 2),
        Campo(c, 3),
        Entero(Campo(c, 4)) ?? 0,
        Entero(Campo(c, 5)) ?? 0,
        Entero(Campo(c, 6)) ?? 0);

    private static PremioDelCatalogo LeerPremio(string[] c) => new(
        Codigo: Campo(c, 1),
        Clase: AClaseDeDiploma(Campo(c, 2)),
        Nombre: Campo(c, 3),
        NombreEspanol: Opcional(c, 4),
        Gestor: Opcional(c, 5),
        Web: Opcional(c, 6),
        WebDeReferencias: Opcional(c, 7),
        Exigencia: AExigencia(Campo(c, 8)),
        MediosValidos: AMedios(Campo(c, 9)),
        ValidaElGestor: Bandera(c, 10),
        Campo: ACampo(Campo(c, 11)),
        CampoLider: ACampo(Campo(c, 12)),
        Separador: Opcional(c, 13),
        CampoExacto: Bandera(c, 14),
        CadenaInicial: Opcional(c, 15),
        CadenaFinal: Opcional(c, 16),
        ReferenciaLibre: Bandera(c, 19),
        ValidoDesde: Opcional(c, 20),
        ValidoHasta: Opcional(c, 21),
        BandasPermitidas: Trozos(Campo(c, 23)),
        ClasesDeModoPermitidas: AClases(Campo(c, 24)),
        SoloEntidadesVigentes: Bandera(c, 25),
        Calculable: Bandera(c, 26),
        MotivoNoCalculable: Opcional(c, 27),
        Descripcion: Opcional(c, 28));

    private static VarianteDelCatalogo LeerVariante(string[] c) => new(
        Codigo: Campo(c, 1),
        Variante: Campo(c, 2),
        Descripcion: Opcional(c, 3),
        Modos: Trozos(Campo(c, 5)),
        Bandas: Trozos(Campo(c, 6)),
        Clase: AClaseDeModo(Campo(c, 7)),
        Continentes: Trozos(Campo(c, 8)),
        Anual: Bandera(c, 9),
        ExigeSatelite: Bandera(c, 10),
        ExcluyeSatelite: Bandera(c, 11),
        ValidoDesde: Opcional(c, 13),
        ValidoHasta: Opcional(c, 14),
        Objetivo: Entero(Campo(c, 15)),
        Sintetica: Bandera(c, 16));

    private static ReferenciaDelCatalogo LeerReferencia(string[] c, string codigo) => new(
        Codigo: codigo,
        Referencia: Campo(c, 2),
        Alias: Opcional(c, 3),
        ValidoDesde: Opcional(c, 4),
        ValidoHasta: Opcional(c, 5),
        Descripcion: Campo(c, 6),
        Grupo: Opcional(c, 7),
        Subgrupo: Opcional(c, 8),
        Gridsquare: Opcional(c, 9),
        Puntuacion: Decimal(Campo(c, 10)),
        Bonus: Decimal(Campo(c, 11)),
        Valida: Bandera(c, 12),
        DxccPermitidos: ANumeros(Campo(c, 14)));

    private static InformeDelCatalogo LeerInforme(string[] c) => new(
        Campo(c, 1), Opcional(c, 2), Opcional(c, 3), Opcional(c, 4), Bandera(c, 5));

    // ── utilidades ───────────────────────────────────────────────────────────

    private static string Campo(string[] campos, int indice) =>
        indice < campos.Length ? campos[indice] : string.Empty;

    private static string? Opcional(string[] campos, int indice)
    {
        var valor = Campo(campos, indice);
        return string.IsNullOrWhiteSpace(valor) ? null : valor;
    }

    private static bool Bandera(string[] campos, int indice) => Campo(campos, indice) == "1";

    private static int? Entero(string texto) =>
        int.TryParse(texto, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;

    private static double Decimal(string texto) =>
        double.TryParse(texto, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;

    private static IReadOnlyList<string> Trozos(string texto) =>
        string.IsNullOrWhiteSpace(texto)
            ? []
            : texto.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static IReadOnlyList<int> ANumeros(string texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return [];
        var trozos = texto.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var numeros = new List<int>(trozos.Length);
        foreach (var t in trozos)
        {
            if (Entero(t) is { } n) numeros.Add(n);
        }
        return numeros;
    }

    /// <summary>
    /// Traduce el tipo de emision del catalogo original a la familia de modos del contrato.
    /// Lo que no se reconozca se lee como «cualquiera», que es la lectura que no descarta nada
    /// sin motivo; el aviso de que no se ha entendido lo da <c>ReglasDeVariante</c>.
    /// </summary>
    private static ClaseDeModo AClaseDeModo(string texto) => texto.ToUpperInvariant() switch
    {
        "CW" => ClaseDeModo.Telegrafia,
        "PHONE" => ClaseDeModo.Fonia,
        "DIGITAL" => ClaseDeModo.Digital,
        _ => ClaseDeModo.Cualquiera,
    };

    private static IReadOnlyList<ClaseDeModo> AClases(string texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return [];
        var clases = new List<ClaseDeModo>();
        foreach (var t in Trozos(texto))
        {
            var clase = AClaseDeModo(t);
            if (clase != ClaseDeModo.Cualquiera && !clases.Contains(clase)) clases.Add(clase);
        }
        // Las tres familias equivalen a «sin restriccion»: no merece la pena filtrar por ellas.
        return clases.Count == 3 ? [] : clases;
    }

    private static ClaseDeDiploma AClaseDeDiploma(string texto) => texto switch
    {
        "PorIndicativo" => ClaseDeDiploma.PorIndicativo,
        "PorCampo" => ClaseDeDiploma.PorCampo,
        _ => ClaseDeDiploma.PorReferencia,
    };

    private static ExigenciaDeConfirmacion AExigencia(string texto) => texto switch
    {
        "Trabajado" => ExigenciaDeConfirmacion.Trabajado,
        "Verificado" => ExigenciaDeConfirmacion.Verificado,
        _ => ExigenciaDeConfirmacion.Confirmado,
    };

    private static CampoDeQso ACampo(string texto) =>
        Enum.TryParse<CampoDeQso>(texto, ignoreCase: true, out var campo) ? campo : CampoDeQso.Ninguno;

    private static IReadOnlyList<MedioDeConfirmacion> AMedios(string texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return [];
        var medios = new List<MedioDeConfirmacion>();
        foreach (var t in texto.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Enum.TryParse<MedioDeConfirmacion>(t, ignoreCase: true, out var medio) &&
                !medios.Contains(medio))
            {
                medios.Add(medio);
            }
        }
        return medios;
    }
}
