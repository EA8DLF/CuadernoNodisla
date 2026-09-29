using Nodisla.Cuaderno.Modos.Ft8;

namespace Nodisla.Cuaderno.Modos.Q65;

/// <summary>
/// Lo que se midio de una candidata: la energia de cada tono en cada simbolo de la trama.
/// </summary>
/// <remarks>
/// Puede ser de un solo periodo o la suma de varios, que es como se promedian mensajes en Q65:
/// no se promedian decisiones, se suman energias, y se decodifica la suma como si fuera una
/// sola trama mas limpia.
/// </remarks>
public sealed class MedidaDeQ65
{
    /// <summary>Tonos de datos por simbolo.</summary>
    public const int TonosDeDatos = CampoDeGalois64.Orden;

    /// <summary>Crea una medida vacia.</summary>
    public MedidaDeQ65(int columna, int casilla)
    {
        Columna = columna;
        Casilla = casilla;
        Energias = new double[TablasDeQ65.SimbolosDeLaTrama * TonosDeDatos];
        EnergiaDelSincronismo = new double[TablasDeQ65.SimbolosDeLaTrama];
    }

    /// <summary>Columna del espectrograma en que empieza la trama.</summary>
    public int Columna { get; }

    /// <summary>Casilla del tono base.</summary>
    public int Casilla { get; }

    /// <summary>Energia de cada tono de datos (1..64) en cada posicion de la trama: fila por posicion.</summary>
    public double[] Energias { get; }

    /// <summary>Energia del tono base en cada posicion de la trama.</summary>
    public double[] EnergiaDelSincronismo { get; }

    /// <summary>Periodos sumados. Uno si es una medida directa.</summary>
    public int Periodos { get; set; } = 1;

    /// <summary>Energia media del ruido por casilla y periodo.</summary>
    public double RuidoPorPeriodo { get; set; }

    /// <summary>Energia de un tono de datos en una posicion.</summary>
    public double Energia(int posicion, int tono) => Energias[(posicion * TonosDeDatos) + tono];

    /// <summary>Suma otra medida sobre esta, para promediar periodos.</summary>
    public void Sumar(MedidaDeQ65 otra)
    {
        ArgumentNullException.ThrowIfNull(otra);
        for (var i = 0; i < Energias.Length; i++) Energias[i] += otra.Energias[i];
        for (var i = 0; i < EnergiaDelSincronismo.Length; i++) EnergiaDelSincronismo[i] += otra.EnergiaDelSincronismo[i];
        Periodos += otra.Periodos;
        RuidoPorPeriodo = DemoduladorDeQ65.EstimarRuidoPorPeriodo(this);
    }

    /// <summary>Copia de la medida, para acumular sin tocar el original.</summary>
    public MedidaDeQ65 Copia()
    {
        var copia = new MedidaDeQ65(Columna, Casilla) { Periodos = Periodos, RuidoPorPeriodo = RuidoPorPeriodo };
        Energias.CopyTo(copia.Energias, 0);
        EnergiaDelSincronismo.CopyTo(copia.EnergiaDelSincronismo, 0);
        return copia;
    }
}

/// <summary>Cuanto se sabe de antemano del mensaje, para la decodificacion con informacion a priori.</summary>
public sealed class MascaraAp
{
    /// <summary>Bits conocidos de cada uno de los 13 simbolos del mensaje, con el primero como mas significativo.</summary>
    public int[] Mascaras { get; } = new int[MensajeDeQ65.SimbolosDelMensaje];

    /// <summary>Valor de los bits conocidos de cada simbolo.</summary>
    public int[] Valores { get; } = new int[MensajeDeQ65.SimbolosDelMensaje];

    /// <summary>Nombre para el registro: <c>CQ</c>, <c>mi indicativo</c>, <c>mi indicativo y DX</c>.</summary>
    public string Nombre { get; init; } = string.Empty;

