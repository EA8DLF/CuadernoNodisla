using Nodisla.Cuaderno.Aplicacion.Puertos;

namespace Nodisla.Cuaderno.Modos.Ft8;

/// <summary>
/// Las constantes de la forma de onda de FT8 y FT4.
/// </summary>
/// <remarks>
/// <para>
/// FT8 y FT4 son el mismo protocolo con dos trajes distintos. Los dos llevan el mismo mensaje
/// de 77 bits, el mismo CRC de 14 y el mismo codigo corrector LDPC(174,91); lo que cambia es
/// como se pone eso en el aire:
/// </para>
/// <list type="bullet">
/// <item>FT8 usa 8 tonos, 6,25 simbolos por segundo y cabe en una ventana de 15 segundos.</item>
/// <item>FT4 usa 4 tonos, 20,83 simbolos por segundo y cabe en 7,5 segundos.</item>
/// </list>
/// <para>
/// Con 4 tonos hacen falta mas simbolos para los mismos 174 bits (87 en vez de 58), pero cada
/// simbolo dura mucho menos, asi que el mensaje entero acaba durando la mitad. El precio es la
/// sensibilidad: en FT4 cada simbolo recoge menos energia y se pierde antes bajo el ruido.
/// </para>
/// <para>
/// La constante de la que cuelga todo lo demas es la <b>separacion entre tonos</b>, que ademas
/// es la inversa de lo que dura un simbolo: 6,25 hercios y 0,16 segundos en FT8; 125/6 hercios
/// y 0,048 segundos en FT4. Esa relacion no es casual, es lo que hace que la fase no de un
/// salto al cambiar de tono, y por eso aqui se guarda como una sola cifra y el resto se deduce.
/// </para>
/// </remarks>
public sealed class ParametrosDelModo
{
    private ParametrosDelModo(
        ModoDelModem modo,
        double espaciadoDeTonosHz,
        int muestrasPorSimboloDeAnalisis,
        int simbolosTotales,
        int simbolosDeDatos,
        int tonos,
        int[][] gruposDeCostas,
        int[] posicionesDeCostas,
        byte[] mapaDeGray,
        int simbolosDeRampa,
        double anchoDeBandaDelFiltro,
        double periodoSegundos,
        double comienzoNominalSegundos)
    {
        Modo = modo;
        EspaciadoDeTonosHz = espaciadoDeTonosHz;
        MuestrasPorSimboloDeAnalisis = muestrasPorSimboloDeAnalisis;
        SimbolosTotales = simbolosTotales;
        SimbolosDeDatos = simbolosDeDatos;
        Tonos = tonos;
        GruposDeCostas = gruposDeCostas;
        PosicionesDeCostas = posicionesDeCostas;
        MapaDeGray = mapaDeGray;
        SimbolosDeRampa = simbolosDeRampa;
        AnchoDeBandaDelFiltro = anchoDeBandaDelFiltro;
        PeriodoSegundos = periodoSegundos;
        ComienzoNominalSegundos = comienzoNominalSegundos;

        MapaDeGrayInverso = new byte[tonos];
        for (var valor = 0; valor < tonos; valor++)
            MapaDeGrayInverso[mapaDeGray[valor]] = (byte)valor;
    }

    /// <summary>Modo al que corresponden estos parametros.</summary>
    public ModoDelModem Modo { get; }

    /// <summary>Separacion entre tonos, en hercios. Es tambien la inversa de lo que dura un simbolo.</summary>
    public double EspaciadoDeTonosHz { get; }

    /// <summary>
    /// Muestras por simbolo con las que trabaja el decodificador.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Es una <b>potencia de dos</b> a proposito, y de ahi sale <see cref="FrecuenciaDeAnalisis"/>.
    /// La razon es que toda la deteccion se hace con transformadas de Fourier, y solo si el
    /// simbolo cae en una potencia de dos se puede pedir que las casillas de la transformada
    /// coincidan <b>exactamente</b> con los tonos del modo.
    /// </para>
    /// <para>
    /// Si no coincidieran, cada tono caeria entre dos casillas y se perderia parte de su energia
    /// —hasta un decibelio— justo en las senales debiles, que son las que cuesta sacar. Por eso
    /// el audio se remuestrea a una frecuencia que parece rara (12800 en FT8, 21333,33 en FT4) en
    /// lugar de usar los 12000 del programa de referencia.
    /// </para>
    /// </remarks>
    public int MuestrasPorSimboloDeAnalisis { get; }

    /// <summary>Frecuencia a la que se remuestrea el audio para decodificar.</summary>
    public double FrecuenciaDeAnalisis => EspaciadoDeTonosHz * MuestrasPorSimboloDeAnalisis;

    /// <summary>Simbolos que se emiten, contando sincronismo, datos y rampas.</summary>
    public int SimbolosTotales { get; }

