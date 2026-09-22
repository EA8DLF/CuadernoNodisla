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

        // De salida, lo unico que sabe cada bit es lo que le dijo el demodulador.
        for (var a = 0; a < _mensajeDeBitAEcuacion.Length; a++)
            _mensajeDeBitAEcuacion[a] = Recortar(confianzas[_variableDeCadaArista[a]]);

        for (var vuelta = 1; vuelta <= vueltasMaximas; vuelta++)
        {
            UltimasVueltas = vuelta;
            PasoDeLasEcuaciones();
            PasoDeLosBits(confianzas);

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
    /// Cada ecuacion le dice a cada uno de sus bits lo que deduce de los demas.
    /// </summary>
    /// <remarks>
    /// La cuenta exacta seria multiplicar las tangentes hiperbolicas de las medias confianzas de
    /// todos los demas bits. Para no repetir el producto una vez por bit se calcula el producto
    /// entero y se divide por el del bit que toca, con la precaucion de tratar aparte el caso en
    /// que alguno valga cero.
    /// </remarks>
    private void PasoDeLasEcuaciones()
    {
        for (var e = 0; e < _codigo.Ecuaciones; e++)
        {
            var desde = _primeraAristaDeCadaEcuacion[e];
            var hasta = _primeraAristaDeCadaEcuacion[e + 1];

            // Se lleva el signo aparte y la magnitud por el metodo del minimo, que es estable
            // y no se va a infinito cuando una confianza es muy alta.
            var signo = 1;
            var menor = float.MaxValue;
            var siguienteMenor = float.MaxValue;
            var aristaDelMenor = -1;

            for (var a = desde; a < hasta; a++)
            {
                var m = _mensajeDeBitAEcuacion[a];
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

            // Factor de correccion del metodo del minimo: sin el, la confianza sale exagerada y
            // el decodificador se convence demasiado pronto de cosas que no son.
            const float Atenuacion = 0.75f;

            for (var a = desde; a < hasta; a++)
            {
                var m = _mensajeDeBitAEcuacion[a];
                var signoSinEste = m < 0 ? -signo : signo;
                var magnitud = a == aristaDelMenor ? siguienteMenor : menor;
                if (magnitud > TopeDeConfianza) magnitud = TopeDeConfianza;
                _mensajeDeEcuacionABit[a] = signoSinEste * magnitud * Atenuacion;
            }
        }
    }

    /// <summary>Cada bit suma lo que le llega de sus ecuaciones y reparte de vuelta.</summary>
    private void PasoDeLosBits(ReadOnlySpan<float> confianzas)
    {
        for (var v = 0; v < _codigo.Longitud; v++) _opinionTotal[v] = confianzas[v];
        for (var a = 0; a < _mensajeDeEcuacionABit.Length; a++)
            _opinionTotal[_variableDeCadaArista[a]] += _mensajeDeEcuacionABit[a];

        for (var a = 0; a < _mensajeDeEcuacionABit.Length; a++)
        {
            // A cada ecuacion se le devuelve la opinion del bit descontando lo que ella misma
            // aporto: si no, la ecuacion se oiria a si misma y se autoconvenceria.
            var v = _variableDeCadaArista[a];
            _mensajeDeBitAEcuacion[a] = Recortar(_opinionTotal[v] - _mensajeDeEcuacionABit[a]);
        }
    }

    private static float Recortar(float valor) =>
        valor > TopeDeConfianza ? TopeDeConfianza : valor < -TopeDeConfianza ? -TopeDeConfianza : valor;
}
