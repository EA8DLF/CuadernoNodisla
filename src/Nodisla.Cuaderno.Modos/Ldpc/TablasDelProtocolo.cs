using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Nodisla.Cuaderno.Modos.Ldpc;

/// <summary>
/// Las tablas de constantes del protocolo que no se pueden deducir: el codigo corrector y la
/// mezcla de FT4.
/// </summary>
/// <remarks>
/// <para>
/// Casi todo lo que hace este modem se puede escribir desde cero a partir de la descripcion del
/// protocolo: los grupos de Costas, el codigo de Gray, la modulacion, el sincronismo, el CRC y
/// el empaquetado de los mensajes. Dos cosas no:
/// </para>
/// <list type="number">
/// <item>
/// La <b>matriz de paridad del codigo LDPC(174,91)</b>. Son 83 ecuaciones de seis o siete bits
/// cada una, elegidas a mano por quien diseno el protocolo. No hay formula que las genere; o se
/// tienen las mismas que todo el mundo o no se decodifica a nadie.
/// </item>
/// <item>
/// La <b>matriz generadora</b> del mismo codigo, que describe lo mismo por el otro camino. No se
/// usa para codificar —eso se despeja de las ecuaciones de arriba— sino para <b>vigilarlas</b>:
/// al cargar el fichero se comprueba que las dos maneras dan la misma palabra, y si discreparan
/// en un solo bit se rechaza el fichero entero. Una tabla de 522 numeros copiada a mano es justo
/// donde se cuela una errata, y una errata aqui no da un error visible: da un modem que
/// decodifica basura de vez en cuando con el CRC cuadrando.
/// </item>
/// <item>
/// La <b>secuencia de mezcla de FT4</b>, diez bytes con los que se revuelve el mensaje antes de
/// codificarlo para que FT4 y FT8 no se confundan entre si en el aire.
/// </item>
/// </list>
/// <para>
/// Mientras esas tablas no esten, el modem <b>no finge</b>: dice que trabaja con un codigo de
/// pruebas, se puede medir consigo mismo de punta a punta, y cualquier decodificacion que salga
/// lleva la marca de que no es interoperable. Es la unica postura honesta; un modem que dijera
/// «listo» y luego no decodificara a nadie seria mucho peor.
/// </para>
/// </remarks>
public sealed class TablasDelProtocolo
{
    /// <summary>Nombre del fichero de tablas que se busca junto al programa.</summary>
    public const string NombreDelFichero = "tablas-ft8.txt";

    private TablasDelProtocolo(CodigoLdpc ldpc, bool esElCodigoReal, byte[] mezclaDeFt4, string procedencia)
    {
        Ldpc = ldpc;
        EsElCodigoReal = esElCodigoReal;
        MezclaDeFt4 = mezclaDeFt4;
        Procedencia = procedencia;
    }

    /// <summary>Codigo corrector con el que trabaja el modem.</summary>
    public CodigoLdpc Ldpc { get; }

    /// <summary>
    /// Falso mientras se este usando el codigo de pruebas.
    /// </summary>
    /// <remarks>
    /// Con esto en falso el modem sigue funcionando entero —codifica, emite a fichero, sincroniza,
    /// demodula y corrige— pero <b>solo se entiende consigo mismo</b>. Quien ensene decodificaciones
    /// al operador tiene que avisarlo.
    /// </remarks>
    public bool EsElCodigoReal { get; }

    /// <summary>Diez bytes con los que FT4 revuelve el mensaje. Todo ceros si no constan.</summary>
    public byte[] MezclaDeFt4 { get; }

    /// <summary>De donde salieron las tablas, para poder decirlo en el registro.</summary>
    public string Procedencia { get; }

