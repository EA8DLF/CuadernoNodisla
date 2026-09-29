using System.Globalization;
using System.Text;

namespace Nodisla.Cuaderno.Concursos.Macros;

/// <summary>Lo que ha salido de expandir una macro.</summary>
/// <param name="Texto">El texto ya sustituido.</param>
/// <param name="Desconocidas">
/// Sustituciones que el motor no reconocio. Se dejan tal cual en el texto: es preferible que
/// el operador vea <c>&lt;LOQUESEA&gt;</c> en pantalla a que la macro salga muda por una
/// errata.
/// </param>
/// <param name="SinDatos">
/// Sustituciones reconocidas que no tenian valor, por ejemplo el nombre del corresponsal
/// cuando aun no se sabe. Se sustituyen por nada, pero conviene poder avisar.
/// </param>
public sealed record ExpansionDeMacro(
    string Texto,
    IReadOnlyList<string> Desconocidas,
    IReadOnlyList<string> SinDatos)
{
    /// <summary>La macro se expandio entera y sin huecos.</summary>
    public bool Limpia => Desconocidas.Count == 0 && SinDatos.Count == 0;
}

/// <summary>
/// Sustituye las etiquetas de una macro por lo que vale en este momento.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por que esta sintaxis y no la de Log4OM.</b> El original escribe sus macros asi:
/// <c>! DE * YR RST &lt;STTXF&gt; NAME IS &lt;NAME&gt;</c>. Se conserva de ahi lo bueno —las
/// etiquetas entre angulos con nombre de campo ADIF, que el operador ya conoce de exportar
/// ficheros— y se descartan dos cosas:
/// </para>
/// <list type="bullet">
/// <item>
/// <b>El <c>*</c> y el <c>!</c> como «mi indicativo» y «su indicativo».</b> Son dos caracteres
/// corrientes que aparecen en texto normal, y eso significa que nunca se puede escribir un
/// asterisco en una macro. Aqui son <c>&lt;MICALL&gt;</c> y <c>&lt;CALL&gt;</c>, que no se
/// pueden confundir con nada.
/// </item>
/// <item>
/// <b>El <c>###</c> separando el texto de la etiqueta del boton.</b> Eso no es sintaxis de
/// macro, es como Log4OM guarda doce macros en un fichero de texto plano. Aqui una macro es un
/// objeto con su nombre, su tecla y su texto.
/// </item>
/// </list>
/// <para>
/// <b>Lo que no lleva la sintaxis, a proposito.</b> No hay nada que hable de telegrafia, ni
/// velocidad, ni numeros cortados, ni pausas. Una macro es texto; el manipulador de telegrafia,
/// el reproductor de voz y el modem digital deciden cada uno como decirlo. En cuanto una macro
/// lleve dentro algo que solo entienda el manipulador, deja de servir para los otros dos.
/// </para>
/// <para>
/// Para escribir un angulo literal se ponen dos, y vale para los dos angulos:
/// <c>&lt;&lt;</c> escribe <c>&lt;</c> y <c>&gt;&gt;</c> escribe <c>&gt;</c>. Asi
/// <c>&lt;&lt;AR&gt;&gt;</c> sale como <c>&lt;AR&gt;</c>, que es lo que el operador espera al
/// escapar una etiqueta entera. Un angulo suelto es texto y se deja como esta.
/// </para>
/// </remarks>
public sealed class MotorDeMacros
{
    /// <summary>Sustituciones que entiende el motor, con lo que significan.</summary>
    /// <remarks>
    /// Sirve para la ayuda y para el editor de macros: el operador tiene que poder ver la
    /// lista sin buscarla en la documentacion.
    /// </remarks>
    public static IReadOnlyDictionary<string, string> Sustituciones { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["CALL"] = "Indicativo del corresponsal",
            ["MICALL"] = "Mi indicativo",
            ["NOMBRE"] = "Nombre del corresponsal",
            ["MINOMBRE"] = "Mi nombre",
            ["QTH"] = "Localidad del corresponsal",
            ["MIQTH"] = "Mi localidad",
            ["LOCATOR"] = "Localizador del corresponsal",
            ["MILOCATOR"] = "Mi localizador",
            ["RST"] = "Informe que envío",
            ["RSTR"] = "Informe que he recibido",
            ["SERIE"] = "Número de serie que toca enviar",
            ["SERIER"] = "Número de serie recibido",
            ["INTERCAMBIO"] = "Intercambio completo que envío",
            ["INTERCAMBIOR"] = "Intercambio recibido",
            ["BANDA"] = "Banda en la que estoy",
            ["MODO"] = "Modo en el que estoy",
            ["FREQ"] = "Frecuencia en MHz",
            ["CONCURSO"] = "Concurso en marcha",
            ["HORA"] = "Hora UTC, en HHMM",
            ["FECHA"] = "Fecha UTC, en aaaa-mm-dd",
        };

