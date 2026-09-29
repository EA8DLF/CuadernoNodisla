using System.Globalization;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Radio.Control.Ft710;
using Nodisla.Cuaderno.Radio.Modelos;

namespace Nodisla.Cuaderno.Radio.Control.Yaesu;

/// <summary>
/// Diferencias de un modelo Yaesu de CAT nuevo (ASCII con <c>;</c>) respecto del FT-710.
/// </summary>
/// <remarks>
/// <para>
/// Toda la familia comparte casi todas las ordenes, asi que hay un solo control
/// (<see cref="ControlFt710"/>) y esta tabla le dice lo que cambia: cifras de la frecuencia, a
/// que VFO se refieren <c>MD0</c> y <c>FT</c>, bandas de <c>BS</c>, mandos y sus rangos, teclas,
/// orden del acoplador, medidores y memorias.
/// </para>
/// <para>
/// <b>Salvo el FT-710, todo esto esta sacado del manual CAT de cada modelo y sin probar con la
/// radio.</b> Lo que hay que confirmar esta en <see cref="ModeloDeEquipo.Notas"/> y en
/// <c>docs/16-modelos.md</c>.
/// </para>
/// </remarks>
public sealed class PerfilYaesu
{
    /// <summary>El modelo del catalogo.</summary>
    public required ModeloDeEquipo Modelo { get; init; }

    /// <summary>Cifras de la frecuencia en <c>FA</c>/<c>FB</c>/<c>IF</c>/<c>MR</c>: 9, u 8 en FTDX3000/1200/5000.</summary>
    public int CifrasDeFrecuencia { get; init; } = 9;

    /// <summary>
    /// <c>MD0</c> es el modo del VFO con el que se opera (el elegido con <c>VS</c>) y
    /// <c>MD1</c> el del otro. Falso en los de doble receptor (FTDX101, FTDX5000), donde
    /// <c>MD0</c> es siempre la banda principal = VFO A y <c>MD1</c> la secundaria = VFO B.
    /// </summary>
    public bool ModoPrincipalEsElActivo { get; init; } = true;

    /// <summary>
    /// <c>FT0</c>/<c>FT1</c> dicen «transmite la banda principal / la otra», relativo al VFO
    /// activo (FT-710, FTDX10). Falso: <c>FT0</c> = VFO A y <c>FT1</c> = VFO B, sin mas.
    /// </summary>
    public bool TransmisionRelativaAlActivo { get; init; } = true;

    /// <summary>Tiene <c>MD1</c> (modo del segundo VFO). El FT-991, el FT-891 y los FTDX1200/3000 no.</summary>
    public bool TieneModoDelSegundoVfo { get; init; } = true;

    /// <summary>Teclas de banda con su codigo de <c>BS</c>.</summary>
    public required IReadOnlyList<TeclaDeBanda> Bandas { get; init; }

    /// <summary>Mandos que se ofrecen (y se preguntan al conectar: el que conteste <c>?;</c> se quita).</summary>
    public required IReadOnlyList<MandoFt710> Mandos { get; init; }

    /// <summary>Teclas del frontal que tiene este modelo por CAT.</summary>
    public required IReadOnlySet<TeclaDelEquipo> Teclas { get; init; }

    /// <summary>Orden para borrar el clarificador: <c>CF001+0000;</c> en el FT-710, <c>RC;</c> en los demas.</summary>
    public string OrdenDeBorrarClarificador { get; init; } = "RC;";

    /// <summary>El paso del dial y del mando de canales se lee del menu del FT-710 (<c>EX0305xx</c>).</summary>
    public bool PasosDelMenuFt710 { get; init; }

    /// <summary>Orden que lanza la sintonia del acoplador: <c>AC003;</c> en el FT-710, <c>AC002;</c> en los demas.</summary>
    public string OrdenDeSintonia { get; init; } = "AC002;";

    /// <summary>
    /// Posicion, en la respuesta a <c>RI0;</c>, del caracter que vale 1 mientras el acoplador
    /// sintoniza (7 en el FT-710). Nulo si el modelo no lo dice por CAT: entonces no se puede ver
    /// el fin de la sintonia y se suelta al tope de tiempo.
    /// </summary>
    public int? PosicionDeSintoniaEnRi { get; init; }

    /// <summary>Medidores de <c>RM</c> (orden, nombre).</summary>
    public IReadOnlyList<(string Orden, string Nombre)> Medidores { get; init; } = MedidoresComunes;

    /// <summary>Ultimo canal de memoria normal.</summary>
    public int Memorias { get; init; } = 99;

    /// <summary>Tiene rotulos de memoria por <c>MT</c>.</summary>
    public bool RotulosDeMemoria { get; init; } = true;

    /// <summary>Se puede saber si esta encendido mirando el codec de audio USB del FT-710.</summary>
    public bool CodecDelFt710 { get; init; }

    /// <summary>Analizador de espectro por el puente FT4222 (solo el FT-710).</summary>
    public bool AnalizadorFt4222 { get; init; }

    /// <summary>Nombre corto para los mensajes (<c>FT-710</c>).</summary>
    public string Nombre => Modelo.Nombre;