    /// <summary>
    /// Carga las tablas del fichero indicado, o se queda con el codigo de pruebas si no lo hay.
    /// </summary>
    /// <param name="ruta">
    /// Fichero de tablas. Si es nulo se busca <see cref="NombreDelFichero"/> junto al programa.
    /// </param>
    /// <param name="registro">Para dejar constancia de lo que se cargo.</param>
    public static TablasDelProtocolo Cargar(string? ruta = null, ILogger? registro = null)
    {
        registro ??= NullLogger.Instance;
        ruta ??= Path.Combine(AppContext.BaseDirectory, NombreDelFichero);

        if (!File.Exists(ruta))
        {
            registro.LogWarning(
                "No se encontró la tabla del código corrector en {Ruta}. El módem trabaja con un código de pruebas: funciona consigo mismo pero no decodifica a nadie más.",
                ruta);
            return DePruebas();
        }

        try
        {
            var (ldpc, mezcla) = Leer(File.ReadAllLines(ruta));
            registro.LogInformation("Tablas del protocolo cargadas de {Ruta}.", ruta);
            return new TablasDelProtocolo(ldpc, esElCodigoReal: true, mezcla, ruta);
        }
        catch (Exception ex) when (ex is IOException or FormatException or ArgumentException)
        {
            // Una tabla a medias es peor que ninguna: decodificaria basura con el CRC cuadrando
            // de vez en cuando. Se rechaza entera.
            registro.LogError(ex, "La tabla {Ruta} no se pudo leer y se descarta entera.", ruta);
            return DePruebas();
        }
    }

    /// <summary>Tablas de pruebas, con el codigo sustituto de las mismas dimensiones.</summary>
    public static TablasDelProtocolo DePruebas() =>
        new(CodigoLdpc.ConstruirDePrueba(), esElCodigoReal: false, new byte[10], "código de pruebas");

