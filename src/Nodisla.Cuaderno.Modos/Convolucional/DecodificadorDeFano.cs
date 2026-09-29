namespace Nodisla.Cuaderno.Modos.Convolucional;

/// <summary>
/// Resultado de una decodificacion secuencial.
/// </summary>
/// <param name="Bits">Los bits decodificados, cola incluida. Vacio si no se llego al final.</param>
/// <param name="Metrica">Metrica acumulada del camino completo. Cuanto mas alta, mas se parece a lo recibido.</param>
/// <param name="Esfuerzo">Movimientos que costo llegar, para saber lo cerca que se estuvo del limite.</param>
public readonly record struct ResultadoDeFano(byte[] Bits, int Metrica, long Esfuerzo)
{
    /// <summary>Se llego al final del arbol.</summary>
    public bool Decodificado => Bits.Length > 0;
}

/// <summary>
/// Decodificador secuencial de Fano para <see cref="CodigoConvolucional"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por que secuencial.</b> Un codigo con longitud de restriccion 32 tiene dos mil millones de
/// estados; el algoritmo de Viterbi, que los recorre todos, es impensable. El de Fano recorre el
/// arbol de mensajes posibles <i>por un solo camino</i>, avanzando mientras lo recibido se
/// parece a lo que ese camino predice y retrocediendo cuando deja de parecerse. Con buena senal
/// va casi derecho al final; con senal en el filo da muchas vueltas, y por eso lleva un limite
/// de esfuerzo: si se agota, no hay mensaje, y no pasa nada. Lo que no puede pasar es que se
/// invente uno.
/// </para>
/// <para>
/// <b>La metrica.</b> Cada bit recibido llega como un valor de 0 a 255 que dice cuanto se parece
/// a un uno (255: seguro que uno; 0: seguro que cero; 128: ni idea). Para cada bit del camino se
/// suma una metrica que es positiva si lo recibido apoya a ese bit y muy negativa si lo
/// contradice, con un sesgo igual a la tasa del codigo para que un camino equivocado tienda a
/// bajar y el bueno a subir. La tabla se calcula una vez suponiendo una relacion senal-ruido de
/// trabajo, que es la del filo: ahi es donde importa que este bien ajustada.
/// </para>
/// <para>
/// <b>El umbral.</b> El algoritmo lleva un umbral que sube en escalones de <see cref="Delta"/>
/// cada vez que llega a un nodo nuevo con metrica sobrada, y baja un escalon cuando se ha
/// quedado sin caminos que lo cumplan. Un escalon pequeno da muchas vueltas; uno grande deja
/// pasar caminos malos. El valor por omision es el habitual para este codigo.
/// </para>
/// <para>
/// <b>La cola.</b> Los ultimos 31 bits del mensaje son ceros que vacian el registro. Aqui no se
/// tratan como bits desconocidos: en esa zona el arbol solo tiene una rama, la del cero. Eso
/// obliga a que un camino, para llegar al final, tenga que casar con 62 bits recibidos sin
/// alternativa, que es lo que mas hace por que el ruido puro no saque nada.
/// </para>
/// <para>
/// Escrito a partir de la descripcion clasica del algoritmo (Fano, 1963; Lin y Costello,
/// <i>Error Control Coding</i>, capitulo de decodificacion secuencial). No hay nada de WSJT-X.
/// </para>
/// </remarks>
public sealed class DecodificadorDeFano
{
    private readonly CodigoConvolucional _codigo;
    private readonly int[] _metricaDelUno = new int[256];
    private readonly int[] _metricaDelCero = new int[256];

    /// <summary>
    /// Crea el decodificador con su tabla de metricas.
    /// </summary>
    /// <param name="codigo">Codigo que hay que deshacer.</param>
    /// <param name="mediaDeLaSenal">
    /// Cuanto se separa de 128, de media, un bit recibido cuando de verdad vale lo que parece.
    /// Es la relacion senal-ruido de trabajo, en unidades del valor blando.
    /// </param>
    /// <param name="desviacion">Dispersion del ruido alrededor de esa media, en las mismas unidades.</param>
    /// <param name="escala">Por cuanto se multiplican las metricas antes de redondearlas a enteros.</param>
    public DecodificadorDeFano(
        CodigoConvolucional codigo,
        double mediaDeLaSenal = 50,
        double desviacion = 30,
        double escala = 10)
    {
        ArgumentNullException.ThrowIfNull(codigo);
        if (mediaDeLaSenal <= 0) throw new ArgumentOutOfRangeException(nameof(mediaDeLaSenal));
        if (desviacion <= 0) throw new ArgumentOutOfRangeException(nameof(desviacion));
        if (escala <= 0) throw new ArgumentOutOfRangeException(nameof(escala));
        _codigo = codigo;

        // Metrica de Fano: log2 de la probabilidad del valor recibido dado el bit, dividida por
        // la probabilidad del valor recibido a secas (media de las dos hipotesis), menos la
        // tasa del codigo. Con ruido gaussiano de la misma varianza en las dos hipotesis, la
        // razon de probabilidades sale de restar dos exponentes.
        const double Tasa = 0.5;
        for (var s = 0; s < 256; s++)
        {
            var x = s - 128.0;
            var e1 = -((x - mediaDeLaSenal) * (x - mediaDeLaSenal)) / (2 * desviacion * desviacion);
            var e0 = -((x + mediaDeLaSenal) * (x + mediaDeLaSenal)) / (2 * desviacion * desviacion);
            var mayor = Math.Max(e0, e1);
            var suma = Math.Exp(e0 - mayor) + Math.Exp(e1 - mayor);
            var log2Suma = mayor / Math.Log(2) + Math.Log2(suma);
            var m1 = (e1 / Math.Log(2)) + 1 - log2Suma - Tasa;
            var m0 = (e0 / Math.Log(2)) + 1 - log2Suma - Tasa;
            _metricaDelUno[s] = (int)Math.Round(escala * m1);
            _metricaDelCero[s] = (int)Math.Round(escala * m0);
        }
    }