    /// <summary>Construye la mascara a partir de un mensaje modelo y las posiciones de bit que se dan por sabidas.</summary>
    public static MascaraAp De(ReadOnlySpan<byte> bits77, IEnumerable<int> posicionesConocidas, string nombre)
    {
        ArgumentNullException.ThrowIfNull(posicionesConocidas);
        var mascara = new MascaraAp { Nombre = nombre };
        foreach (var posicion in posicionesConocidas)
        {
            var simbolo = posicion / CampoDeGalois64.BitsPorElemento;
            var bit = CampoDeGalois64.BitsPorElemento - 1 - (posicion % CampoDeGalois64.BitsPorElemento);
            mascara.Mascaras[simbolo] |= 1 << bit;
            if (bits77[posicion] != 0) mascara.Valores[simbolo] |= 1 << bit;
        }
        return mascara;
    }
}

/// <summary>
/// Saca de una candidata las energias por simbolo y las convierte en probabilidades.
/// </summary>
/// <remarks>
/// <para>
/// <b>De energia a probabilidad.</b> Con deteccion no coherente de tonos, lo que se tiene por
/// simbolo son 64 energias, y la del tono emitido es de media mas alta que las demas. Con un
/// canal que desvanece (Rayleigh), la verosimilitud de cada tono es una exponencial de su
/// energia partido por el ruido, escalada por un factor que depende de la relacion senal-ruido
/// por simbolo. Ese factor se estima del propio tono de sincronismo de la candidata y se acota,
/// porque estimarlo mal por arriba hace al decodificador demasiado confiado y por abajo
/// demasiado timido; ninguna de las dos cosas inventa mensajes, pero las dos pierden algunos.
/// </para>
/// <para>
/// <b>Lo que se sabe de antemano.</b> El bit de relleno del ultimo simbolo es cero siempre, y
/// eso entra como conocimiento fijo, no como suposicion. Lo demas —que el mensaje empieza por
/// CQ, o por mi indicativo— es informacion a priori de verdad, y se aplica solo si el operador
/// la dio.
/// </para>
/// </remarks>
public static class DemoduladorDeQ65
{
    private const int M = CampoDeGalois64.Orden;
    private const int Trama = TablasDeQ65.SimbolosDeLaTrama;

    /// <summary>Mide las energias de una candidata.</summary>
    public static MedidaDeQ65 Medir(EspectrogramaDeQ65 espectrograma, CandidataDeQ65 candidata)
    {
        ArgumentNullException.ThrowIfNull(espectrograma);
        var p = espectrograma.Parametros;
        var medida = new MedidaDeQ65(candidata.Columna, candidata.Casilla);
        for (var i = 0; i < Trama; i++)
        {
            var columna = candidata.Columna + (i * ParametrosDeQ65.ColumnasPorSimbolo);
            medida.EnergiaDelSincronismo[i] = espectrograma.Energia(columna, candidata.Casilla);
            for (var s = 0; s < M; s++)
                medida.Energias[(i * M) + s] = espectrograma.Energia(columna, candidata.Casilla + ((s + 1) * p.CasillasPorTono));
        }
        medida.RuidoPorPeriodo = EstimarRuidoPorPeriodo(medida);
        return medida;
    }

    /// <summary>
    /// Ruido medio por casilla y periodo, por la mediana de las energias de la medida. Con
    /// varios periodos sumados la mediana de la suma se corrige con la aproximacion de
    /// Chen y Rubin para la distribucion gamma.
    /// </summary>
    public static double EstimarRuidoPorPeriodo(MedidaDeQ65 medida)
    {
        ArgumentNullException.ThrowIfNull(medida);
        var valores = (double[])medida.Energias.Clone();
        Array.Sort(valores);
        var mediana = valores[valores.Length / 2];
        var n = medida.Periodos;
        var medianaDeGamma = n == 1 ? Math.Log(2) : n - (1.0 / 3) + (8.0 / (405 * n));
        return Math.Max(mediana / medianaDeGamma, 1e-30);
    }

