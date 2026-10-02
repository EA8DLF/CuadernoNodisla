using System.Globalization;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Idiomas;

namespace Nodisla.Cuaderno.Radio.Control.Ft710;

/// <summary>Cual de los dos receptores del equipo.</summary>
public enum VfoDelEquipo
{
    /// <summary>El VFO principal, que es el que lo tiene todo.</summary>
    Principal,

    /// <summary>El segundo VFO, que tiene menos mandos.</summary>
    Secundario,
}

/// <summary>
/// Un mando del FT-710: que orden lo lee, que orden lo acciona y como se pasa del numero
/// interno del equipo a lo que el operador quiere ver.
/// </summary>
/// <param name="Mando">Mando del puerto de aplicacion.</param>
/// <param name="Consulta">Orden de consulta sin punto y coma, por ejemplo <c>AG0</c>.</param>
/// <param name="Digitos">Cuantos digitos ocupa el valor en la orden.</param>
/// <param name="Rango">Rango que se ensena al operador, ya en unidades de verdad.</param>
/// <param name="DelEquipo">Pasa el numero interno del equipo al valor del operador.</param>
/// <param name="AlEquipo">Pasa el valor del operador al numero interno del equipo.</param>
public sealed record MandoFt710(
    MandoDeEquipo Mando,
    string Consulta,
    int Digitos,
    RangoDeMando Rango,
    Func<int, double> DelEquipo,
    Func<double, int> AlEquipo)
{
    /// <summary>
    /// Consulta equivalente para el segundo VFO, cuando el equipo la admite.
    /// </summary>
    /// <remarks>
    /// Solo estan puestas las que se han visto contestar de verdad. <c>SH1</c>, <c>NA1</c>,
    /// <c>RG1</c>, <c>GT1</c> y <c>RA1</c> contestan <c>?;</c>: esos mandos existen unicamente
    /// para el VFO principal.
    /// </remarks>
    public string? ConsultaDelSegundoVfo { get; init; }

    /// <summary>El valor viaja con signo delante, como en <c>IS00+0000</c>.</summary>
    public bool ConSigno { get; init; }

    /// <summary>
    /// Cifras de relleno que el equipo mete entre la orden y el valor.
    /// </summary>
    /// <remarks>
    /// El desplazamiento de frecuencia intermedia contesta <c>IS00+0000</c>: tras <c>IS0</c> hay
    /// un cero que no es parte del valor. Sin esto, el valor no se lee y el mando parece no
    /// existir.
    /// </remarks>
    public int CifrasDeRelleno { get; init; }

    /// <summary>
    /// Lectura propia, para las ordenes cuyo valor no va al final en cifras decimales: el
    /// analizador (<c>SS06</c> con un digito hexadecimal y relleno detras), el clarificador
    /// (<c>CF000</c>, dos interruptores en una respuesta) o la pantalla (<c>DA</c>, tres valores).
    /// Recibe la respuesta entera sin el punto y coma y devuelve el valor del operador.
    /// </summary>
    public Func<string, double?>? LecturaPropia { get; init; }

    /// <summary>
    /// Escritura propia: recibe el valor del operador ya ajustado al rango y, si
    /// <see cref="NecesitaLoQueHay"/>, la respuesta actual del equipo a la consulta.
    /// </summary>
    public Func<double, string?, string>? EscrituraPropia { get; init; }

    /// <summary>
    /// Para escribir hay que saber lo que tiene ahora el equipo: <c>CF000</c> lleva a la vez el
    /// clarificador de recepcion y el de transmision, y <c>DA</c> el contraste, el brillo y los
    /// pilotos. Tocar uno sin leer los otros los pisaria.
    /// </summary>
    public bool NecesitaLoQueHay { get; init; }

    /// <summary>Orden de lectura, lista para mandar.</summary>
    public string OrdenDeLectura => Consulta + Cat.Fin;

    /// <summary>Orden de lectura del segundo VFO, si lo admite.</summary>
    public string? OrdenDeLecturaDelSegundoVfo =>
        ConsultaDelSegundoVfo is null ? null : ConsultaDelSegundoVfo + Cat.Fin;

    /// <summary>Consulta para el VFO indicado, o nulo si ese VFO no tiene este mando.</summary>
    /// <param name="vfo">VFO del que se quiere la consulta.</param>
    /// <returns>La consulta sin punto y coma.</returns>
    public string? ConsultaDe(VfoDelEquipo vfo) =>
        vfo == VfoDelEquipo.Principal ? Consulta : ConsultaDelSegundoVfo;

    /// <summary>Orden de escritura para un valor del operador.</summary>
    /// <param name="valor">Valor en las unidades del operador.</param>
    /// <param name="vfo">VFO al que se le manda.</param>
    /// <returns>La orden lista para mandar.</returns>
    /// <exception cref="NotSupportedException">Si ese VFO no tiene este mando.</exception>
    public string OrdenDeEscritura(double valor, VfoDelEquipo vfo = VfoDelEquipo.Principal) =>
        OrdenDeEscritura(valor, vfo, loQueHay: null);

    /// <summary>Orden de escritura, con lo que contesta ahora el equipo si hace falta.</summary>
    /// <param name="valor">Valor en las unidades del operador.</param>
    /// <param name="vfo">VFO al que se le manda.</param>
    /// <param name="loQueHay">Respuesta actual a la consulta, sin punto y coma.</param>
    /// <returns>La orden lista para mandar.</returns>
    /// <exception cref="NotSupportedException">Si ese VFO no tiene este mando.</exception>
    public string OrdenDeEscritura(double valor, VfoDelEquipo vfo, string? loQueHay)
    {
        var consulta = ConsultaDe(vfo)
            ?? throw new NotSupportedException(Textos.F("Servicios.Radio.MandoNoEnVfo", Mando, vfo));

        if (EscrituraPropia is { } propia)
        {
            return propia(Rango.Ajustar(valor), loQueHay);
        }

        var interno = AlEquipo(Rango.Ajustar(valor));
        var relleno = new string('0', CifrasDeRelleno);
        var cifras = ConSigno
            ? (interno < 0 ? "-" : "+") + Math.Abs(interno).ToString($"D{Digitos}", CultureInfo.InvariantCulture)
            : interno.ToString($"D{Digitos}", CultureInfo.InvariantCulture);

        return consulta + relleno + cifras + Cat.Fin;
    }

    /// <summary>
    /// Interpreta la respuesta del equipo y devuelve el valor en unidades del operador.
    /// </summary>
    /// <remarks>
    /// El equipo no siempre contesta con la misma orden que se le pregunta: a <c>SQ1;</c>
    /// responde <c>SQ0000;</c>, con el indice del primer VFO. Por eso se compara solo la parte
    /// de letras y luego se saltan tantos caracteres como tenia la consulta.
    /// </remarks>
    /// <param name="respuesta">Respuesta sin el punto y coma.</param>
    /// <param name="vfo">VFO al que se pregunto.</param>
    /// <returns>El valor, o nulo si la respuesta no vale.</returns>
    public double? Interpretar(string? respuesta, VfoDelEquipo vfo = VfoDelEquipo.Principal)
    {
        if (OrdenesFt710.DiceQueNoLoAdmite(respuesta))
        {
            return null;
        }

        var consulta = ConsultaDe(vfo);
        if (consulta is null)
        {
            return null;
        }

        var texto = respuesta!.Trim();
        if (LecturaPropia is { } propia)
        {
            return texto.StartsWith(consulta, StringComparison.OrdinalIgnoreCase) ? propia(texto) : null;
        }

        var letras = new string(consulta.TakeWhile(char.IsAsciiLetter).ToArray());
        var aSaltar = consulta.Length + CifrasDeRelleno;
        if (!texto.StartsWith(letras, StringComparison.OrdinalIgnoreCase) || texto.Length <= aSaltar)
        {
            return null;
        }

        var cifras = texto[aSaltar..];
        return int.TryParse(
            cifras,
            NumberStyles.Integer | NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture,
            out var interno)
            ? DelEquipo(interno)
            : null;
    }
}