    /// <summary>
    /// Formato del fichero de tablas.
    /// </summary>
    /// <remarks>
    /// Se eligio texto y no un binario para que se pueda mirar, comparar y corregir a mano, y
    /// para que un error de transcripcion se vea. Las lineas que empiezan por almohadilla son
    /// comentarios. La primera linea util dice las dimensiones; luego viene una linea por
    /// ecuacion de paridad con los numeros de los bits que toca, contando desde cero. Una linea
    /// que empiece por <c>MEZCLA-FT4</c> lleva los diez bytes en hexadecimal, y cada linea que
    /// empiece por <c>GENERADORA</c> lleva una fila de la matriz generadora, tambien en
    /// hexadecimal. La generadora es opcional; si esta, se cruza con las ecuaciones y el fichero
    /// no se acepta si no cuadran.
    /// </remarks>
    private static (CodigoLdpc Ldpc, byte[] MezclaDeFt4) Leer(string[] lineas)
    {
        var mezcla = new byte[10];
        var ecuaciones = new List<int[]>();
        var generadora = new List<byte[]>();
        var longitud = 0;
        var bitsDeMensaje = 0;

        foreach (var cruda in lineas)
        {
            var linea = cruda.Trim();
            if (linea.Length == 0 || linea[0] == '#') continue;

            if (linea.StartsWith("GENERADORA", StringComparison.OrdinalIgnoreCase))
            {
                var hex = linea["GENERADORA".Length..].Trim().Replace(" ", string.Empty, StringComparison.Ordinal);
                if (hex.Length % 2 != 0) throw new FormatException("Una fila de la generadora trae un número impar de dígitos hexadecimales.");
                var fila = new byte[hex.Length / 2];
                for (var i = 0; i < fila.Length; i++)
                    fila[i] = byte.Parse(hex.AsSpan(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                generadora.Add(fila);
                continue;
            }

            if (linea.StartsWith("MEZCLA-FT4", StringComparison.OrdinalIgnoreCase))
            {
                var hex = linea["MEZCLA-FT4".Length..].Trim().Replace(" ", string.Empty, StringComparison.Ordinal);
                if (hex.Length != 20) throw new FormatException("La mezcla de FT4 debe traer diez bytes en hexadecimal.");
                for (var i = 0; i < 10; i++)
                    mezcla[i] = byte.Parse(hex.AsSpan(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                continue;
            }

            var numeros = linea.Split([' ', '\t', ','], StringSplitOptions.RemoveEmptyEntries)
                               .Select(t => int.Parse(t, CultureInfo.InvariantCulture))
                               .ToArray();

            if (longitud == 0)
            {
                if (numeros.Length != 2) throw new FormatException("La primera línea debe traer la longitud y los bits de mensaje.");
                longitud = numeros[0];
                bitsDeMensaje = numeros[1];
                continue;
            }

            foreach (var v in numeros)
                if (v < 0 || v >= longitud)
                    throw new FormatException($"El bit {v} se sale de la palabra de {longitud} bits.");
            ecuaciones.Add(numeros);
        }

        if (longitud == 0) throw new FormatException("El fichero no trae dimensiones.");
        if (ecuaciones.Count != longitud - bitsDeMensaje)
            throw new FormatException($"Hacen falta {longitud - bitsDeMensaje} ecuaciones y hay {ecuaciones.Count}.");

        var h = new bool[ecuaciones.Count][];
        for (var e = 0; e < ecuaciones.Count; e++)
        {
            h[e] = new bool[longitud];
            foreach (var v in ecuaciones[e]) h[e][v] = true;
        }

        var ldpc = CodigoLdpc.DesdeMatrizDeParidad(h, bitsDeMensaje);
        if (generadora.Count > 0) CruzarConLaGeneradora(ldpc, generadora);
        return (ldpc, mezcla);
    }

    /// <summary>
    /// Comprueba que codificar despejando las ecuaciones da lo mismo que la tabla generadora.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Las dos tablas describen el mismo codigo por caminos distintos: una dice que bits tienen
    /// que sumar cero y la otra dice directamente como se calcula cada bit de paridad. Si las
    /// dos estan bien copiadas <b>tienen que coincidir en los 174 bits de los 91 mensajes que
    /// llevan un solo uno</b>, y de ahi en todos los demas, porque cualquier mensaje es una suma
    /// de esos.
    /// </para>
    /// <para>
    /// Basta con esos 91 casos: el codigo es lineal, asi que si las dos maneras coinciden en una
    /// base coinciden en todo. Son 91 codificaciones y se hacen una sola vez al arrancar.
    /// </para>
    /// <para>
    /// Si discrepan, hay una errata en una de las dos y no se sabe en cual. Se rechaza el
    /// fichero entero: el modem prefiere avisar de que no tiene tablas antes que trabajar con
    /// unas en las que no se puede confiar.
    /// </para>
    /// </remarks>
    private static void CruzarConLaGeneradora(CodigoLdpc ldpc, List<byte[]> generadora)
    {
        var paridad = ldpc.Longitud - ldpc.BitsDeMensaje;
        if (generadora.Count != paridad)
            throw new FormatException($"La generadora debe traer {paridad} filas y trae {generadora.Count}.");

        var bytesPorFila = (ldpc.BitsDeMensaje + 7) / 8;
        foreach (var fila in generadora)
            if (fila.Length != bytesPorFila)
                throw new FormatException($"Cada fila de la generadora debe traer {bytesPorFila} bytes y hay una con {fila.Length}.");

        var unidad = new byte[ldpc.BitsDeMensaje];
        for (var i = 0; i < ldpc.BitsDeMensaje; i++)
        {
            Array.Clear(unidad);
            unidad[i] = 1;
            var palabra = ldpc.Codificar(unidad);
            for (var e = 0; e < paridad; e++)
            {
                var segunLaGeneradora = (byte)((generadora[e][i / 8] >> (7 - (i % 8))) & 1);
                if (palabra[ldpc.BitsDeMensaje + e] != segunLaGeneradora)
                    throw new FormatException(
                        $"Las dos tablas del código no dicen lo mismo: para el mensaje con un uno en el bit {i}, " +
                        $"el bit de paridad {e} sale {palabra[ldpc.BitsDeMensaje + e]} despejando las ecuaciones y " +
                        $"{segunLaGeneradora} según la generadora. Hay una errata en una de las dos.");
            }
        }
    }
}
