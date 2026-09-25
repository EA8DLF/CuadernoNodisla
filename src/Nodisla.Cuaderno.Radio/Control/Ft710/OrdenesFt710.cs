namespace Nodisla.Cuaderno.Radio.Control.Ft710;

/// <summary>Se lanza cuando se intenta mandar al equipo una orden que puede hacer daño.</summary>
public sealed class OrdenPeligrosaException : Exception
{
    /// <summary>Crea la excepcion.</summary>
    /// <param name="orden">La orden que se intentaba mandar.</param>
    /// <param name="porque">Por que no se manda.</param>
    public OrdenPeligrosaException(string orden, string porque)
        : base($"La orden «{orden}» no se envía: {porque}")
    {
        Orden = orden;
    }

    /// <summary>La orden que se intentaba mandar.</summary>
    public string Orden { get; }
}

/// <summary>
/// Que ordenes se le pueden mandar al FT-710 y cuales no.
/// </summary>
/// <remarks>
/// <b>Esta lista se escribe a mano y no se deduce.</b> En Yaesu hay ordenes que parecen
/// consultas y son acciones: <c>SV;</c> no pregunta nada, intercambia los VFO. Recorrer el
/// alfabeto «a ver qué contesta» puede cambiarle el equipo al operador sin que se entere, o
/// ponerlo en antena.
/// </remarks>
public static class OrdenesFt710
{
    /// <summary>
    /// Ordenes que jamas se mandan, ni sondeando ni en crudo.
    /// </summary>
    /// <remarks>
    /// <c>PS0</c> apaga el equipo, <c>MW</c> escribe memorias y <c>KY</c> manipula telegrafia,
    /// que ademas transmite.
    /// </remarks>
    public static IReadOnlyList<string> Prohibidas { get; } = ["PS0", "MW", "KY"];

    /// <summary>
    /// Ordenes de sondeo, escritas a mano a partir de la captura del equipo de EA8DLF.
    /// </summary>
    /// <remarks>
    /// Son todas consultas sin efecto: el control las prueba una a una al conectar para saber
    /// que sabe hacer este equipo. Las que contestan <c>?;</c> se dan por no admitidas.
    /// Aqui no esta <c>TX;</c> a proposito: en este equipo pone la radio en antena.
    /// </remarks>
    public static IReadOnlyList<string> Sondeo { get; } =
    [
        // Confirmadas en la captura del 21-09-2026.
        "ID;", "PS;", "FA;", "FB;", "VS;", "IF;", "MD0;", "SH0;", "NA0;",
        "NB0;", "NL0;", "NR0;", "RL0;", "BP00;", "BP01;", "CO00;", "CO01;",
        "AG0;", "RG0;", "SQ0;", "MG;", "PC;", "AC;", "VX;", "VG;", "VD;",
        "GT0;", "PA0;", "RA0;", "KS;", "KP;", "BI;", "BC0;", "SM0;",
        "RM1;", "RM3;", "RM4;", "RM5;", "RM6;", "RM7;",
        "FT;", "ST;", "MC;", "LK;", "DT0;",

        // Añadidas con el barrido completo del 22-09-2026.
        "MD1;", "NB1;", "AG1;", "SQ1;", "PA1;", "CN00;",
        "IS0;", "IS1;", "OS0;", "OS1;", "RI0;", "DT1;",

        // Añadidas con el manual CAT, 25-09-2026. El canal de memoria son TRES cifras: con dos
        // el equipo contesta «?;» y parece que no sepa leer memorias.
        "MR001;", "MT001;", "VE0;", "VE1;", "VE2;", "VE3;",
    ];

    /// <summary>
    /// Comprueba que una orden se puede mandar al equipo.
    /// </summary>
    /// <param name="orden">Orden con su punto y coma.</param>
    /// <exception cref="OrdenPeligrosaException">Si la orden apaga, escribe memorias o manipula.</exception>
    public static void ComprobarQueEsSegura(string orden)
    {
        var limpia = orden.Trim().ToUpperInvariant();

        foreach (var prohibida in Prohibidas)
        {
            if (limpia.StartsWith(prohibida, StringComparison.Ordinal))
            {
                throw new OrdenPeligrosaException(
                    orden,
                    prohibida switch
                    {
                        "PS0" => "apagaría el equipo",
                        "MW" => "escribiría en las memorias del equipo",
                        _ => "manipularía telegrafía y pondría el equipo en antena",
                    });
            }
        }
    }

    /// <summary>
    /// Comprueba que una orden vale para mandarla en crudo desde la interfaz.
    /// </summary>
    /// <param name="orden">Orden con su punto y coma.</param>
    /// <exception cref="OrdenPeligrosaException">
    /// Si ademas de lo anterior pone el equipo en antena: transmitir se pide siempre al
    /// vigilante del PTT, nunca por la valvula de escape.
    /// </exception>
    public static void ComprobarQueValeEnCrudo(string orden)
    {
        ComprobarQueEsSegura(orden);

        var limpia = orden.Trim().ToUpperInvariant();
        if (limpia.StartsWith("TX", StringComparison.Ordinal))
        {
            throw new OrdenPeligrosaException(
                orden,
                "pondría el equipo en antena sin pasar por el vigilante del PTT");
        }
    }

    /// <summary>
    /// La respuesta del equipo dice que no admite esa orden <b>tal y como se ha escrito</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Un <c>?;</c> <b>no prueba que la orden no exista</b>. Puede ser que falte un parametro o
    /// que tenga otra longitud. Paso de verdad: se probo <c>MT00;</c> con dos cifras de canal,
    /// contesto <c>?;</c> y se dio por hecho que este equipo no sabia leer memorias. El canal
    /// son tres cifras: con <c>MT001;</c> contesta perfectamente.
    /// </para>
    /// <para>
    /// Asi que antes de dar una capacidad por ausente hay que mirar el <b>manual de referencia
    /// CAT</b> —que no es el manual de operacion— y comprobar el formato. Y al reves: en algunos
    /// sitios el <c>?;</c> es informacion util, como en la lectura de memorias, donde significa
    /// que ese canal esta vacio.
    /// </para>
    /// </remarks>
    /// <param name="respuesta">Lo que contesto el equipo.</param>
    /// <returns>Verdadero si el equipo no admite la orden tal y como se escribio.</returns>
    public static bool DiceQueNoLoAdmite(string? respuesta) =>
        respuesta is null || respuesta.Trim() == Cat.NoAdmitido;
}
