using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nodisla.Cuaderno.Idiomas;

namespace Nodisla.Cuaderno.Modos.Ldpc;

/// <summary>
/// La tabla de un codigo corrector LDPC cualquiera —MSK144, FST4, FST4W, el codigo corto de
/// MSK144— cargada de su fichero de datos y cruzada con su generadora.
/// </summary>
/// <remarks>
/// <para>
/// Es la version general de <see cref="TablasDelProtocolo"/>, que nacio para FT8 y lleva ademas
/// la mezcla de FT4. Aqui solo hay un codigo: se le dice que fichero y que dimensiones se
/// esperan, y devuelve el codigo real si el fichero esta y cuadra, o un codigo de pruebas de las
/// mismas dimensiones si no. Lo mismo que hace el de FT8, con la misma regla: <b>no fingir</b>.
/// </para>
/// <para>
/// <b>Formato del fichero.</b> El de <c>tablas-ft8.txt</c>: comentarios con almohadilla, una
/// primera linea con la longitud y los bits de mensaje, una linea por ecuacion de paridad con
/// los bits que toca, y filas <c>GENERADORA</c> en hexadecimal para cruzarlas con las
/// ecuaciones. Se anade una linea opcional <c>MENSAJE-EN</c> con la posicion de cada bit del
/// mensaje dentro de la palabra, para los codigos que no llevan el mensaje delante.
/// </para>
/// </remarks>
public sealed class TablaLdpc
{
    private TablaLdpc(CodigoLdpc codigo, bool esElCodigoReal, string procedencia)
    {
        Codigo = codigo;
        EsElCodigoReal = esElCodigoReal;
        Procedencia = procedencia;
    }

    /// <summary>El codigo corrector.</summary>
    public CodigoLdpc Codigo { get; }

    /// <summary>Falso mientras se este usando el codigo de pruebas.</summary>
    public bool EsElCodigoReal { get; }

    /// <summary>De donde salio, para decirlo en el registro.</summary>
    public string Procedencia { get; }