    /// <summary>Expande una macro.</summary>
    /// <param name="plantilla">Texto de la macro, con sus etiquetas.</param>
    /// <param name="contexto">Lo que se sabe en este momento.</param>
    /// <returns>El texto expandido y lo que no se pudo sustituir.</returns>
    public ExpansionDeMacro Expandir(string? plantilla, ContextoDeMacro contexto)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        if (string.IsNullOrEmpty(plantilla)) return new ExpansionDeMacro(string.Empty, [], []);

        var salida = new StringBuilder(plantilla.Length + 32);
        List<string>? desconocidas = null;
        List<string>? sinDatos = null;

        for (var i = 0; i < plantilla.Length; i++)
        {
            var c = plantilla[i];
            if (c != '<')
            {
                // El angulo de cierre tambien se escapa duplicandolo: si no, escapar una
                // etiqueta entera («<<AR>>») dejaria el cierre doblado en pantalla.
                if (c == '>' && i + 1 < plantilla.Length && plantilla[i + 1] == '>') i++;
                salida.Append(c);
                continue;
            }
            if (i + 1 < plantilla.Length && plantilla[i + 1] == '<')
            {
                salida.Append('<');
                i++;
                continue;
            }

            var cierre = plantilla.IndexOf('>', i + 1);
            if (cierre < 0)
            {
                // Un angulo sin cerrar es texto, no una etiqueta rota.
                salida.Append(c);
                continue;
            }

            var etiqueta = plantilla[(i + 1)..cierre];
            i = cierre;

            var (nombre, ancho) = Partir(etiqueta);
            if (!Resolver(nombre, contexto, out var valor))
            {
                (desconocidas ??= []).Add(nombre);
                salida.Append('<').Append(etiqueta).Append('>');
                continue;
            }
            if (string.IsNullOrEmpty(valor)) (sinDatos ??= []).Add(nombre);
            salida.Append(Rellenar(valor, ancho));
        }

        return new ExpansionDeMacro(
            salida.ToString(),
            (IReadOnlyList<string>?)desconocidas ?? [],
            (IReadOnlyList<string>?)sinDatos ?? []);
    }

    /// <summary>
    /// Separa <c>SERIE:3</c> en el nombre y el ancho.
    /// </summary>
    /// <remarks>
    /// El ancho solo rellena con ceros por la izquierda, que es lo unico que hace falta: los
    /// numeros de serie se dicen de tres cifras casi siempre, y quien no los quiera asi
    /// escribe <c>&lt;SERIE&gt;</c> a secas.
    /// </remarks>
    private static (string Nombre, int Ancho) Partir(string etiqueta)
    {
        var dosPuntos = etiqueta.IndexOf(':');
        if (dosPuntos < 0) return (etiqueta.Trim(), 0);
        var nombre = etiqueta[..dosPuntos].Trim();
        return int.TryParse(etiqueta[(dosPuntos + 1)..].Trim(), NumberStyles.Integer,
            CultureInfo.InvariantCulture, out var ancho) && ancho is > 0 and <= 12
            ? (nombre, ancho)
            : (nombre, 0);
    }

    private static string Rellenar(string valor, int ancho) =>
        ancho <= 0 || valor.Length >= ancho ? valor : valor.PadLeft(ancho, '0');

    private static bool Resolver(string nombre, ContextoDeMacro c, out string valor)
    {
        switch (nombre.ToUpperInvariant())
        {
            case "CALL": valor = (c.Corresponsal ?? string.Empty).Trim().ToUpperInvariant(); return true;
            case "MICALL": valor = c.MiIndicativo.Valor; return true;
            case "NOMBRE": valor = c.Nombre ?? string.Empty; return true;
            case "MINOMBRE": valor = c.MiNombre ?? string.Empty; return true;
            case "QTH": valor = c.Qth ?? string.Empty; return true;
            case "MIQTH": valor = c.MiQth ?? string.Empty; return true;
            case "LOCATOR": valor = c.Locator.Valor; return true;
            case "MILOCATOR": valor = c.MiLocator.Valor; return true;
            case "RST": valor = c.InformeEnviado.Texto; return true;
            case "RSTR": valor = c.InformeRecibido.Texto; return true;
            case "SERIE": valor = Numero(c.Serie); return true;
            case "SERIER": valor = Numero(c.SerieRecibida); return true;
            case "INTERCAMBIO": valor = c.Intercambio ?? string.Empty; return true;
            case "INTERCAMBIOR": valor = c.IntercambioRecibido ?? string.Empty; return true;
            case "BANDA": valor = c.Banda.Nombre; return true;
            case "MODO": valor = c.Modo.NombreUsual; return true;
            case "FREQ": valor = c.Frecuencia.EsCero ? string.Empty : c.Frecuencia.AAdif(); return true;
            case "CONCURSO": valor = c.Concurso ?? string.Empty; return true;
            case "HORA": valor = c.AhoraUtc.UtcDateTime.ToString("HHmm", CultureInfo.InvariantCulture); return true;
            case "FECHA": valor = c.AhoraUtc.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); return true;
            default:
                if (c.Propias.TryGetValue(nombre, out var propio)) { valor = propio; return true; }
                valor = string.Empty;
                return false;
        }
    }

    private static string Numero(int? valor) =>
        valor?.ToString("0", CultureInfo.InvariantCulture) ?? string.Empty;
}
