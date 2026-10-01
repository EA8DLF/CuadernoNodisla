namespace Nodisla.Cuaderno.Modos.Wspr;

/// <summary>
/// Las constantes de la forma de onda de WSPR.
/// </summary>
/// <remarks>
/// <para>
/// WSPR es lento a proposito. Cada simbolo dura 8192/12000 segundos (0,683 s) y los cuatro
/// tonos estan separados justo la inversa de eso, 1,4648 Hz, con lo que la senal entera cabe
/// en 6 hercios. Los 162 simbolos duran 110,6 segundos y se emiten en una ventana de dos
/// minutos, empezando un segundo despues del minuto par. A cambio de esa lentitud, la energia
/// de cada bit es enorme comparada con el ruido que cabe en 1,5 Hz, y por eso una baliza de
/// medio vatio se oye al otro lado del mundo.
/// </para>
/// <para>
/// <b>Como se analiza.</b> El audio llega a 12000 muestras por segundo, que es lo que graban
/// todos los programas de WSPR y lo que hace que un simbolo sean 8192 muestras justas. La banda
/// de WSPR son 200 Hz alrededor de 1500 Hz, asi que lo primero es trasladar esos 1500 Hz a cero,
/// quedarse con ±187,5 Hz y bajar a 375 muestras por segundo complejas: un simbolo son entonces
/// 256 muestras y la resolucion de una transformada de 512 puntos es media separacion de tonos.
/// Para mirar de cerca una candidata se baja otra vez, a 23,4375 muestras por segundo, donde un
/// simbolo son 16 muestras y cada tono cae en media casilla de una transformada de 16 puntos.
/// </para>
/// </remarks>
public static class ParametrosWspr
{
    /// <summary>Muestras por segundo a las que se analiza el audio.</summary>
    public const int FrecuenciaDeAnalisis = 12000;

    /// <summary>Muestras de un simbolo a la frecuencia de analisis.</summary>
    public const int MuestrasPorSimboloDeAudio = 8192;

    /// <summary>Simbolos de una transmision.</summary>
    public const int Simbolos = Tablas.TablasWspr.Simbolos;

    /// <summary>Separacion entre tonos, que es tambien la inversa de la duracion del simbolo.</summary>
    public const double EspaciadoDeTonosHz = (double)FrecuenciaDeAnalisis / MuestrasPorSimboloDeAudio;

    /// <summary>Duracion de un simbolo, en segundos.</summary>
    public const double DuracionDeSimboloSegundos = (double)MuestrasPorSimboloDeAudio / FrecuenciaDeAnalisis;

    /// <summary>Duracion de la senal completa, en segundos.</summary>
    public const double DuracionDeLaSenalSegundos = Simbolos * DuracionDeSimboloSegundos;

    /// <summary>Duracion de la ventana: dos minutos.</summary>
    public const double PeriodoSegundos = 120.0;

    /// <summary>Segundo de la ventana en que empieza la senal por convenio.</summary>
    public const double ComienzoNominalSegundos = 1.0;

    /// <summary>Centro de la banda de WSPR dentro del audio.</summary>
    public const double CentroDeLaBandaHz = 1500.0;

    /// <summary>Media anchura de la banda que se busca: 1400 a 1600 Hz, con un poco de margen.</summary>
    public const double MediaAnchuraDeBusquedaHz = 110.0;

    /// <summary>Muestras por segundo de la banda base compleja donde se buscan candidatas.</summary>
    public const double FrecuenciaDeBandaBase = 375.0;

    /// <summary>Muestras por simbolo en la banda base.</summary>
    public const int MuestrasPorSimboloDeBandaBase = 256;

    /// <summary>Muestras por segundo de la senal estrecha con la que se demodula una candidata.</summary>
    public const double FrecuenciaEstrecha = FrecuenciaDeBandaBase / 16;

    /// <summary>Muestras por simbolo en la senal estrecha.</summary>
    public const int MuestrasPorSimboloEstrecho = 16;

    /// <summary>Ancho de banda de referencia de los informes de relacion senal-ruido.</summary>
    public const double AnchoDeBandaDeReferencia = 2500.0;

    /// <summary>
    /// Posicion en la transmision de cada bit codificado.
    /// </summary>
    /// <remarks>
    /// El entrelazado es por inversion de bits: se recorren los numeros de 0 a 255, se les da la
    /// vuelta a sus ocho bits, y los que caen por debajo de 162 dicen, en orden, a que simbolo
    /// va cada bit codificado. Asi un chasquido que se lleve varios simbolos seguidos reparte
    /// sus errores por todo el mensaje, que es lo que mejor sabe arreglar el codigo.
    /// </remarks>
    public static ReadOnlySpan<byte> PosicionDeCadaBit => Entrelazado;

    private static readonly byte[] Entrelazado = CalcularEntrelazado();

    private static byte[] CalcularEntrelazado()
    {
        var posiciones = new byte[Simbolos];
        var p = 0;
        for (var i = 0; i < 256 && p < Simbolos; i++)
        {
            var j = 0;
            for (var bit = 0; bit < 8; bit++)
                if ((i & (1 << bit)) != 0) j |= 1 << (7 - bit);
            if (j < Simbolos) posiciones[p++] = (byte)j;
        }
        return posiciones;
    }
}
