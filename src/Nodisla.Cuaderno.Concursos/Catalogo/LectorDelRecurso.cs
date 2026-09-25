using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Concursos.Catalogo;

/// <summary>
/// Lee el catalogo de concursos incrustado en el ensamblado.
/// </summary>
/// <remarks>
/// El recurso es el mismo texto que vive en <c>recursos\concursos\</c>, comprimido: una
/// linea por registro y la primera columna diciendo de que tipo es. Las lineas que empiezan
/// por <c>#</c> son comentarios y los campos vacios del final se recortan, asi que hay que
/// leer tolerando lineas cortas. Es el mismo trato que se da al recurso de paises y al de
/// diplomas, a proposito: un formato para todos y un solo modo de equivocarse.
/// </remarks>
public static class LectorDelRecurso
{
    /// <summary>Nombre del recurso incrustado.</summary>
    public const string NombreDelRecurso = "Nodisla.Cuaderno.Concursos.Recursos.catalogo-concursos.tsv.gz";

    /// <summary>Lee el catalogo entero.</summary>
    /// <returns>Los concursos con sus reglas, listos para consultar.</returns>
    /// <exception cref="InvalidOperationException">El recurso falta o no trae cabecera.</exception>
    public static CatalogoDeConcursos Leer() => Leer(AbrirRecurso());

    /// <summary>Lee un catalogo de cualquier flujo de texto ya descomprimido.</summary>
    /// <param name="texto">Flujo con el recurso en texto.</param>
    /// <returns>Los concursos con sus reglas.</returns>
    /// <exception cref="InvalidOperationException">El texto no trae cabecera.</exception>
    public static CatalogoDeConcursos Leer(TextReader texto)
    {
        ArgumentNullException.ThrowIfNull(texto);

        CabeceraDelCatalogo? cabecera = null;
        var concursos = new List<ReglaDeConcurso>();
        var puntos = new Dictionary<string, List<ReglaDePuntos>>(StringComparer.OrdinalIgnoreCase);
        var mults = new Dictionary<string, List<ReglaDeMultiplicador>>(StringComparer.OrdinalIgnoreCase);

        foreach (var campos in Lineas(texto))
        {
            switch (campos[0])
            {
                case "V":
                    cabecera = LeerCabecera(campos);
                    break;
                case "C":
                    concursos.Add(LeerConcurso(campos));
                    break;
                case "P":
                    Anadir(puntos, Campo(campos, 1), LeerPuntos(campos));
                    break;
                case "M":
                    Anadir(mults, Campo(campos, 1), LeerMultiplicador(campos));
                    break;
                default:
                    // Un tipo de registro que aun no existe no debe romper nada: se ignora.
                    break;
            }
        }

        if (cabecera is null)
            throw new InvalidOperationException("El catalogo de concursos no trae cabecera.");

        var completos = concursos
            .Select(c => c with
            {
                Puntuacion = puntos.TryGetValue(c.Codigo, out var p) ? p : [],
                Multiplicadores = mults.TryGetValue(c.Codigo, out var m) ? m : [],
            })
            .ToList();

        return new CatalogoDeConcursos(cabecera, completos);
    }

    private static void Anadir<T>(Dictionary<string, List<T>> mapa, string clave, T valor)
    {
        if (!mapa.TryGetValue(clave, out var lista))
        {
            lista = [];
            mapa[clave] = lista;
        }
        lista.Add(valor);
    }

    private static TextReader AbrirRecurso()
    {
        var ensamblado = typeof(LectorDelRecurso).GetTypeInfo().Assembly;
        var flujo = ensamblado.GetManifestResourceStream(NombreDelRecurso)
            ?? throw new InvalidOperationException(
                $"Falta el recurso incrustado {NombreDelRecurso}. Se genera con recursos\\concursos\\generar-concursos.py.");
        return new StreamReader(new GZipStream(flujo, CompressionMode.Decompress), System.Text.Encoding.UTF8);
    }

    private static IEnumerable<string[]> Lineas(TextReader lector)
    {
        while (lector.ReadLine() is { } linea)
        {
            if (linea.Length == 0 || linea[0] == '#') continue;
            var campos = linea.Split('\t');
            if (campos.Length > 0 && campos[0].Length > 0) yield return campos;
        }
    }

