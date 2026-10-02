using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Radio.Modelos;

using Nodisla.Cuaderno.Idiomas;

namespace Nodisla.Cuaderno.Radio.Control.Icom;

/// <summary>Como se reparten los dos VFO de la interfaz en un ICOM.</summary>
public enum RepartoDeVfos
{
    /// <summary>
    /// VFO A y VFO B de un solo receptor (IC-7300, IC-705, IC-7100). <c>25</c>/<c>26</c> con
    /// 00 = el VFO elegido y 01 = el otro; el nombre A/B del elegido lo recuerda el control
    /// porque estas radios no dicen por CI-V si estan en A o en B.
    /// </summary>
    VfoAyB,

    /// <summary>
    /// A = MAIN y B = SUB, dos receptores de verdad (IC-7610, IC-7851). <c>25</c>/<c>26</c> con
    /// 00 = MAIN y 01 = SUB, y <c>07 D2</c> dice cual esta elegido.
    /// </summary>
    PrincipalYSecundario,

    /// <summary>
    /// IC-9700: tiene MAIN y SUB, pero <c>25</c>/<c>26</c> solo valen para la banda MAIN y no hay
    /// orden <c>29</c>. A y B son el VFO A y B de la banda MAIN; la SUB no se ensena.
    /// </summary>
    VfoAyBDeLaPrincipal,
}

/// <summary>Una tecla de banda de un ICOM y como se llega a ella.</summary>
/// <param name="Tecla">La tecla para la botonera.</param>
/// <param name="CodigoDePila">Codigo de banda de la pila (<c>1A 01 bb 01</c>), o nulo si no se usa.</param>
/// <param name="HerciosPorOmision">Donde ir si la pila no contesta.</param>
/// <param name="ModoPorOmision">Modo (nombre Hamlib) si la pila no contesta.</param>
public sealed record BandaIcom(TeclaDeBanda Tecla, byte? CodigoDePila, long HerciosPorOmision, string ModoPorOmision);

/// <summary>
/// Lo que distingue a cada ICOM por CI-V, sacado de su manual.
/// </summary>
/// <param name="Modelo">El modelo del catalogo.</param>
/// <param name="PotenciaMaxima">Vatios del 100 % de <c>14 0A</c> y del medidor Po.</param>
/// <param name="Atenuadores">Decibelios de cada posicion de <c>11</c> (la 0 es apagado).</param>
/// <param name="Preamplificadores">Nombre de cada posicion de <c>16 02</c>.</param>
/// <param name="Reparto">Como se reparten A y B.</param>
/// <param name="Sintonizador">Tiene <c>1C 01</c> (acoplador interno o AH-4/AH-705 externo).</param>
/// <param name="Xit">Tiene <c>21 02</c> (desplazamiento de transmision).</param>
/// <param name="Apf">Tiene filtro de pico de audio <c>16 32</c>.</param>
/// <param name="LeeMemorias">Se leen las memorias con <c>1A 00 cc cc</c> (formato sin bancos).</param>
/// <param name="Bandas">Teclas de banda.</param>
/// <param name="EscalaTension">Puntos (crudo, voltios) de <c>15 15</c>.</param>
/// <param name="EscalaCorriente">Puntos (crudo, amperios) de <c>15 16</c>.</param>
/// <param name="PasosDeSintonia">Hercios de cada codigo de <c>10</c>.</param>
public sealed record PerfilIcom(
    ModeloDeEquipo Modelo,
    int PotenciaMaxima,
    IReadOnlyList<int> Atenuadores,
    IReadOnlyList<string> Preamplificadores,
    RepartoDeVfos Reparto,
    bool Sintonizador,
    bool Xit,
    bool Apf,
    bool LeeMemorias,
    IReadOnlyList<BandaIcom> Bandas,
    IReadOnlyList<(int Crudo, double Valor)> EscalaTension,
    IReadOnlyList<(int Crudo, double Valor)> EscalaCorriente,
    IReadOnlyList<int> PasosDeSintonia)
{
    /// <summary>Direccion CI-V de fabrica.</summary>
    public byte Direccion => Modelo.DireccionCiv ?? 0;
}