/// <summary>
/// Tabla de mandos del FT-710, sacada de lo que el equipo contesta de verdad.
/// </summary>
/// <remarks>
/// <para>
/// Las conversiones que estan puestas son las que se pueden justificar: la velocidad del
/// manipulador y la potencia vienen en sus unidades, el tono de telegrafia es
/// <c>300 + n × 10</c> hercios (por eso <c>KP40</c> son 700 Hz, que es el valor de fabrica), y
/// el volumen y la ganancia de radiofrecuencia van de 0 a 255 tal y como dice la captura.
/// </para>
/// <para>
/// El ancho de filtro y el retardo del circuito de voz ya salen en sus unidades: las tablas
/// estaban en el manual de referencia CAT, que no es el manual de operacion. Ver
/// <see cref="DeDondeSalenLasTablas"/>, <see cref="AnchosDeFiltroFt710"/> y
/// <see cref="RetardosDeVozFt710"/>.
/// </para>
/// <para>
/// La frecuencia de la muesca (<c>BP01</c>) va en pasos de 10 Hz segun el manual CAT, y asi
/// se ensena desde la validacion contra el equipo real del 27-09-2026
/// (<c>docs/12-cat-ft710-validado.md</c>).
/// </para>
/// </remarks>
public static class MandosFt710
{
    /// <summary>
    /// De donde salen las tablas de este modulo.
    /// </summary>
    /// <remarks>
    /// Del <b>manual de referencia CAT</b> de Yaesu, <c>FT-710_CAT_OM_ENG_2306-C.pdf</c>, que no
    /// es el mismo documento que el manual de operacion: aquel describe el frontal y este las
    /// ordenes. El ancho de filtro y el retardo de voz se dieron por irresolubles mirando el
    /// documento equivocado, y estuvieron a punto de costar escribir en el equipo del operador
    /// para deducirlos a mano.
    /// </remarks>
    public const string DeDondeSalenLasTablas =
        "Manual de referencia CAT de Yaesu (FT-710_CAT_OM_ENG_2306-C.pdf), distinto del manual "
        + "de operación.";

