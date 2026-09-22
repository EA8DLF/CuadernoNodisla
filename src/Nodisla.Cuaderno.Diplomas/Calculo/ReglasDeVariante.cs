using System.Globalization;
using System.Text;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Diplomas.Catalogo;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Diplomas.Calculo;

/// <summary>
/// Traduce un diploma y una de sus variantes a las condiciones SQL que filtran el cuaderno.
/// </summary>
/// <remarks>
/// <para>
/// Todas las condiciones se escriben sobre el alias <c>q</c> de la tabla <c>qso</c> y salen como
/// un fragmento que empieza por <c>AND</c>, para poder pegarlo tanto en un <c>WHERE</c> como en
/// el <c>ON</c> de un <c>LEFT JOIN</c>. Los valores se toman del catalogo compilado, no del
/// operador, pero aun asi se escapan: una comilla suelta en un nombre de banda no puede romper
/// una consulta.
/// </para>
/// <para>
/// Las restricciones del diploma y las de la variante se aplican <b>las dos</b>. Cuando las dos
/// listan bandas, se cuenta solo lo que esta en ambas: contar de menos es la direccion segura.
/// </para>
/// </remarks>
public sealed class ReglasDeVariante
{
    private ReglasDeVariante(
        PremioDelCatalogo premio,
        VarianteDelCatalogo variante,
        string filtro,
        string condicionDeConfirmacion,
        IReadOnlyList<string> bandas,
        IReadOnlyList<string> avisos)
    {
        Premio = premio;
        Variante = variante;
        Filtro = filtro;
        CondicionDeConfirmacion = condicionDeConfirmacion;
        BandasEfectivas = bandas;
        Avisos = avisos;
    }

    /// <summary>Diploma al que pertenecen las reglas.</summary>
    public PremioDelCatalogo Premio { get; }

    /// <summary>Variante concreta.</summary>
    public VarianteDelCatalogo Variante { get; }

    /// <summary>Condiciones sobre el contacto, ya en SQL, empezando por <c>AND</c>.</summary>
    public string Filtro { get; }

    /// <summary>Condicion SQL que dice si el contacto esta confirmado como el diploma exige.</summary>
    public string CondicionDeConfirmacion { get; }

    /// <summary>Bandas que de verdad cuentan, cruzando las del diploma con las de la variante.</summary>
    public IReadOnlyList<string> BandasEfectivas { get; }

    /// <summary>
    /// Lo que el motor no ha podido aplicar tal cual y conviene decirle al operador. Cada aviso
    /// significa que se esta contando de menos, nunca de mas.
    /// </summary>
    public IReadOnlyList<string> Avisos { get; }

    /// <summary>Construye las reglas de una variante.</summary>
    /// <param name="premio">Diploma del catalogo.</param>
    /// <param name="variante">Variante del catalogo.</param>
    /// <returns>Las reglas listas para generar consultas.</returns>
    public static ReglasDeVariante Construir(PremioDelCatalogo premio, VarianteDelCatalogo variante)
    {
        ArgumentNullException.ThrowIfNull(premio);
        ArgumentNullException.ThrowIfNull(variante);

        var sql = new StringBuilder();
        var avisos = new List<string>();

        // ── ventana de fechas del diploma y de la variante ───────────────────
        var desde = Mayor(premio.ValidoDesde, variante.ValidoDesde);
        var hasta = Menor(premio.ValidoHasta, variante.ValidoHasta);
        if (!string.IsNullOrEmpty(desde)) sql.Append($" AND q.qso_inicio_utc >= {Literal(desde)}");
        if (!string.IsNullOrEmpty(hasta)) sql.Append($" AND q.qso_inicio_utc <= {Literal(hasta + " 23:59:59")}");

        if (variante.Anual)
        {
            // Los diplomas anuales solo cuentan lo del ano en curso. La fecha la pone SQLite
            // para que el corte no dependa de la hora local de la maquina.
            sql.Append(" AND q.qso_inicio_utc >= strftime('%Y', 'now') || '-01-01'");
        }

        // ── bandas ───────────────────────────────────────────────────────────
        var bandas = Interseccion(premio.BandasPermitidas, variante.Bandas);
        if (bandas.Count > 0)
        {
            sql.Append($" AND q.band IN ({Lista(bandas)})");
        }
        else if (premio.BandasPermitidas.Count > 0 && variante.Bandas.Count > 0)
        {
            // Las dos listas existen pero no comparten ninguna banda: nada puede contar.
            sql.Append(" AND 0");
            avisos.Add("Las bandas del diploma y las de la variante no coinciden en ninguna.");
        }

        // ── modos y tipo de emision ──────────────────────────────────────────
        if (variante.Modos.Count > 0)
        {
            sql.Append($" AND (q.mode IN ({Lista(variante.Modos)}) " +
                       $"OR COALESCE(q.submode, '') IN ({Lista(variante.Modos)}))");
        }

        var emision = Emision(premio, variante, avisos);
        if (emision is not null) sql.Append(emision);

        // ── continentes ──────────────────────────────────────────────────────
        if (variante.Continentes.Count > 0)
        {
            sql.Append($" AND COALESCE(q.cont, '') IN ({Lista(variante.Continentes)})");
        }

        // ── satelite ─────────────────────────────────────────────────────────
        if (variante.ExigeSatelite) sql.Append(" AND COALESCE(q.prop_mode, '') = 'SAT'");
        if (variante.ExcluyeSatelite) sql.Append(" AND COALESCE(q.prop_mode, '') <> 'SAT'");

        return new ReglasDeVariante(
            premio, variante, sql.ToString(), Confirmacion(premio), bandas, avisos);
    }

