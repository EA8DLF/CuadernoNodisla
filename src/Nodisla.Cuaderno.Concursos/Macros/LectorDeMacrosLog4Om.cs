using System.Globalization;
using System.Text;

namespace Nodisla.Cuaderno.Concursos.Macros;

/// <summary>
/// Trae las macros que el operador ya tenia escritas en Log4OM.
/// </summary>
/// <remarks>
/// <para>
/// El original las guarda en <c>%AppData%\Log4OM2\macro\</c>, un fichero por juego, una linea
/// por tecla, con esta forma:
/// </para>
/// <code>
/// ! DE * YR RST &lt;STTXF&gt; &lt;STTX&gt; NAME IS &lt;NAME&gt;###QSO
/// </code>
/// <para>
/// El texto va antes del <c>###</c> y la etiqueta del boton detras. Dentro del texto, el
/// <c>*</c> es mi indicativo, el <c>!</c> es el del corresponsal y las etiquetas entre angulos
/// son campos: <c>STTX</c> es el numero de serie enviado y <c>STTXF</c> el mismo numero con
/// ceros delante.
/// </para>
/// <para>
/// Aqui se traduce todo eso a la sintaxis propia. La traduccion se hace al importar y no al
/// usar: asi el operador acaba con macros escritas de una sola manera, y no con un dialecto
/// heredado que haya que seguir entendiendo para siempre.
/// </para>
/// </remarks>
public static class LectorDeMacrosLog4Om
{
    private static readonly (string De, string A)[] Etiquetas =
    [
        ("<STTXF>", "<SERIE:3>"),
        ("<STTX>", "<SERIE>"),
        ("<SRX>", "<SERIER>"),
        ("<NAME>", "<NOMBRE>"),
        ("<MY_NAME>", "<MINOMBRE>"),
        ("<QTH>", "<QTH>"),
        ("<MY_QTH>", "<MIQTH>"),
        ("<GRIDSQUARE>", "<LOCATOR>"),
        ("<MY_GRIDSQUARE>", "<MILOCATOR>"),
        ("<RST_SENT>", "<RST>"),
        ("<RST_RCVD>", "<RSTR>"),
        ("<BAND>", "<BANDA>"),
        ("<MODE>", "<MODO>"),
        ("<FREQ>", "<FREQ>"),
        ("<CONTEST_ID>", "<CONCURSO>"),
        ("<TIME_ON>", "<HORA>"),
        ("<QSO_DATE>", "<FECHA>"),
        ("<CALL>", "<CALL>"),
        ("<STATION_CALLSIGN>", "<MICALL>"),
    ];

    /// <summary>Lee un fichero de macros de Log4OM.</summary>
    /// <param name="ruta">Fichero, por ejemplo <c>defaultMacro.txt</c>.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <returns>El juego de macros ya traducido.</returns>
    public static async Task<JuegoDeMacros> LeerAsync(string ruta, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ruta);
        var lineas = await File.ReadAllLinesAsync(ruta, ct).ConfigureAwait(false);
        return Leer(Path.GetFileNameWithoutExtension(ruta), lineas);
    }

    /// <summary>Convierte las lineas de un fichero de macros de Log4OM.</summary>
    /// <param name="nombre">Como llamar al juego resultante.</param>
    /// <param name="lineas">Lineas del fichero, en orden.</param>
    /// <returns>
    /// El juego traducido. Las lineas vacias del original —que las hay, para dejar teclas
    /// libres— no producen macro.
    /// </returns>
    public static JuegoDeMacros Leer(string nombre, IEnumerable<string> lineas)
    {
        ArgumentNullException.ThrowIfNull(lineas);
        var macros = new List<Macro>(12);
        var tecla = 0;
        foreach (var cruda in lineas)
        {
            tecla++;
            var linea = cruda.TrimStart('﻿').Trim();
            if (linea.Length == 0 || linea == "###") continue;

            var corte = linea.IndexOf("###", StringComparison.Ordinal);
            var texto = corte >= 0 ? linea[..corte] : linea;
            var etiqueta = corte >= 0 ? linea[(corte + 3)..].Trim() : string.Empty;
            texto = Traducir(texto).Trim();
            if (texto.Length == 0) continue;

            if (etiqueta.Length == 0) etiqueta = "F" + tecla.ToString(CultureInfo.InvariantCulture);
            macros.Add(new Macro("F" + tecla.ToString(CultureInfo.InvariantCulture), etiqueta, texto));
        }
        return new JuegoDeMacros(
            string.IsNullOrWhiteSpace(nombre) ? "Importadas de Log4OM" : nombre, macros);
    }

    /// <summary>Traduce el texto de una macro de Log4OM a la sintaxis propia.</summary>
    /// <param name="texto">Texto original.</param>
    /// <returns>El texto con las etiquetas y los comodines ya cambiados.</returns>
    public static string Traducir(string? texto)
    {
        if (string.IsNullOrEmpty(texto)) return string.Empty;

        var salida = new StringBuilder(texto.Length + 16);
        foreach (var c in texto)
        {
            // El asterisco y la admiracion son los dos comodines del original. Se cambian
            // primero, antes de tocar las etiquetas, porque no pueden aparecer dentro de una.
            salida.Append(c switch
            {
                '*' => "<MICALL>",
                '!' => "<CALL>",
                _ => c.ToString(),
            });
        }

        var resultado = salida.ToString();
        foreach (var (de, a) in Etiquetas)
        {
            if (!de.Equals(a, StringComparison.Ordinal))
            {
                resultado = resultado.Replace(de, a, StringComparison.OrdinalIgnoreCase);
            }
        }
        return resultado;
    }
}
