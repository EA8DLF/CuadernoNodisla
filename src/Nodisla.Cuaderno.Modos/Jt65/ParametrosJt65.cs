namespace Nodisla.Cuaderno.Modos.Jt65;

/// <summary>Submodos de JT65: mismo protocolo, tonos mas o menos separados.</summary>
public enum SubmodoJt65
{
    /// <summary>2,69 Hz entre tonos, 178 Hz de ancho. El de HF.</summary>
    A = 1,
    /// <summary>5,38 Hz entre tonos, 355 Hz de ancho. El de EME en 2 m y 70 cm.</summary>
    B = 2,
    /// <summary>10,77 Hz entre tonos, 711 Hz de ancho. Para bandas altas con mucha deriva.</summary>
    C = 4,
}

/// <summary>
/// Las cifras que definen JT65 y que usan por igual el generador y el decodificador.
/// </summary>
/// <remarks>
/// <para>
/// JT65 se disenó a 11025 muestras por segundo con simbolos de 4096 muestras: de ahi salen
/// el espaciado de tonos (11025/4096 = 2,6917 Hz, la inversa de la duracion del simbolo) y los
/// 46,8 segundos de la transmision (126 × 0,3715 s). El decodificador analiza el audio a esa
/// misma frecuencia para que un simbolo sea exactamente una transformada de 4096 puntos y cada
/// tono caiga justo en una casilla.
/// </para>
/// <para>
/// La transmision empieza en el segundo 1 del minuto y acaba en el 47,8; el resto del minuto
/// queda para que el decodificador trabaje y el operador conteste.
/// </para>
/// </remarks>
public sealed class ParametrosJt65
{
    private ParametrosJt65(SubmodoJt65 submodo)
    {
        Submodo = submodo;
        EspaciadoEnCasillas = (int)submodo;
    }

    /// <summary>Submodo.</summary>
    public SubmodoJt65 Submodo { get; }

    /// <summary>Frecuencia de analisis, en muestras por segundo.</summary>
    public const int FrecuenciaDeAnalisis = 11025;

    /// <summary>Muestras por simbolo a la frecuencia de analisis.</summary>
    public const int MuestrasPorSimbolo = 4096;

    /// <summary>Espaciado de tonos del submodo A, en hercios, que es tambien la anchura de una casilla de la transformada.</summary>
    public const double CasillaHz = (double)FrecuenciaDeAnalisis / MuestrasPorSimbolo;

    /// <summary>Duracion de un simbolo, en segundos.</summary>
    public const double DuracionDeSimboloSegundos = (double)MuestrasPorSimbolo / FrecuenciaDeAnalisis;

    /// <summary>Ventana de transmision, en segundos.</summary>
    public const double PeriodoSegundos = 60;

    /// <summary>Segundo del minuto en el que empieza la transmision.</summary>
    public const double ComienzoNominalSegundos = 1.0;

    /// <summary>Casillas de la transformada entre dos tonos vecinos: 1, 2 o 4 segun el submodo.</summary>
    public int EspaciadoEnCasillas { get; }

    /// <summary>Espaciado entre tonos, en hercios.</summary>
    public double EspaciadoDeTonosHz => EspaciadoEnCasillas * CasillaHz;

    /// <summary>Ancho de banda que ocupa la senal, en hercios.</summary>
    public double AnchoDeBandaHz => TablasJt65.TonosTotales * EspaciadoDeTonosHz;

    /// <summary>Muestras que dura la senal a la frecuencia de analisis.</summary>
    public int MuestrasDeLaSenal => TablasJt65.Simbolos * MuestrasPorSimbolo;

    /// <summary>Duracion de la senal, en segundos.</summary>
    public double DuracionDeLaSenalSegundos => TablasJt65.Simbolos * DuracionDeSimboloSegundos;

    /// <summary>Muestras de una ventana entera a la frecuencia de analisis.</summary>
    public int MuestrasDeLaVentana => (int)Math.Round(PeriodoSegundos * FrecuenciaDeAnalisis);

    /// <summary>JT65A.</summary>
    public static ParametrosJt65 A { get; } = new(SubmodoJt65.A);

    /// <summary>JT65B.</summary>
    public static ParametrosJt65 B { get; } = new(SubmodoJt65.B);

    /// <summary>JT65C.</summary>
    public static ParametrosJt65 C { get; } = new(SubmodoJt65.C);

    /// <summary>Los parametros del submodo pedido.</summary>
    public static ParametrosJt65 De(SubmodoJt65 submodo) => submodo switch
    {
        SubmodoJt65.A => A,
        SubmodoJt65.B => B,
        SubmodoJt65.C => C,
        _ => throw new ArgumentOutOfRangeException(nameof(submodo)),
    };
}
