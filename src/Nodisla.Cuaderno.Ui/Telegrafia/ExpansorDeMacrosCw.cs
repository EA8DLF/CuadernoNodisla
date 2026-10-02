using System.Globalization;
using System.Text.RegularExpressions;
using Nodisla.Cuaderno.Radio.Cw;

namespace Nodisla.Cuaderno.Ui.Telegrafia;

/// <summary>Con que se rellenan las variables de una macro.</summary>
/// <param name="MiIndicativo">El indicativo propio (<c>{MICALL}</c>).</param>
/// <param name="Indicativo">El del contacto nuevo (<c>{CALL}</c>).</param>
/// <param name="Rst">El RST que se da (<c>{RST}</c>); vacio, 599 (5NN en concurso).</param>
/// <param name="Numero">Numero de serie (<c>{NR}</c>).</param>
/// <param name="Nombre">Nombre del corresponsal (<c>{NOMBRE}</c>).</param>
/// <param name="Qth">QTH del corresponsal (<c>{QTH}</c>).</param>
/// <param name="Localizador">Localizador del corresponsal (<c>{LOC}</c>).</param>
/// <param name="MiNombre">Nombre propio (<c>{MINOMBRE}</c>).</param>
/// <param name="MiQth">QTH propio (<c>{MIQTH}</c>).</param>
/// <param name="MiLocalizador">Localizador propio (<c>{MILOC}</c>).</param>
/// <param name="Concurso">Es el juego de concurso: RST con N y T, y <c>{CQ}</c> = CQ TEST.</param>
public sealed record ContextoDeMacroCw(
    string MiIndicativo,
    string Indicativo,
    string Rst,
    int Numero,
    string Nombre,
    string Qth,
    string Localizador,
    string MiNombre,
    string MiQth,
    string MiLocalizador,
    bool Concurso);

/// <summary>Lo que sale de una macro.</summary>
/// <param name="Trozos">El texto, en trozos con su velocidad.</param>
/// <param name="FaltaIndicativo">Usa <c>{CALL}</c> y el contacto nuevo no tiene indicativo.</param>
/// <param name="FaltaMiIndicativo">Usa <c>{MICALL}</c> y no hay perfil de estacion con indicativo.</param>
/// <param name="Desconocidas">Variables que no existen (se quitan).</param>
public sealed record ResultadoDeMacroCw(
    IReadOnlyList<TrozoCw> Trozos,
    bool FaltaIndicativo,
    bool FaltaMiIndicativo,
    IReadOnlyList<string> Desconocidas)
{
    /// <summary>Todo el texto, para enseñarlo.</summary>
    public string Texto => string.Join(' ', Trozos.Select(t => t.Texto));

    /// <summary>No hay nada que mandar.</summary>
    public bool Vacio => Trozos.Count == 0;
}

/// <summary>
/// Rellena las variables de una macro y aplica los cambios de velocidad.
/// </summary>
/// <remarks>
/// <para>Variables: <c>{MICALL} {CALL} {RST} {NR} {NOMBRE} {QTH} {LOC} {MINOMBRE} {MIQTH} {MILOC}</c>
/// y los atajos <c>{CQ} {TU} {73} {AGN} {?}</c>.</para>
/// <para>Velocidad dentro de la macro: <c>&lt;+5&gt;</c> y <c>&lt;-5&gt;</c> suben o bajan desde
/// ahi hasta el final; <c>&lt;WPM 25&gt;</c> la pone fija. Los prosignos se escriben igual,
/// entre angulos: <c>&lt;SK&gt; &lt;AR&gt; &lt;BT&gt; &lt;KN&gt;</c>.</para>
/// </remarks>
public static partial class ExpansorDeMacrosCw
{
    /// <summary>Las variables que se conocen, para la ayuda de la pantalla.</summary>
    public static IReadOnlyList<string> Variables { get; } =
    [
        "{MICALL}", "{CALL}", "{RST}", "{NR}", "{NOMBRE}", "{QTH}", "{LOC}", "{MINOMBRE}", "{MIQTH}", "{MILOC}",
        "{CQ}", "{TU}", "{73}", "{AGN}", "{?}",
    ];