    /// <summary>
    /// Dice si un contacto con esta banda y este modo puede contar para la variante. Es la
    /// misma decision que el filtro SQL, resuelta en memoria para el aviso instantaneo.
    /// </summary>
    /// <param name="banda">Banda del contacto.</param>
    /// <param name="modo">Modo del contacto.</param>
    /// <returns>Cierto si la variante admite ese hueco.</returns>
    public bool AdmiteHueco(Banda banda, Modo modo)
    {
        if (BandasEfectivas.Count > 0)
        {
            if (banda.EsVacia) return false;
            var vale = false;
            foreach (var b in BandasEfectivas)
            {
                if (b.Equals(banda.Nombre, StringComparison.OrdinalIgnoreCase)) { vale = true; break; }
            }
            if (!vale) return false;
        }

        if (Variante.Modos.Count > 0)
        {
            var vale = false;
            foreach (var m in Variante.Modos)
            {
                if (m.Equals(modo.Principal, StringComparison.OrdinalIgnoreCase) ||
                    (modo.Submodo is not null && m.Equals(modo.Submodo, StringComparison.OrdinalIgnoreCase)))
                {
                    vale = true;
                    break;
                }
            }
            if (!vale) return false;
        }

        var emision = TipoDeEmision.De(modo.Principal);
        if (TipoDeEmision.EsConocido(Variante.TipoDeEmision) &&
            !emision.Equals(Variante.TipoDeEmision, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (Premio.EmisionPermitida.Count > 0)
        {
            var vale = false;
            foreach (var e in Premio.EmisionPermitida)
            {
                if (e.Equals(emision, StringComparison.OrdinalIgnoreCase)) { vale = true; break; }
            }
            if (!vale) return false;
        }

        return true;
    }

    // ── piezas ───────────────────────────────────────────────────────────────

    private static string? Emision(
        PremioDelCatalogo premio, VarianteDelCatalogo variante, List<string> avisos)
    {
        var tipos = new List<string>();
        if (TipoDeEmision.EsConocido(variante.TipoDeEmision))
        {
            tipos.Add(variante.TipoDeEmision!);
        }
        else if (!string.IsNullOrWhiteSpace(variante.TipoDeEmision))
        {
            avisos.Add($"Tipo de emisión «{variante.TipoDeEmision}» desconocido; no se filtra por él.");
        }

        if (tipos.Count == 0 && premio.EmisionPermitida.Count is > 0 and < 3)
        {
            foreach (var e in premio.EmisionPermitida)
            {
                if (TipoDeEmision.EsConocido(e)) tipos.Add(e);
            }
        }

        if (tipos.Count == 0) return null;

        var condiciones = new List<string>();
        foreach (var tipo in tipos)
        {
            if (tipo.Equals(TipoDeEmision.Cw, StringComparison.OrdinalIgnoreCase))
            {
                condiciones.Add($"q.mode IN ({Lista(TipoDeEmision.ModosDeCw)})");
            }
            else if (tipo.Equals(TipoDeEmision.Fonia, StringComparison.OrdinalIgnoreCase))
            {
                condiciones.Add($"q.mode IN ({Lista(TipoDeEmision.ModosDeFonia)})");
            }
            else
            {
                var noDigitales = new List<string>(TipoDeEmision.ModosDeCw);
                noDigitales.AddRange(TipoDeEmision.ModosDeFonia);
                condiciones.Add($"q.mode NOT IN ({Lista(noDigitales)})");
            }
        }

        return $" AND ({string.Join(" OR ", condiciones)})";
    }

    private static string Confirmacion(PremioDelCatalogo premio)
    {
        if (premio.Exigencia == ExigenciaDeConfirmacion.Trabajado) return "1";

        // «Confirmado» acepta la V de ADIF ademas de la Y: una confirmacion verificada por el
        // servicio vale para el diploma igual o mas que una simple. «Verificado» exige la V.
        var estados = premio.Exigencia == ExigenciaDeConfirmacion.Verificado
            ? "c.recibida = 'V'"
            : "c.recibida IN ('Y', 'V')";

        var servicio = premio.MediosValidos.Count > 0
            ? $" AND c.servicio IN ({Lista(premio.MediosValidos.Select(CodigoDelMedio).ToList())})"
            : string.Empty;

        return $"EXISTS (SELECT 1 FROM qso_confirmacion c WHERE c.qso_id = q.id AND {estados}{servicio})";
    }

    /// <summary>Codigo con el que la capa de datos guarda cada via de confirmacion.</summary>
    /// <param name="medio">Via de confirmacion.</param>
    /// <returns>El codigo de texto, por ejemplo <c>LOTW</c>.</returns>
    public static string CodigoDelMedio(MedioDeConfirmacion medio) => medio switch
    {
        MedioDeConfirmacion.Papel => "QSL",
        MedioDeConfirmacion.Lotw => "LOTW",
        MedioDeConfirmacion.Eqsl => "EQSL",
        MedioDeConfirmacion.ClubLog => "CLUBLOG",
        MedioDeConfirmacion.QrzCom => "QRZCOM",
        MedioDeConfirmacion.HamQth => "HAMQTH",
        MedioDeConfirmacion.HrdLog => "HRDLOG",
        MedioDeConfirmacion.QrzCq => "QRZCQ",
        _ => medio.ToString().ToUpperInvariant(),
    };

    /// <summary>Escribe un valor como literal de texto de SQL, doblando las comillas.</summary>
    /// <param name="valor">Valor a escribir.</param>
    /// <returns>El literal, comillas incluidas.</returns>
    public static string Literal(string valor) =>
        "'" + (valor ?? string.Empty).Replace("'", "''", StringComparison.Ordinal) + "'";

    /// <summary>Escribe una lista de valores como lista de literales separada por comas.</summary>
    /// <param name="valores">Valores a escribir.</param>
    /// <returns>El texto para un <c>IN (…)</c>.</returns>
    public static string Lista(IReadOnlyCollection<string> valores) =>
        valores.Count == 0 ? "''" : string.Join(", ", valores.Select(Literal));

    private static IReadOnlyList<string> Interseccion(
        IReadOnlyList<string> unas, IReadOnlyList<string> otras)
    {
        if (unas.Count == 0) return otras;
        if (otras.Count == 0) return unas;

        var comunes = new List<string>();
        foreach (var u in unas)
        {
            foreach (var o in otras)
            {
                if (u.Equals(o, StringComparison.OrdinalIgnoreCase)) { comunes.Add(u); break; }
            }
        }
        return comunes;
    }

    private static string? Mayor(string? una, string? otra) =>
        string.IsNullOrEmpty(una) ? otra
        : string.IsNullOrEmpty(otra) ? una
        : string.CompareOrdinal(una, otra) >= 0 ? una : otra;

    private static string? Menor(string? una, string? otra) =>
        string.IsNullOrEmpty(una) ? otra
        : string.IsNullOrEmpty(otra) ? una
        : string.CompareOrdinal(una, otra) <= 0 ? una : otra;

    /// <summary>Numero entero en formato invariable, para cuando hace falta en SQL.</summary>
    /// <param name="valor">Numero a escribir.</param>
    /// <returns>El numero en texto.</returns>
    public static string Numero(int valor) => valor.ToString(CultureInfo.InvariantCulture);
}
