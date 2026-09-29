namespace Nodisla.Cuaderno.Modos.Jt9;

/// <summary>
/// Constantes del protocolo JT9 que no se pueden deducir: las posiciones del sincronismo, el
/// entrelazado y el codigo de Gray.
/// </summary>
/// <remarks>
/// <para>
/// <b>Procedencia.</b> Descripcion del protocolo JT9 en la guia de usuario de WSJT-X (apendice
/// «Protocol Specifications») y en J. Taylor, K1JT, «JT9: a new mode for HF» (2012). Son datos
/// del protocolo; no se ha copiado codigo. Ver <c>Tablas/LEEME-jt65-jt9.md</c>.
/// </para>
/// </remarks>
public static class TablasJt9
{
    /// <summary>Simbolos por transmision: 69 de datos y 16 de sincronismo.</summary>
    public const int Simbolos = 85;

    /// <summary>Simbolos de datos.</summary>
    public const int SimbolosDeDatos = 69;

    /// <summary>Bits que salen del codigo convolucional: (72 + 31) × 2.</summary>
    public const int BitsCodificados = 206;

    /// <summary>Tonos: el 0 es el de sincronismo y del 1 al 8 van los datos.</summary>
    public const int Tonos = 9;

    /// <summary>
    /// Posiciones (0..84) de los 16 simbolos de sincronismo, que suenan en el tono 0.
    /// </summary>
    /// <remarks>
    /// En la descripcion del protocolo se dan de 1 a 85: 1, 2, 6, 13, 22, 25, 33, 35, 50, 55,
    /// 66, 72, 79, 81, 84 y 85. Estan repartidas de forma que el patron se distinga bien de
    /// cualquier desplazamiento de si mismo.
    /// </remarks>
    public static int[] PosicionesDeSincronismo { get; } = [0, 1, 5, 12, 21, 24, 32, 34, 49, 54, 65, 71, 78, 80, 83, 84];

    /// <summary>Posiciones (0..84) de los 69 simbolos de datos, en orden.</summary>
    public static int[] PosicionesDeDatos { get; } =
        Enumerable.Range(0, Simbolos).Where(p => Array.IndexOf(PosicionesDeSincronismo, p) < 0).ToArray();

    /// <summary>1 en las posiciones de sincronismo, 0 en las de datos.</summary>
    public static byte[] Sincronismo { get; } =
        Enumerable.Range(0, Simbolos).Select(p => (byte)(Array.IndexOf(PosicionesDeSincronismo, p) < 0 ? 0 : 1)).ToArray();

    /// <summary>
    /// Entrelazado por inversion de bits: el bit codificado <c>p</c> va a la posicion
    /// <c>PosicionDeCadaBit[p]</c> de la secuencia de 206 bits que se reparte en simbolos.
    /// </summary>
    /// <remarks>
    /// Es el mismo esquema de WSPR: se recorren los numeros de 0 a 255, se invierte el orden de
    /// sus ocho bits y se van tomando, en ese orden, los que caen por debajo de 206. Separa los
    /// bits vecinos del codigo convolucional a lo largo de la transmision.
    /// </remarks>
    public static int[] PosicionDeCadaBit { get; } = Entrelazado();

    /// <summary>Codigo de Gray de 3 bits: el simbolo <c>s</c> se emite en el tono <c>1 + Gray[s]</c>.</summary>
    public static byte[] Gray { get; } = Enumerable.Range(0, 8).Select(s => (byte)(s ^ (s >> 1))).ToArray();

    /// <summary>Inverso de <see cref="Gray"/>: del tono de datos menos uno al simbolo.</summary>
    public static byte[] GrayInverso { get; } = Invertir(Gray);

    private static int[] Entrelazado()
    {
        var tabla = new int[BitsCodificados];
        var k = 0;
        for (var i = 0; i < 256 && k < BitsCodificados; i++)
        {
            var invertido = 0;
            for (var b = 0; b < 8; b++) if ((i & (1 << b)) != 0) invertido |= 1 << (7 - b);
            if (invertido < BitsCodificados) tabla[k++] = invertido;
        }
        return tabla;
    }

    private static byte[] Invertir(byte[] tabla)
    {
        var inversa = new byte[tabla.Length];
        for (var i = 0; i < tabla.Length; i++) inversa[tabla[i]] = (byte)i;
        return inversa;
    }
}

/// <summary>
/// Las cifras que definen JT9.
/// </summary>
/// <remarks>
/// JT9 se disenó a 12000 muestras por segundo con simbolos de 6912 muestras (0,576 s), de
/// donde sale el espaciado de tonos de 1,736 Hz; con nueve tonos la senal ocupa 15,6 Hz, una
/// decima parte de JT65. La transmision son 85 simbolos, 48,96 s, y empieza en el segundo 1.
/// </remarks>
public static class ParametrosJt9
{
    /// <summary>Frecuencia de analisis, en muestras por segundo.</summary>
    public const int FrecuenciaDeAnalisis = 12000;

    /// <summary>Muestras por simbolo a la frecuencia de analisis.</summary>
    public const int MuestrasPorSimbolo = 6912;

    /// <summary>Espaciado entre tonos, en hercios.</summary>
    public const double EspaciadoDeTonosHz = (double)FrecuenciaDeAnalisis / MuestrasPorSimbolo;

    /// <summary>Duracion de un simbolo, en segundos.</summary>
    public const double DuracionDeSimboloSegundos = (double)MuestrasPorSimbolo / FrecuenciaDeAnalisis;

    /// <summary>Ventana de transmision, en segundos.</summary>
    public const double PeriodoSegundos = 60;

    /// <summary>Segundo del minuto en el que empieza la transmision.</summary>
    public const double ComienzoNominalSegundos = 1.0;

    /// <summary>Ancho de banda de la senal, en hercios.</summary>
    public const double AnchoDeBandaHz = TablasJt9.Tonos * EspaciadoDeTonosHz;

    /// <summary>Muestras que dura la senal.</summary>
    public const int MuestrasDeLaSenal = TablasJt9.Simbolos * MuestrasPorSimbolo;

    /// <summary>Duracion de la senal, en segundos.</summary>
    public const double DuracionDeLaSenalSegundos = TablasJt9.Simbolos * DuracionDeSimboloSegundos;

    /// <summary>Muestras de una ventana entera.</summary>
    public const int MuestrasDeLaVentana = (int)(PeriodoSegundos * FrecuenciaDeAnalisis);
}