    /// <summary>
    /// Carga la tabla del fichero, o se queda con un codigo de pruebas de las mismas dimensiones.
    /// </summary>
    /// <param name="nombreDelFichero">Nombre del fichero, que se busca junto al programa si no se da ruta.</param>
    /// <param name="longitud">Bits por palabra que tiene que traer.</param>
    /// <param name="bitsDeMensaje">Bits de mensaje que tiene que traer.</param>
    /// <param name="ruta">Ruta completa, si no es la de junto al programa.</param>
    /// <param name="registro">Para dejar constancia.</param>
    /// <param name="semillaDePruebas">Semilla del codigo de pruebas, distinta por modo para que no se confundan.</param>
    public static TablaLdpc Cargar(
        string nombreDelFichero,
        int longitud,
        int bitsDeMensaje,
        string? ruta = null,
        ILogger? registro = null,
        int semillaDePruebas = 8)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nombreDelFichero);
        registro ??= NullLogger.Instance;
        ruta ??= Path.Combine(AppContext.BaseDirectory, nombreDelFichero);

        if (!File.Exists(ruta))
        {
            registro.LogWarning(
                "No se encontró la tabla del código corrector {Nombre} en {Ruta}. Se trabaja con un código de pruebas ({Longitud},{Bits}): funciona consigo mismo pero no decodifica a nadie más.",
                nombreDelFichero, ruta, longitud, bitsDeMensaje);
            return DePruebas(longitud, bitsDeMensaje, semillaDePruebas);
        }

        try
        {
            var codigo = Leer(File.ReadAllLines(ruta), longitud, bitsDeMensaje);
            registro.LogInformation("Tabla del código corrector cargada de {Ruta}.", ruta);
            return new TablaLdpc(codigo, esElCodigoReal: true, ruta);
        }
        catch (Exception ex) when (ex is IOException or FormatException or ArgumentException)
        {
            registro.LogError(ex, "La tabla {Ruta} no se pudo leer y se descarta entera.", ruta);
            return DePruebas(longitud, bitsDeMensaje, semillaDePruebas);
        }
    }

    /// <summary>Un codigo de pruebas de las dimensiones pedidas, marcado como tal.</summary>
    public static TablaLdpc DePruebas(int longitud, int bitsDeMensaje, int semilla = 8) =>
        new(CodigoLdpc.ConstruirDePrueba(semilla, longitud, bitsDeMensaje), esElCodigoReal: false, Textos.T("Servicios.Modos.CodigoDePruebas"));

    /// <summary>Lee y comprueba el contenido de un fichero de tablas.</summary>
    /// <param name="lineas">Lineas del fichero.</param>
    /// <param name="longitudEsperada">Bits por palabra que tiene que traer.</param>
    /// <param name="bitsDeMensajeEsperados">Bits de mensaje que tiene que traer.</param>
    /// <exception cref="FormatException">Si el fichero esta mal formado o no cuadra consigo mismo.</exception>
    public static CodigoLdpc Leer(string[] lineas, int longitudEsperada, int bitsDeMensajeEsperados)
    {
        ArgumentNullException.ThrowIfNull(lineas);
        var ecuaciones = new List<int[]>();
        var generadora = new List<byte[]>();
        int[]? posicionesDeMensaje = null;
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

            if (linea.StartsWith("MENSAJE-EN", StringComparison.OrdinalIgnoreCase))
            {
                posicionesDeMensaje = Numeros(linea["MENSAJE-EN".Length..]);
                continue;
            }

            var numeros = Numeros(linea);
            if (longitud == 0)
            {
                if (numeros.Length != 2) throw new FormatException("La primera línea debe traer la longitud y los bits de mensaje.");
                longitud = numeros[0];
                bitsDeMensaje = numeros[1];
                if (longitud != longitudEsperada || bitsDeMensaje != bitsDeMensajeEsperados)
                    throw new FormatException(
                        $"El fichero trae un código ({longitud},{bitsDeMensaje}) y se esperaba ({longitudEsperada},{bitsDeMensajeEsperados}).");
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
        if (posicionesDeMensaje is not null && posicionesDeMensaje.Length != bitsDeMensaje)
            throw new FormatException($"MENSAJE-EN debe traer {bitsDeMensaje} posiciones y trae {posicionesDeMensaje.Length}.");

        var h = new bool[ecuaciones.Count][];
        for (var e = 0; e < ecuaciones.Count; e++)
        {
            h[e] = new bool[longitud];
            foreach (var v in ecuaciones[e]) h[e][v] = true;
        }

        var codigo = posicionesDeMensaje is null
            ? CodigoLdpc.DesdeMatrizDeParidad(h, bitsDeMensaje)
            : CodigoLdpc.DesdeMatrizDeParidad(h, posicionesDeMensaje);
        if (generadora.Count > 0) CruzarConLaGeneradora(codigo, generadora);
        return codigo;
    }

    private static int[] Numeros(string texto) =>
        texto.Split([' ', '\t', ','], StringSplitOptions.RemoveEmptyEntries)
             .Select(t => int.Parse(t, CultureInfo.InvariantCulture))
             .ToArray();

    /// <summary>
    /// Comprueba que codificar despejando las ecuaciones da lo mismo que la generadora, en los
    /// mensajes de la base (un solo uno cada uno). Como el codigo es lineal, si coinciden en la
    /// base coinciden en todo.
    /// </summary>
    private static void CruzarConLaGeneradora(CodigoLdpc codigo, List<byte[]> generadora)
    {
        var paridad = codigo.Ecuaciones;
        if (generadora.Count != paridad)
            throw new FormatException($"La generadora debe traer {paridad} filas y trae {generadora.Count}.");
        var bytesPorFila = (codigo.BitsDeMensaje + 7) / 8;
        foreach (var fila in generadora)
            if (fila.Length != bytesPorFila)
                throw new FormatException($"Cada fila de la generadora debe traer {bytesPorFila} bytes y hay una con {fila.Length}.");

        var unidad = new byte[codigo.BitsDeMensaje];
        var posiciones = codigo.PosicionesDeParidad;
        for (var i = 0; i < codigo.BitsDeMensaje; i++)
        {
            Array.Clear(unidad);
            unidad[i] = 1;
            var palabra = codigo.Codificar(unidad);
            for (var e = 0; e < paridad; e++)
            {
                var segunLaGeneradora = (byte)((generadora[e][i / 8] >> (7 - (i % 8))) & 1);
                if (palabra[posiciones[e]] != segunLaGeneradora)
                    throw new FormatException(
                        $"Las dos tablas del código no dicen lo mismo: para el mensaje con un uno en el bit {i}, " +
                        $"el bit de paridad {e} sale {palabra[posiciones[e]]} despejando las ecuaciones y " +
                        $"{segunLaGeneradora} según la generadora. Hay una errata en una de las dos.");
            }
        }
    }
}