    /// <summary>Rellena una macro.</summary>
    /// <param name="plantilla">El texto de la macro.</param>
    /// <param name="contexto">Con que se rellena.</param>
    /// <param name="wpm">Velocidad de partida.</param>
    /// <param name="minima">Velocidad mas baja que admite el equipo.</param>
    /// <param name="maxima">Velocidad mas alta que admite el equipo.</param>
    /// <returns>Los trozos y lo que falto.</returns>
    public static ResultadoDeMacroCw Expandir(string plantilla, ContextoDeMacroCw contexto, int wpm, int minima = 5, int maxima = 60)
    {
        ArgumentNullException.ThrowIfNull(plantilla);
        ArgumentNullException.ThrowIfNull(contexto);

        var faltaIndicativo = false;
        var faltaMio = false;
        var desconocidas = new List<string>();

        var texto = Variable().Replace(plantilla, m =>
        {
            var nombre = m.Groups["nombre"].Value.ToUpperInvariant();
            switch (nombre)
            {
                case "MICALL":
                    if (string.IsNullOrWhiteSpace(contexto.MiIndicativo)) faltaMio = true;
                    return contexto.MiIndicativo;
                case "CALL":
                    if (string.IsNullOrWhiteSpace(contexto.Indicativo)) faltaIndicativo = true;
                    return contexto.Indicativo;
                case "RST": return Rst(contexto);
                case "NR": return contexto.Numero.ToString("000", CultureInfo.InvariantCulture);
                case "NOMBRE": return contexto.Nombre;
                case "QTH": return contexto.Qth;
                case "LOC": return contexto.Localizador;
                case "MINOMBRE": return contexto.MiNombre;
                case "MIQTH": return contexto.MiQth;
                case "MILOC": return contexto.MiLocalizador;
                case "CQ": return contexto.Concurso ? "CQ TEST" : "CQ";
                case "TU": return "TU";
                case "73": return "73";
                case "AGN": return "AGN";
                case "?": return "?";
                default:
                    desconocidas.Add(m.Value);
                    return string.Empty;
            }
        });

        var trozos = new List<TrozoCw>();
        var actual = Math.Clamp(wpm, minima, maxima);
        var resto = texto;
        while (resto.Length > 0)
        {
            var m = Velocidad().Match(resto);
            var antes = m.Success ? resto[..m.Index] : resto;
            Anadir(trozos, antes, actual);
            if (!m.Success) break;

            if (m.Groups["fija"].Success)
            {
                actual = int.Parse(m.Groups["fija"].Value, CultureInfo.InvariantCulture);
            }
            else
            {
                var cambio = int.Parse(m.Groups["cuanto"].Value, CultureInfo.InvariantCulture);
                actual += m.Groups["signo"].Value == "-" ? -cambio : cambio;
            }

            actual = Math.Clamp(actual, minima, maxima);
            resto = resto[(m.Index + m.Length)..];
        }

        return new ResultadoDeMacroCw(trozos, faltaIndicativo, faltaMio, desconocidas);
    }

    /// <summary>El RST que se da: el del contacto nuevo o 599; en concurso, con N por 9 y T por 0.</summary>
    /// <param name="contexto">El contexto.</param>
    /// <returns>El RST.</returns>
    public static string Rst(ContextoDeMacroCw contexto)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        var rst = string.IsNullOrWhiteSpace(contexto.Rst) ? "599" : contexto.Rst.Trim().ToUpperInvariant();
        return contexto.Concurso ? rst.Replace('9', 'N').Replace('0', 'T') : rst;
    }

    private static void Anadir(List<TrozoCw> trozos, string texto, int wpm)
    {
        var limpio = string.Join(' ', texto.ToUpperInvariant().Split((char[])[' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
        if (limpio.Length == 0) return;
        if (trozos.Count > 0 && trozos[^1].Wpm == wpm)
        {
            trozos[^1] = trozos[^1] with { Texto = trozos[^1].Texto + " " + limpio };
            return;
        }

        trozos.Add(new TrozoCw(limpio, wpm));
    }

    [GeneratedRegex(@"\{(?<nombre>[A-Za-z0-9?]+)\}", RegexOptions.CultureInvariant)]
    private static partial Regex Variable();

    [GeneratedRegex(@"<\s*(?:WPM\s*(?<fija>\d{1,2})|(?<signo>[+-])\s*(?<cuanto>\d{1,2}))\s*>", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex Velocidad();
}
