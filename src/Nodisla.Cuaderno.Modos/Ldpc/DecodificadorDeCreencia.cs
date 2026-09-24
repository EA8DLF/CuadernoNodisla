namespace Nodisla.Cuaderno.Modos.Ldpc;

/// <summary>
/// Corrige los errores del mensaje pasandose informacion entre las ecuaciones de paridad.
/// </summary>
/// <remarks>
/// <para>
/// <b>La idea, en cristiano.</b> El demodulador no entrega bits sino grados de confianza: «este
/// bit es un cero, y estoy bastante seguro», «este no lo se». Cada ecuacion de paridad puede
/// aprovechar eso: si de los siete bits que toca hay seis de los que esta bastante seguro, la
/// ecuacion deduce cuanto vale el septimo y se lo dice. Ese septimo bit recibe opiniones de
/// todas las ecuaciones donde aparece, las suma a la suya propia y se forma una opinion nueva,
/// que reparte de vuelta. A la vuelta siguiente las ecuaciones trabajan ya con opiniones mejores.
/// </para>
/// <para>
/// A eso se le llama propagacion de creencias, y de ahi el nombre. En la practica basta con unas
/// pocas decenas de vueltas: o el mensaje cuaja enseguida, o no va a cuajar.
/// </para>
/// <para>
/// <b>Regla de oro.</b> Que las ecuaciones acaben cuadrando no demuestra que el mensaje sea el
/// que se emitio. El proceso puede converger a otra palabra valida del codigo, distinta de la
/// buena, sobre todo con mucho ruido. Lo unico que separa una cosa de la otra es el CRC, y por
/// eso este decodificador devuelve los bits pero <b>no</b> se pronuncia sobre si son ciertos.
/// </para>
/// </remarks>
public sealed class DecodificadorDeCreencia
{
    private readonly CodigoLdpc _codigo;
    private readonly int[] _variableDeCadaArista;
    private readonly int[] _primeraAristaDeCadaEcuacion;
    private readonly float[] _mensajeDeEcuacionABit;
    private readonly float[] _mensajeDeBitAEcuacion;
    private readonly float[] _opinionTotal;
    private readonly byte[] _decision;

    /// <summary>
    /// Tope de la confianza que se maneja, en unidades de logaritmo de razon de probabilidades.
    /// </summary>
    /// <remarks>
    /// Veinte significa «una probabilidad contra casi quinientos millones». Pasado ese punto la
    /// tangente hiperbolica ya vale uno para el ordenador y dejar que la confianza siga creciendo
    /// solo sirve para que una tanda de ruido convenza al decodificador de algo falso sin
    /// posibilidad de rectificar.
    /// </remarks>
    private const float TopeDeConfianza = 20f;

    /// <summary>Prepara el decodificador para un codigo concreto y reserva su memoria.</summary>
    /// <param name="codigo">Codigo con el que se va a trabajar.</param>
    public DecodificadorDeCreencia(CodigoLdpc codigo)
    {
        ArgumentNullException.ThrowIfNull(codigo);
        _codigo = codigo;

        var aristas = 0;
        foreach (var ecuacion in codigo.VariablesDeCadaEcuacion) aristas += ecuacion.Length;

        _variableDeCadaArista = new int[aristas];
        _primeraAristaDeCadaEcuacion = new int[codigo.Ecuaciones + 1];
        var k = 0;
        for (var e = 0; e < codigo.Ecuaciones; e++)
        {
            _primeraAristaDeCadaEcuacion[e] = k;
            foreach (var v in codigo.VariablesDeCadaEcuacion[e]) _variableDeCadaArista[k++] = v;
        }
        _primeraAristaDeCadaEcuacion[codigo.Ecuaciones] = k;

        _mensajeDeEcuacionABit = new float[aristas];
        _mensajeDeBitAEcuacion = new float[aristas];
        _opinionTotal = new float[codigo.Longitud];
        _decision = new byte[codigo.Longitud];
    }

    /// <summary>Vueltas que se dieron en la ultima llamada.</summary>
    public int UltimasVueltas { get; private set; }