    /// <summary>Simbolos que llevan los 174 bits del mensaje codificado.</summary>
    public int SimbolosDeDatos { get; }

    /// <summary>Tonos distintos: 8 en FT8 y 4 en FT4.</summary>
    public int Tonos { get; }

    /// <summary>Bits que transporta cada simbolo: 3 en FT8 y 2 en FT4.</summary>
    public int BitsPorSimbolo => Tonos == 8 ? 3 : 2;

    /// <summary>
    /// Los grupos de Costas, que son la senal de sincronismo.
    /// </summary>
    /// <remarks>
    /// Un grupo de Costas es una secuencia de tonos elegida para que, al correlarla consigo
    /// misma desplazada en tiempo o en frecuencia, el resultado caiga en picado. Eso la hace
    /// facil de encontrar dentro del ruido y permite medir a la vez a que hora empieza la senal
    /// y en que frecuencia esta. FT8 repite el mismo grupo de siete tonos al principio, en
    /// medio y al final; FT4 usa cuatro grupos distintos de cuatro tonos.
    /// </remarks>
    public int[][] GruposDeCostas { get; }

    /// <summary>Posicion, en simbolos, donde empieza cada grupo de Costas.</summary>
    public int[] PosicionesDeCostas { get; }

    /// <summary>
    /// Traduccion de valor de 3 (o 2) bits a tono.
    /// </summary>
    /// <remarks>
    /// Es un codigo de Gray: tonos vecinos se diferencian en un solo bit. Cuando el ruido hace
    /// que el decodificador confunda un tono con el de al lado —que es el error mas probable,
    /// porque son los que menos se parecen entre si— solo se equivoca un bit y no dos o tres.
    /// El corrector de errores arregla un bit suelto con mucha mas facilidad.
    /// </remarks>
    public byte[] MapaDeGray { get; }

    /// <summary>Traduccion de tono a valor, la inversa de <see cref="MapaDeGray"/>.</summary>
    public byte[] MapaDeGrayInverso { get; }

    /// <summary>
    /// Simbolos de rampa al principio y al final, que FT4 usa y FT8 no.
    /// </summary>
    /// <remarks>
    /// FT4 emite un simbolo de tono cero antes y despues del mensaje para que la portadora
    /// suba y baje sin dar un golpe seco, que ensuciaria las frecuencias de al lado. En FT8
    /// ese papel lo hace el suavizado de la envolvente del primer y ultimo simbolo.
    /// </remarks>
    public int SimbolosDeRampa { get; }

    /// <summary>Parametro de ancho de banda del filtro gaussiano de la modulacion.</summary>
    public double AnchoDeBandaDelFiltro { get; }

    /// <summary>Duracion de la ventana del modo: 15 s en FT8, 7,5 s en FT4.</summary>
    public double PeriodoSegundos { get; }

    /// <summary>Segundo de la ventana en que empieza la senal por convenio.</summary>
    public double ComienzoNominalSegundos { get; }

    /// <summary>Duracion de un simbolo, en segundos.</summary>
    public double DuracionDeSimboloSegundos => 1.0 / EspaciadoDeTonosHz;

    /// <summary>Muestras que ocupa el mensaje entero a la frecuencia de analisis.</summary>
    public int MuestrasDeLaSenal => SimbolosTotales * MuestrasPorSimboloDeAnalisis;

    /// <summary>Duracion de la senal completa, en segundos.</summary>
    public double DuracionDeLaSenalSegundos => SimbolosTotales * DuracionDeSimboloSegundos;

    /// <summary>Muestras que ocupa una ventana entera a la frecuencia de analisis.</summary>
    public int MuestrasDeLaVentana => (int)Math.Round(PeriodoSegundos * FrecuenciaDeAnalisis);

    /// <summary>Muestras que dura un simbolo a la frecuencia de muestreo indicada.</summary>
    /// <param name="frecuenciaDeMuestreo">Muestras por segundo del audio.</param>
    /// <exception cref="ArgumentException">Si el simbolo no cae en un numero entero de muestras.</exception>
    public int MuestrasPorSimbolo(int frecuenciaDeMuestreo)
    {
        var exacto = frecuenciaDeMuestreo / EspaciadoDeTonosHz;
        var redondeado = (int)Math.Round(exacto);
        if (Math.Abs(exacto - redondeado) > 1e-9)
            throw new ArgumentException(
                $"A {frecuenciaDeMuestreo} muestras por segundo, un símbolo de {Modo} no cae en un número entero de muestras.",
                nameof(frecuenciaDeMuestreo));
        return redondeado;
    }

    /// <summary>Ancho de banda que ocupa la senal, en hercios.</summary>
    public double AnchoDeBandaHz => Tonos * EspaciadoDeTonosHz;

