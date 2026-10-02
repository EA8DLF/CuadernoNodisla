using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nodisla.Cuaderno.Idiomas;

namespace Nodisla.Cuaderno.Modos.Q65;

/// <summary>
/// Las constantes del protocolo Q65 que no se pueden deducir: la estructura del codigo
/// QRA(65,15), las posiciones de sincronismo y el polinomio del CRC.
/// </summary>
/// <remarks>
/// <para>
/// Se cargan de <c>tablas-q65.txt</c>, que va junto al programa con su procedencia escrita
/// (ver <c>Tablas/LEEME-q65.md</c>). Si el fichero falta o esta mal, el modo no finge: avisa por
/// el registro y trabaja con un codigo de pruebas de la misma forma, que sirve para medir el
/// decodificador pero <b>solo se entiende consigo mismo</b>.
/// </para>
/// <para>
/// Al cargar se comprueban dos cosas que no dependen de fiarse de nadie: que las repeticiones
/// declaradas coinciden con las entradas del acumulador, y que los pesos de cada simbolo suman
/// cero en el cuerpo, que es la propiedad de diseno por la que el acumulador acaba en cero. Una
/// errata en un solo numero rompe una de las dos y tira el fichero entero.
/// </para>
/// </remarks>
public sealed class TablasDeQ65
{
    /// <summary>Nombre del fichero que se busca junto al programa.</summary>
    public const string NombreDelFichero = "tablas-q65.txt";

    /// <summary>Simbolos de informacion del codigo: 13 del mensaje y 2 del CRC.</summary>
    public const int SimbolosDeInformacion = 15;

    /// <summary>Simbolos de la palabra de codigo.</summary>
    public const int LongitudDeLaPalabra = 65;

    /// <summary>Simbolos de sincronismo en la trama.</summary>
    public const int SimbolosDeSincronismo = 22;

    /// <summary>Simbolos emitidos por trama: 63 de datos y 22 de sincronismo.</summary>
    public const int SimbolosDeLaTrama = 85;

    private TablasDeQ65(int[] entradas, int[] pesosLog, int[] posicionesDeSincronismo, int polinomioDelCrc, bool esElCodigoReal, string procedencia)
    {
        EntradasDelAcumulador = entradas;
        PesosDelAcumulador = pesosLog;
        PosicionesDeSincronismo = posicionesDeSincronismo;
        PolinomioDelCrc = polinomioDelCrc;
        EsElCodigoReal = esElCodigoReal;
        Procedencia = procedencia;
        PosicionesDeDatos = Enumerable.Range(0, SimbolosDeLaTrama).Where(p => !posicionesDeSincronismo.Contains(p)).ToArray();
    }

    /// <summary>Simbolo de informacion que entra en el acumulador en cada uno de los 51 pasos.</summary>
    public int[] EntradasDelAcumulador { get; }

    /// <summary>Logaritmo del peso de cada paso.</summary>
    public int[] PesosDelAcumulador { get; }

    /// <summary>Posiciones de la trama, contando desde cero, que llevan el tono de sincronismo.</summary>
    public int[] PosicionesDeSincronismo { get; }

    /// <summary>Las 63 posiciones de la trama que llevan datos, en orden.</summary>
    public int[] PosicionesDeDatos { get; }

    /// <summary>Polinomio del CRC de 12 bits, reflejado.</summary>
    public int PolinomioDelCrc { get; }

    /// <summary>Falso mientras se use el codigo de pruebas.</summary>
    public bool EsElCodigoReal { get; }

    /// <summary>De donde salieron las tablas, para el registro y la pantalla.</summary>
    public string Procedencia { get; }

    /// <summary>
    /// Carga las tablas del fichero indicado, o se queda con el codigo de pruebas si no lo hay.
    /// </summary>
    public static TablasDeQ65 Cargar(string? ruta = null, ILogger? registro = null)
    {
        registro ??= NullLogger.Instance;
        ruta ??= Path.Combine(AppContext.BaseDirectory, NombreDelFichero);

        if (!File.Exists(ruta))
        {
            registro.LogWarning(
                "No se encontró la tabla del código de Q65 en {Ruta}. Q65 trabaja con un código de pruebas: funciona consigo mismo pero no decodifica a nadie más.",
                ruta);
            return DePruebas();
        }

        try
        {
            var tablas = Leer(File.ReadAllLines(ruta), ruta);
            registro.LogInformation("Tablas de Q65 cargadas de {Ruta}.", ruta);
            return tablas;
        }
        catch (Exception ex) when (ex is IOException or FormatException or ArgumentException)
        {
            registro.LogError(ex, "La tabla {Ruta} no se pudo leer y se descarta entera.", ruta);
            return DePruebas();
        }
    }

