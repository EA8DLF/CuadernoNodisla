using System.Globalization;
using System.Text;

namespace Nodisla.Cuaderno.Servicios.Adif;

/// <summary>
/// Lectura y escritura de ADIF reducidas a lo que necesitan los servicios de confirmacion.
/// </summary>
/// <remarks>
/// <para>
/// El programa tiene un analizador ADIF completo en <c>Nodisla.Cuaderno.Adif</c>, y aqui no se
/// usa a proposito. Lo que va y viene de estos servicios no son contactos del cuaderno: son
/// registros parciales que hay que leer como pares de campos (el informe de LoTW trae
/// <c>APP_LOTW_RXQSL</c>, el buzon de eQSL trae <c>APP_EQSL_AG</c>) y, sobre todo, registros
/// que hay que escribir con una <b>lista de campos distinta para cada servicio</b>: eQSL
/// rechaza el registro entero si le llega un campo que no conoce.
/// </para>
/// <para>
/// Es deliberadamente tolerante: la longitud declarada se usa como pista y, si no cuadra con
/// el contenido, se vuelve a buscar el siguiente campo. Los ficheros reales vienen con
/// longitudes mal contadas cuando hay acentos.
/// </para>
/// </remarks>
public static class AdifLigero
{
    /// <summary>
    /// Lee los registros de un texto ADI. Devuelve un diccionario por registro, sin interpretar
    /// los valores: eso es cosa de cada servicio.
    /// </summary>
    /// <param name="texto">Contenido del fichero ADI.</param>
    public static IReadOnlyList<IReadOnlyDictionary<string, string>> LeerRegistros(string texto)
    {
        var registros = new List<IReadOnlyDictionary<string, string>>();
        if (string.IsNullOrEmpty(texto)) return registros;

        var actual = NuevoRegistro();
        var i = 0;

        // La cabecera termina en <EOH>. Si el fichero no la trae, todo es cuerpo: hay
        // programas que exportan sin cabecera y el registro no debe perderse.
        var finDeCabecera = texto.IndexOf("<EOH>", StringComparison.OrdinalIgnoreCase);
        if (finDeCabecera >= 0) i = finDeCabecera + 5;

        while (i < texto.Length)
        {
            var abre = texto.IndexOf('<', i);
            if (abre < 0) break;
            var cierra = texto.IndexOf('>', abre + 1);
            if (cierra < 0) break;

            var etiqueta = texto.AsSpan(abre + 1, cierra - abre - 1);
            i = cierra + 1;

            if (etiqueta.Equals("EOR", StringComparison.OrdinalIgnoreCase))
            {
                if (actual.Count > 0) registros.Add(actual);
                actual = NuevoRegistro();
                continue;
            }

            if (etiqueta.Equals("EOH", StringComparison.OrdinalIgnoreCase))
            {
                actual = NuevoRegistro();
                continue;
            }

            // Forma NOMBRE:longitud[:tipo]
            var primerDosPuntos = etiqueta.IndexOf(':');
            if (primerDosPuntos <= 0) continue;

            var nombre = etiqueta[..primerDosPuntos].Trim().ToString();
            var resto = etiqueta[(primerDosPuntos + 1)..];
            var segundoDosPuntos = resto.IndexOf(':');
            var textoLongitud = segundoDosPuntos >= 0 ? resto[..segundoDosPuntos] : resto;
            if (!int.TryParse(textoLongitud, NumberStyles.Integer, CultureInfo.InvariantCulture, out var longitud)
                || longitud < 0)
            {
                continue;
            }

            var valor = LeerValor(texto, ref i, longitud);
            if (nombre.Length > 0) actual[nombre] = valor;
        }

        if (actual.Count > 0) registros.Add(actual);
        return registros;
    }

    /// <summary>
    /// Toma la longitud declarada como pista. Si tras ella no viene el campo siguiente, se
    /// ignora y se corta en el proximo <c>&lt;</c>: un fichero mal contado sigue siendo legible.
    /// </summary>
    private static string LeerValor(string texto, ref int i, int longitud)
    {
        var disponible = texto.Length - i;
        var fin = i + Math.Min(longitud, disponible);

        var siguiente = texto.IndexOf('<', i);
        if (siguiente >= 0 && siguiente < fin) fin = siguiente;

        var valor = texto[i..fin];
        i = fin;
        return valor.Trim();
    }

    private static Dictionary<string, string> NuevoRegistro() =>
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Escribe un fichero ADI con la cabecera y los registros indicados.</summary>
    /// <param name="registros">Registros ya filtrados a los campos que acepta el servicio.</param>
    /// <param name="cabecera">Campos de cabecera; se escriben antes del <c>&lt;EOH&gt;</c>.</param>
    public static string EscribirRegistros(
        IEnumerable<IReadOnlyDictionary<string, string>> registros,
        IReadOnlyDictionary<string, string>? cabecera = null)
    {
        ArgumentNullException.ThrowIfNull(registros);

        var sb = new StringBuilder();
        sb.Append("Cuaderno NODISLA\r\n");
        if (cabecera is not null)
        {
            foreach (var (campo, valor) in cabecera) EscribirCampo(sb, campo, valor);
        }
        sb.Append("<EOH>\r\n");

        foreach (var registro in registros)
        {
            foreach (var (campo, valor) in registro) EscribirCampo(sb, campo, valor);
            sb.Append("<EOR>\r\n");
        }
        return sb.ToString();
    }

    /// <summary>Escribe un campo ADI suelto, con su longitud declarada.</summary>
    /// <param name="sb">Destino.</param>
    /// <param name="campo">Nombre del campo.</param>
    /// <param name="valor">Valor; si esta vacio no se escribe nada.</param>
    public static void EscribirCampo(StringBuilder sb, string campo, string? valor)
    {
        ArgumentNullException.ThrowIfNull(sb);
        if (string.IsNullOrEmpty(valor)) return;
        sb.Append('<').Append(campo.ToUpperInvariant()).Append(':')
          .Append(valor.Length.ToString(CultureInfo.InvariantCulture)).Append('>')
          .Append(valor).Append(' ');
    }
}