    /// <summary>Lo que puede ajustar el mando FUNC, en el orden del manual CAT (SF0 1…H).</summary>
    public static readonly string[] FuncionesDelMandoFunc =
    [
        "SCOPE LEVEL", "PEAK", "COLOR", "CONTRAST", "DIMMER", "M-GROUP", "MIC GAIN", "PROC LEVEL",
        "AMC LEVEL", "VOX GAIN", "VOX DELAY", "ANTI VOX", "RF POWER", "MONI LEVEL", "CW SPEED",
        "CW PITCH", "BK-DELAY",
    ];

    /// <summary>Lo que puede ajustar el mando DSP (SF1 1…5).</summary>
    public static readonly string[] FuncionesDelMandoDsp = ["SHIFT", "WIDTH", "NOTCH", "CONTOUR", "APF"];

    private static readonly Func<int, double> Igual = valor => valor;
    private static readonly Func<double, int> IgualInverso = valor => (int)Math.Round(valor, MidpointRounding.AwayFromZero);

    /// <summary>Todos los mandos que este control sabe manejar.</summary>
    /// <remarks>
    /// Que esten aqui no quiere decir que el equipo los admita: al conectar se prueba uno a uno
    /// —y por cada VFO— y el que conteste <c>?;</c> se queda fuera.
    /// </remarks>
    public static IReadOnlyList<MandoFt710> Todos { get; } =
    [
        // ── Nivel y potencia ────────────────────────────────────────────────
        new(MandoDeEquipo.Volumen, "AG0", 3, new RangoDeMando(MandoDeEquipo.Volumen, 0, 255, 1), Igual, IgualInverso)
        {
            ConsultaDelSegundoVfo = "AG1",
        },
        new(MandoDeEquipo.GananciaRf, "RG0", 3, new RangoDeMando(MandoDeEquipo.GananciaRf, 0, 255, 1), Igual, IgualInverso),
        new(MandoDeEquipo.GananciaMicrofono, "MG", 3, new RangoDeMando(MandoDeEquipo.GananciaMicrofono, 0, 100, 1), Igual, IgualInverso),
        new(MandoDeEquipo.Potencia, "PC", 3, new RangoDeMando(MandoDeEquipo.Potencia, 5, 100, 1, "W"), Igual, IgualInverso),
        new(MandoDeEquipo.Silenciador, "SQ0", 3, new RangoDeMando(MandoDeEquipo.Silenciador, 0, 100, 1), Igual, IgualInverso)
        {
            ConsultaDelSegundoVfo = "SQ1",
        },

        // ── Recepcion ───────────────────────────────────────────────────────
        new(
            MandoDeEquipo.Atenuador,
            "RA0",
            1,
            new RangoDeMando(MandoDeEquipo.Atenuador, 0, 3, 1, "dB", ["Apagado", "6 dB", "12 dB", "18 dB"]),
            Igual,
            IgualInverso),
        new(
            MandoDeEquipo.Preamplificador,
            "PA0",
            1,
            new RangoDeMando(MandoDeEquipo.Preamplificador, 0, 2, 1, null, ["Sin preamplificador", "Amplificador 1", "Amplificador 2"]),
            Igual,
            IgualInverso)
        {
            ConsultaDelSegundoVfo = "PA1",
        },
        new(
            MandoDeEquipo.Agc,
            "GT0",
            1,
            new RangoDeMando(
                MandoDeEquipo.Agc,
                0,
                6,
                1,
                null,
                ["Apagado", "Rápido", "Medio", "Lento", "Automático rápido", "Automático medio", "Automático lento"]),
            Igual,
            // El manual CAT: al LEER, 4/5/6 son automatico rapido/medio/lento (el equipo del operador
            // contesta GT06); al ESCRIBIR solo existe 4, «automatico», y el equipo elige la
            // velocidad segun el modo. Mandar GT05 o GT06 es una orden que el manual no recoge.
            valor => Math.Min(IgualInverso(valor), 4)),
        new(MandoDeEquipo.SupresorDeRuido, "NB0", 1, new RangoDeMando(MandoDeEquipo.SupresorDeRuido, 0, 1, 1), Igual, IgualInverso)
        {
            ConsultaDelSegundoVfo = "NB1",
        },
        new(MandoDeEquipo.NivelSupresorDeRuido, "NL0", 3, new RangoDeMando(MandoDeEquipo.NivelSupresorDeRuido, 0, 10, 1), Igual, IgualInverso),
        new(MandoDeEquipo.ReductorDeRuido, "NR0", 1, new RangoDeMando(MandoDeEquipo.ReductorDeRuido, 0, 1, 1), Igual, IgualInverso),
        new(MandoDeEquipo.NivelReductorDeRuido, "RL0", 2, new RangoDeMando(MandoDeEquipo.NivelReductorDeRuido, 1, 15, 1), Igual, IgualInverso),
        new(MandoDeEquipo.MuescaAutomatica, "BC0", 1, new RangoDeMando(MandoDeEquipo.MuescaAutomatica, 0, 1, 1), Igual, IgualInverso),
        new(MandoDeEquipo.MuescaManual, "BP00", 3, new RangoDeMando(MandoDeEquipo.MuescaManual, 0, 1, 1), Igual, IgualInverso),

        // Frecuencia de la muesca: el manual CAT la da en pasos de 10 Hz (001-320 = 10-3200 Hz).
        // Las dos capturas del equipo real son coherentes con ello (sin escribir nada): el 22-09 contesto
        // BP01150 con el contorno en CO011500 (1500 Hz los dos, el valor de fabrica), y el
        // 27-09 BP01001 con CO010010 (10 Hz los dos, el minimo).
        new(
            MandoDeEquipo.FrecuenciaDeMuesca,
            "BP01",
            3,
            new RangoDeMando(MandoDeEquipo.FrecuenciaDeMuesca, 10, 3200, 10, "Hz"),
            valor => valor * 10d,
            valor => (int)Math.Round(valor / 10d, MidpointRounding.AwayFromZero)),

        new(MandoDeEquipo.Contorno, "CO00", 4, new RangoDeMando(MandoDeEquipo.Contorno, 0, 1, 1), Igual, IgualInverso),
        new(
            MandoDeEquipo.FrecuenciaDeContorno,
            "CO01",
            4,
            new RangoDeMando(MandoDeEquipo.FrecuenciaDeContorno, 10, 3200, 10, "Hz"),
            Igual,
            IgualInverso),

        // El ancho en hercios depende del modo, asi que el rango de verdad lo arma el control
        // con el modo que tenga puesto el equipo (ver AnchosDeFiltroFt710). Esto es el de
        // reserva, para cuando todavia no se sabe en que modo esta.
        new(MandoDeEquipo.AnchoDeFiltro, "SH0", 3, new RangoDeMando(MandoDeEquipo.AnchoDeFiltro, 0, 23, 1, "índice"), Igual, IgualInverso),

        // Desplazamiento de la frecuencia intermedia. El equipo contesta con signo (IS00+0000).
        // El manual CAT da el recorrido: de -1200 a +1200 Hz en pasos de 20. Comprobado en la
        // radio el 28-09-2026: IS00+0100 e IS00-0240 se leen de vuelta tal cual.
        new(
            MandoDeEquipo.DesplazamientoFi,
            "IS0",
            4,
            new RangoDeMando(MandoDeEquipo.DesplazamientoFi, -1200, 1200, 20, "Hz"),
            Igual,
            IgualInverso)
        {
            ConsultaDelSegundoVfo = "IS1",
            ConSigno = true,
            CifrasDeRelleno = 1,
        },

        // ── Telegrafia ──────────────────────────────────────────────────────
        new(
            MandoDeEquipo.TonoCw,
            "KP",
            2,
            new RangoDeMando(MandoDeEquipo.TonoCw, 300, 1050, 10, "Hz"),
            valor => 300d + (valor * 10d),
            valor => (int)Math.Round((valor - 300d) / 10d, MidpointRounding.AwayFromZero)),
        new(MandoDeEquipo.VelocidadKeyer, "KS", 3, new RangoDeMando(MandoDeEquipo.VelocidadKeyer, 4, 60, 1, "ppm"), Igual, IgualInverso),
        new(MandoDeEquipo.BreakIn, "BI", 1, new RangoDeMando(MandoDeEquipo.BreakIn, 0, 1, 1), Igual, IgualInverso),

        // ── Transmision ─────────────────────────────────────────────────────
        new(MandoDeEquipo.Vox, "VX", 1, new RangoDeMando(MandoDeEquipo.Vox, 0, 1, 1), Igual, IgualInverso),
        new(MandoDeEquipo.GananciaVox, "VG", 3, new RangoDeMando(MandoDeEquipo.GananciaVox, 0, 100, 1), Igual, IgualInverso),

        // Retardo del circuito de voz, ya en milisegundos: la tabla esta en el manual CAT.
        // Se escribe con dos cifras porque es lo que contesta este firmware, aunque el manual
        // declare cuatro; al leer se admiten las dos longitudes.
        new(
            MandoDeEquipo.RetardoVox,
            "VD",
            2,
            new RangoDeMando(
                MandoDeEquipo.RetardoVox,
                0,
                RetardosDeVozFt710.IndiceMaximo,
                1,
                "ms",
                RetardosDeVozFt710.Etiquetas()),
            Igual,
            IgualInverso),

        // El acoplador: sintonizar emite portadora, asi que va marcado y solo se acciona dentro
        // de una transmision pedida al vigilante del PTT. En el manual CAT, AC P1 P2 P3 con
        // P3 = 0 apagado, 1 encendido y 3 «Tuning Start»; el 2 es un guion. Antes la posicion
        // «Sintonizar» mandaba AC002, que no es nada.
        new(
            MandoDeEquipo.Sintonizador,
            "AC",
            3,
            new RangoDeMando(
                MandoDeEquipo.Sintonizador,
                0,
                2,
                1,
                null,
                ["Apagado", "Encendido", "Sintonizar (emite portadora)"])
            {
                TransmiteAlAccionar = true,
            },
            valor => valor >= 2 ? 2 : valor,
            valor => IgualInverso(valor) >= 2 ? 3 : IgualInverso(valor)),
        new(MandoDeEquipo.AntiVox, "AV", 3, new RangoDeMando(MandoDeEquipo.AntiVox, 1, 100, 1), Igual, IgualInverso),
        new(MandoDeEquipo.NivelAmc, "AO", 3, new RangoDeMando(MandoDeEquipo.NivelAmc, 1, 100, 1), Igual, IgualInverso),
        new(MandoDeEquipo.Compresor, "PL", 3, new RangoDeMando(MandoDeEquipo.Compresor, 1, 100, 1), Igual, IgualInverso),
        new(MandoDeEquipo.Monitor, "ML1", 3, new RangoDeMando(MandoDeEquipo.Monitor, 0, 100, 1), Igual, IgualInverso),
        new(
            MandoDeEquipo.RetardoBreakIn,
            "SD",
            2,
            new RangoDeMando(
                MandoDeEquipo.RetardoBreakIn,
                0,
                RetardosDeVozFt710.IndiceMaximo,
                1,
                "ms",
                RetardosDeVozFt710.Etiquetas()),
            Igual,
            IgualInverso),

        // ── Frecuencia ──────────────────────────────────────────────────────
        new(MandoDeEquipo.Split, "ST", 1, new RangoDeMando(MandoDeEquipo.Split, 0, 1, 1), Igual, IgualInverso),

        // El clarificador (CLAR). RT/XT/RC contestan «?;» en este firmware; la orden buena es CF:
        // CF000 da los dos interruptores (P4 recepcion, P5 transmision) y CF001 el desplazamiento
        // con signo. Comprobado en la radio el 28-09-2026: CF00010000 enciende el de recepcion y
        // el dial (FA) pasa a leerse con el desplazamiento sumado.
        new(MandoDeEquipo.Rit, "CF000", 1, new RangoDeMando(MandoDeEquipo.Rit, 0, 1, 1), Igual, IgualInverso)
        {
            LecturaPropia = texto => Cifra(texto, 5),
            EscrituraPropia = (valor, hay) => $"CF000{(valor >= 0.5 ? '1' : '0')}{Caracter(hay, 6, '0')}000;",
            NecesitaLoQueHay = true,
        },
        new(MandoDeEquipo.Xit, "CF000", 1, new RangoDeMando(MandoDeEquipo.Xit, 0, 1, 1), Igual, IgualInverso)
        {
            LecturaPropia = texto => Cifra(texto, 6),
            EscrituraPropia = (valor, hay) => $"CF000{Caracter(hay, 5, '0')}{(valor >= 0.5 ? '1' : '0')}000;",
            NecesitaLoQueHay = true,
        },
        new(
            MandoDeEquipo.DesplazamientoRit,
            "CF001",
            4,
            new RangoDeMando(MandoDeEquipo.DesplazamientoRit, -9990, 9990, 10, "Hz"),
            Igual,
            IgualInverso)
        {
            ConSigno = true,
        },
        new(MandoDeEquipo.FiltroEstrecho, "NA0", 1, new RangoDeMando(MandoDeEquipo.FiltroEstrecho, 0, 1, 1), Igual, IgualInverso),
        new(MandoDeEquipo.Bloqueo, "LK", 1, new RangoDeMando(MandoDeEquipo.Bloqueo, 0, 1, 1), Igual, IgualInverso),
        new(
            MandoDeEquipo.SintoniaFinaRapida,
            "FN",
            1,
            new RangoDeMando(MandoDeEquipo.SintoniaFinaRapida, 0, 2, 1, null, ["Normal", "Fina (FINE)", "Rápida (FAST)"]),
            Igual,
            IgualInverso),
        new(MandoDeEquipo.TonoDeReferenciaCw, "CS", 1, new RangoDeMando(MandoDeEquipo.TonoDeReferenciaCw, 0, 1, 1), Igual, IgualInverso),

        // Filtro de pico de audio (APF): CO02 encendido, CO03 de 0000 a 0050 = -250 a +250 Hz.
        new(MandoDeEquipo.Apf, "CO02", 4, new RangoDeMando(MandoDeEquipo.Apf, 0, 1, 1), Igual, IgualInverso),
        new(
            MandoDeEquipo.FrecuenciaApf,
            "CO03",
            4,
            new RangoDeMando(MandoDeEquipo.FrecuenciaApf, -250, 250, 10, "Hz"),
            valor => (valor - 25) * 10d,
            valor => (int)Math.Round(valor / 10d, MidpointRounding.AwayFromZero) + 25),

        // ── Pantalla y analizador del equipo ────────────────────────────────
        // DA00 cc bb ll: contraste, brillo de la pantalla y brillo de los pilotos, de 00 a 20.
        new(MandoDeEquipo.ContrastePantalla, "DA", 2, new RangoDeMando(MandoDeEquipo.ContrastePantalla, 0, 20, 1), Igual, IgualInverso)
        {
            LecturaPropia = texto => DosCifras(texto, 4),
            EscrituraPropia = (valor, hay) => PonerDosCifras(hay, 4, valor),
            NecesitaLoQueHay = true,
        },
        new(MandoDeEquipo.BrilloPantalla, "DA", 2, new RangoDeMando(MandoDeEquipo.BrilloPantalla, 0, 20, 1), Igual, IgualInverso)
        {
            LecturaPropia = texto => DosCifras(texto, 6),
            EscrituraPropia = (valor, hay) => PonerDosCifras(hay, 6, valor),
            NecesitaLoQueHay = true,
        },
        Analizador(MandoDeEquipo.EspectroVelocidad, '0', ["SLOW1", "SLOW2", "FAST1", "FAST2", "FAST3", "STOP"]),
        Analizador(MandoDeEquipo.EspectroPicos, '1', ["LV1", "LV2", "LV3", "LV4", "LV5"]),
        Analizador(
            MandoDeEquipo.EspectroColor,
            '3',
            ["COLOR-1", "COLOR-2", "COLOR-3", "COLOR-4", "COLOR-5", "COLOR-6", "COLOR-7", "COLOR-8", "COLOR-9", "COLOR-10", "COLOR-11"]),
        Analizador(
            MandoDeEquipo.EspectroAncho,
            '5',
            ["1 kHz", "2 kHz", "5 kHz", "10 kHz", "20 kHz", "50 kHz", "100 kHz", "200 kHz", "500 kHz", "1 MHz"]),
        new(
            MandoDeEquipo.EspectroModo,
            "SS06",
            1,
            new RangoDeMando(MandoDeEquipo.EspectroModo, 0, ModosDelAnalizadorFt710.Todos.Count - 1, 1, null, ModosDelAnalizadorFt710.Etiquetas()),
            Igual,
            IgualInverso)
        {
            LecturaPropia = texto => texto.Length > 4 ? ModosDelAnalizadorFt710.DesdeLoLeido(texto[4]) : null,
            EscrituraPropia = (valor, _) => $"SS06{ModosDelAnalizadorFt710.Todos[(int)valor].AlEscribir}0000;",
        },

        // Nivel de referencia: SS04 y cinco caracteres con signo y un decimal («-05.0»).
        new(MandoDeEquipo.EspectroNivel, "SS04", 5, new RangoDeMando(MandoDeEquipo.EspectroNivel, -30, 30, 0.5, "dB"), Igual, IgualInverso)
        {
            LecturaPropia = texto => texto.Length >= 9
                && double.TryParse(texto[4..9], NumberStyles.Float, CultureInfo.InvariantCulture, out var db)
                    ? db
                    : null,
            EscrituraPropia = (valor, _) =>
                "SS04" + (valor < 0 ? "-" : "+") + Math.Abs(valor).ToString("00.0", CultureInfo.InvariantCulture) + Cat.Fin,
        },

        // ── Los dos mandos de funcion del frontal ───────────────────────────
        // SF0 dice que ajusta el mando FUNC y SF1 que ajusta el DSP. El valor es un caracter:
        // de 1 a 9 y de A a H (SF0D = RF POWER, que es lo que tenia la radio del operador).
        new(
            MandoDeEquipo.FuncionDelMandoFunc,
            "SF0",
            1,
            new RangoDeMando(MandoDeEquipo.FuncionDelMandoFunc, 1, FuncionesDelMandoFunc.Length, 1, null, FuncionesDelMandoFunc),
            Igual,
            IgualInverso)
        {
            LecturaPropia = texto => texto.Length > 3 ? CaracterAFuncion(texto[3]) : null,
            EscrituraPropia = (valor, _) => $"SF0{FuncionACaracter((int)valor)};",
        },
        new(
            MandoDeEquipo.FuncionDelMandoDsp,
            "SF1",
            1,
            new RangoDeMando(MandoDeEquipo.FuncionDelMandoDsp, 1, FuncionesDelMandoDsp.Length, 1, null, FuncionesDelMandoDsp),
            Igual,
            IgualInverso)
        {
            LecturaPropia = texto => texto.Length > 3 ? CaracterAFuncion(texto[3]) : null,
            EscrituraPropia = (valor, _) => $"SF1{FuncionACaracter((int)valor)};",
        },
    ];

