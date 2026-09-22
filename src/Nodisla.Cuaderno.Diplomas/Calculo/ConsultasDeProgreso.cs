using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Diplomas.Catalogo;
using Nodisla.Cuaderno.Dominio.Entidades;

namespace Nodisla.Cuaderno.Diplomas.Calculo;

/// <summary>Cuantas referencias distintas se han alcanzado en una variante.</summary>
/// <remarks>
/// Es una clase con propiedades y no un registro posicional porque la lee Dapper: SQLite
/// devuelve los enteros como <c>INTEGER</c> de 64 bits y asi se ajustan al tipo declarado.
/// </remarks>
public sealed class RecuentoDeVariante
{
    /// <summary>Referencias distintas trabajadas.</summary>
    public int Trabajadas { get; set; }

    /// <summary>De esas, cuantas estan confirmadas como el diploma exige.</summary>
    public int Confirmadas { get; set; }
}

/// <summary>Una referencia alcanzada y si esta confirmada.</summary>
/// <param name="Valor">Codigo de la referencia.</param>
/// <param name="Confirmada">Esta confirmada como el diploma exige.</param>
/// <param name="PrimerQsoId">Contacto mas antiguo que la aporto.</param>
public sealed record ReferenciaAlcanzada(string Valor, bool Confirmada, long? PrimerQsoId);

/// <summary>
/// Genera el SQL que resuelve el progreso de una variante.
/// </summary>
/// <remarks>
/// <para>
/// El progreso no se calcula recorriendo el cuaderno: sale de una consulta agregada contra las
/// tablas hijas indexadas (<c>qso_confirmacion</c> y <c>qso_referencia</c>) y contra el catalogo
/// compilado, que se adjunta a la misma conexion con el alias <c>cat</c>. Es la diferencia de
/// fondo con el programa original, que guarda las confirmaciones dentro de un JSON y por eso no
/// tiene mas remedio que recorrerlo todo en memoria.
/// </para>
/// <para>
/// Todas las consultas se construyen alrededor de una expresion comun, <c>alcanzadas</c>, que
/// agrupa por referencia y dice de cada una si esta confirmada y que contacto la aporto primero.
/// El universo de referencias sale del catalogo, no del cuaderno, asi que lo que falta se sabe
/// sin mirar ni un contacto mas.
/// </para>
/// </remarks>
public static class ConsultasDeProgreso
{
    /// <summary>Alias con el que se adjunta el catalogo compilado a la conexion del cuaderno.</summary>
    public const string AliasDelCatalogo = "cat";

    /// <summary>Dice si el motor sabe calcular esta variante.</summary>
    /// <param name="reglas">Reglas de la variante.</param>
    /// <returns>Cierto si se puede generar una consulta con sentido.</returns>
    public static bool SePuedeCalcular(ReglasDeVariante reglas)
    {
        ArgumentNullException.ThrowIfNull(reglas);
        if (!reglas.Premio.Calculable) return false;
        return reglas.Premio.Clase != ClaseDeDiploma.PorCampo ||
               reglas.Premio.Campo != CampoDeQso.Ninguno;
    }

    /// <summary>
    /// Dice si la variante tiene un universo de referencias cerrado en el catalogo, que es lo
    /// que permite listar lo que falta.
    /// </summary>
    /// <param name="reglas">Reglas de la variante.</param>
    /// <returns>Cierto si el catalogo enumera todas las referencias posibles.</returns>
    public static bool TieneUniverso(ReglasDeVariante reglas)
    {
        ArgumentNullException.ThrowIfNull(reglas);
        return !reglas.Premio.ReferenciaLibre;
    }

    /// <summary>Consulta que devuelve el recuento de trabajadas y confirmadas.</summary>
    /// <param name="reglas">Reglas de la variante.</param>
    /// <returns>El SQL completo.</returns>
    public static string Recuento(ReglasDeVariante reglas) =>
        $"""
        {Alcanzadas(reglas)}
        SELECT COUNT(*)                     AS Trabajadas,
               COALESCE(SUM(confirmada), 0) AS Confirmadas
          FROM alcanzadas
        """;

    /// <summary>Consulta que devuelve cada referencia alcanzada.</summary>
    /// <param name="reglas">Reglas de la variante.</param>
    /// <returns>El SQL completo.</returns>
    public static string ReferenciasAlcanzadas(ReglasDeVariante reglas) =>
        $"""
        {Alcanzadas(reglas)}
        SELECT valor      AS Valor,
               confirmada AS Confirmada,
               primero    AS PrimerQsoId
          FROM alcanzadas
        """;

