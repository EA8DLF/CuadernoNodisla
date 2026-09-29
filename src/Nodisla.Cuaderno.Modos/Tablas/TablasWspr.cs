namespace Nodisla.Cuaderno.Modos.Tablas;

/// <summary>
/// Las constantes del protocolo WSPR que no se pueden deducir: el vector de sincronismo, los
/// polinomios del codigo convolucional y la tabla de potencias.
/// </summary>
/// <remarks>
/// <para>
/// <b>Procedencia.</b> Todo lo que hay aqui esta publicado como especificacion del protocolo,
/// no como codigo: Joe Taylor, K1JT, <i>WSPR 2.0 User's Guide</i>, apendice «Protocol
/// specification» (2010), y la descripcion del proceso de codificacion de Andy Talbot, G4JNT,
/// <i>The WSPR Coding Process</i> (2009). Son constantes del estandar de facto, como el
/// polinomio de un CRC: o se tienen las mismas que todo el mundo o no se decodifica a nadie.
/// </para>
/// <para>
/// <b>No hay ni una linea de WSJT-X aqui.</b> WSJT-X es GPLv3 y no se ha copiado ni traducido
/// nada de el. El codificador, el entrelazador, el sincronizador, el demodulador y el
/// decodificador secuencial de Fano de este modem estan escritos desde cero en C# a partir de
/// la descripcion publicada.
/// </para>
/// <para>
/// Van en un fichero de codigo y no en uno de datos, a diferencia de la tabla LDPC de FT8, porque
/// son poco mas de doscientos numeros publicados en el propio manual del protocolo y no traen
/// aviso de licencia que tenga que viajar con ellos.
/// </para>
/// </remarks>
public static class TablasWspr
{
    /// <summary>
    /// Vector de sincronismo: un bit por cada uno de los 162 simbolos de la transmision.
    /// </summary>
    /// <remarks>
    /// Es una secuencia seudoaleatoria con buenas propiedades de autocorrelacion, elegida por el
    /// disenador del protocolo. Cada simbolo emitido lleva este bit en la posicion baja del tono
    /// (tono = sincronismo + 2 × dato), asi que la mitad de la energia de la senal se gasta en
    /// que el receptor pueda encontrarla en tiempo y en frecuencia. Tiene 63 unos y 99 ceros.
    /// Fuente: WSPR 2.0 User's Guide, apendice de especificacion del protocolo.
    /// </remarks>
    public static ReadOnlySpan<byte> VectorDeSincronismo =>
    [
        1, 1, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 1, 1, 1, 0, 0, 0, 1, 0, 0, 1, 0, 1, 1, 1, 1, 0, 0, 0,
        0, 0, 0, 0, 1, 0, 0, 1, 0, 1, 0, 0, 0, 0, 0, 0, 1, 0, 1, 1, 0, 0, 1, 1, 0, 1, 0, 0, 0, 1,
        1, 0, 1, 0, 0, 0, 0, 1, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 0, 1, 0, 0, 1, 0, 1, 1, 0, 0, 0, 1,
        1, 0, 1, 0, 1, 0, 0, 0, 1, 0, 0, 0, 0, 0, 1, 0, 0, 1, 0, 0, 1, 1, 1, 0, 1, 1, 0, 0, 1, 1,
        0, 1, 0, 0, 0, 1, 1, 1, 0, 0, 0, 0, 0, 1, 0, 1, 0, 0, 1, 1, 0, 0, 0, 0, 0, 0, 0, 1, 1, 0,
        1, 0, 1, 1, 0, 0, 0, 1, 1, 0, 0, 0,
    ];

    /// <summary>Simbolos de una transmision: los 162 del vector de sincronismo.</summary>
    public const int Simbolos = 162;

    /// <summary>
    /// Primer polinomio del codigo convolucional de longitud de restriccion 32 y tasa 1/2.
    /// </summary>
    /// <remarks>
    /// Los dos polinomios son los del codigo «Layland-Lushbaugh» de longitud de restriccion 32,
    /// que ademas de WSPR usa JT9 (y el JT4). Cada bit de entrada se mete por la parte baja de un
    /// registro de 32 bits y cada polinomio saca un bit: la paridad del registro enmascarado con
    /// el polinomio. Fuente: WSPR 2.0 User's Guide y G4JNT, <i>The WSPR Coding Process</i>.
    /// </remarks>
    public const uint PolinomioA = 0xF2D05351;

    /// <summary>Segundo polinomio del mismo codigo. Ver <see cref="PolinomioA"/>.</summary>
    public const uint PolinomioB = 0xE4613C47;

    /// <summary>Longitud de restriccion del codigo: bits que ve cada bit de salida.</summary>
    public const int LongitudDeRestriccion = 32;

    /// <summary>
    /// Potencias que admite el protocolo, en dBm.
    /// </summary>
    /// <remarks>
    /// Solo valen las que acaban en 0, 3 o 7, que son las aproximaciones enteras de 1, 2 y 5
    /// vatios por decada. El campo de potencia tiene sitio para 0 a 63, pero el protocolo se
    /// apoya en que solo se usen estas: en los mensajes de tipo 2 el resto de valores del campo
    /// es lo que indica que el indicativo lleva prefijo o sufijo. Un mensaje decodificado con
    /// una potencia que no este aqui es basura y se rechaza.
    /// </remarks>
    public static ReadOnlySpan<byte> PotenciasValidasDbm =>
        [0, 3, 7, 10, 13, 17, 20, 23, 27, 30, 33, 37, 40, 43, 47, 50, 53, 57, 60];

    /// <summary>Dice si una potencia en dBm es de las que admite el protocolo.</summary>
    public static bool EsPotenciaValida(int dbm) => dbm is >= 0 and <= 60 && (dbm % 10) is 0 or 3 or 7;

    /// <summary>
    /// Constante de inicializacion del resumen de indicativos compuestos.
    /// </summary>
    /// <remarks>
    /// Los mensajes de tipo 3 no llevan el indicativo entero sino un resumen de 15 bits, que se
    /// calcula con la funcion <i>lookup3</i> de Bob Jenkins (dominio publico) sobre los
    /// caracteres del indicativo, con este valor inicial, y quedandose con los 15 bits bajos.
    /// Fuente: WSPR 2.0 User's Guide, mensajes de tipo 3.
    /// </remarks>
    public const uint SemillaDelResumen = 146;
}