    /// <summary>Relacion senal-ruido por simbolo estimada en el tono de sincronismo, en veces.</summary>
    public static double SenalRuidoDelSincronismo(MedidaDeQ65 medida, TablasDeQ65 tablas)
    {
        ArgumentNullException.ThrowIfNull(medida);
        ArgumentNullException.ThrowIfNull(tablas);
        double suma = 0;
        foreach (var s in tablas.PosicionesDeSincronismo) suma += medida.EnergiaDelSincronismo[s];
        var media = suma / tablas.PosicionesDeSincronismo.Length;
        return Math.Max(0, (media / medida.RuidoPorPeriodo / medida.Periodos) - 1);
    }

    /// <summary>
    /// Las 65 filas de 64 probabilidades que necesita el decodificador.
    /// </summary>
    /// <param name="medida">Energias de la candidata.</param>
    /// <param name="tablas">Posiciones de datos.</param>
    /// <param name="factor">Escala de la exponencial: relacion senal-ruido por simbolo partido por uno mas ella.</param>
    /// <param name="ap">Lo que se sabe de antemano, o nulo.</param>
    public static double[] Intrinsecas(MedidaDeQ65 medida, TablasDeQ65 tablas, double factor, MascaraAp? ap)
    {
        ArgumentNullException.ThrowIfNull(medida);
        ArgumentNullException.ThrowIfNull(tablas);
        var intrinsecas = new double[CodigoQra.Longitud * M];
        Array.Fill(intrinsecas, 1.0 / M);

        var escala = factor / medida.RuidoPorPeriodo;
        for (var t = 0; t < MensajeDeQ65.SimbolosEmitidos; t++)
        {
            var posicion = tablas.PosicionesDeDatos[t];
            var variable = MensajeDeQ65.IndiceEnLaPalabra(t);
            var fila = intrinsecas.AsSpan(variable * M, M);
            double maximo = 0;
            for (var s = 0; s < M; s++) maximo = Math.Max(maximo, medida.Energia(posicion, s));
            for (var s = 0; s < M; s++) fila[s] = Math.Exp(escala * (medida.Energia(posicion, s) - maximo));
        }

        // El bit de relleno del simbolo 13 es siempre cero: los valores impares no existen.
        var ultimo = intrinsecas.AsSpan((MensajeDeQ65.SimbolosDelMensaje - 1) * M, M);
        for (var s = 1; s < M; s += 2) ultimo[s] = 0;

        if (ap is not null)
        {
            for (var simbolo = 0; simbolo < MensajeDeQ65.SimbolosDelMensaje; simbolo++)
            {
                var mascara = ap.Mascaras[simbolo];
                if (mascara == 0) continue;
                var fila = intrinsecas.AsSpan(simbolo * M, M);
                for (var s = 0; s < M; s++)
                    if ((s & mascara) != (ap.Valores[simbolo] & mascara)) fila[s] = 0;
            }
        }

        return intrinsecas;
    }

    /// <summary>
    /// Cuanto se parece una palabra decodificada a lo que se oyo, en desviaciones tipicas del
    /// ruido: la suma, sobre los 63 simbolos emitidos, de la energia del tono que dice la
    /// palabra menos lo que valdria con ruido solo.
    /// </summary>
    /// <remarks>
    /// Es el freno contra los mensajes inventados. Una palabra que el codigo da por buena pero
    /// que no se corresponde con energia de verdad en los tonos que dice no la emitio nadie:
    /// el corrector, sobre todo con informacion a priori, puede cerrar una palabra valida
    /// apoyandose en ruido, y esta cifra lo delata.
    /// </remarks>
    public static double Verosimilitud(MedidaDeQ65 medida, ReadOnlySpan<int> palabra, TablasDeQ65 tablas)
    {
        ArgumentNullException.ThrowIfNull(medida);
        ArgumentNullException.ThrowIfNull(tablas);
        var n = medida.Periodos;
        double suma = 0;
        for (var t = 0; t < MensajeDeQ65.SimbolosEmitidos; t++)
        {
            var posicion = tablas.PosicionesDeDatos[t];
            var tono = palabra[MensajeDeQ65.IndiceEnLaPalabra(t)];
            suma += (medida.Energia(posicion, tono) / medida.RuidoPorPeriodo) - n;
        }
        return suma / Math.Sqrt((double)MensajeDeQ65.SimbolosEmitidos * n);
    }