    /// <summary>
    /// Consulta del detalle: el universo de referencias del catalogo con lo trabajado y lo
    /// confirmado al lado.
    /// </summary>
    /// <param name="reglas">Reglas de la variante.</param>
    /// <param name="limite">Tope de filas.</param>
    /// <returns>El SQL completo.</returns>
    public static string Detalle(ReglasDeVariante reglas, int limite)
    {
        ArgumentNullException.ThrowIfNull(reglas);

        if (!TieneUniverso(reglas))
        {
            // Sin universo (WPX, VUCC) no hay «lo que falta»: solo se puede listar lo hecho.
            return $"""
                {Alcanzadas(reglas)}
                SELECT valor      AS Referencia,
                       NULL       AS Nombre,
                       1          AS Trabajada,
                       confirmada AS Confirmada,
                       primero    AS PrimerQsoId
                  FROM alcanzadas
                 ORDER BY valor
                 LIMIT {ReglasDeVariante.Numero(limite)}
                """;
        }

        return $"""
            {Alcanzadas(reglas)}
            SELECT pr.referencia                                     AS Referencia,
                   pr.descripcion                                    AS Nombre,
                   CASE WHEN a.valor IS NOT NULL THEN 1 ELSE 0 END   AS Trabajada,
                   COALESCE(a.confirmada, 0)                         AS Confirmada,
                   a.primero                                         AS PrimerQsoId
              FROM {AliasDelCatalogo}.premio_referencia pr
              LEFT JOIN alcanzadas a ON a.valor = pr.referencia COLLATE NOCASE
             WHERE pr.award_code = {ReglasDeVariante.Literal(reglas.Premio.Codigo)}
               {(reglas.Premio.CuentanLasBorradas ? string.Empty : "AND pr.valido = 1")}
             GROUP BY pr.referencia
             ORDER BY pr.referencia
             LIMIT {ReglasDeVariante.Numero(limite)}
            """;
    }

    /// <summary>Cuantas referencias tiene el universo de un diploma en el catalogo.</summary>
    /// <param name="reglas">Reglas de la variante.</param>
    /// <returns>El SQL completo, o nulo si el diploma no tiene universo cerrado.</returns>
    public static string? TamanoDelUniverso(ReglasDeVariante reglas)
    {
        ArgumentNullException.ThrowIfNull(reglas);
        if (!TieneUniverso(reglas)) return null;

        return $"""
            SELECT COUNT(DISTINCT referencia)
              FROM {AliasDelCatalogo}.premio_referencia
             WHERE award_code = {ReglasDeVariante.Literal(reglas.Premio.Codigo)}
               {(reglas.Premio.CuentanLasBorradas ? string.Empty : "AND valido = 1")}
            """;
    }

    // ── el nucleo comun ──────────────────────────────────────────────────────

    private static string Alcanzadas(ReglasDeVariante reglas)
    {
        ArgumentNullException.ThrowIfNull(reglas);

        var confirmada = $"MAX(CASE WHEN {reglas.CondicionDeConfirmacion} THEN 1 ELSE 0 END)";

        return reglas.Premio.Clase switch
        {
            ClaseDeDiploma.PorReferencia => PorReferencia(reglas, confirmada),
            ClaseDeDiploma.PorIndicativo => PorIndicativo(reglas, confirmada),
            _ => PorCampo(reglas, confirmada),
        };
    }

    private static string PorReferencia(ReglasDeVariante reglas, string confirmada)
    {
        var codigo = ReglasDeVariante.Literal(reglas.Premio.Codigo);
        var (tipo, programa) = TipoDeReferenciaDe(reglas.Premio.Codigo);
        var filtroPrograma = programa is null
            ? string.Empty
            : $" AND r.programa = {ReglasDeVariante.Literal(programa)}";

        return $"""
            WITH alcanzadas AS (
              SELECT pr.referencia AS valor,
                     MIN(q.id)     AS primero,
                     {confirmada}  AS confirmada
                FROM qso_referencia r
                JOIN qso q ON q.id = r.qso_id
                JOIN {AliasDelCatalogo}.premio_referencia pr
                     ON pr.award_code = {codigo}
                    AND pr.referencia = r.referencia
                    AND pr.valido = 1
                    {VentanaDeLaReferencia()}
               WHERE r.award_code = {ReglasDeVariante.Literal(tipo)}
                 AND r.propia = 0{filtroPrograma}
                 {reglas.Filtro}
               GROUP BY pr.referencia
            )
            """;
    }