/// <summary>
/// Los ICOM que maneja el cuaderno por CI-V. <b>Todos programados segun el manual y sin probar
/// con la radio real.</b>
/// </summary>
/// <remarks>
/// Manuales usados (descargados de icomjapan.com el 29-09-2026): IC-7300 «Full manual»
/// (IC-7300_ENG_FM_12b, cap. 19), IC-705 «CI-V Reference Guide» (IC-705_ENG_CI-V_6), IC-7610 «CI-V
/// Reference Guide» (IC-7610_ENG_CI-V_4), IC-9700 «CI-V Reference Guide» (IC-9700_ENG_CI-V_4),
/// IC-7100 «Full manual» (IC-7100_ENG_FM_5, cap. 20), IC-7850/7851 «Instruction manual»
/// (IC-7850_7851_ENG_IM_3, cap. 18).
/// </remarks>
public static class ModelosIcom
{
    private static readonly int[] PasosHf = [10, 100, 1_000, 5_000, 9_000, 10_000, 12_500, 20_000, 25_000];
    private static readonly int[] PasosVhf = [10, 100, 1_000, 5_000, 6_250, 9_000, 10_000, 12_500, 20_000, 25_000, 50_000, 100_000];
    private static readonly int[] Pasos9700 = [10, 100, 500, 1_000, 5_000, 6_250, 10_000, 12_500, 20_000, 25_000, 50_000, 100_000];
    private static readonly (int, double)[] Tension13V = [(0, 0), (13, 10), (241, 16)];
    private static readonly string[] Preamp12 = ["Sin preamplificador", "Preamplificador 1", "Preamplificador 2"];

    private static TeclaDeBanda Tecla(int codigo, string rotulo, string banda, string descripcion) =>
        new(codigo, rotulo, Banda.TryParse(banda, out var b) ? b : Banda.Vacia, descripcion);

    private static BandaIcom B(int codigo, string rotulo, string banda, string descripcion, bool conPila, long hz, string modo) =>
        new(Tecla(codigo, rotulo, banda, descripcion), conPila ? (byte)codigo : null, hz, modo);

    /// <summary>Bandas de HF y 6 m con los codigos de pila 01…11 del IC-7300/IC-7610/IC-7851.</summary>
    private static IReadOnlyList<BandaIcom> BandasHf(bool conPila) =>
    [
        B(1, "1.8", "160m", "160 m (1,8 MHz)", conPila, 1_840_000, "LSB"),
        B(2, "3.5", "80m", "80 m (3,5 MHz)", conPila, 3_700_000, "LSB"),
        B(3, "7", "40m", "40 m (7 MHz)", conPila, 7_100_000, "LSB"),
        B(4, "10", "30m", "30 m (10 MHz)", conPila, 10_120_000, "CW"),
        B(5, "14", "20m", "20 m (14 MHz)", conPila, 14_200_000, "USB"),
        B(6, "18", "17m", "17 m (18 MHz)", conPila, 18_130_000, "USB"),
        B(7, "21", "15m", "15 m (21 MHz)", conPila, 21_200_000, "USB"),
        B(8, "24", "12m", "12 m (24,9 MHz)", conPila, 24_950_000, "USB"),
        B(9, "28", "10m", "10 m (28 MHz)", conPila, 28_500_000, "USB"),
        B(10, "50", "6m", "6 m (50 MHz)", conPila, 50_150_000, "USB"),
    ];

    private static CapacidadesDelModelo Capacidades(bool dosVfos, bool dosReceptores, bool sintonizador, int memorias, long min, long max) =>
        new(dosVfos, dosReceptores, Analizador: false, sintonizador, memorias, Mandos: true, Encendido: true, min, max);

    private static ModeloDeEquipo Modelo(string clave, string nombre, byte direccion, int[] velocidades, CapacidadesDelModelo capacidades, string notas) =>
        new(clave, Fabricante.Icom, nombre, ProtocoloCat.IcomCiv, null, direccion, velocidades, capacidades,
            "FrontalIc" + nombre[3..], ProbadoConRadio: false, Notas: notas);

    private const string NotaComun =
        "Programado según el manual CI-V, sin probar con radio real. Confirmar: eco del bus, transceive, "
        + "escala de medidores, pila de banda, que A/B se lee bien y el PTT (1C 00) con carga ficticia.";

