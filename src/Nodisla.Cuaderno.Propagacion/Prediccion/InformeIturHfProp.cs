using System.Globalization;

namespace Nodisla.Cuaderno.Propagacion.Prediccion;

/// <summary>Una fila del informe de ITURHFProp: lo calculado para una frecuencia.</summary>
/// <param name="FrecuenciaMhz">Frecuencia de la fila.</param>
/// <param name="FiabilidadBasica">Fiabilidad basica del circuito, de 0 a 1.</param>
/// <param name="PotenciaRecibidaDbw">Potencia mediana en recepcion, en decibelios sobre un vatio.</param>
/// <param name="RelacionSenalRuidoDb">Relacion senal-ruido mediana, en decibelios.</param>
/// <param name="MufBasicaMhz">Maxima frecuencia utilizable basica del trayecto.</param>
/// <param name="Saltos">Modo mas bajo de la capa F2, es decir, numero de saltos.</param>
public sealed record FilaIturHfProp(
    double FrecuenciaMhz,
    double FiabilidadBasica,
    double? PotenciaRecibidaDbw,
    double? RelacionSenalRuidoDb,
    double? MufBasicaMhz,
    int? Saltos);

/// <summary>
/// Lee el informe CSV que produce ITURHFProp.
/// </summary>
/// <remarks>
/// <para>
/// El informe trae su propia cabecera con una linea <c>Column NN: descripcion</c> por columna,
/// asi que <b>no se leen posiciones fijas</b>: se busca cada dato por su nombre. Si un dia
/// cambian el orden o anaden columnas, esto sigue funcionando; y si falta una columna, se sabe.
/// </para>
/// <para>
/// El modelo marca con -307 lo que no ha podido calcular. Ese valor no se convierte en un
/// numero: se devuelve nulo, porque -307 dBW no es una senal debil, es un hueco.
/// </para>
/// </remarks>
public static class InformeIturHfProp
{
    /// <summary>Valor con el que el modelo marca lo que no ha calculado.</summary>
    public const double ValorAusente = -307.0;

    /// <summary>Lee el informe entero.</summary>
    /// <param name="texto">Contenido del fichero de salida.</param>
    /// <returns>Una fila por frecuencia, o una lista vacia si el informe no se entiende.</returns>
    public static IReadOnlyList<FilaIturHfProp> Leer(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
        {
            return [];
        }

        var lineas = texto.Split('\n');
        var columnas = LeerCabecera(lineas);
        if (columnas.Count == 0)
        {
            return [];
        }

        var frecuencia = Buscar(columnas, "Frequency");
        var fiabilidad = Buscar(columnas, "BCR - ");
        if (frecuencia < 0 || fiabilidad < 0)
        {
            // Sin frecuencia o sin fiabilidad el informe no sirve para nada.
            return [];
        }

        var potencia = Buscar(columnas, "Pr - ");
        var senalRuido = Buscar(columnas, "SNR - ");
        var muf = Buscar(columnas, "BMUF - ");
        var modoF2 = Buscar(columnas, "Lowest order mode for the F2 layer");

        var filas = new List<FilaIturHfProp>();
        foreach (var linea in lineas)
        {
            var campos = Campos(linea);
            if (campos is null || campos.Length <= fiabilidad)
            {
                continue;
            }

            var f = Numero(campos, frecuencia);
            var bcr = Numero(campos, fiabilidad);
            if (f is null || bcr is null)
            {
                continue;
            }

            filas.Add(new FilaIturHfProp(
                f.Value,
                Math.Clamp(bcr.Value / 100.0, 0.0, 1.0),
                Numero(campos, potencia),
                Numero(campos, senalRuido),
                Numero(campos, muf),
                Modo(campos, modoF2)));
        }

        return filas;
    }

    /// <summary>
    /// Saca de la cabecera que hay en cada columna. La clave es el numero de columna menos uno.
    /// </summary>
    private static Dictionary<int, string> LeerCabecera(string[] lineas)
    {
        var columnas = new Dictionary<int, string>();
        foreach (var linea in lineas)
        {
            var limpia = linea.Trim();
            if (!limpia.StartsWith("Column ", StringComparison.Ordinal))
            {
                continue;
            }

            var separador = limpia.IndexOf(':', StringComparison.Ordinal);
            if (separador < 0)
            {
                continue;
            }

            var numero = limpia[7..separador].Trim();
            if (int.TryParse(numero, NumberStyles.Integer, CultureInfo.InvariantCulture, out var indice)
                && indice >= 1)
            {
                columnas[indice - 1] = limpia[(separador + 1)..].Trim();
            }
        }

        return columnas;
    }

    /// <summary>Indice de la columna cuya descripcion empieza por el texto dado, o -1.</summary>
    private static int Buscar(Dictionary<int, string> columnas, string principio)
    {
        foreach (var (indice, descripcion) in columnas)
        {
            if (descripcion.StartsWith(principio, StringComparison.OrdinalIgnoreCase))
            {
                return indice;
            }
        }

        return -1;
    }

    /// <summary>
    /// Trocea una linea de datos. Las lineas de datos empiezan por el mes en dos cifras seguido
    /// de una coma; todo lo demas del fichero es cabecera o adorno.
    /// </summary>
    private static string[]? Campos(string linea)
    {
        var limpia = linea.Trim();
        if (limpia.Length < 4 || !char.IsAsciiDigit(limpia[0]) || !char.IsAsciiDigit(limpia[1]) || limpia[2] != ',')
        {
            return null;
        }

        return limpia.Split(',');
    }

    private static double? Numero(string[] campos, int indice)
    {
        if (indice < 0 || indice >= campos.Length)
        {
            return null;
        }

        if (!double.TryParse(
                campos[indice].Trim(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var valor))
        {
            return null;
        }

        // -307 es el hueco del modelo. Se devuelve nulo, no el numero.
        return Math.Abs(valor - ValorAusente) < 0.005 ? null : valor;
    }

    /// <summary>
    /// Saca el numero de saltos de un modo como <c>1F2</c> o <c>2F2</c>.
    /// </summary>
    private static int? Modo(string[] campos, int indice)
    {
        if (indice < 0 || indice >= campos.Length)
        {
            return null;
        }

        var texto = campos[indice].Trim();
        if (texto.Length == 0 || !char.IsAsciiDigit(texto[0]))
        {
            return null;
        }

        var cifras = 0;
        while (cifras < texto.Length && char.IsAsciiDigit(texto[cifras]))
        {
            cifras++;
        }

        return int.TryParse(texto[..cifras], NumberStyles.Integer, CultureInfo.InvariantCulture, out var saltos)
               && saltos > 0
            ? saltos
            : null;
    }
}