    private static string PorIndicativo(ReglasDeVariante reglas, string confirmada)
    {
        var codigo = ReglasDeVariante.Literal(reglas.Premio.Codigo);

        // Algunos diplomas por indicativo listan indicativos completos (160MMI) y otros
        // patrones con asterisco (CCC: 3B8*). Hay que admitir las dos formas, y tambien el
        // alias, que a veces trae un segundo patron.
        const string Coincide = """
            (q.call = pr.referencia
             OR (INSTR(pr.referencia, '*') > 0 AND UPPER(q.call) GLOB UPPER(pr.referencia))
             OR (COALESCE(pr.alias, '') <> '' AND
                 (q.call = pr.alias
                  OR (INSTR(pr.alias, '*') > 0 AND UPPER(q.call) GLOB UPPER(pr.alias)))))
            """;

        return $"""
            WITH alcanzadas AS (
              SELECT pr.referencia AS valor,
                     MIN(q.id)     AS primero,
                     {confirmada}  AS confirmada
                FROM {AliasDelCatalogo}.premio_referencia pr
                JOIN qso q ON {Coincide}
               WHERE pr.award_code = {codigo}
                 AND pr.valido = 1
                 {VentanaDeLaReferencia()}
                 {reglas.Filtro}
               GROUP BY pr.referencia
            )
            """;
    }

    /// <summary>
    /// Consulta que lista las referencias de un diploma por indicativo, para poder decidir en
    /// memoria a cual corresponde el indicativo que se acaba de teclear.
    /// </summary>
    /// <param name="codigo">Codigo del diploma.</param>
    /// <returns>El SQL completo.</returns>
    public static string PatronesDeIndicativo(string codigo)
    {
        ArgumentException.ThrowIfNullOrEmpty(codigo);

        return $"""
            SELECT referencia AS Referencia, COALESCE(alias, '') AS Alias
              FROM {AliasDelCatalogo}.premio_referencia
             WHERE award_code = {ReglasDeVariante.Literal(codigo)} AND valido = 1
            """;
    }

    private static string PorCampo(ReglasDeVariante reglas, string confirmada)
    {
        var expresion = ExpresionDelCampo(reglas.Premio);
        var codigo = ReglasDeVariante.Literal(reglas.Premio.Codigo);

        if (reglas.Premio.ReferenciaLibre)
        {
            // WPX y VUCC no tienen lista: vale cualquier valor que traiga el contacto.
            return $"""
                WITH alcanzadas AS (
                  SELECT {expresion} AS valor,
                         MIN(q.id)   AS primero,
                         {confirmada} AS confirmada
                    FROM qso q
                   WHERE {expresion} <> ''
                     {reglas.Filtro}
                   GROUP BY valor
                )
                """;
        }

        var soloVigentes = reglas.Premio.CuentanLasBorradas ? string.Empty : "AND pr.valido = 1";

        return $"""
            WITH alcanzadas AS (
              SELECT pr.referencia AS valor,
                     MIN(q.id)     AS primero,
                     {confirmada}  AS confirmada
                FROM qso q
                JOIN {AliasDelCatalogo}.premio_referencia pr
                     ON pr.award_code = {codigo}
                    AND pr.referencia = {expresion} COLLATE NOCASE
                    {soloVigentes}
                    {VentanaDeLaReferencia()}
               WHERE {expresion} <> ''
                 {reglas.Filtro}
               GROUP BY pr.referencia
            )
            """;
    }

    /// <summary>
    /// La referencia solo cuenta si el contacto cae dentro de su ventana de validez. Es lo que
    /// hace que una entidad DXCC borrada cuente para los contactos de cuando existia y no para
    /// los de despues. Las ventanas de las entidades las pone el resolutor del dominio al
    /// compilar el catalogo.
    /// </summary>
    private static string VentanaDeLaReferencia() =>
        """
            AND (pr.valido_desde = '' OR q.qso_inicio_utc >= pr.valido_desde)
                AND (pr.valido_hasta = '' OR q.qso_inicio_utc <= pr.valido_hasta || ' 23:59:59')
        """;