    /// <summary>La frecuencia en cifras para <c>FA</c>/<c>FB</c>.</summary>
    /// <param name="hercios">Hercios.</param>
    /// <returns>Las cifras, con ceros delante.</returns>
    public string Cifras(long hercios) => hercios.ToString(
        CifrasDeFrecuencia == 8 ? "D8" : "D9",
        CultureInfo.InvariantCulture);

    /// <summary>
    /// La frecuencia mas alta que cabe en las cifras. No se acota a la del equipo: fuera de
    /// banda el propio equipo contesta o se niega, como con el FT-710 de siempre.
    /// </summary>
    public long HerciosMaximo => CifrasDeFrecuencia == 8 ? 99_999_999 : 999_999_999;

    /// <summary>Tope del dial al girarlo desde el programa (75 MHz en el FT-710, como siempre).</summary>
    public long TopeDelDial { get; init; } = 75_000_000;

    // ── Tablas comunes ──────────────────────────────────────────────────

    /// <summary>Medidores de la familia: <c>RM1</c> S, <c>RM3</c> compresion, <c>RM4</c> ALC, <c>RM5</c> potencia, <c>RM6</c> ROE, <c>RM7</c> corriente.</summary>
    public static IReadOnlyList<(string Orden, string Nombre)> MedidoresComunes { get; } =
    [
        ("RM1", "señal"),
        ("RM3", "compresión"),
        ("RM4", "control automático de nivel"),
        ("RM5", "potencia"),
        ("RM6", "relación de onda estacionaria"),
        ("RM7", "corriente"),
    ];

    /// <summary>Todas las teclas.</summary>
    public static IReadOnlySet<TeclaDelEquipo> TodasLasTeclas { get; } = new HashSet<TeclaDelEquipo>(Enum.GetValues<TeclaDelEquipo>());

    /// <summary>
    /// Teclas de los demas modelos: todas menos DSP RESET, que en el FT-710 se hace a mano con
    /// ordenes de formato propio (<c>IS00+0000</c>, <c>CO0x</c>...) que no son iguales en toda
    /// la familia.
    /// </summary>
    public static IReadOnlySet<TeclaDelEquipo> TeclasSinDspReset { get; } =
        new HashSet<TeclaDelEquipo>(Enum.GetValues<TeclaDelEquipo>().Where(t => t != TeclaDelEquipo.RestablecerDsp));

    /// <summary>Como <see cref="TeclasSinDspReset"/> y sin ZIN (FTDX3000 y FTDX5000 no tienen <c>ZI</c>).</summary>
    public static IReadOnlySet<TeclaDelEquipo> TeclasSinZin { get; } =
        new HashSet<TeclaDelEquipo>(TeclasSinDspReset.Where(t => t != TeclaDelEquipo.AjusteACero));

    /// <summary>Bandas HF + 6 m con los codigos de <c>BS</c> 00-11 de toda la familia.</summary>
    /// <param name="con60">El modelo tiene la tecla de 60 m (5 MHz): BS02.</param>
    /// <param name="extra">Bandas de mas (70 MHz, 144, 430...).</param>
    /// <returns>Las teclas.</returns>
    public static IReadOnlyList<TeclaDeBanda> BandasHf(bool con60, params TeclaDeBanda[] extra)
    {
        var bandas = new List<TeclaDeBanda>
        {
            Tecla(0, "1.8", "160m", "160 m (1,8 MHz)"),
            Tecla(1, "3.5", "80m", "80 m (3,5 MHz)"),
        };
        if (con60) bandas.Add(Tecla(2, "5", "60m", "60 m (5 MHz)"));
        bandas.AddRange(
        [
            Tecla(3, "7", "40m", "40 m (7 MHz)"),
            Tecla(4, "10", "30m", "30 m (10 MHz)"),
            Tecla(5, "14", "20m", "20 m (14 MHz)"),
            Tecla(6, "18", "17m", "17 m (18 MHz)"),
            Tecla(7, "21", "15m", "15 m (21 MHz)"),
            Tecla(8, "24", "12m", "12 m (24,9 MHz)"),
            Tecla(9, "28", "10m", "10 m (28 MHz)"),
            Tecla(10, "50", "6m", "6 m (50 MHz)"),
        ]);
        bandas.AddRange(extra);
        bandas.Add(new TeclaDeBanda(11, "GEN", Banda.Vacia, "Cobertura general (recepción fuera de las bandas)"));
        return bandas;
    }

    /// <summary>Una tecla de banda.</summary>
    /// <param name="codigo">Codigo de <c>BS</c>.</param>
    /// <param name="rotulo">Rotulo.</param>
    /// <param name="banda">Banda (<c>40m</c>).</param>
    /// <param name="descripcion">Para el operador.</param>
    /// <returns>La tecla.</returns>
    public static TeclaDeBanda Tecla(int codigo, string rotulo, string banda, string descripcion) =>
        new(codigo, rotulo, Banda.TryParse(banda, out var b) ? b : Banda.Vacia, descripcion);
}