    /// <summary>Todos los perfiles.</summary>
    public static IReadOnlyList<PerfilIcom> Todos { get; } =
    [
        new PerfilIcom(
            Modelo("icom-ic7300", "IC-7300", 0x94, [115200, 19200, 9600, 4800],
                Capacidades(true, false, true, 99, 30_000, 74_800_000),
                NotaComun + " USB: «CI-V USB Port» en «Unlink from [REMOTE]» para 115200; «CI-V USB Echo Back» OFF por omisión."),
            100, [0, 20], Preamp12, RepartoDeVfos.VfoAyB, Sintonizador: true, Xit: true, Apf: false, LeeMemorias: true,
            [.. BandasHf(true), B(11, "GEN", "", Textos.T("Servicios.Radio.CoberturaGeneral"), true, 9_700_000, "AM")],
            Tension13V, [(0, 0), (97, 10), (146, 15), (241, 25)], PasosHf),

        new PerfilIcom(
            Modelo("icom-ic705", "IC-705", 0xA4, [115200, 19200, 9600, 4800],
                Capacidades(true, false, true, 0, 30_000, 470_000_000),
                NotaComun + " El 1C 01 maneja el acoplador externo (AH-705). Memorias con bancos: no se leen. "
                + "Red (Wi-Fi) no implementada: solo USB. Registro 01 de la pila de banda = «Display on left side»: confirmar que es el último usado."),
            10, [0, 20], Preamp12, RepartoDeVfos.VfoAyB, Sintonizador: true, Xit: true, Apf: false, LeeMemorias: false,
            [.. BandasHf(true), B(13, "144", "2m", "2 m (144 MHz)", true, 144_300_000, "USB"),
             B(14, "430", "70cm", "70 cm (430 MHz)", true, 432_200_000, "USB"),
             B(15, "GEN", "", Textos.T("Servicios.Radio.CoberturaGeneral"), true, 9_700_000, "AM")],
            [(0, 0), (75, 5), (241, 16)], [(0, 0), (121, 2), (241, 4)], PasosVhf),

        new PerfilIcom(
            Modelo("icom-ic7610", "IC-7610", 0x98, [115200, 19200, 9600, 4800],
                Capacidades(true, true, true, 99, 30_000, 60_000_000),
                NotaComun + " A = MAIN, B = SUB. Red (LAN) no implementada: solo USB."),
            100, [0, 3, 6, 9, 12, 15, 18, 21, 24, 27, 30, 33, 36, 39, 42, 45], Preamp12, RepartoDeVfos.PrincipalYSecundario,
            Sintonizador: true, Xit: true, Apf: true, LeeMemorias: true,
            [.. BandasHf(true), B(11, "GEN", "", Textos.T("Servicios.Radio.CoberturaGeneral"), true, 9_700_000, "AM")],
            [(0, 0), (151, 10), (211, 16)], [(0, 0), (77, 10), (165, 20), (241, 30)], PasosHf),

        new PerfilIcom(
            Modelo("icom-ic9700", "IC-9700", 0xA2, [115200, 19200, 9600, 4800],
                Capacidades(true, true, false, 0, 144_000_000, 1_300_000_000),
                NotaComun + " A/B = VFO A/B de la banda MAIN (25/26 solo valen para MAIN). La banda SUB no se enseña. "
                + "Red (LAN) no implementada: solo USB. Memorias con bancos: no se leen."),
            100, [0, 10], ["Sin preamplificador", "Preamplificador", "Preamplificador externo", "Interno y externo"],
            RepartoDeVfos.VfoAyBDeLaPrincipal, Sintonizador: false, Xit: false, Apf: false, LeeMemorias: false,
            [B(1, "144", "2m", "2 m (144 MHz)", true, 144_300_000, "USB"),
             B(2, "430", "70cm", "70 cm (430 MHz)", true, 432_200_000, "USB"),
             B(3, "1200", "23cm", "23 cm (1,2 GHz)", true, 1_296_200_000, "USB")],
            Tension13V, [(0, 0), (121, 10), (241, 20)], Pasos9700),

        new PerfilIcom(
            Modelo("icom-ic7100", "IC-7100", 0x88, [19200, 9600, 4800, 1200],
                Capacidades(true, false, true, 0, 30_000, 470_000_000),
                NotaComun + " El 1C 01 maneja el acoplador externo (AH-4). Sin XIT por CI-V. La pila de banda no se usa "
                + "(no se ha verificado su tabla de códigos): las teclas de banda van a una frecuencia fija."),
            100, [0, 12], Preamp12, RepartoDeVfos.VfoAyB, Sintonizador: true, Xit: false, Apf: false, LeeMemorias: false,
            [.. BandasHf(false), B(13, "144", "2m", "2 m (144 MHz)", false, 144_300_000, "USB"),
             B(14, "430", "70cm", "70 cm (430 MHz)", false, 432_200_000, "USB")],
            Tension13V, [(0, 0), (97, 10), (146, 15), (241, 25)], PasosVhf),

        new PerfilIcom(
            Modelo("icom-ic7851", "IC-7851", 0x8E, [115200, 19200, 9600, 4800],
                Capacidades(true, true, true, 99, 30_000, 60_000_000),
                NotaComun + " A = MAIN, B = SUB. La tensión del medidor Vd es la de la etapa final (44–52 V)."),
            200, [0, 3, 6, 9, 12, 15, 18, 21], Preamp12, RepartoDeVfos.PrincipalYSecundario,
            Sintonizador: true, Xit: true, Apf: true, LeeMemorias: true,
            [.. BandasHf(true), B(11, "GEN", "", Textos.T("Servicios.Radio.CoberturaGeneral"), true, 9_700_000, "AM")],
            [(151, 44), (180, 48), (211, 52)], [(0, 0), (165, 10), (241, 15)], PasosHf),
    ];

    /// <summary>El perfil de un modelo del catalogo.</summary>
    /// <param name="modelo">Modelo.</param>
    /// <returns>Su perfil, o nulo si no es un ICOM de esta lista.</returns>
    public static PerfilIcom? De(ModeloDeEquipo modelo) =>
        Todos.FirstOrDefault(p => string.Equals(p.Modelo.Clave, modelo?.Clave, StringComparison.OrdinalIgnoreCase));
}