    /// <summary>Expresion SQL que saca del contacto la referencia de un diploma por campo.</summary>
    /// <param name="premio">Diploma del catalogo.</param>
    /// <returns>Una expresion que devuelve el codigo de la referencia, o cadena vacia.</returns>
    public static string ExpresionDelCampo(PremioDelCatalogo premio)
    {
        ArgumentNullException.ThrowIfNull(premio);

        var principal = Columna(premio.Campo);
        principal = Recortar(principal, premio.CadenaInicial, premio.CadenaFinal);

        if (premio.CampoLider == CampoDeQso.Ninguno) return principal;

        var lider = Columna(premio.CampoLider);
        var separador = ReglasDeVariante.Literal(premio.Separador ?? string.Empty);
        return $"CASE WHEN {lider} <> '' AND {principal} <> '' " +
               $"THEN {lider} || {separador} || {principal} ELSE '' END";
    }

    private static string Columna(CampoDeQso campo) => campo switch
    {
        CampoDeQso.Dxcc => "CASE WHEN q.dxcc > 0 THEN CAST(q.dxcc AS TEXT) ELSE '' END",
        CampoDeQso.State => "TRIM(COALESCE(q.state, ''))",
        CampoDeQso.CqZone => "CASE WHEN q.cqz IS NULL THEN '' ELSE CAST(q.cqz AS TEXT) END",
        CampoDeQso.ItuZone => "CASE WHEN q.ituz IS NULL THEN '' ELSE CAST(q.ituz AS TEXT) END",
        CampoDeQso.Continent => "TRIM(COALESCE(q.cont, ''))",
        CampoDeQso.Pfx => "TRIM(COALESCE(q.pfx, ''))",
        CampoDeQso.Gridsquare4 => "UPPER(SUBSTR(COALESCE(q.gridsquare, ''), 1, 4))",
        CampoDeQso.Cnty => "TRIM(COALESCE(q.cnty, ''))",
        CampoDeQso.Qth => "TRIM(COALESCE(q.qth, ''))",
        CampoDeQso.Address => "TRIM(COALESCE(q.address, ''))",
        _ => "''",
    };

    /// <summary>
    /// Algunos diplomas guardan la referencia dentro de otro campo, entre dos marcas: el italiano
    /// WAIP escribe la provincia entre parentesis dentro de la direccion. Si falta alguna de las
    /// dos marcas no se recorta nada, porque adivinar donde acaba la referencia seria inventar.
    /// </summary>
    private static string Recortar(string expresion, string? inicial, string? final)
    {
        if (string.IsNullOrEmpty(inicial) || string.IsNullOrEmpty(final)) return expresion;

        var ini = ReglasDeVariante.Literal(inicial);
        var fin = ReglasDeVariante.Literal(final);
        var largo = inicial.Length;

        return $"""
            CASE WHEN INSTR({expresion}, {ini}) > 0
                  AND INSTR({expresion}, {fin}) > INSTR({expresion}, {ini}) + {largo} - 1
                 THEN TRIM(SUBSTR({expresion},
                                  INSTR({expresion}, {ini}) + {largo},
                                  INSTR({expresion}, {fin}) - INSTR({expresion}, {ini}) - {largo}))
                 ELSE '' END
            """;
    }

    /// <summary>
    /// Traduce el codigo del diploma al programa con el que la capa de datos guarda las
    /// referencias del contacto.
    /// </summary>
    /// <remarks>
    /// <c>qso_referencia.award_code</c> guarda el nombre del tipo del dominio
    /// (<see cref="TipoDeReferencia"/>) y, cuando el programa no esta modelado, guarda
    /// <c>OTRA</c> y deja el nombre en <c>programa</c>. Filtrar primero por el tipo permite que
    /// el indice <c>ix_ref_award_ref</c> siga sirviendo.
    /// </remarks>
    /// <param name="codigo">Codigo del diploma.</param>
    /// <returns>El tipo guardado y, si hace falta, el nombre del programa.</returns>
    public static (string Tipo, string? Programa) TipoDeReferenciaDe(string codigo)
    {
        ArgumentException.ThrowIfNullOrEmpty(codigo);

        foreach (var tipo in Enum.GetValues<TipoDeReferencia>())
        {
            if (tipo == TipoDeReferencia.Otra) continue;
            if (tipo.ToString().Equals(codigo, StringComparison.OrdinalIgnoreCase))
            {
                return (tipo.ToString().ToUpperInvariant(), null);
            }
        }

        return (TipoDeReferencia.Otra.ToString().ToUpperInvariant(), codigo);
    }
}