    private static string Campo(string[] campos, int indice) =>
        indice < campos.Length ? campos[indice].Trim() : string.Empty;

    private static int Entero(string[] campos, int indice) =>
        int.TryParse(Campo(campos, indice), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;

    private static CabeceraDelCatalogo LeerCabecera(string[] campos) => new(
        Entero(campos, 1),
        DateOnly.TryParse(Campo(campos, 2), CultureInfo.InvariantCulture, DateTimeStyles.None, out var fecha)
            ? fecha
            : default,
        Entero(campos, 3),
        Entero(campos, 4));

    private static ReglaDeConcurso LeerConcurso(string[] campos) =>
        new(Campo(campos, 1),
            Campo(campos, 2),
            Vacio(Campo(campos, 3)),
            LeerEnumerado(Campo(campos, 4), EstadoDeLasReglas.SoloCatalogo))
        {
            Modos = Lista(Campo(campos, 5)),
            Bandas = Bandas(Campo(campos, 6)),
            Enviado = Intercambios(Campo(campos, 7)),
            Recibido = Intercambios(Campo(campos, 8)),
            Celebracion = LeerCelebracion(campos),
            Duplicado = LeerEnumerado(Campo(campos, 15), AmbitoDeDuplicado.Banda),
            Notas = Vacio(Campo(campos, 16)),
        };

    private static CelebracionDeConcurso? LeerCelebracion(string[] campos)
    {
        var mes = Entero(campos, 9);
        if (mes is < 1 or > 12) return null;
        var horas = Entero(campos, 14);
        return new CelebracionDeConcurso(
            mes,
            Math.Clamp(Entero(campos, 10), 1, 5),
            Campo(campos, 11).Equals("S", StringComparison.OrdinalIgnoreCase),
            LeerDia(Campo(campos, 12)),
            LeerHora(Campo(campos, 13)),
            TimeSpan.FromHours(horas > 0 ? horas : 24));
    }

    private static DayOfWeek LeerDia(string texto) => texto.ToLowerInvariant() switch
    {
        "lun" => DayOfWeek.Monday,
        "mar" => DayOfWeek.Tuesday,
        "mie" => DayOfWeek.Wednesday,
        "jue" => DayOfWeek.Thursday,
        "vie" => DayOfWeek.Friday,
        "dom" => DayOfWeek.Sunday,
        _ => DayOfWeek.Saturday,
    };

    private static TimeOnly LeerHora(string texto)
    {
        if (texto.Length == 4
            && int.TryParse(texto.AsSpan(0, 2), NumberStyles.Integer, CultureInfo.InvariantCulture, out var h)
            && int.TryParse(texto.AsSpan(2, 2), NumberStyles.Integer, CultureInfo.InvariantCulture, out var m)
            && h is >= 0 and < 24 && m is >= 0 and < 60)
        {
            return new TimeOnly(h, m);
        }
        return TimeOnly.MinValue;
    }

    private static ReglaDePuntos LeerPuntos(string[] campos) => new(
        LeerEnumerado(Campo(campos, 2), AmbitoDePuntos.Cualquiera),
        Entero(campos, 3),
        Bandas(Campo(campos, 4)),
        Lista(Campo(campos, 5)));

    private static ReglaDeMultiplicador LeerMultiplicador(string[] campos) => new(
        LeerEnumerado(Campo(campos, 2), TipoDeMultiplicador.Dxcc),
        LeerEnumerado(Campo(campos, 3), AlcanceDeMultiplicador.PorBanda));

    private static string? Vacio(string texto) => texto.Length == 0 ? null : texto;

    private static IReadOnlyList<string> Lista(string texto) =>
        texto.Length == 0 || texto == "*"
            ? []
            : texto.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static IReadOnlyList<Banda> Bandas(string texto) =>
        Lista(texto).Select(n => Banda.TryParse(n, out var b) ? b : Banda.Vacia)
                    .Where(b => !b.EsVacia)
                    .ToArray();

    private static IReadOnlyList<TipoDeIntercambio> Intercambios(string texto) =>
        Lista(texto).Select(n => LeerEnumerado(n, TipoDeIntercambio.Texto)).ToArray();

    private static T LeerEnumerado<T>(string texto, T porOmision) where T : struct, Enum =>
        Enum.TryParse<T>(texto, ignoreCase: true, out var valor) ? valor : porOmision;
}
