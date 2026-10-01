namespace Nodisla.Cuaderno.Modos.Fst4;

/// <summary>
/// Las constantes de la forma de onda de FST4 y FST4W, para cada periodo.
/// </summary>
/// <remarks>
/// <para>
/// <b>Que es FST4, para quien no lo haya usado.</b> Es el modo lento de las bandas bajas (2200
/// y 630 metros), pensado para sacar senales que estan treinta y tantos decibelios por debajo
/// del ruido a base de emitir muy despacio y muy estrecho. Los siete periodos —de 15 segundos
/// a media hora— son la misma trama de 160 simbolos con distinta velocidad: cuanto mas largo el
/// periodo, mas dura cada simbolo, mas estrechos son los tonos y mas sensible es el modo.
/// FST4W es la variante baliza, con mensajes tipo WSPR de 50 bits, solo en los periodos de
/// 120 segundos en adelante.
/// </para>
/// <para>
/// <b>La trama.</b> 4-GFSK: 120 simbolos de dos bits llevan los 240 bits del codigo LDPC, y
/// cinco grupos de ocho simbolos de sincronismo —dos patrones de Costas 4×4 encadenados,
/// alternando S1 y S2— van al principio, al final y cada 30 simbolos de datos:
/// S1 D30 S2 D30 S1 D30 S2 D30 S1.
/// </para>
/// <para>
/// <b>La velocidad de cada periodo</b> se fija en muestras por simbolo a 12000 muestras por
/// segundo: 720, 1680, 3888, 8200, 21504, 66560 y 134400. Son constantes del protocolo. Para
/// analizar, el audio se remuestrea a una frecuencia en la que el simbolo cae en una potencia
/// de dos, por la misma razon que en FT8: que los tonos caigan justo en las casillas de la
/// transformada.
/// </para>
/// <para>
/// Fuente: Franke, Somerville y Taylor, «Quick-Start Guide to FST4 and FST4W» (apendice A) y
/// «The FT4 and FT8 Communications Protocols», QEX julio/agosto 2020; constantes del codigo en
/// <c>Tablas/tablas-fst4.txt</c> y <c>Tablas/tablas-fst4w.txt</c>.
/// </para>
/// </remarks>
public sealed class ParametrosFst4
{
    private static readonly int[] PeriodosAdmitidos = [15, 30, 60, 120, 300, 900, 1800];
    private static readonly int[] MuestrasPorSimboloDelProtocolo = [720, 1680, 3888, 8200, 21504, 66560, 134400];
    private static readonly int[] MuestrasPorSimboloDeAnalisisPorPeriodo = [512, 1024, 4096, 8192, 16384, 65536, 131072];
    private static readonly Dictionary<int, ParametrosFst4> Cache = [];

    private int[]? _posicionesDeDatos;

    private ParametrosFst4(int periodoSegundos, int muestrasPorSimboloA12000, int muestrasPorSimboloDeAnalisis)
    {
        PeriodoSegundos = periodoSegundos;
        MuestrasPorSimboloA12000 = muestrasPorSimboloA12000;
        MuestrasPorSimboloDeAnalisis = muestrasPorSimboloDeAnalisis;
    }

    /// <summary>Frecuencia de muestreo en la que se definen las velocidades del protocolo.</summary>
    public const int FrecuenciaDelProtocolo = 12000;

    /// <summary>Simbolos por trama, contando el sincronismo.</summary>
    public const int SimbolosTotales = 160;

    /// <summary>Simbolos que llevan los 240 bits del codigo.</summary>
    public const int SimbolosDeDatos = 120;

    /// <summary>Tonos distintos.</summary>
    public const int Tonos = 4;

    /// <summary>Bits por simbolo.</summary>
    public const int BitsPorSimbolo = 2;

    /// <summary>Bits de la palabra de codigo.</summary>
    public const int BitsDePalabra = 240;

    /// <summary>Bits de mensaje mas CRC en FST4: 77 + 24.</summary>
    public const int BitsConCrcFst4 = 101;

