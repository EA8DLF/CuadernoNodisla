using System.Globalization;
using Nodisla.Cuaderno.Aplicacion.Puertos;

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
    public string OrdenDeEscritura(double valor, VfoDelEquipo vfo = VfoDelEquipo.Principal)
    {
        var consulta = ConsultaDe(vfo)
            ?? throw new NotSupportedException($"El mando {Mando} no existe en el VFO {vfo}.");

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
/// Lo unico que sigue sin convertirse es lo que Yaesu llama «nivel» de la muesca
/// (<c>BP01</c>), porque ni el manual ni ninguna captura dicen en que unidad viene.
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
            IgualInverso),
        new(MandoDeEquipo.SupresorDeRuido, "NB0", 1, new RangoDeMando(MandoDeEquipo.SupresorDeRuido, 0, 1, 1), Igual, IgualInverso)
        {
            ConsultaDelSegundoVfo = "NB1",
        },
        new(MandoDeEquipo.NivelSupresorDeRuido, "NL0", 3, new RangoDeMando(MandoDeEquipo.NivelSupresorDeRuido, 0, 10, 1), Igual, IgualInverso),
        new(MandoDeEquipo.ReductorDeRuido, "NR0", 1, new RangoDeMando(MandoDeEquipo.ReductorDeRuido, 0, 1, 1), Igual, IgualInverso),
        new(MandoDeEquipo.NivelReductorDeRuido, "RL0", 2, new RangoDeMando(MandoDeEquipo.NivelReductorDeRuido, 1, 15, 1), Igual, IgualInverso),
        new(MandoDeEquipo.MuescaAutomatica, "BC0", 1, new RangoDeMando(MandoDeEquipo.MuescaAutomatica, 0, 1, 1), Igual, IgualInverso),
        new(MandoDeEquipo.MuescaManual, "BP00", 3, new RangoDeMando(MandoDeEquipo.MuescaManual, 0, 1, 1), Igual, IgualInverso),

        // Yaesu llama a esto «nivel» de la muesca y el equipo contesta 150. Que cada paso sean
        // diez hercios es lo que dice el manual de la familia, pero no se ha podido comprobar
        // sin escribir en el equipo: se ensena el indice tal cual antes que dar hercios que no
        // constan.
        new(
            MandoDeEquipo.FrecuenciaDeMuesca,
            "BP01",
            3,
            new RangoDeMando(MandoDeEquipo.FrecuenciaDeMuesca, 0, 320, 1, "índice"),
            Igual,
            IgualInverso),

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
        // Se deja de solo lectura porque el recorrido no consta en ninguna captura, y accionarlo
        // con un rango inventado seria mover el receptor a donde no toca.
        new(
            MandoDeEquipo.DesplazamientoFi,
            "IS0",
            4,
            new RangoDeMando(MandoDeEquipo.DesplazamientoFi, -9999, 9999, 1, "Hz") { SoloLectura = true },
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
        // de una transmision pedida al vigilante del PTT.
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
            Igual,
            IgualInverso),

        // ── Frecuencia ──────────────────────────────────────────────────────
        new(MandoDeEquipo.Split, "ST", 1, new RangoDeMando(MandoDeEquipo.Split, 0, 1, 1), Igual, IgualInverso),
    ];

    /// <summary>Busca la descripcion de un mando.</summary>
    /// <param name="mando">Mando buscado.</param>
    /// <returns>La descripcion, o nulo si este control no lo maneja.</returns>
    public static MandoFt710? Buscar(MandoDeEquipo mando) =>
        Todos.FirstOrDefault(descripcion => descripcion.Mando == mando);
}
