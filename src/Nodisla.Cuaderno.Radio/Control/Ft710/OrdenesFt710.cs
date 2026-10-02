using Nodisla.Cuaderno.Idiomas;

namespace Nodisla.Cuaderno.Radio.Control.Ft710;

/// <summary>Se lanza cuando se intenta mandar al equipo una orden que puede hacer daño.</summary>
public sealed class OrdenPeligrosaException : Exception
{
    /// <summary>Crea la excepcion.</summary>
    /// <param name="orden">La orden que se intentaba mandar.</param>
    /// <param name="porque">Por que no se manda.</param>
    public OrdenPeligrosaException(string orden, string porque)
        : base(Textos.F("Servicios.Radio.OrdenPeligrosa", orden, porque))
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

    // ── Telegrafia por el manipulador interno (manual CAT FT-710, 2306-C, pag. 14) ──────────
    //
    // KM P1 P2…P2 ;  escribe la memoria de TEXTO del manipulador P1 (1-5), hasta 50 caracteres.
    // KM P1 ;        la lee.
    // KY P1 P2 ;     P1 0 = memoria de TEXTO, 1 = de MENSAJE; P2 1-5 = reproducir esa memoria,
    //                P2 0 = PARAR. Solo «Set»: no se puede preguntar si sigue manipulando.
    // KS P1P1P1 ;    velocidad 004-060 WPM.
    //
    // El FT-710 NO tiene «KY texto;» como otros Yaesu: el texto libre se escribe en una memoria
    // de texto (KM) y se reproduce (KY0n). Por eso un mensaje largo va en trozos de 50.

    /// <summary>Caracteres que caben en una memoria de texto del manipulador (manual, pag. 14).</summary>
    public const int LetrasPorMemoriaDelManipulador = 50;

    /// <summary>Velocidad mas baja del manipulador (<c>KS004;</c>).</summary>
    public const int WpmMinima = 4;

    /// <summary>Velocidad mas alta del manipulador (<c>KS060;</c>).</summary>
    public const int WpmMaxima = 60;

    /// <summary>
    /// Para el manipulador en el acto: <c>KY</c> con P1 = 0 (texto) y P2 = 0 (STOP).
    /// </summary>
    /// <remarks>Parar nunca hace daño: esta orden pasa siempre, sin ambito ninguno.</remarks>
    public const string PararElManipulador = "KY00;";

    /// <summary>
    /// Ambito en el que se deja pasar <c>KM</c> con texto y <c>KY</c> de reproducir. Solo lo abre
    /// <see cref="ConManipulacionAutorizadaAsync"/>, que es <c>internal</c>, y solo lo usa el
    /// control con el PTT ya pedido al vigilante.
    /// </summary>
    private static readonly AsyncLocal<bool> ManipulacionAutorizada = new();

    /// <summary>
    /// Ejecuta la escritura de la memoria de texto y su reproduccion dejando pasar <c>KM</c> y
    /// <c>KY</c> solo mientras dura, y solo en esa rama de la ejecucion.
    /// </summary>
    internal static async Task ConManipulacionAutorizadaAsync(Func<Task> manipular)
    {
        ArgumentNullException.ThrowIfNull(manipular);
        ManipulacionAutorizada.Value = true;
        try
        {
            await manipular().ConfigureAwait(false);
        }
        finally
        {
            ManipulacionAutorizada.Value = false;
        }
    }