    /// <summary>
    /// Intenta recuperar la palabra de codigo a partir de las confianzas del demodulador.
    /// </summary>
    /// <param name="confianzas">
    /// Una por bit emitido. <b>Positivo quiere decir que el bit parece un cero</b> y negativo que
    /// parece un uno; cuanto mas lejos del cero, mas seguridad. Es el convenio habitual del
    /// logaritmo de la razon de probabilidades.
    /// </param>
    /// <param name="palabra">Destino de los bits recuperados.</param>
    /// <param name="vueltasMaximas">Cuantas vueltas como mucho.</param>
    /// <returns>
    /// Cierto si al final todas las ecuaciones de paridad cuadran. <b>No</b> quiere decir que el
    /// mensaje sea correcto: eso lo dice el CRC.
    /// </returns>
    public bool TryDecodificar(ReadOnlySpan<float> confianzas, Span<byte> palabra, int vueltasMaximas = 60)
    {
        if (confianzas.Length != _codigo.Longitud)
            throw new ArgumentException($"Hacen falta {_codigo.Longitud} confianzas y llegan {confianzas.Length}.", nameof(confianzas));
        if (palabra.Length != _codigo.Longitud)
            throw new ArgumentException($"La palabra debe tener {_codigo.Longitud} bits.", nameof(palabra));

        Array.Clear(_mensajeDeEcuacionABit);
        UltimasVueltas = 0;

        // De salida, la opinion de cada bit es solo lo que dijo el demodulador.
        for (var v = 0; v < _codigo.Longitud; v++) _opinionTotal[v] = Recortar(confianzas[v]);

        for (var vuelta = 1; vuelta <= vueltasMaximas; vuelta++)
        {
            UltimasVueltas = vuelta;
            UnaPasadaPorLasEcuaciones();

            for (var v = 0; v < _codigo.Longitud; v++)
                _decision[v] = (byte)(_opinionTotal[v] < 0 ? 1 : 0);

            if (_codigo.CumpleParidad(_decision))
            {
                _decision.AsSpan().CopyTo(palabra);
                return true;
            }
        }

        _decision.AsSpan().CopyTo(palabra);
        return false;
    }

    /// <summary>Confianza final de cada bit tras la ultima llamada, util para la recuperacion profunda.</summary>
    public ReadOnlySpan<float> OpinionFinal => _opinionTotal;

    /// <summary>
    /// Recorre las ecuaciones una a una, y cada una deja su conclusion puesta antes de que
    /// trabaje la siguiente.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Hay dos maneras de organizar esto. La ingenua es que todas las ecuaciones opinen a la vez
    /// sobre la situacion de partida y luego se sumen todas las opiniones. La que se usa aqui es
    /// que cada ecuacion trabaje ya con lo que dedujeron las anteriores <i>en esta misma vuelta</i>.
    /// </para>
    /// <para>
    /// La diferencia importa: con el barrido secuencial la informacion se propaga por el mensaje
    /// entero en la mitad de vueltas, y en un mensaje tan corto como este —donde no hay tiempo
    /// para muchas vueltas— eso se traduce en recuperar senales que de la otra forma se pierden.
    /// </para>
    /// <para>
    /// La cuenta de cada ecuacion es el metodo del minimo: el signo es el producto de los signos
    /// de los demas bits y la magnitud es la del mas dudoso de ellos, porque una cadena no aguanta
    /// mas que su eslabon mas debil. Se multiplica por un factor menor que uno porque ese metodo
    /// es optimista y sin corregirlo el decodificador se convence demasiado pronto.
    /// </para>
    /// </remarks>
    private void UnaPasadaPorLasEcuaciones()
    {
        // Factor de correccion del metodo del minimo.
        const float Atenuacion = 0.75f;

        for (var e = 0; e < _codigo.Ecuaciones; e++)
        {
            var desde = _primeraAristaDeCadaEcuacion[e];
            var hasta = _primeraAristaDeCadaEcuacion[e + 1];

            // Lo que cada bit le dice a esta ecuacion es su opinion menos lo que ella misma le
            // aporto la vez anterior: si no, la ecuacion se oiria a si misma y se convenceria
            // de lo que ya creia.
            var signo = 1;
            var menor = float.MaxValue;
            var siguienteMenor = float.MaxValue;
            var aristaDelMenor = -1;

            for (var a = desde; a < hasta; a++)
            {
                var m = Recortar(_opinionTotal[_variableDeCadaArista[a]] - _mensajeDeEcuacionABit[a]);
                _mensajeDeBitAEcuacion[a] = m;
                if (m < 0) signo = -signo;
                var magnitud = MathF.Abs(m);
                if (magnitud < menor)
                {
                    siguienteMenor = menor;
                    menor = magnitud;
                    aristaDelMenor = a;
                }
                else if (magnitud < siguienteMenor)
                {
                    siguienteMenor = magnitud;
                }
            }

            for (var a = desde; a < hasta; a++)
            {
                var m = _mensajeDeBitAEcuacion[a];
                var signoSinEste = m < 0 ? -signo : signo;
                var magnitud = a == aristaDelMenor ? siguienteMenor : menor;
                if (magnitud > TopeDeConfianza) magnitud = TopeDeConfianza;
                var nuevo = signoSinEste * magnitud * Atenuacion;

                // La opinion del bit se actualiza aqui mismo, no al final de la vuelta.
                _opinionTotal[_variableDeCadaArista[a]] += nuevo - _mensajeDeEcuacionABit[a];
                _mensajeDeEcuacionABit[a] = nuevo;
            }
        }
    }

    private static float Recortar(float valor) =>
        valor > TopeDeConfianza ? TopeDeConfianza : valor < -TopeDeConfianza ? -TopeDeConfianza : valor;
}
