using System.Globalization;
using Nodisla.Cuaderno.Aplicacion.Puertos;

namespace Nodisla.Cuaderno.Radio.Control.Ft710;

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
    /// <summary>Orden de lectura, lista para mandar.</summary>
    public string OrdenDeLectura => Consulta + Cat.Fin;

    /// <summary>Orden de escritura para un valor del operador.</summary>
    /// <param name="valor">Valor en las unidades del operador.</param>
    /// <returns>La orden lista para mandar.</returns>
    public string OrdenDeEscritura(double valor)
    {
        var ajustado = Rango.Ajustar(valor);
        var interno = AlEquipo(ajustado);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{Consulta}{interno.ToString($"D{Digitos}", CultureInfo.InvariantCulture)}{Cat.Fin}");
    }

    /// <summary>
    /// Interpreta la respuesta del equipo y devuelve el valor en unidades del operador.
    /// </summary>
    /// <param name="respuesta">Respuesta sin el punto y coma.</param>
    /// <returns>El valor, o nulo si la respuesta no vale.</returns>
    public double? Interpretar(string? respuesta)
    {
        if (OrdenesFt710.DiceQueNoLoAdmite(respuesta))
        {
            return null;
        }

        var texto = respuesta!.Trim();
        if (!texto.StartsWith(Consulta, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var cifras = texto[Consulta.Length..];
        return int.TryParse(cifras, NumberStyles.Integer, CultureInfo.InvariantCulture, out var interno)
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
/// Lo que <b>no</b> se convierte es lo que no se puede confirmar sin el manual CAT o sin
/// escribir en el equipo de Jose: el <b>ancho de filtro</b> (<c>SH0</c>) y el <b>retardo del
/// circuito de voz</b> (<c>VD</c>) se dejan como indice, con su unidad puesta a <c>índice</c>
/// para que la interfaz no mienta. Inventarse la tabla seria peor que enseñar el numero.
/// </para>
/// </remarks>
public static class MandosFt710
{
    private static readonly Func<int, double> Igual = valor => valor;
    private static readonly Func<double, int> IgualInverso = valor => (int)Math.Round(valor, MidpointRounding.AwayFromZero);

    /// <summary>Todos los mandos que este control sabe manejar.</summary>
    /// <remarks>
    /// Que esten aqui no quiere decir que el equipo los admita: al conectar se prueba uno a uno
    /// y el que conteste <c>?;</c> se queda fuera.
    /// </remarks>
    public static IReadOnlyList<MandoFt710> Todos { get; } =
    [
        // ── Nivel y potencia ────────────────────────────────────────────────
        new(MandoDeEquipo.Volumen, "AG0", 3, new RangoDeMando(MandoDeEquipo.Volumen, 0, 255, 1), Igual, IgualInverso),
        new(MandoDeEquipo.GananciaRf, "RG0", 3, new RangoDeMando(MandoDeEquipo.GananciaRf, 0, 255, 1), Igual, IgualInverso),
        new(MandoDeEquipo.GananciaMicrofono, "MG", 3, new RangoDeMando(MandoDeEquipo.GananciaMicrofono, 0, 100, 1), Igual, IgualInverso),
        new(MandoDeEquipo.Potencia, "PC", 3, new RangoDeMando(MandoDeEquipo.Potencia, 5, 100, 1, "W"), Igual, IgualInverso),
        new(MandoDeEquipo.Silenciador, "SQ0", 3, new RangoDeMando(MandoDeEquipo.Silenciador, 0, 100, 1), Igual, IgualInverso),

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
            IgualInverso),
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
        new(MandoDeEquipo.SupresorDeRuido, "NB0", 1, new RangoDeMando(MandoDeEquipo.SupresorDeRuido, 0, 1, 1), Igual, IgualInverso),
        new(MandoDeEquipo.NivelSupresorDeRuido, "NL0", 3, new RangoDeMando(MandoDeEquipo.NivelSupresorDeRuido, 0, 10, 1), Igual, IgualInverso),
        new(MandoDeEquipo.ReductorDeRuido, "NR0", 1, new RangoDeMando(MandoDeEquipo.ReductorDeRuido, 0, 1, 1), Igual, IgualInverso),
        new(MandoDeEquipo.NivelReductorDeRuido, "RL0", 2, new RangoDeMando(MandoDeEquipo.NivelReductorDeRuido, 1, 15, 1), Igual, IgualInverso),
        new(MandoDeEquipo.MuescaAutomatica, "BC0", 1, new RangoDeMando(MandoDeEquipo.MuescaAutomatica, 0, 1, 1), Igual, IgualInverso),
        new(MandoDeEquipo.MuescaManual, "BP00", 3, new RangoDeMando(MandoDeEquipo.MuescaManual, 0, 1, 1), Igual, IgualInverso),
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

        // Indice de filtro: el equipo no da hercios y la tabla de equivalencias no esta
        // confirmada, asi que se enseña el indice y se dice que lo es.
        new(MandoDeEquipo.AnchoDeFiltro, "SH0", 3, new RangoDeMando(MandoDeEquipo.AnchoDeFiltro, 0, 21, 1, "índice"), Igual, IgualInverso),

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

        // Indice de retardo: el equipo contesta dos cifras y la tabla en milisegundos no esta
        // confirmada. Se enseña el indice antes que inventar los milisegundos.
        new(MandoDeEquipo.RetardoVox, "VD", 2, new RangoDeMando(MandoDeEquipo.RetardoVox, 0, 99, 1, "índice"), Igual, IgualInverso),

        // El acoplador: apagado, encendido y «sintonizar». Sintonizar emite portadora, asi que
        // el mando va marcado con TransmiteAlAccionar y solo se puede tocar dentro de una
        // transmision pedida al vigilante del PTT, que es quien lo suelta si la cosa se tuerce.
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