    /// <summary>Orden de velocidad del manipulador, acotada a 4-60 WPM: <c>KS025;</c>.</summary>
    /// <param name="wpm">Palabras por minuto.</param>
    /// <returns>La orden.</returns>
    public static string OrdenDeVelocidad(int wpm) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"KS{Math.Clamp(wpm, WpmMinima, WpmMaxima):000};");

    /// <summary>Orden que escribe la memoria de texto <paramref name="memoria"/>: <c>KM5CQ CQ;</c>.</summary>
    /// <param name="memoria">Memoria del manipulador, 1 a 5.</param>
    /// <param name="texto">Texto ya preparado con <see cref="TextoParaElManipulador"/>.</param>
    /// <returns>La orden.</returns>
    public static string OrdenDeEscribirTexto(int memoria, string texto)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(memoria, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(memoria, 5);
        ArgumentNullException.ThrowIfNull(texto);
        if (texto.Length is 0 or > LetrasPorMemoriaDelManipulador)
        {
            throw new ArgumentOutOfRangeException(nameof(texto), texto.Length, $"Una memoria de texto lleva de 1 a {LetrasPorMemoriaDelManipulador} caracteres.");
        }

        return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"KM{memoria}{texto};");
    }

    /// <summary>Orden que reproduce la memoria de texto <paramref name="memoria"/>: <c>KY05;</c>.</summary>
    /// <param name="memoria">Memoria del manipulador, 1 a 5.</param>
    /// <returns>La orden.</returns>
    public static string OrdenDeReproducirTexto(int memoria)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(memoria, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(memoria, 5);
        return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"KY0{memoria};");
    }

    /// <summary>
    /// Deja un texto en lo que sabe manipular la memoria de texto del FT-710.
    /// </summary>
    /// <remarks>
    /// Mayusculas, cifras, espacio y <c>/ ? . , = + -</c>. <c>&lt;AR&gt;</c> pasa a <c>+</c> y
    /// <c>&lt;BT&gt;</c> a <c>=</c>; los demas prosignos se mandan como sus dos letras. Fuera
    /// queda todo lo demas, y sobre todo <c>#</c> (en la memoria de texto es «numero de
    /// concurso», el del propio equipo) y <c>;</c> (cerraria la orden CAT a la mitad).
    /// </remarks>
    /// <param name="texto">Texto con prosignos entre angulos.</param>
    /// <returns>El texto limpio, sin espacios dobles ni en los extremos.</returns>
    public static string TextoParaElManipulador(string texto)
    {
        ArgumentNullException.ThrowIfNull(texto);
        var t = texto.ToUpperInvariant().Replace("<AR>", "+", StringComparison.Ordinal).Replace("<BT>", "=", StringComparison.Ordinal);
        var limpio = new System.Text.StringBuilder(t.Length);
        foreach (var c in t)
        {
            if (c is (>= 'A' and <= 'Z') or (>= '0' and <= '9') or ' ' or '/' or '?' or '.' or ',' or '=' or '+' or '-')
            {
                limpio.Append(c);
            }
        }

        return string.Join(' ', limpio.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    /// Ambito en el que se deja pasar <c>PS0;</c>. Solo lo abre <see cref="ConApagadoAutorizadoAsync"/>,
    /// y es <c>internal</c>: nada fuera de este ensamblado puede activarlo.
    /// </summary>
    private static readonly AsyncLocal<bool> ApagadoAutorizado = new();

    /// <summary>
    /// Ejecuta el apagado dejando pasar <c>PS0;</c> solo mientras dura, y solo en esa rama de la
    /// ejecucion (AsyncLocal: el sondeo que corre a la vez en otro hilo no lo hereda).
    /// </summary>
    internal static async Task ConApagadoAutorizadoAsync(Func<Task> apagar)
    {
        ArgumentNullException.ThrowIfNull(apagar);
        ApagadoAutorizado.Value = true;
        try
        {
            await apagar().ConfigureAwait(false);
        }
        finally
        {
            ApagadoAutorizado.Value = false;
        }
    }

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

        // Parar el manipulador pasa siempre: no transmite, corta.
        if (limpia is PararElManipulador or "KY10;") return;

        // KM con texto escribe una memoria del manipulador: solo dentro del ambito de manipular.
        // Leerla («KM5;») si vale.
        if (limpia.StartsWith("KM", StringComparison.Ordinal) && limpia.Length > 4 && !ManipulacionAutorizada.Value)
        {
            throw new OrdenPeligrosaException(orden, Textos.T("Servicios.Radio.Peligro.Telegrafia"));
        }

        foreach (var prohibida in Prohibidas)
        {
            // KY de reproducir la memoria de texto (KY01;…KY05;) dentro del ambito que abre el
            // control con el PTT pedido al vigilante. Nada mas: ni la memoria de MENSAJE (KY1n),
            // ni otra longitud, ni fuera del ambito. Autorizado por Jose el 02-10-2026 («quiero
            // poder transmitir CW desde el cuaderno»).
            if (prohibida == "KY" && ManipulacionAutorizada.Value && limpia.Length == 5
                && limpia[2] == '0' && limpia[3] is >= '1' and <= '5' && limpia[4] == ';')
            {
                continue;
            }

            // La UNICA excepcion: PS0; exacta, dentro del ambito que abre ControlFt710.ApagarAsync
            // tras la pulsacion larga de LOCK y la confirmacion del operador. Autorizado por Jose
            // expresamente el 29-09-2026 («Encender y apagar con LOCK: … adelante»). Fuera de ese
            // ambito PS0 sigue prohibida: ni sondeo, ni ordenes en crudo, ni nada.
            if (prohibida == "PS0" && limpia == "PS0;" && ApagadoAutorizado.Value) continue;

            if (limpia.StartsWith(prohibida, StringComparison.Ordinal))
            {
                throw new OrdenPeligrosaException(
                    orden,
                    prohibida switch
                    {
                        "PS0" => Textos.T("Servicios.Radio.Peligro.Apagaria"),
                        "MW" => Textos.T("Servicios.Radio.Peligro.EscribiriaMemorias"),
                        _ => Textos.T("Servicios.Radio.Peligro.Telegrafia"),
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
                Textos.T("Servicios.Radio.Peligro.SinVigilante"));
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