    /// <summary>Bits de mensaje mas CRC en FST4W: 50 + 24.</summary>
    public const int BitsConCrcFst4w = 74;

    /// <summary>Parametro de ancho de banda del filtro gaussiano de la modulacion.</summary>
    public const double AnchoDeBandaDelFiltro = 2.0;

    /// <summary>Primer patron de sincronismo, dos Costas 4×4 encadenados.</summary>
    public static ReadOnlySpan<byte> SincronismoUno => [0, 1, 3, 2, 1, 0, 2, 3];

    /// <summary>Segundo patron de sincronismo.</summary>
    public static ReadOnlySpan<byte> SincronismoDos => [2, 3, 1, 0, 3, 2, 0, 1];

    /// <summary>Simbolo en que empieza cada grupo de sincronismo.</summary>
    public static ReadOnlySpan<int> PosicionesDeSincronismo => [0, 38, 76, 114, 152];

    /// <summary>Simbolos de cada grupo de sincronismo.</summary>
    public const int SimbolosPorGrupoDeSincronismo = 8;

    /// <summary>Traduccion de valor de dos bits a tono: codigo de Gray.</summary>
    public static ReadOnlySpan<byte> MapaDeGray => [0, 1, 3, 2];

    /// <summary>Traduccion de tono a valor de dos bits.</summary>
    public static ReadOnlySpan<byte> MapaDeGrayInverso => [0, 1, 3, 2];

    /// <summary>Nombre del fichero de la tabla LDPC(240,101) de FST4.</summary>
    public const string FicheroDeTablas = "tablas-fst4.txt";

    /// <summary>Nombre del fichero de la tabla LDPC(240,74) de FST4W.</summary>
    public const string FicheroDeTablasFst4w = "tablas-fst4w.txt";

    /// <summary>
    /// La secuencia con la que FST4 revuelve los 77 bits antes del CRC, para que un CQ no sea
    /// una tira de ceros. Es la misma de FT4. Fuente: Quick-Start Guide, apendice A.
    /// </summary>
    public static ReadOnlySpan<byte> Mezcla =>
    [
        0, 1, 0, 0, 1, 0, 1, 0, 0, 1, 0, 1, 1, 1, 1, 0, 1, 0, 0, 0, 1, 0, 0, 1, 1, 0, 1, 1, 0, 1, 0, 0,
        1, 0, 1, 1, 0, 0, 0, 0, 1, 0, 0, 0, 1, 0, 1, 0, 0, 1, 1, 1, 1, 0, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1,
        1, 0, 1, 1, 1, 1, 1, 0, 0, 0, 1, 0, 1,
    ];

    /// <summary>Duracion del periodo, en segundos.</summary>
    public int PeriodoSegundos { get; }

    /// <summary>Muestras por simbolo a 12000 muestras por segundo, que es como el protocolo fija la velocidad.</summary>
    public int MuestrasPorSimboloA12000 { get; }

    /// <summary>Muestras por simbolo con las que trabaja el decodificador, potencia de dos.</summary>
    public int MuestrasPorSimboloDeAnalisis { get; }

    /// <summary>Simbolos por segundo, que es tambien la separacion entre tonos.</summary>
    public double Baudios => (double)FrecuenciaDelProtocolo / MuestrasPorSimboloA12000;

    /// <summary>Separacion entre tonos, en hercios.</summary>
    public double EspaciadoDeTonosHz => Baudios;

    /// <summary>Duracion de un simbolo, en segundos.</summary>
    public double DuracionDeSimboloSegundos => 1.0 / Baudios;

    /// <summary>Frecuencia a la que se remuestrea el audio para decodificar.</summary>
    public double FrecuenciaDeAnalisis => Baudios * MuestrasPorSimboloDeAnalisis;

    /// <summary>Duracion de la senal completa, en segundos.</summary>
    public double DuracionDeLaSenalSegundos => SimbolosTotales * DuracionDeSimboloSegundos;