    /// <summary>
    /// FT8: 8 tonos separados 6,25 Hz, 79 simbolos de 0,16 s, ventana de 15 segundos.
    /// </summary>
    public static ParametrosDelModo Ft8 { get; } = new(
        modo: ModoDelModem.Ft8,
        espaciadoDeTonosHz: 6.25,
        muestrasPorSimboloDeAnalisis: 2048,
        simbolosTotales: 79,
        simbolosDeDatos: 58,
        tonos: 8,
        // El mismo grupo de siete tonos, repetido tres veces. Que haya uno en medio permite
        // seguir una senal que se mueve en frecuencia mientras dura el mensaje.
        gruposDeCostas: [[3, 1, 4, 0, 6, 5, 2], [3, 1, 4, 0, 6, 5, 2], [3, 1, 4, 0, 6, 5, 2]],
        posicionesDeCostas: [0, 36, 72],
        mapaDeGray: [0, 1, 3, 2, 5, 6, 4, 7],
        simbolosDeRampa: 0,
        anchoDeBandaDelFiltro: 2.0,
        periodoSegundos: 15.0,
        comienzoNominalSegundos: 0.5);

    /// <summary>
    /// FT4: 4 tonos separados 20,83 Hz, 105 simbolos de 0,048 s, ventana de 7,5 segundos.
    /// </summary>
    public static ParametrosDelModo Ft4 { get; } = new(
        modo: ModoDelModem.Ft4,
        // 125/6 hercios exactos. Escrito asi y no como 20,8333 para que no se pierda precision:
        // a lo largo de los cinco segundos del mensaje, un redondeo aqui desplazaria el final.
        espaciadoDeTonosHz: 125.0 / 6.0,
        muestrasPorSimboloDeAnalisis: 1024,
        simbolosTotales: 105,
        simbolosDeDatos: 87,
        tonos: 4,
        // Cuatro grupos distintos: con solo cuatro tonos, un unico patron repetido se podria
        // confundir consigo mismo desplazado un bloque entero.
        gruposDeCostas: [[0, 1, 3, 2], [1, 0, 2, 3], [2, 3, 1, 0], [3, 2, 0, 1]],
        posicionesDeCostas: [1, 34, 67, 100],
        mapaDeGray: [0, 1, 3, 2],
        simbolosDeRampa: 1,
        anchoDeBandaDelFiltro: 1.0,
        periodoSegundos: 7.5,
        comienzoNominalSegundos: 0.5);

    /// <summary>Parametros del modo indicado.</summary>
    public static ParametrosDelModo De(ModoDelModem modo) => modo switch
    {
        ModoDelModem.Ft8 => Ft8,
        ModoDelModem.Ft4 => Ft4,
        _ => throw new ArgumentOutOfRangeException(nameof(modo), modo, "Modo no soportado por el modem propio."),
    };

    /// <summary>
    /// Dice si el simbolo que ocupa esa posicion es de sincronismo, y con que tono.
    /// </summary>
    /// <param name="posicion">Indice del simbolo dentro del mensaje emitido.</param>
    /// <param name="tono">Tono que le toca, si es de sincronismo.</param>
    /// <returns>Cierto si la posicion pertenece a un grupo de Costas.</returns>
    public bool EsSimboloDeSincronismo(int posicion, out int tono)
    {
        for (var g = 0; g < PosicionesDeCostas.Length; g++)
        {
            var inicio = PosicionesDeCostas[g];
            var grupo = GruposDeCostas[g];
            if (posicion >= inicio && posicion < inicio + grupo.Length)
            {
                tono = grupo[posicion - inicio];
                return true;
            }
        }
        tono = 0;
        return false;
    }

    /// <summary>
    /// Posiciones, en orden, de los simbolos que llevan datos.
    /// </summary>
    /// <remarks>
    /// Se calcula una sola vez: son las posiciones que no son de sincronismo ni de rampa. El
    /// demodulador recorre esta lista para saber de donde saca cada trozo de los 174 bits.
    /// </remarks>
    public int[] PosicionesDeDatos => _posicionesDeDatos ??= CalcularPosicionesDeDatos();

    private int[]? _posicionesDeDatos;

    private int[] CalcularPosicionesDeDatos()
    {
        var lista = new List<int>(SimbolosDeDatos);
        // Las rampas de FT4 estan en el primer y ultimo simbolo y no llevan informacion.
        var primera = SimbolosDeRampa;
        var ultima = SimbolosTotales - SimbolosDeRampa;
        for (var i = primera; i < ultima; i++)
        {
            if (EsSimboloDeSincronismo(i, out _)) continue;
            lista.Add(i);
        }
        if (lista.Count != SimbolosDeDatos)
            throw new InvalidOperationException(
                $"Las constantes de {Modo} no cuadran: salen {lista.Count} simbolos de datos y deberian ser {SimbolosDeDatos}.");
        return [.. lista];
    }
}