    /// <summary>Escalon del umbral. Ver la nota de la clase.</summary>
    public int Delta { get; init; } = 60;

    /// <summary>
    /// Movimientos por bit que se conceden antes de darse por vencido. El total es esto por el
    /// numero de bits del mensaje.
    /// </summary>
    public int EsfuerzoPorBit { get; init; } = 10_000;

    /// <summary>Metrica que suma un bit recibido con valor blando <paramref name="blando"/> si el camino dice <paramref name="bit"/>.</summary>
    public int Metrica(int bit, byte blando) => bit == 0 ? _metricaDelCero[blando] : _metricaDelUno[blando];

    /// <summary>
    /// Intenta decodificar una secuencia de bits recibidos.
    /// </summary>
    /// <param name="blandos">
    /// Un valor de 0 a 255 por cada bit codificado, en el orden en que los saco el codificador
    /// (para cada bit de entrada, primero el del polinomio A y luego el del B). Tiene que haber
    /// exactamente el doble que bits totales.
    /// </param>
    /// <param name="bitsDeDatos">Bits que llevan informacion. Los que faltan hasta el total son la cola de ceros.</param>
    /// <param name="bitsTotales">Bits de entrada al codificador, cola incluida.</param>
    /// <returns>El camino encontrado, o un resultado sin bits si se agoto el esfuerzo.</returns>
    public ResultadoDeFano Decodificar(ReadOnlySpan<byte> blandos, int bitsDeDatos, int bitsTotales)
    {
        if (bitsTotales <= 0) throw new ArgumentOutOfRangeException(nameof(bitsTotales));
        if (bitsDeDatos < 0 || bitsDeDatos > bitsTotales) throw new ArgumentOutOfRangeException(nameof(bitsDeDatos));
        if (blandos.Length != 2 * bitsTotales)
            throw new ArgumentException($"Hacen falta {2 * bitsTotales} valores blandos y llegan {blandos.Length}.", nameof(blandos));

        var n = bitsTotales;
        var metrica = new int[n + 1];
        var registro = new uint[n + 1];
        var orden = new byte[n + 1];
        var bits = new byte[n];
        var limite = (long)EsfuerzoPorBit * n;
        long esfuerzo = 0;

        var d = 0;
        var umbral = 0;

        while (true)
        {
            if (++esfuerzo > limite) return new ResultadoDeFano([], metrica[d], esfuerzo);

            // Mirar hacia delante por la rama que toca: la mejor si es la primera vez que se
            // mira desde este nodo, la otra si ya se volvio de la mejor.
            if (RamaElegida(blandos, d, registro[d], orden[d], bitsDeDatos, out var bit, out var metricaDeRama)
                && metrica[d] + metricaDeRama >= umbral)
            {
                // Es la primera visita al nodo de destino si desde el que se sale no habia
                // margen de sobra sobre el umbral; solo entonces se aprieta el umbral.
                var primeraVisita = metrica[d] < umbral + Delta;
                registro[d + 1] = CodigoConvolucional.Avanzar(registro[d], bit);
                metrica[d + 1] = metrica[d] + metricaDeRama;
                bits[d] = (byte)bit;
                d++;
                orden[d] = 0;
                if (d == n) return new ResultadoDeFano(bits, metrica[d], esfuerzo);
                if (primeraVisita) umbral += Delta * ((metrica[d] - umbral) / Delta);
                continue;
            }

            // La rama de delante no cumple. Mirar hacia atras hasta encontrar un nodo con otra
            // rama sin probar; si no hay manera de retroceder, bajar el umbral y volver a mirar
            // hacia delante desde donde se esta.
            while (true)
            {
                if (d == 0 || metrica[d - 1] < umbral)
                {
                    umbral -= Delta;
                    orden[d] = 0;
                    break;
                }

                d--;
                if (orden[d] == 0 && d < bitsDeDatos)
                {
                    orden[d] = 1;
                    break;
                }
            }
        }
    }

    /// <summary>
    /// La rama que toca mirar desde un nodo: la mejor de las dos (orden 0) o la peor (orden 1).
    /// En la cola solo existe la rama del cero.
    /// </summary>
    private bool RamaElegida(
        ReadOnlySpan<byte> blandos, int profundidad, uint registro, byte orden, int bitsDeDatos,
        out int bit, out int metricaDeRama)
    {
        var a = blandos[2 * profundidad];
        var b = blandos[(2 * profundidad) + 1];

        var registro0 = CodigoConvolucional.Avanzar(registro, 0);
        var m0 = Metrica(_codigo.BitA(registro0), a) + Metrica(_codigo.BitB(registro0), b);

        if (profundidad >= bitsDeDatos)
        {
            bit = 0;
            metricaDeRama = m0;
            return orden == 0;
        }

        var registro1 = CodigoConvolucional.Avanzar(registro, 1);
        var m1 = Metrica(_codigo.BitA(registro1), a) + Metrica(_codigo.BitB(registro1), b);

        var mejorEsUno = m1 > m0;
        if (orden == 0)
        {
            bit = mejorEsUno ? 1 : 0;
            metricaDeRama = mejorEsUno ? m1 : m0;
        }
        else
        {
            bit = mejorEsUno ? 0 : 1;
            metricaDeRama = mejorEsUno ? m0 : m1;
        }
        return true;
    }
}