    private static MandoFt710 Analizador(MandoDeEquipo mando, char parametro, IReadOnlyList<string> etiquetas) =>
        new(mando, $"SS0{parametro}", 1, new RangoDeMando(mando, 0, etiquetas.Count - 1, 1, null, etiquetas), Igual, IgualInverso)
        {
            // SS0 P2 P3 P4…P7: el valor es P3, un digito hexadecimal, y detras cuatro ceros.
            LecturaPropia = texto => texto.Length > 4 && int.TryParse(texto[4..5], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)
                ? v
                : null,
            EscrituraPropia = (valor, _) => $"SS0{parametro}{((int)valor).ToString("X", CultureInfo.InvariantCulture)}0000;",
        };

    private static double? Cifra(string texto, int posicion) =>
        texto.Length > posicion && char.IsAsciiDigit(texto[posicion]) ? texto[posicion] - '0' : null;

    private static char Caracter(string? texto, int posicion, char porOmision) =>
        texto is not null && texto.Length > posicion ? texto[posicion] : porOmision;

    private static double? DosCifras(string texto, int posicion) =>
        texto.Length >= posicion + 2 && int.TryParse(texto.AsSpan(posicion, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var v)
            ? v
            : null;

    private static string PonerDosCifras(string? hay, int posicion, double valor)
    {
        if (hay is null || hay.Length < 10)
        {
            throw new InvalidOperationException("Para cambiar la pantalla hay que saber antes lo que tiene (DA;).");
        }

        var cifras = ((int)valor).ToString("D2", CultureInfo.InvariantCulture);
        return string.Concat(hay.AsSpan(0, posicion), cifras, hay.AsSpan(posicion + 2, 10 - posicion - 2)) + Cat.Fin;
    }

    private static double? CaracterAFuncion(char c) => c switch
    {
        >= '1' and <= '9' => c - '0',
        >= 'A' and <= 'H' => c - 'A' + 10,
        >= 'a' and <= 'h' => c - 'a' + 10,
        _ => null,
    };

    private static char FuncionACaracter(int funcion) =>
        funcion <= 9 ? (char)('0' + funcion) : (char)('A' + funcion - 10);

    /// <summary>Busca la descripcion de un mando.</summary>
    /// <param name="mando">Mando buscado.</param>
    /// <returns>La descripcion, o nulo si este control no lo maneja.</returns>
    public static MandoFt710? Buscar(MandoDeEquipo mando) =>
        Todos.FirstOrDefault(descripcion => descripcion.Mando == mando);
}
