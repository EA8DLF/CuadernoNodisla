using System.Globalization;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Radio.Control.Ft710;
using Nodisla.Cuaderno.Radio.Modelos;

using Nodisla.Cuaderno.Idiomas;

namespace Nodisla.Cuaderno.Radio.Control.Yaesu;

/// <summary>
/// Los modelos Yaesu de CAT nuevo, cada uno con sus diferencias respecto del FT-710.
/// </summary>
/// <remarks>
/// Fuentes: los manuales CAT oficiales de yaesu.com (FTDX10_CAT_OM_ENG_2308-F,
/// FTDX101D_CAT_OM_ENG_1909-C, FT-991_CAT_OM_ENG_1612-D0, FT-991A_CAT_OM_ENG_1711-D,
/// FT-891_CAT_OM_ENG_1909-C, FTDX3000_CAT_OM_ENG, FTDX1200_CAT_OM_ENG, FTDX5000_CAT_OM_ENG_1907-D).
/// Solo el FT-710 esta validado con la radio.
/// </remarks>
public static class PerfilesYaesu
{
    private static readonly int[] VelocidadesUsb = [38400, 115200, 19200, 9600, 4800];

    private static readonly Func<int, double> Igual = v => v;
    private static readonly Func<double, int> IgualInverso = v => (int)Math.Round(v, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Mandos que solo tienen sentido en el FT-710 tal y como estan escritos (analizador por
    /// <c>SS</c>, mandos FUNC/DSP por <c>SF</c>, pantalla por <c>DA</c>).
    /// </summary>
    private static readonly HashSet<MandoDeEquipo> SoloFt710 =
    [
        MandoDeEquipo.EspectroVelocidad, MandoDeEquipo.EspectroPicos, MandoDeEquipo.EspectroColor,
        MandoDeEquipo.EspectroAncho, MandoDeEquipo.EspectroModo, MandoDeEquipo.EspectroNivel,
        MandoDeEquipo.FuncionDelMandoFunc, MandoDeEquipo.FuncionDelMandoDsp,
        MandoDeEquipo.ContrastePantalla, MandoDeEquipo.BrilloPantalla,
    ];

    private static CapacidadesDelModelo Hf(bool dosReceptores = false, bool analizador = false, bool sintonizador = true, int memorias = 99, long maximo = 56_000_000) =>
        new(DosVfos: true, DosReceptores: dosReceptores, Analizador: analizador, Sintonizador: sintonizador, Memorias: memorias,
            Mandos: true, Encendido: true, HerciosMinimo: 30_000, HerciosMaximo: maximo);

    private static ModeloDeEquipo Modelo(string clave, string nombre, string id, string? frontal, CapacidadesDelModelo capacidades, bool probado = false, string? notas = null, int[]? velocidades = null) =>
        new($"yaesu-{clave}", Fabricante.Yaesu, nombre, ProtocoloCat.YaesuAscii, id, null, velocidades ?? VelocidadesUsb, capacidades, frontal, probado, notas);

    private const string NotaComun =
        "Programado según el manual CAT, sin probar con la radio. Confirmar con la radio real: a qué VFO se refiere MD0 "
        + "y FT, la escala de los medidores (en el FT-710 el manual no bastó), el formato de RM y SM, el ancho de filtro (SH) "
        + "y que la sintonía del acoplador arranca con AC002.";

    /// <summary>FT-710 (ID0800): el de EA8DLF, validado con la radio.</summary>
    public static PerfilYaesu Ft710 { get; } = new()
    {
        Modelo = Modelo("ft710", "FT-710", ControlFt710.IdentificadorFt710, "FrontalFt710",
            Hf(analizador: true, memorias: 99), probado: true, velocidades: [115200, 38400, 19200, 9600, 4800]),
        Bandas = ControlFt710.BandasFt710,
        Mandos = MandosFt710.Todos,
        Teclas = PerfilYaesu.TodasLasTeclas,
        OrdenDeBorrarClarificador = "CF001+0000;",
        PasosDelMenuFt710 = true,
        OrdenDeSintonia = "AC003;",
        PosicionDeSintoniaEnRi = 7,
        CodecDelFt710 = true,
        AnalizadorFt4222 = true,
    };

    /// <summary>FTDX10 (ID0761).</summary>
    public static PerfilYaesu Ftdx10 { get; } = new()
    {
        Modelo = Modelo("ftdx10", "FTDX10", "0761", "FrontalFtdx10", Hf(), notas: NotaComun
            + " Misma generación que el FT-710: comprobar si SV intercambia de verdad o solo cambia de VFO."),
        Bandas = PerfilYaesu.BandasHf(con60: true),
        Mandos = Mandos(
            quitar: [MandoDeEquipo.Rit, MandoDeEquipo.Xit, MandoDeEquipo.DesplazamientoRit],
            poner: [Sintonizador(), RitPorRt(), XitPorXt()]),
        Teclas = PerfilYaesu.TeclasSinDspReset,
    };

    /// <summary>FTDX101D (ID0681).</summary>
    public static PerfilYaesu Ftdx101D { get; } = Ftdx101("ftdx101d", "FTDX101D", "0681", 100);

    /// <summary>FTDX101MP (ID0682).</summary>
    public static PerfilYaesu Ftdx101Mp { get; } = Ftdx101("ftdx101mp", "FTDX101MP", "0682", 200);

    private static PerfilYaesu Ftdx101(string clave, string nombre, string id, int vatios) => new()
    {
        Modelo = Modelo(clave, nombre, id, "FrontalFtdx101", Hf(dosReceptores: true, maximo: 74_000_000), notas: NotaComun
            + " Doble receptor: aquí MD0/FT se toman como banda principal = VFO A y secundaria = VFO B (manual), no como en el FT-710."
            + (vatios > 100 ? " El manual 1909-C dice PC 005-100; el MP da 200 W: confirmar el rango de PC." : string.Empty)),
        ModoPrincipalEsElActivo = false,
        TransmisionRelativaAlActivo = false,
        Bandas = PerfilYaesu.BandasHf(con60: true, PerfilYaesu.Tecla(17, "70", "4m", Textos.T("Servicios.Radio.Banda4m"))),
        Mandos = Mandos(
            quitar: [MandoDeEquipo.Rit, MandoDeEquipo.Xit, MandoDeEquipo.DesplazamientoRit, MandoDeEquipo.AntiVox],
            poner: [Sintonizador(), RitPorRt(), XitPorXt(), Potencia(vatios), Ancho(3, 21)]),
        Teclas = PerfilYaesu.TeclasSinDspReset,
    };

    /// <summary>FT-991 (ID0570).</summary>
    public static PerfilYaesu Ft991 { get; } = Ft991Comun("ft991", "FT-991", "0570");

    /// <summary>FT-991A (ID0670; ojo: no 0570, que es el FT-991 sin A).</summary>
    public static PerfilYaesu Ft991A { get; } = Ft991Comun("ft991a", "FT-991A", "0670");

    private static PerfilYaesu Ft991Comun(string clave, string nombre, string id) => new()
    {
        Modelo = Modelo(clave, nombre, id, "FrontalFt991", Hf(memorias: 117, maximo: 470_000_000), notas: NotaComun
            + " No tiene VS, ST ni MD1: el VFO de trabajo es siempre el A por CAT, el split se lee de FT y el modo del VFO B no se sabe."),
        TieneModoDelSegundoVfo = false,
        TransmisionRelativaAlActivo = false,
        Memorias = 117,
        TopeDelDial = 470_000_000,
        Bandas = PerfilYaesu.BandasHf(
            con60: true,
            PerfilYaesu.Tecla(15, "144", "2m", "2 m (144 MHz)"),
            PerfilYaesu.Tecla(16, "430", "70cm", "70 cm (430 MHz)")),
        Mandos = Mandos(
            quitar: [MandoDeEquipo.Rit, MandoDeEquipo.Xit, MandoDeEquipo.DesplazamientoRit, MandoDeEquipo.AntiVox,
                MandoDeEquipo.NivelAmc, MandoDeEquipo.SintoniaFinaRapida, MandoDeEquipo.Apf, MandoDeEquipo.FrecuenciaApf, MandoDeEquipo.Split],
            poner: [Sintonizador(), RitPorRt(), XitPorXt(), SplitPorFt(), Atenuador(conPasos: false), Ancho(2, 21),
                DesplazamientoFi(relleno: 0, maximo: 1200), RetardoEnMilisegundos(MandoDeEquipo.RetardoVox, "VD"),
                RetardoEnMilisegundos(MandoDeEquipo.RetardoBreakIn, "SD")]),
        Teclas = PerfilYaesu.TeclasSinDspReset,
    };

    /// <summary>FT-891 (ID0650).</summary>
    public static PerfilYaesu Ft891 { get; } = new()
    {
        Modelo = Modelo("ft891", "FT-891", "0650", "FrontalFt891", Hf(sintonizador: false), notas: NotaComun
            + " Sin acoplador interno: AC maneja el FC-50 externo si está. No tiene VS ni MD1. El clarificador va por CF como en el FT-710 (sin RT/XT)."),
        TieneModoDelSegundoVfo = false,
        Bandas = PerfilYaesu.BandasHf(con60: false),
        Mandos = Mandos(
            quitar: [MandoDeEquipo.AntiVox, MandoDeEquipo.NivelAmc, MandoDeEquipo.SintoniaFinaRapida, MandoDeEquipo.Apf,
                MandoDeEquipo.FrecuenciaApf],
            poner: [Sintonizador(), Atenuador(conPasos: false), Preamplificador(unoSolo: true), Ancho891(), DesplazamientoFi891(),
                RetardoEnMilisegundos(MandoDeEquipo.RetardoVox, "VD"), RetardoEnMilisegundos(MandoDeEquipo.RetardoBreakIn, "SD")]),
        Teclas = PerfilYaesu.TeclasSinDspReset,
        OrdenDeBorrarClarificador = "RC;",
    };

    /// <summary>FTDX3000 (ID0460, que el manual CAT no trae: sale de Hamlib).</summary>
    public static PerfilYaesu Ftdx3000 { get; } = Antiguo("ftdx3000", "FTDX3000", "0460", 100, PerfilYaesu.TeclasSinZin,
        " El manual CAT del FTDX3000 no documenta la orden ID: el 0460 es el que usa Hamlib. Confirmar.");

    /// <summary>FTDX1200 (ID0583 sin la FFT-1; 0582 con ella).</summary>
    public static PerfilYaesu Ftdx1200 { get; } = Antiguo("ftdx1200", "FTDX1200", "0583", 100, PerfilYaesu.TeclasSinDspReset,
        " Con la unidad FFT-1 contesta ID0582, que también se reconoce como FTDX1200.");

    /// <summary>FTDX5000 (ID0362).</summary>
    public static PerfilYaesu Ftdx5000 { get; } = Antiguo("ftdx5000", "FTDX5000", "0362", 200, PerfilYaesu.TeclasSinZin,
        " Doble receptor: MD0/FT = principal (VFO A), MD1 = secundario (VFO B). El manual da PC 000-255: se ofrece 5-200 W, confirmar.",
        dosReceptores: true);

    private static PerfilYaesu Antiguo(string clave, string nombre, string id, int vatios, IReadOnlySet<TeclaDelEquipo> teclas, string nota, bool dosReceptores = false) => new()
    {
        Modelo = Modelo(clave, nombre, id, null, Hf(dosReceptores: dosReceptores), notas: NotaComun
            + " Frecuencia en 8 cifras (FA14250000;). Sin ST: el split se lee y se pone con FT." + nota,
            velocidades: [38400, 19200, 9600, 4800]),
        CifrasDeFrecuencia = 8,
        ModoPrincipalEsElActivo = !dosReceptores,
        TieneModoDelSegundoVfo = dosReceptores,
        TransmisionRelativaAlActivo = false,
        RotulosDeMemoria = false,
        Bandas = PerfilYaesu.BandasHf(con60: clave == "ftdx5000"),
        Mandos = Mandos(
            quitar: [MandoDeEquipo.Rit, MandoDeEquipo.Xit, MandoDeEquipo.DesplazamientoRit, MandoDeEquipo.AntiVox,
                MandoDeEquipo.NivelAmc, MandoDeEquipo.SintoniaFinaRapida, MandoDeEquipo.Apf, MandoDeEquipo.FrecuenciaApf, MandoDeEquipo.Split],
            poner: [Sintonizador(), RitPorRt(), XitPorXt(), SplitPorFt(), Potencia(vatios), Ancho(2, 21),
                DesplazamientoFi(relleno: 0, maximo: 1000),
                RetardoEnMilisegundos(MandoDeEquipo.RetardoVox, "VD"), RetardoEnMilisegundos(MandoDeEquipo.RetardoBreakIn, "SD")]),
        Teclas = teclas,
    };

    /// <summary>Todos, el FT-710 el primero.</summary>
    public static IReadOnlyList<PerfilYaesu> Todos { get; } =
        [Ft710, Ftdx10, Ftdx101D, Ftdx101Mp, Ft991, Ft991A, Ft891, Ftdx3000, Ftdx1200, Ftdx5000];

    /// <summary>El perfil de un modelo.</summary>
    /// <param name="modelo">Modelo.</param>
    /// <returns>Su perfil, o nulo.</returns>
    public static PerfilYaesu? De(ModeloDeEquipo? modelo) =>
        modelo is null ? null : Todos.FirstOrDefault(p => p.Modelo.Clave == modelo.Clave);

    /// <summary>El perfil que contesta ese <c>ID;</c>.</summary>
    /// <param name="identificador">Cuatro cifras.</param>
    /// <returns>El perfil, o nulo si no se conoce.</returns>
    public static PerfilYaesu? PorIdentificador(string? identificador)
    {
        var id = identificador?.Trim();
        if (id == "0582") return Ftdx1200; // FTDX1200 con la FFT-1
        return Todos.FirstOrDefault(p => p.Modelo.IdentificadorYaesu == id);
    }

    // ── Construccion de las tablas de mandos ────────────────────────────

    private static IReadOnlyList<MandoFt710> Mandos(MandoDeEquipo[] quitar, MandoFt710[] poner)
    {
        var fuera = new HashSet<MandoDeEquipo>(quitar.Concat(SoloFt710).Concat(poner.Select(m => m.Mando)));
        return MandosFt710.Todos.Where(m => !fuera.Contains(m.Mando)).Concat(poner).ToList();
    }

    private static MandoFt710 Potencia(int vatios) =>
        new(MandoDeEquipo.Potencia, "PC", 3, new RangoDeMando(MandoDeEquipo.Potencia, 5, vatios, 1, "W"), Igual, IgualInverso);

    /// <summary>Acoplador: <c>AC00x</c> con 0 apagado, 1 encendido, 2 «Tuning Start».</summary>
    private static MandoFt710 Sintonizador() =>
        new(
            MandoDeEquipo.Sintonizador,
            "AC",
            3,
            new RangoDeMando(MandoDeEquipo.Sintonizador, 0, 2, 1, null, ["Apagado", "Encendido", "Sintonizar (emite portadora)"])
            {
                TransmiteAlAccionar = true,
            },
            v => v >= 2 ? 2 : v,
            v => Math.Clamp(IgualInverso(v), 0, 2));

    private static MandoFt710 RitPorRt() =>
        new(MandoDeEquipo.Rit, "RT", 1, new RangoDeMando(MandoDeEquipo.Rit, 0, 1, 1), Igual, IgualInverso);

    private static MandoFt710 XitPorXt() =>
        new(MandoDeEquipo.Xit, "XT", 1, new RangoDeMando(MandoDeEquipo.Xit, 0, 1, 1), Igual, IgualInverso);

    /// <summary>
    /// Split sin <c>ST</c>: se lee de <c>FT</c> (1 = transmite el VFO B) y se pone con
    /// <c>FT3</c> (transmitir por B) o <c>FT2</c> (por A). Supone que se opera en el A.
    /// </summary>
    private static MandoFt710 SplitPorFt() =>
        new(MandoDeEquipo.Split, "FT", 1, new RangoDeMando(MandoDeEquipo.Split, 0, 1, 1), Igual, IgualInverso)
        {
            LecturaPropia = texto => texto.Length > 2 ? (texto[2] == '1' ? 1 : 0) : null,
            EscrituraPropia = (valor, _) => valor >= 0.5 ? "FT3;" : "FT2;",
        };

    private static MandoFt710 Atenuador(bool conPasos) => conPasos
        ? MandosFt710.Buscar(MandoDeEquipo.Atenuador)!
        : new(MandoDeEquipo.Atenuador, "RA0", 1, new RangoDeMando(MandoDeEquipo.Atenuador, 0, 1, 1, null, ["Apagado", "Encendido"]), Igual, IgualInverso);

    private static MandoFt710 Preamplificador(bool unoSolo) =>
        new(MandoDeEquipo.Preamplificador, "PA0", 1,
            new RangoDeMando(MandoDeEquipo.Preamplificador, 0, unoSolo ? 1 : 2, 1, null,
                unoSolo ? ["Sin preamplificador (IPO)", "Amplificador"] : ["Sin preamplificador", "Amplificador 1", "Amplificador 2"]),
            Igual, IgualInverso);

    /// <summary>Ancho de filtro por indice: <c>SH0</c> + 3 cifras (<c>0xx</c>) o + 2 cifras.</summary>
    private static MandoFt710 Ancho(int cifras, int maximo) =>
        new(MandoDeEquipo.AnchoDeFiltro, "SH0", cifras, new RangoDeMando(MandoDeEquipo.AnchoDeFiltro, 0, maximo, 1, "índice"), Igual, IgualInverso);

    /// <summary>FT-891: <c>SH0</c> + P2 (0 apagado, 1 encendido) + dos cifras; se escribe siempre encendido.</summary>
    private static MandoFt710 Ancho891() =>
        new(MandoDeEquipo.AnchoDeFiltro, "SH0", 2, new RangoDeMando(MandoDeEquipo.AnchoDeFiltro, 0, 21, 1, "índice"), Igual, IgualInverso)
        {
            LecturaPropia = texto => texto.Length >= 6 && int.TryParse(texto.AsSpan(4, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? v : null,
            EscrituraPropia = (valor, _) => string.Create(CultureInfo.InvariantCulture, $"SH01{(int)valor:D2};"),
        };

    private static MandoFt710 DesplazamientoFi(int relleno, int maximo) =>
        new(MandoDeEquipo.DesplazamientoFi, "IS0", 4, new RangoDeMando(MandoDeEquipo.DesplazamientoFi, -maximo, maximo, 20, "Hz"), Igual, IgualInverso)
        {
            ConSigno = true,
            CifrasDeRelleno = relleno,
        };

    /// <summary>FT-891: <c>IS0</c> + P2 (0 apagado, 1 encendido) + signo + 4 cifras.</summary>
    private static MandoFt710 DesplazamientoFi891() =>
        new(MandoDeEquipo.DesplazamientoFi, "IS0", 4, new RangoDeMando(MandoDeEquipo.DesplazamientoFi, -1200, 1200, 20, "Hz"), Igual, IgualInverso)
        {
            LecturaPropia = texto => texto.Length >= 9 && int.TryParse(texto.AsSpan(4, 5), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var v) ? v : null,
            EscrituraPropia = (valor, _) =>
            {
                var v = (int)valor;
                return string.Create(CultureInfo.InvariantCulture, $"IS0{(v == 0 ? '0' : '1')}{(v < 0 ? '-' : '+')}{Math.Abs(v):D4};");
            },
        };

    /// <summary><c>VD</c>/<c>SD</c> en milisegundos con cuatro cifras (0030-3000).</summary>
    private static MandoFt710 RetardoEnMilisegundos(MandoDeEquipo mando, string orden) =>
        new(mando, orden, 4, new RangoDeMando(mando, 30, 3000, 10, "ms"), Igual, IgualInverso);
}