    /// <summary>Ancho de banda que ocupa la senal, en hercios (cuatro tonos).</summary>
    public double AnchoDeBandaHz => Tonos * EspaciadoDeTonosHz;

    /// <summary>
    /// Segundo del periodo en que empieza la senal por convenio: 0,5 en el periodo de 15 s y
    /// 1,0 en los demas.
    /// </summary>
    public double ComienzoNominalSegundos => PeriodoSegundos == 15 ? 0.5 : 1.0;

    /// <summary>Muestras que ocupa la senal entera a la frecuencia de analisis.</summary>
    public int MuestrasDeLaSenal => SimbolosTotales * MuestrasPorSimboloDeAnalisis;

    /// <summary>
    /// Frecuencia mas baja que se busca por omision. En los periodos cortos, toda la banda de
    /// audio; en los largos y en FST4W, 1500 ± 100 Hz como en WSPR.
    /// </summary>
    public double FrecuenciaMinimaPorOmision => PeriodoSegundos >= 300 ? 1400 : 100;

    /// <summary>Frecuencia mas alta que se busca por omision.</summary>
    public double FrecuenciaMaximaPorOmision => PeriodoSegundos >= 300 ? 1600 : 3000;

    /// <summary>Todos los periodos que admite el protocolo.</summary>
    public static ReadOnlySpan<int> Periodos => PeriodosAdmitidos;

    /// <summary>Periodos que admite FST4W: de 120 segundos en adelante.</summary>
    public static ReadOnlySpan<int> PeriodosDeFst4w => PeriodosAdmitidos.AsSpan(3);

    /// <summary>Parametros del periodo indicado.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Si el periodo no es uno de los siete del protocolo.</exception>
    public static ParametrosFst4 DelPeriodo(int periodoSegundos)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(periodoSegundos, out var p)) return p;
            var i = Array.IndexOf(PeriodosAdmitidos, periodoSegundos);
            if (i < 0)
                throw new ArgumentOutOfRangeException(nameof(periodoSegundos), periodoSegundos, "FST4 admite periodos de 15, 30, 60, 120, 300, 900 y 1800 segundos.");
            p = new ParametrosFst4(periodoSegundos, MuestrasPorSimboloDelProtocolo[i], MuestrasPorSimboloDeAnalisisPorPeriodo[i]);
            Cache[periodoSegundos] = p;
            return p;
        }
    }

    /// <summary>Muestras que dura un simbolo a la frecuencia de muestreo indicada, si cae entero.</summary>
    public double MuestrasPorSimbolo(int frecuenciaDeMuestreo) => (double)frecuenciaDeMuestreo * MuestrasPorSimboloA12000 / FrecuenciaDelProtocolo;

    /// <summary>Dice si el simbolo de esa posicion es de sincronismo, y con que tono.</summary>
    public static bool EsSimboloDeSincronismo(int posicion, out int tono)
    {
        var posiciones = PosicionesDeSincronismo;
        for (var g = 0; g < posiciones.Length; g++)
        {
            var inicio = posiciones[g];
            if (posicion < inicio || posicion >= inicio + SimbolosPorGrupoDeSincronismo) continue;
            var patron = (g & 1) == 0 ? SincronismoUno : SincronismoDos;
            tono = patron[posicion - inicio];
            return true;
        }
        tono = 0;
        return false;
    }

    /// <summary>Posiciones, en orden, de los 120 simbolos de datos.</summary>
    public int[] PosicionesDeDatos => _posicionesDeDatos ??= CalcularPosicionesDeDatos();

    private static int[] CalcularPosicionesDeDatos()
    {
        var lista = new List<int>(SimbolosDeDatos);
        for (var i = 0; i < SimbolosTotales; i++)
            if (!EsSimboloDeSincronismo(i, out _)) lista.Add(i);
        if (lista.Count != SimbolosDeDatos)
            throw new InvalidOperationException($"Las constantes de FST4 no cuadran: salen {lista.Count} simbolos de datos.");
        return [.. lista];
    }
}