    /// <summary>Relacion senal-ruido referida a 2500 Hz de una palabra decodificada, en decibelios.</summary>
    public static double RelacionSenalRuidoDb(MedidaDeQ65 medida, ReadOnlySpan<int> palabra, TablasDeQ65 tablas, ParametrosDeQ65 parametros)
    {
        ArgumentNullException.ThrowIfNull(medida);
        ArgumentNullException.ThrowIfNull(tablas);
        ArgumentNullException.ThrowIfNull(parametros);
        double suma = 0;
        for (var t = 0; t < MensajeDeQ65.SimbolosEmitidos; t++)
            suma += medida.Energia(tablas.PosicionesDeDatos[t], palabra[MensajeDeQ65.IndiceEnLaPalabra(t)]);
        var porSimbolo = (suma / MensajeDeQ65.SimbolosEmitidos / medida.RuidoPorPeriodo / medida.Periodos) - 1;
        var relacion = Math.Max(porSimbolo, 1e-3) * parametros.Baudios / GeneradorDeSenalDeQ65.AnchoDeBandaDeReferencia;
        return 10 * Math.Log10(relacion);
    }

    /// <summary>
    /// Las mascaras de informacion a priori que se pueden usar con lo que sabe el operador.
    /// </summary>
    /// <param name="miIndicativo">Indicativo propio, o nulo.</param>
    /// <param name="indicativoDx">Indicativo del corresponsal, o nulo.</param>
    /// <remarks>
    /// De menos a mas atrevida: que el mensaje empiece por CQ; que vaya dirigido a mi; y que
    /// vaya dirigido a mi desde el corresponsal con el que estoy. Cada una fija mas bits y por
    /// eso cada una tiene mas riesgo de cerrar una palabra apoyada en ruido, que es lo que
    /// vigila la verosimilitud.
    /// </remarks>
    public static List<MascaraAp> MascarasDisponibles(string? miIndicativo, string? indicativoDx)
    {
        var mascaras = new List<MascaraAp>();
        var primerCampo = Enumerable.Range(0, 29).Concat(Enumerable.Range(74, 3)).ToArray();
        var dosCampos = Enumerable.Range(0, 58).Concat(Enumerable.Range(74, 3)).ToArray();

        if (MensajeDe77Bits.TryEmpaquetar("CQ K1ABC FN42", out var cq, out _) && EsNormal(cq))
            mascaras.Add(MascaraAp.De(cq, primerCampo, "CQ"));

        if (!string.IsNullOrWhiteSpace(miIndicativo)
            && MensajeDe77Bits.TryEmpaquetar($"{miIndicativo} K1ABC FN42", out var mio, out _) && EsNormal(mio))
        {
            mascaras.Add(MascaraAp.De(mio, primerCampo, "mi indicativo"));
            if (!string.IsNullOrWhiteSpace(indicativoDx)
                && MensajeDe77Bits.TryEmpaquetar($"{miIndicativo} {indicativoDx} FN42", out var ambos, out _) && EsNormal(ambos))
                mascaras.Add(MascaraAp.De(ambos, dosCampos, "mi indicativo y DX"));
        }
        return mascaras;
    }

    private static bool EsNormal(byte[] bits77) =>
        EmpaquetadoDeBits.Leer(bits77, 74, 3) == 1;
}
