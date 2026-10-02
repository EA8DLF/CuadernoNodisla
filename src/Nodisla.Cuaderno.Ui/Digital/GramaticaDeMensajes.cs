using System.Globalization;
using Nodisla.Cuaderno.Idiomas;
using Nodisla.Cuaderno.Modos.Ft8;

namespace Nodisla.Cuaderno.Ui.Digital;

/// <summary>Como se opera: normal, hound, o uno de los modos de concurso de WSJT-X.</summary>
public enum TipoDeOperacion
{
    /// <summary>Contacto normal: localizador, informe, R+informe, RR73, 73.</summary>
    Normal,

    /// <summary>Hound de fox/hound: se llama a un DX por encima de 1000 Hz y se le sigue al responder.</summary>
    Hound,

    /// <summary>Fox de fox/hound: ser el DX. Solo la gramatica; el secuenciador de fox queda por hacer.</summary>
    Fox,

    /// <summary>Concurso NA VHF: el intercambio es el localizador; <c>CQ TEST</c>.</summary>
    NaVhf,

    /// <summary>Concurso EU VHF: informe, numero de serie y localizador de seis; <c>CQ TEST</c>.</summary>
    EuVhf,

    /// <summary>ARRL Field Day: clase y seccion; <c>CQ FD</c>.</summary>
    FieldDay,

    /// <summary>ARRL RTTY Roundup: RST y estado o numero; <c>CQ RU</c>.</summary>
    RttyRoundup,

    /// <summary>WW Digi: el intercambio es el localizador; <c>CQ WW</c>.</summary>
    WwDigi,
}

/// <summary>Lo que hace falta para componer los seis mensajes.</summary>
/// <param name="MiIndicativo">El propio.</param>
/// <param name="MiLocalizador">El propio, de cuatro (o seis en EU VHF).</param>
/// <param name="DxCall">El corresponsal.</param>
/// <param name="DxGrid">Su localizador, para Tx1 solo informativo.</param>
/// <param name="Informe">Informe que se le manda, en decibelios.</param>
/// <param name="CqDirigido">Sufijo del CQ: vacio, <c>DX</c>, <c>EU</c>, <c>POTA</c>…</param>
/// <param name="Intercambio">Intercambio de concurso, tal como se teclea: <c>2A EMA</c>, <c>579 MA</c>, <c>570123 IO91NP</c>.</param>
/// <param name="Tx4ConRrr">Tx4 va con <c>RRR</c> en vez de <c>RR73</c>.</param>
/// <param name="Operacion">Como se opera.</param>
public readonly record struct DatosDeLosMensajes(
    string MiIndicativo,
    string MiLocalizador,
    string DxCall,
    string DxGrid,
    int Informe,
    string CqDirigido,
    string Intercambio,
    bool Tx4ConRrr,
    TipoDeOperacion Operacion);

/// <summary>
/// Compone los mensajes Tx1 a Tx6 con la gramatica de WSJT-X, para cada tipo de operacion.
/// </summary>
/// <remarks>
/// Los huecos se dejan visibles —<c>&lt;DX&gt;</c>, <c>&lt;YO&gt;</c>— en vez de en blanco:
/// un mensaje a medias tiene que verse a medias, no parecer completo.
/// </remarks>
public static class GramaticaDeMensajes
{
    /// <summary>Numero de mensajes: Tx1 a Tx6.</summary>
    public const int Cuantos = 6;

    /// <summary>Compone los seis mensajes. El indice 0 es Tx1.</summary>
    public static string[] Generar(DatosDeLosMensajes datos)
    {
        var yo = Hueco(datos.MiIndicativo, "<YO>");
        var dx = Hueco(datos.DxCall, "<DX>");
        var grid4 = datos.MiLocalizador.Length >= 4 ? datos.MiLocalizador[..4].ToUpperInvariant() : "<LOC>";
        var informe = datos.Informe.ToString("+00;-00;+00", CultureInfo.InvariantCulture);
        var rr = datos.Tx4ConRrr ? "RRR" : "RR73";
        var intercambio = datos.Intercambio.Trim().ToUpperInvariant();

        var (tx2, tx3) = datos.Operacion switch
        {
            TipoDeOperacion.NaVhf or TipoDeOperacion.WwDigi =>
                ($"{dx} {yo} {grid4}", $"{dx} {yo} R {grid4}"),
            TipoDeOperacion.EuVhf or TipoDeOperacion.FieldDay or TipoDeOperacion.RttyRoundup =>
                ($"{dx} {yo} {Hueco(intercambio, "<INTERCAMBIO>")}", $"{dx} {yo} R {Hueco(intercambio, "<INTERCAMBIO>")}"),
            _ => ($"{dx} {yo} {informe}", $"{dx} {yo} R{informe}"),
        };

        if (datos.Operacion is TipoDeOperacion.Normal or TipoDeOperacion.Hound
            && TryGenerarConIndicativoRaro(datos, yo, dx, grid4, informe, rr, out var conRaro))
        {
            return conRaro;
        }

        var cq = datos.Operacion switch
        {
            TipoDeOperacion.NaVhf or TipoDeOperacion.EuVhf => "CQ TEST",
            TipoDeOperacion.FieldDay => "CQ FD",
            TipoDeOperacion.RttyRoundup => "CQ RU",
            TipoDeOperacion.WwDigi => "CQ WW",
            _ => datos.CqDirigido.Trim().Length > 0 ? $"CQ {datos.CqDirigido.Trim().ToUpperInvariant()}" : "CQ",
        };

        return
        [
            $"{dx} {yo} {grid4}",
            tx2,
            tx3,
            $"{dx} {yo} {rr}",
            $"{dx} {yo} 73",
            $"{cq} {yo} {grid4}",
        ];
    }