    /// <summary>Lee las tablas de las lineas de un fichero.</summary>
    /// <exception cref="FormatException">Si falta algo o algo no cuadra.</exception>
    public static TablasDeQ65 Leer(IEnumerable<string> lineas, string procedencia = "memoria")
    {
        ArgumentNullException.ThrowIfNull(lineas);
        int[]? entradas = null, pesos = null, sincronismo = null, repeticiones = null;
        int? crc = null;

        foreach (var cruda in lineas)
        {
            var linea = cruda.Trim();
            if (linea.Length == 0 || linea[0] == '#') continue;
            var partes = linea.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var valores = partes.Skip(1).ToArray();
            switch (partes[0])
            {
                case "SIMBOLOS":
                    var dims = Enteros(valores);
                    if (dims.Length != 3 || dims[0] != SimbolosDeInformacion || dims[1] != LongitudDeLaPalabra || dims[2] != CampoDeGalois64.Orden)
                        throw new FormatException("Las dimensiones no son las de Q65: 15 simbolos de informacion, 65 de palabra, cuerpo de 64.");
                    break;
                case "SINCRONISMO":
                    sincronismo = Enteros(valores).Select(p => p - 1).ToArray();
                    break;
                case "ENTRADAS":
                    entradas = Enteros(valores);
                    break;
                case "PESOS":
                    pesos = Enteros(valores);
                    break;
                case "REPETICIONES":
                    repeticiones = Enteros(valores);
                    break;
                case "CRC12":
                    crc = int.Parse(valores.Single(), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                    break;
                default:
                    throw new FormatException($"Linea desconocida: «{partes[0]}».");
            }
        }

        if (entradas is null || pesos is null || sincronismo is null || repeticiones is null || crc is null)
            throw new FormatException("Faltan lineas en la tabla de Q65.");

        Comprobar(entradas, pesos, sincronismo, repeticiones);
        return new TablasDeQ65(entradas, pesos, sincronismo, crc.Value, esElCodigoReal: true, procedencia);
    }

    /// <summary>
    /// Un codigo de pruebas con la misma forma que el de verdad: mismas repeticiones, otra
    /// baraja y otros pesos, con la propiedad de cierre del acumulador.
    /// </summary>
    public static TablasDeQ65 DePruebas()
    {
        int[] repeticiones = [3, 3, 3, 3, 3, 3, 3, 4, 4, 4, 4, 4, 4, 3, 3];
        int[] sincronismo = [0, 8, 11, 12, 14, 21, 22, 25, 26, 32, 34, 37, 45, 49, 54, 59, 61, 65, 68, 73, 75, 84];
        var azar = new Random(650915);

        while (true)
        {
            var secuencia = new List<int>();
            for (var s = 0; s < repeticiones.Length; s++)
                for (var r = 0; r < repeticiones[s]; r++) secuencia.Add(s);
            var entradas = secuencia.OrderBy(_ => azar.Next()).ToArray();
            var pesos = new int[entradas.Length];
            for (var k = 0; k < pesos.Length; k++) pesos[k] = azar.Next(CampoDeGalois64.Orden - 1);

            // El ultimo paso de cada simbolo se ajusta para que sus pesos sumen cero. Si la
            // suma de los demas ya es cero no hay peso valido, y se vuelve a barajar.
            var valido = true;
            for (var s = 0; s < repeticiones.Length && valido; s++)
            {
                var pasos = Enumerable.Range(0, entradas.Length).Where(k => entradas[k] == s).ToArray();
                var suma = 0;
                foreach (var k in pasos.Take(pasos.Length - 1)) suma ^= CampoDeGalois64.Potencia(pesos[k]);
                if (suma == 0) { valido = false; break; }
                pesos[pasos[^1]] = CampoDeGalois64.Logaritmo(suma);
            }
            if (!valido) continue;

            Comprobar(entradas, pesos, sincronismo, repeticiones);
            return new TablasDeQ65(entradas, pesos, sincronismo, 0xF01, esElCodigoReal: false,
                Textos.T("Servicios.Modos.CodigoDePruebasQ65"));
        }
    }

    private static int[] Enteros(string[] valores) =>
        valores.Select(v => int.Parse(v, CultureInfo.InvariantCulture)).ToArray();

    private static void Comprobar(int[] entradas, int[] pesos, int[] sincronismo, int[] repeticiones)
    {
        const int Pasos = LongitudDeLaPalabra - SimbolosDeInformacion + 1;
        if (entradas.Length != Pasos) throw new FormatException($"Hacen falta {Pasos} entradas del acumulador y hay {entradas.Length}.");
        if (pesos.Length != Pasos) throw new FormatException($"Hacen falta {Pasos} pesos y hay {pesos.Length}.");
        if (repeticiones.Length != SimbolosDeInformacion) throw new FormatException("Las repeticiones no son una por simbolo de informacion.");
        if (entradas.Any(e => e < 0 || e >= SimbolosDeInformacion)) throw new FormatException("Una entrada del acumulador se sale de los simbolos de informacion.");
        if (pesos.Any(p => p < 0 || p >= CampoDeGalois64.Orden - 1)) throw new FormatException("Un peso se sale del cuerpo.");

        if (sincronismo.Length != SimbolosDeSincronismo) throw new FormatException("No hay 22 posiciones de sincronismo.");
        for (var i = 0; i < sincronismo.Length; i++)
        {
            if (sincronismo[i] < 0 || sincronismo[i] >= SimbolosDeLaTrama) throw new FormatException("Una posicion de sincronismo se sale de la trama.");
            if (i > 0 && sincronismo[i] <= sincronismo[i - 1]) throw new FormatException("Las posiciones de sincronismo no van en orden.");
        }

        for (var s = 0; s < SimbolosDeInformacion; s++)
        {
            var pasos = Enumerable.Range(0, Pasos).Where(k => entradas[k] == s).ToArray();
            if (pasos.Length != repeticiones[s])
                throw new FormatException($"El simbolo {s} entra {pasos.Length} veces y las repeticiones dicen {repeticiones[s]}.");
            var suma = 0;
            foreach (var k in pasos) suma ^= CampoDeGalois64.Potencia(pesos[k]);
            if (suma != 0)
                throw new FormatException($"Los pesos del simbolo {s} no suman cero: el acumulador no cerraria.");
        }
    }
}
