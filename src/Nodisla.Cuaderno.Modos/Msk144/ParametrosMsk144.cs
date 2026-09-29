namespace Nodisla.Cuaderno.Modos.Msk144;

/// <summary>
/// Las constantes del protocolo MSK144.
/// </summary>
/// <remarks>
/// <para>
/// <b>Que es MSK144, para quien no lo haya usado.</b> Es el modo de dispersion meteorica: un
/// meteoro deja una estela ionizada que refleja VHF durante unas decimas de segundo, y en ese
/// «ping» hay que meter el mensaje entero. Por eso MSK144 es rapidisimo: 2000 bits por segundo,
/// tramas de 72 milisegundos, y la misma trama repetida sin parar durante todo el periodo, para
/// que cualquier ping, caiga cuando caiga, contenga al menos una trama completa.
/// </para>
/// <para>
/// <b>La modulacion.</b> MSK es FSK de fase continua con dos tonos separados la mitad de la
/// velocidad de bit: 1000 y 2000 Hz a 2000 baudios, portadora en 1500 Hz. Vista de otra manera,
/// es OQPSK con pulsos de medio seno: los bits pares van por el canal en cuadratura y los
/// impares por el en fase, cada uno como un medio seno de un milisegundo, y los dos canales
/// desplazados medio pulso. Esa vista es la que usa el demodulador, porque permite demodular
/// coherentemente y sumar varias tramas en fase.
/// </para>
/// <para>
/// <b>La trama.</b> 77 bits de mensaje —los mismos que FT8— mas un CRC de 13 dan 90; el codigo
/// LDPC(128,90) los deja en 128; y dos palabras de sincronismo de 8 bits completan los 144:
/// sincronismo, 48 bits de datos, sincronismo, 80 bits de datos.
/// </para>
/// <para>
/// Fuente: S. Franke (K9AN) y J. Taylor (K1JT), «The MSK144 Protocol for Meteor-Scatter
/// Communication», QEX julio/agosto 2017, y las constantes del protocolo con su procedencia en
/// <c>Tablas/tablas-msk144.txt</c> y <c>Tablas/tablas-msk40.txt</c>.
/// </para>
/// </remarks>
public static class ParametrosMsk144
{
    /// <summary>Bits por segundo.</summary>
    public const int Baudios = 2000;

    /// <summary>Portadora, en hercios. Los tonos quedan en 1000 y 2000 Hz.</summary>
    public const double PortadoraHz = 1500;

    /// <summary>
    /// Muestras por segundo con las que trabaja el decodificador.
    /// </summary>
    /// <remarks>
    /// Doce mil da seis muestras por bit y doce por pulso, y la portadora cae en un octavo
    /// exacto de la frecuencia de muestreo, asi que bajar la senal a banda base es una tabla de
    /// ocho senos y cosenos.
    /// </remarks>
    public const int FrecuenciaDeAnalisis = 12000;

    /// <summary>Muestras por bit a la frecuencia de analisis.</summary>
    public const int MuestrasPorBit = FrecuenciaDeAnalisis / Baudios;

    /// <summary>Bits de una trama: 8 + 48 + 8 + 80.</summary>
    public const int BitsPorTrama = 144;

    /// <summary>Muestras de una trama a la frecuencia de analisis: 72 ms.</summary>
    public const int MuestrasPorTrama = BitsPorTrama * MuestrasPorBit;

    /// <summary>Duracion de una trama, en segundos.</summary>
    public const double DuracionDeTramaSegundos = (double)BitsPorTrama / Baudios;

    /// <summary>Bits de mensaje empaquetado, los mismos 77 de FT8.</summary>
    public const int BitsDeMensaje = 77;

    /// <summary>Bits del mensaje mas su CRC de 13: los que entran al codigo corrector.</summary>
    public const int BitsConCrc = 90;

    /// <summary>Bits de la palabra de codigo.</summary>
    public const int BitsDePalabra = 128;

    /// <summary>Bits de cada palabra de sincronismo.</summary>
    public const int BitsDeSincronismo = 8;

    /// <summary>Periodo de transmision por omision, en segundos.</summary>
    public const double PeriodoSegundos = 15.0;

    /// <summary>
    /// La palabra de sincronismo de 8 bits, tal y como la fija el protocolo.
    /// </summary>
    /// <remarks>
    /// Aparece dos veces en cada trama: en los bits 0 a 7 y en los 56 a 63. Fuente: constantes
    /// publicadas del protocolo (WSJT-X, <c>genmsk_128_90</c>, dato <c>s8</c>); solo el dato.
    /// </remarks>
    public static ReadOnlySpan<byte> Sincronismo => [0, 1, 1, 1, 0, 0, 1, 0];

    /// <summary>Bit de la trama donde empieza la primera palabra de sincronismo.</summary>
    public const int PrimerSincronismo = 0;

    /// <summary>Bit de la trama donde empieza la segunda palabra de sincronismo.</summary>
    public const int SegundoSincronismo = 56;

    /// <summary>Bits de datos entre las dos palabras de sincronismo.</summary>
    public const int BitsDelPrimerTramo = 48;

    /// <summary>Bits de datos despues de la segunda palabra de sincronismo.</summary>
    public const int BitsDelSegundoTramo = 80;

    /// <summary>Bits de una trama corta: 8 de sincronismo y 32 de codigo.</summary>
    public const int BitsPorTramaCorta = 40;

    /// <summary>Muestras de una trama corta: 20 ms.</summary>
    public const int MuestrasPorTramaCorta = BitsPorTramaCorta * MuestrasPorBit;

    /// <summary>Bits de mensaje de la trama corta: 12 de resumen y 4 de informe.</summary>
    public const int BitsDeMensajeCorto = 16;

    /// <summary>Bits de la palabra de codigo corta.</summary>
    public const int BitsDePalabraCorta = 32;

    /// <summary>
    /// La palabra de sincronismo de la trama corta, que es la larga al reves.
    /// </summary>
    public static ReadOnlySpan<byte> SincronismoCorto => [0, 1, 0, 0, 1, 1, 1, 0];

    /// <summary>Nombre del fichero de la tabla LDPC(128,90).</summary>
    public const string FicheroDeTablas = "tablas-msk144.txt";

    /// <summary>Nombre del fichero de la tabla LDPC(32,16) de los mensajes cortos.</summary>
    public const string FicheroDeTablasCortas = "tablas-msk40.txt";

    /// <summary>Dice si el bit <paramref name="posicion"/> de la trama larga es de sincronismo.</summary>
    public static bool EsBitDeSincronismo(int posicion) =>
        (posicion >= PrimerSincronismo && posicion < PrimerSincronismo + BitsDeSincronismo) ||
        (posicion >= SegundoSincronismo && posicion < SegundoSincronismo + BitsDeSincronismo);

    /// <summary>
    /// Posicion en la trama de cada uno de los 128 bits de la palabra de codigo.
    /// </summary>
    public static int PosicionEnLaTrama(int bitDePalabra) =>
        bitDePalabra < BitsDelPrimerTramo
            ? BitsDeSincronismo + bitDePalabra
            : (2 * BitsDeSincronismo) + bitDePalabra;
}