    /// <summary>
    /// La secuencia cuando uno de los dos indicativos no cabe en el formato corriente
    /// (<c>HB10GBT</c>, <c>PJ4/K1ABC</c>…), como la compone WSJT-X.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Un indicativo raro solo cabe entero en el mensaje de tipo 4, que no lleva localizador ni
    /// informe; en los demas viaja resumido entre angulos. La regla es que cada indicativo raro
    /// viaje entero al menos una vez y que el resto de la secuencia lo lleve resumido:
    /// </para>
    /// <para>
    /// <b>El DX es raro</b> (el caso de HB10GBT): <c>&lt;DX&gt; YO LOC</c>, <c>&lt;DX&gt; YO -14</c>,
    /// <c>&lt;DX&gt; YO R-14</c>, <c>&lt;DX&gt; YO RR73</c>, <c>DX &lt;YO&gt; 73</c> y el CQ de siempre.
    /// El DX ya mando su indicativo entero en su CQ, y el propio va siempre en claro.
    /// </para>
    /// <para>
    /// <b>El propio es raro</b>: <c>&lt;DX&gt; YO</c>, <c>DX &lt;YO&gt; -14</c>, <c>DX &lt;YO&gt; R-14</c>,
    /// <c>&lt;DX&gt; YO RR73</c>, <c>DX &lt;YO&gt; 73</c> y <c>CQ YO</c> sin localizador.
    /// </para>
    /// </remarks>
    private static bool TryGenerarConIndicativoRaro(
        DatosDeLosMensajes datos, string yo, string dx, string grid4, string informe, string rr, out string[] mensajes)
    {
        mensajes = [];
        var hayYo = !string.IsNullOrWhiteSpace(datos.MiIndicativo);
        var hayDx = !string.IsNullOrWhiteSpace(datos.DxCall);
        var yoRaro = hayYo && !MensajeDe77Bits.EsIndicativoEstandar(yo) && MensajeDe77Bits.EsIndicativoEmitible(yo);
        var dxRaro = hayDx && !MensajeDe77Bits.EsIndicativoEstandar(dx) && MensajeDe77Bits.EsIndicativoEmitible(dx);
        // Con huecos sin rellenar se deja la gramatica de siempre: los huecos se ven como huecos.
        if (!hayYo || !hayDx || (!yoRaro && !dxRaro)) return false;

        var cqDirigido = datos.CqDirigido.Trim().ToUpperInvariant();
        var cqCorriente = cqDirigido.Length > 0 ? $"CQ {cqDirigido} {yo} {grid4}" : $"CQ {yo} {grid4}";

        if (!yoRaro)
        {
            mensajes =
            [
                $"<{dx}> {yo} {grid4}",
                $"<{dx}> {yo} {informe}",
                $"<{dx}> {yo} R{informe}",
                $"<{dx}> {yo} {rr}",
                $"{dx} <{yo}> 73",
                cqCorriente,
            ];
            return true;
        }

        // El propio es raro. Si el DX tambien lo es, en los mensajes con informe van los dos
        // resumidos; el DX nunca puede ir en claro junto al propio en claro.
        var dxEnInforme = dxRaro ? $"<{dx}>" : dx;
        mensajes =
        [
            $"<{dx}> {yo}",
            $"{dxEnInforme} <{yo}> {informe}",
            $"{dxEnInforme} <{yo}> R{informe}",
            $"<{dx}> {yo} {rr}",
            $"{dxEnInforme} <{yo}> 73",
            $"CQ {yo}",
        ];
        return true;
    }

    /// <summary>El intercambio de concurso lo pone el operador y no el programa.</summary>
    public static bool NecesitaIntercambio(TipoDeOperacion operacion) =>
        operacion is TipoDeOperacion.EuVhf or TipoDeOperacion.FieldDay or TipoDeOperacion.RttyRoundup;

    /// <summary>El intercambio es el localizador: no hay informe en decibelios.</summary>
    public static bool IntercambioEsLocalizador(TipoDeOperacion operacion) =>
        operacion is TipoDeOperacion.NaVhf or TipoDeOperacion.WwDigi;

    /// <summary>Nombre para el selector.</summary>
    public static string Nombre(TipoDeOperacion operacion) => operacion switch
    {
        TipoDeOperacion.Normal => Textos.T("Digital.Operacion.Normal"),
        TipoDeOperacion.Hound => Textos.T("Digital.Operacion.Hound"),
        TipoDeOperacion.Fox => Textos.T("Digital.Operacion.Fox"),
        TipoDeOperacion.NaVhf => Textos.T("Digital.Operacion.NaVhf"),
        TipoDeOperacion.EuVhf => Textos.T("Digital.Operacion.EuVhf"),
        TipoDeOperacion.FieldDay => "ARRL Field Day",
        TipoDeOperacion.RttyRoundup => "ARRL RTTY Roundup",
        TipoDeOperacion.WwDigi => "WW Digi",
        _ => operacion.ToString(),
    };

    private static string Hueco(string valor, string siFalta) =>
        string.IsNullOrWhiteSpace(valor) ? siFalta : valor.Trim().ToUpperInvariant();
}
