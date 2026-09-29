namespace Nodisla.Cuaderno.Modos.Jt65;

/// <summary>
/// Constantes del protocolo JT65 que no se pueden deducir: el vector de sincronismo, el
/// entrelazado y el codigo de Gray.
/// </summary>
/// <remarks>
/// <para>
/// <b>Procedencia.</b> J. Taylor, K1JT, «The JT65 Communications Protocol», QEX,
/// septiembre-octubre de 2005, y la descripcion del protocolo en la guia de usuario de WSJT-X
/// (apendice «Protocol Specifications»). Son datos del protocolo, del mismo tipo que el
/// polinomio de un CRC: o se tienen los mismos que todo el mundo o no se decodifica a nadie.
/// No se ha copiado codigo. Ver <c>Tablas/LEEME-jt65-jt9.md</c>.
/// </para>
/// </remarks>
public static class TablasJt65
{
    /// <summary>Simbolos por transmision: 63 de datos y 63 de sincronismo.</summary>
    public const int Simbolos = 126;

    /// <summary>Simbolos de datos, uno por simbolo del Reed-Solomon.</summary>
    public const int SimbolosDeDatos = 63;

    /// <summary>
    /// Vector de sincronismo: un 1 en cada intervalo donde suena el tono de sincronismo y un 0
    /// donde va un simbolo de datos. Tiene exactamente 63 unos y 63 ceros.
    /// </summary>
    public static ReadOnlySpan<byte> Sincronismo =>
    [
        1, 0, 0, 1, 1, 0, 0, 0, 1, 1, 1, 1, 1, 1, 0, 1, 0, 1, 0, 0, 0, 1, 0, 1, 1, 0, 0, 1, 0, 0, 0, 1,
        1, 1, 0, 0, 1, 1, 1, 1, 0, 1, 1, 0, 1, 1, 1, 1, 0, 0, 0, 1, 1, 0, 1, 0, 1, 0, 1, 1, 0, 0, 1, 1,
        0, 1, 0, 1, 0, 1, 0, 0, 1, 0, 0, 0, 0, 0, 0, 1, 1, 0, 0, 0, 0, 0, 0, 0, 1, 1, 0, 1, 0, 0, 1, 0,
        1, 1, 0, 1, 0, 1, 0, 1, 0, 0, 1, 1, 0, 0, 1, 0, 0, 1, 0, 0, 0, 0, 1, 1, 1, 1, 1, 1, 1, 1,
    ];

    /// <summary>Posiciones (0..125) de los 63 simbolos de sincronismo.</summary>
    public static int[] PosicionesDeSincronismo { get; } = Posiciones(1);

    /// <summary>Posiciones (0..125) de los 63 simbolos de datos, en orden.</summary>
    public static int[] PosicionesDeDatos { get; } = Posiciones(0);

    /// <summary>
    /// Entrelazado: el simbolo <c>i</c> de la palabra Reed-Solomon (paridad primero, mensaje al
    /// final) sale al aire en el intervalo de datos <c>EntrelazadoHaciaElAire[i]</c>.
    /// </summary>
    /// <remarks>
    /// Los 63 simbolos se escriben en una tabla de 7 filas por 9 columnas por columnas y se leen
    /// por filas: el de la posicion <c>7j + i</c> va a la <c>9i + j</c>. Reparte los simbolos
    /// consecutivos de la palabra a lo largo del minuto para que un desvanecimiento de unos
    /// segundos no se lleve seguidos mas simbolos de los que el codigo corrige.
    /// </remarks>
    public static int[] EntrelazadoHaciaElAire { get; } = Entrelazado();

    /// <summary>Inverso de <see cref="EntrelazadoHaciaElAire"/>: del intervalo de datos a la posicion en la palabra.</summary>
    public static int[] EntrelazadoDesdeElAire { get; } = Invertir(EntrelazadoHaciaElAire);

    /// <summary>Codigo de Gray de 6 bits: el simbolo <c>s</c> se emite en el tono de datos <c>Gray[s]</c>.</summary>
    /// <remarks>Gray binario reflejado: <c>g = s XOR (s >> 1)</c>. Dos tonos vecinos difieren en un solo bit.</remarks>
    public static byte[] Gray { get; } = Enumerable.Range(0, 64).Select(s => (byte)(s ^ (s >> 1))).ToArray();

    /// <summary>Inverso de <see cref="Gray"/>: del tono de datos al simbolo.</summary>
    public static byte[] GrayInverso { get; } = Invertir(Gray);

    /// <summary>
    /// Distancia, en tonos, entre el tono de sincronismo y el primer tono de datos. El tono 1
    /// no se usa: los 64 tonos de datos van del 2 al 65.
    /// </summary>
    public const int PrimerTonoDeDatos = 2;

    /// <summary>Tonos distintos que caben: sincronismo, uno vacio y 64 de datos.</summary>
    public const int TonosTotales = 66;

    private static int[] Posiciones(byte valor)
    {
        var lista = new List<int>(63);
        var s = Sincronismo;
        for (var i = 0; i < s.Length; i++) if (s[i] == valor) lista.Add(i);
        return [.. lista];
    }

    private static int[] Entrelazado()
    {
        var tabla = new int[SimbolosDeDatos];
        for (var i = 0; i < 7; i++)
            for (var j = 0; j < 9; j++)
                tabla[(7 * j) + i] = (9 * i) + j;
        return tabla;
    }

    private static int[] Invertir(int[] tabla)
    {
        var inversa = new int[tabla.Length];
        for (var i = 0; i < tabla.Length; i++) inversa[tabla[i]] = i;
        return inversa;
    }

    private static byte[] Invertir(byte[] tabla)
    {
        var inversa = new byte[tabla.Length];
        for (var i = 0; i < tabla.Length; i++) inversa[tabla[i]] = (byte)i;
        return inversa;
    }
}
