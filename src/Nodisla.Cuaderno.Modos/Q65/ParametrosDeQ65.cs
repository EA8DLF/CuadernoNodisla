namespace Nodisla.Cuaderno.Modos.Q65;

/// <summary>Submodo de Q65: cuanto se separan los tonos respecto a la velocidad de simbolo.</summary>
public enum SubmodoDeQ65
{
    /// <summary>Separacion igual a la velocidad de simbolo. El de HF y VHF terrestre.</summary>
    A,
    /// <summary>Separacion doble.</summary>
    B,
    /// <summary>Separacion cuadruple.</summary>
    C,
    /// <summary>Separacion por ocho.</summary>
    D,
    /// <summary>Separacion por dieciseis. Para dispersion muy ancha en microondas.</summary>
    E,
}

/// <summary>
/// Los numeros de un submodo y periodo de Q65.
/// </summary>
/// <remarks>
/// <para>
/// Q65 tiene cinco periodos (15, 30, 60, 120 y 300 segundos) y cinco submodos (A a E). El
/// periodo fija la duracion del simbolo, y con ella la velocidad de simbolo; el submodo
/// multiplica la separacion entre tonos por 1, 2, 4, 8 o 16 sin tocar la duracion. Cuanto mas
/// separados los tonos, mas dispersion Doppler aguanta la senal y mas ancho de banda ocupa.
/// </para>
/// <para>
/// El audio se analiza a 6000 muestras por segundo: es la mitad de la frecuencia con la que se
/// definio el protocolo, las duraciones de simbolo siguen siendo un numero entero de muestras
/// y cubre de sobra la banda de audio del equipo.
/// </para>
/// </remarks>
public sealed class ParametrosDeQ65
{
    /// <summary>Muestras por segundo a las que se analiza.</summary>
    public const int FrecuenciaDeAnalisis = 6000;

    /// <summary>Frecuencia mas baja en la que se busca el tono base.</summary>
    public const double FrecuenciaMinimaHz = 200;

    /// <summary>Frecuencia mas alta que puede ocupar el tono mas agudo.</summary>
    public const double FrecuenciaMaximaHz = 2950;

    /// <summary>Columnas del espectrograma por simbolo.</summary>
    public const int ColumnasPorSimbolo = 4;

    /// <summary>Casillas de frecuencia por tono del submodo A.</summary>
    public const int CasillasPorTonoBase = 2;

    private static readonly int[] PeriodosValidos = [15, 30, 60, 120, 300];
    private static readonly int[] MuestrasPorSimboloPorPeriodo = [900, 1800, 3600, 8000, 20736];

    private ParametrosDeQ65(int periodoSegundos, SubmodoDeQ65 submodo)
    {
        PeriodoSegundos = periodoSegundos;
        Submodo = submodo;
        MuestrasPorSimbolo = MuestrasPorSimboloPorPeriodo[Array.IndexOf(PeriodosValidos, periodoSegundos)];
        if (FrecuenciaMinimaHz + AnchoDeBandaHz > FrecuenciaMaximaHz)
            throw new ArgumentException($"{Nombre} ocupa {AnchoDeBandaHz:0} Hz y no cabe en la banda de audio.", nameof(submodo));
    }

    /// <summary>Periodo de transmision, en segundos.</summary>
    public int PeriodoSegundos { get; }

    /// <summary>Submodo.</summary>
    public SubmodoDeQ65 Submodo { get; }

    /// <summary>Nombre corriente del modo, por ejemplo <c>Q65-60A</c>.</summary>
    public string Nombre => $"Q65-{PeriodoSegundos}{Submodo}";

    /// <summary>Muestras que dura un simbolo a la frecuencia de analisis.</summary>
    public int MuestrasPorSimbolo { get; }

    /// <summary>Duracion de un simbolo, en segundos.</summary>
    public double DuracionDeSimboloSegundos => (double)MuestrasPorSimbolo / FrecuenciaDeAnalisis;

    /// <summary>Simbolos por segundo, que es tambien la separacion de tonos del submodo A.</summary>
    public double Baudios => (double)FrecuenciaDeAnalisis / MuestrasPorSimbolo;

    /// <summary>Por cuanto multiplica el submodo la separacion de tonos.</summary>
    public int Multiplicador => 1 << (int)Submodo;

    /// <summary>Separacion entre tonos, en hercios.</summary>
    public double EspaciadoDeTonosHz => Baudios * Multiplicador;

    /// <summary>Ancho de banda de la senal: 65 tonos.</summary>
    public double AnchoDeBandaHz => MensajeDeQ65.Tonos * EspaciadoDeTonosHz;

    /// <summary>Duracion de la trama de 85 simbolos.</summary>
    public double DuracionDeLaSenalSegundos => TablasDeQ65.SimbolosDeLaTrama * DuracionDeSimboloSegundos;

    /// <summary>
    /// Segundos despues del comienzo del periodo en que arranca la senal: medio segundo en los
    /// periodos cortos y uno en los largos, como manda el protocolo.
    /// </summary>
    public double ComienzoNominalSegundos => PeriodoSegundos <= 30 ? 0.5 : 1.0;

    /// <summary>Muestras de una ventana entera.</summary>
    public int MuestrasDeLaVentana => PeriodoSegundos * FrecuenciaDeAnalisis;

    /// <summary>Muestras entre dos columnas del espectrograma: un cuarto de simbolo.</summary>
    public int Salto => MuestrasPorSimbolo / ColumnasPorSimbolo;

    /// <summary>Puntos de la transformada: el doble del simbolo, para tener media casilla por tono.</summary>
    public int PuntosDeLaTransformada => 2 * MuestrasPorSimbolo;

    /// <summary>Hercios por casilla del espectrograma: la mitad de la velocidad de simbolo.</summary>
    public double HzPorCasilla => Baudios / CasillasPorTonoBase;

    /// <summary>Casillas entre dos tonos consecutivos del submodo.</summary>
    public int CasillasPorTono => CasillasPorTonoBase * Multiplicador;

    /// <summary>Frecuencia mas alta que puede tener el tono base.</summary>
    public double FrecuenciaMaximaDelTonoBaseHz => FrecuenciaMaximaHz - AnchoDeBandaHz;

    /// <summary>Desfase mas temprano que se busca, en segundos.</summary>
    public double DesfaseMinimoSegundos => -1.0;

    /// <summary>Desfase mas tardio que se busca: hasta donde quepa la trama entera en la ventana.</summary>
    public double DesfaseMaximoSegundos => PeriodoSegundos - DuracionDeLaSenalSegundos - ComienzoNominalSegundos;

    /// <summary>Los parametros del periodo y submodo indicados.</summary>
    /// <exception cref="ArgumentException">Si el periodo no existe o el submodo no cabe en la banda.</exception>
    public static ParametrosDeQ65 De(int periodoSegundos, SubmodoDeQ65 submodo)
    {
        if (!PeriodosValidos.Contains(periodoSegundos))
            throw new ArgumentException("Los periodos de Q65 son 15, 30, 60, 120 y 300 segundos.", nameof(periodoSegundos));
        if (!Enum.IsDefined(submodo)) throw new ArgumentException("Submodo desconocido.", nameof(submodo));
        return new ParametrosDeQ65(periodoSegundos, submodo);
    }
}
