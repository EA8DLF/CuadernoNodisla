namespace Nodisla.Cuaderno.Modos.ReedSolomon;

/// <summary>Como fue una decodificacion blanda, para poder medirla.</summary>
/// <param name="Decodificada">Se llego a una palabra de codigo que pasa el criterio de aceptacion.</param>
/// <param name="Puntuacion">Verosimilitud de la palabra aceptada (o de la mejor rechazada); ver <see cref="DecodificadorBlando.PuntuacionMinima"/>.</param>
/// <param name="Corregidas">Simbolos en que la palabra aceptada difiere de la decision dura.</param>
/// <param name="Intentos">Intentos que se gastaron.</param>
/// <param name="PalabrasRechazadas">Palabras de codigo que salieron de algun intento y no pasaron el criterio.</param>
public readonly record struct ResultadoBlando(bool Decodificada, double Puntuacion, int Corregidas, int Intentos, int PalabrasRechazadas);

/// <summary>
/// Decodificador Reed-Solomon con decision blanda: borrados probabilisticos y Berlekamp-Massey
/// repetido, con un criterio de aceptacion estricto.
/// </summary>
/// <remarks>
/// <para>
/// <b>La idea.</b> Un decodificador algebraico solo sabe corregir <c>t = 25</c> errores en
/// JT65, y a −24 dB la mitad de los 63 simbolos llegan mal. Pero el demodulador sabe algo mas
/// que el simbolo mas probable de cada posicion: sabe <i>cuanto</i> destaca sobre el segundo.
/// Si se le dice al decodificador «estos simbolos no te los creas» (borrados), corrige
/// <c>e + 2t' ≤ 51</c>: con 40 borrados aun corrige 5 errores mas entre los 23 que quedan. El
/// truco es que no se sabe con certeza cuales borrar; asi que se prueba muchas veces con
/// conjuntos de borrados elegidos al azar, mas probables cuanto menos fiable es el simbolo, y
/// se mira si alguna vez sale una palabra de codigo creible. Es el planteamiento que Franke y
/// Taylor describen para JT65 en QEX (2016); esta es una implementacion propia de esa idea,
/// no de su codigo, con su propio reparto de borrados y su propia metrica.
/// </para>
/// <para>
/// <b>El peligro.</b> Con 51 borrados cualquier eleccion de 12 simbolos define una palabra de
/// codigo: el decodificador algebraico <i>siempre</i> devuelve algo. Por eso hacen falta dos
/// cosas: un tope de borrados por intento (para que las palabras que salgan tengan que estar
/// apoyadas en bastantes simbolos de verdad) y un criterio de aceptacion que compare la
/// palabra con lo que oyo el demodulador y tire las que no cuadran.
/// </para>
/// <para>
/// <b>El criterio de aceptacion.</b> A cada palabra de codigo que sale de un intento se le
/// calcula su verosimilitud frente a la hipotesis de que ahi solo hay ruido: para cada posicion,
/// el logaritmo de la razon de verosimilitudes de que el tono de la palabra lleve la senal
/// (<c>ln I0(2·sqrt(s·z)) − s</c>, con <c>z</c> la potencia de esa casilla en unidades de ruido
/// y <c>s</c> la relacion senal-ruido por simbolo, que es la estadistica de la FSK no
/// coherente), sumado sobre las 63 posiciones. Una palabra verdadera suma una cantidad grande y
/// positiva en cada posicion, porque su tono lleva la senal; una inventada solo suma en las
/// pocas posiciones que se conservaron (que son maximos del ruido) y resta en todas las demas,
/// donde su tono es una casilla cualquiera de ruido. A −24 dB la verdadera puntua unos 140 y
/// las inventadas quedan por debajo de cero; el umbral se pone entre las dos poblaciones
/// midiendo, con ruido puro y con senales de nivel conocido, no a ojo.
/// </para>
/// </remarks>
public sealed class DecodificadorBlando
{
    private readonly CodigoReedSolomon _codigo;
    private readonly int _n;
    private readonly int _q;

    /// <summary>Crea el decodificador para un codigo.</summary>
    public DecodificadorBlando(CodigoReedSolomon codigo)
    {
        ArgumentNullException.ThrowIfNull(codigo);
        _codigo = codigo;
        _n = codigo.Longitud;
        _q = codigo.Campo.Tamano;
    }

    /// <summary>Codigo con el que trabaja.</summary>
    public CodigoReedSolomon Codigo => _codigo;

    /// <summary>Intentos con borrados al azar, como mucho, por palabra.</summary>
    public int Intentos { get; set; } = 10000;

    /// <summary>
    /// Verosimilitud minima que se acepta.
    /// </summary>
    /// <remarks>
    /// Medida en el banco: las palabras verdaderas de JT65 en el filo (−24/−25 dB) puntuan por
    /// encima de 80 casi siempre; las inventadas con ruido puro no pasan de 20. Ver
    /// <c>resultados-jt65.md</c>.
    /// </remarks>
    public double PuntuacionMinima { get; set; } = 45;

    /// <summary>
    /// Simbolos corregidos como mucho respecto a la decision dura, sea cual sea la senal. Por
    /// debajo de este tope manda otro que depende de la relacion senal-ruido: los errores duros
    /// esperados (<see cref="ProbabilidadDeErrorDeSimbolo"/>) mas cuatro desviaciones.
    /// </summary>
    /// <remarks>
    /// Una palabra que difiere de lo oido en mas simbolos que esto no se sostiene: ni siquiera
    /// la verdadera llegaria por aqui, porque quedarian menos simbolos buenos que los que hacen
    /// falta para determinarla.
    /// </remarks>
    public int CorreccionesMaximas { get; set; } = 48;

    /// <summary>Borrados minimos por intento al azar.</summary>
    public int BorradosMinimos { get; set; } = 40;

    /// <summary>Borrados maximos por intento al azar (nunca mas de <c>n − k − 4</c>).</summary>
    public int BorradosMaximos { get; set; } = 47;

    /// <summary>
    /// Cuanto pesa la probabilidad de acierto de cada simbolo al elegir cuales se conservan: a
    /// mas exponente, mas deterministas los intentos.
    /// </summary>
    public double ExponenteDeFiabilidad { get; set; } = 1.0;

    /// <summary>Intentos deterministas al principio: se borran los <c>e</c> simbolos menos fiables para varios <c>e</c>.</summary>
    public int PasoDeBorradosFijos { get; set; } = 4;

    /// <summary>
    /// Intenta decodificar a partir de las potencias medidas.
    /// </summary>
    /// <param name="potencias">
    /// <c>n × q</c> valores, posicion a posicion: la potencia de cada uno de los <c>q</c>
    /// simbolos posibles en cada posicion de la palabra, ya divididas por la potencia media del
    /// ruido en una casilla.
    /// </param>
    /// <param name="palabra">Destino de los <c>n</c> simbolos si se decodifica.</param>
    /// <param name="azar">Generador de numeros al azar, para que la prueba se pueda repetir.</param>
    /// <param name="relacionPorSimbolo">
    /// Potencia de la senal partida por la del ruido en una casilla, estimada aparte (en JT65,
    /// con el tono de sincronismo). Con ella se calcula la probabilidad de que cada decision dura
    /// sea la buena; si no se conoce, se estima de las propias potencias.
    /// </param>
    /// <returns>Lo que paso.</returns>
    public ResultadoBlando Decodificar(ReadOnlySpan<float> potencias, Span<byte> palabra, Random azar, double relacionPorSimbolo = 0)
    {
        ArgumentNullException.ThrowIfNull(azar);
        if (potencias.Length != _n * _q) throw new ArgumentException($"Hacen falta {_n * _q} potencias.", nameof(potencias));
        if (palabra.Length != _n) throw new ArgumentException($"Hacen falta {_n} posiciones.", nameof(palabra));

        Span<byte> dura = stackalloc byte[_n];
        Span<float> mejor = stackalloc float[_n];
        Span<double> fiabilidad = stackalloc double[_n];
        double sumaDeMaximos = 0;
        for (var i = 0; i < _n; i++)
        {
            var fila = potencias.Slice(i * _q, _q);
            var p1 = float.MinValue;
            var h = 0;
            for (var v = 0; v < _q; v++)
                if (fila[v] > p1) { p1 = fila[v]; h = v; }
            dura[i] = (byte)h;
            mejor[i] = p1;
            sumaDeMaximos += p1;
        }

        // Si no se conoce la relacion senal-ruido por simbolo, se estima: la potencia media del
        // ganador es aproximadamente la de la senal mas el maximo de q-1 casillas de ruido
        // (cuyo valor esperado es la suma armonica H(q-1)).
        var s = relacionPorSimbolo;
        if (s <= 0)
        {
            var maximoDeRuido = 0.0;
            for (var k = 1; k < _q; k++) maximoDeRuido += 1.0 / k;
            s = Math.Max(0.5, (sumaDeMaximos / _n) - maximoDeRuido);
        }

        // Probabilidad de que la decision dura de cada posicion sea la buena. En FSK no coherente
        // con ruido gaussiano, la verosimilitud de que el tono k lleve la senal es proporcional a
        // I0(2·sqrt(s·z_k)), con z_k la potencia de la casilla en unidades de ruido; se normaliza
        // sobre los q tonos. Es la misma cuenta que hace un demodulador optimo, y da una
        // probabilidad de verdad, no un cociente a ojo.
        // Tope de correcciones para esta relacion: los errores duros que se esperan con s mas
        // cuatro desviaciones. Con senal fuerte casi no hay errores y una palabra que corrige
        // treinta simbolos es otra cosa (una candidata desalineada, otra senal); sin este tope,
        // como la verosimilitud crece con s, una palabra basura apoyada en unos pocos simbolos
        // muy fuertes sumaria mas que el umbral.
        var errorPorSimbolo = ProbabilidadDeErrorDeSimbolo(s, _q);
        var esperados = _n * errorPorSimbolo;
        _topeDeCorrecciones = Math.Min(CorreccionesMaximas,
            (int)Math.Ceiling(esperados + (4 * Math.Sqrt(esperados * (1 - errorPorSimbolo))) + 3));

        var raizDeS = Math.Sqrt(s);
        var verosimilitud = new float[_n * _q];
        for (var i = 0; i < _n; i++)
        {
            var fila = potencias.Slice(i * _q, _q);
            var filaDeVerosimilitud = verosimilitud.AsSpan(i * _q, _q);
            for (var v = 0; v < _q; v++)
                filaDeVerosimilitud[v] = (float)(LogBesselI0(2 * raizDeS * Math.Sqrt(Math.Max(0, fila[v]))) - s);
            var lMaximo = filaDeVerosimilitud[dura[i]];
            double suma = 0;
            for (var v = 0; v < _q; v++) suma += Math.Exp(filaDeVerosimilitud[v] - lMaximo);
            var acierto = 1.0 / suma;
            // Peso para conservar el simbolo: sus probabilidades a favor.
            fiabilidad[i] = Math.Min(1e6, acierto / Math.Max(1e-6, 1 - acierto));
        }

        Span<int> sindromes = stackalloc int[_codigo.Raices];
        var yaEsDeCodigo = _codigo.Sindromes(dura, sindromes);
        Span<byte> candidata = stackalloc byte[_n];

        // Primero la decision dura tal cual: con senal holgada basta y no cuesta nada.
        dura.CopyTo(candidata);
        var rechazadas = 0;
        var mejorPuntuacion = double.NegativeInfinity;
        if (yaEsDeCodigo || _codigo.TryCorregir(candidata, sindromes, [], out _))
        {
            var d = Puntuar(verosimilitud, dura, candidata, out var corregidas);
            if (Acepta(d, corregidas))
            {
                candidata.CopyTo(palabra);
                return new ResultadoBlando(true, d, corregidas, 0, 0);
            }
            rechazadas++;
            mejorPuntuacion = d;
        }

        Span<double> claves = stackalloc double[_n];
        Span<int> orden = stackalloc int[_n];
        Span<int> borrados = stackalloc int[_n];
        var borradosMaximos = Math.Min(BorradosMaximos, _codigo.Raices - 4);
        var borradosMinimos = Math.Clamp(BorradosMinimos, 0, borradosMaximos);
        var mejorCorregidas = 0;
        var intento = 0;

        // Primero unos intentos deterministas: se borran los e menos fiables, para varios e.
        // Con senal razonable esto ya resuelve y no gasta azar.
        for (var i = 0; i < _n; i++) { claves[i] = fiabilidad[i]; orden[i] = i; }
        OrdenarPorClave(claves, orden);
        var paso = Math.Max(1, PasoDeBorradosFijos);
        for (var e = paso; e <= borradosMaximos; e += paso)
        {
            intento++;
            for (var i = 0; i < e; i++) borrados[i] = orden[i];
            dura.CopyTo(candidata);
            if (!_codigo.TryCorregir(candidata, sindromes, borrados[..e], out _)) continue;
            var d = Puntuar(verosimilitud, dura, candidata, out var corregidas);
            if (Acepta(d, corregidas))
            {
                candidata.CopyTo(palabra);
                return new ResultadoBlando(true, d, corregidas, intento, rechazadas);
            }
            rechazadas++;
            if (d > mejorPuntuacion) { mejorPuntuacion = d; mejorCorregidas = corregidas; }
        }

        // Luego los intentos con borrados al azar: se eligen los simbolos que se conservan por
        // muestreo ponderado sin reemplazo, con peso creciente con la probabilidad de acierto
        // (Efraimidis y Spirakis: clave u^(1/peso), se conservan las claves mas altas). Asi los
        // simbolos claros casi siempre se quedan, los dudosos van y vienen, y cada intento prueba
        // un subconjunto distinto.
        for (; intento < Intentos; intento++)
        {
            for (var i = 0; i < _n; i++)
            {
                var peso = Math.Pow(fiabilidad[i], ExponenteDeFiabilidad);
                claves[i] = Math.Pow(azar.NextDouble(), 1.0 / peso);
                orden[i] = i;
            }
            var cuantosBorrar = azar.Next(borradosMinimos, borradosMaximos + 1);
            // Los cuantosBorrar de clave mas baja se borran. Seleccion parcial por ordenacion
            // completa: n es 63 y esto no es lo que cuesta.
            OrdenarPorClave(claves, orden);
            for (var i = 0; i < cuantosBorrar; i++) borrados[i] = orden[i];

            dura.CopyTo(candidata);
            if (!_codigo.TryCorregir(candidata, sindromes, borrados[..cuantosBorrar], out _)) continue;

            var d = Puntuar(verosimilitud, dura, candidata, out var corregidas);
            if (Acepta(d, corregidas))
            {
                candidata.CopyTo(palabra);
                return new ResultadoBlando(true, d, corregidas, intento, rechazadas);
            }
            rechazadas++;
            if (d > mejorPuntuacion) { mejorPuntuacion = d; mejorCorregidas = corregidas; }
        }

        return new ResultadoBlando(false, mejorPuntuacion, mejorCorregidas, Intentos, rechazadas);
    }

    /// <summary>
    /// Logaritmo de la funcion de Bessel modificada de primera especie y orden cero.
    /// </summary>
    /// <remarks>
    /// Aproximaciones polinomicas de Abramowitz y Stegun (9.8.1 y 9.8.2), con error relativo
    /// menor de dos diezmillonesimas. Para argumentos grandes se usa la forma asintotica, que
    /// es la que evita desbordar: I0(700) no cabe en un double, pero su logaritmo si.
    /// </remarks>
    public static double LogBesselI0(double x)
    {
        var ax = Math.Abs(x);
        if (ax < 3.75)
        {
            var y = (x / 3.75) * (x / 3.75);
            var i0 = 1.0 + (y * (3.5156229 + (y * (3.0899424 + (y * (1.2067492 + (y * (0.2659732 + (y * (0.0360768 + (y * 0.0045813)))))))))));
            return Math.Log(i0);
        }
        var t = 3.75 / ax;
        var cola = 0.39894228 + (t * (0.01328592 + (t * (0.00225319 + (t * (-0.00157565 + (t * (0.00916281 + (t * (-0.02057706 + (t * (0.02635537 + (t * (-0.01647633 + (t * 0.00392377)))))))))))))));
        return ax - (0.5 * Math.Log(ax)) + Math.Log(cola);
    }

    private int _topeDeCorrecciones;

    private bool Acepta(double puntuacion, int corregidas) =>
        puntuacion >= PuntuacionMinima && corregidas <= _topeDeCorrecciones;

    /// <summary>
    /// Probabilidad de que la decision dura de un simbolo sea erronea en FSK no coherente de
    /// <paramref name="q"/> tonos con relacion <paramref name="s"/> por simbolo (potencia de la
    /// senal partida por la del ruido en una casilla).
    /// </summary>
    /// <remarks>
    /// Es la integral de libro (Proakis, <i>Digital Communications</i>, FSK ortogonal no
    /// coherente): la casilla buena tiene distribucion de Rice y el simbolo sale bien si las
    /// q−1 de ruido, exponenciales, quedan todas por debajo. La forma cerrada es una suma
    /// alternada con coeficientes binomiales enormes que no se puede evaluar en coma flotante,
    /// asi que se integra numericamente en logaritmos.
    /// </remarks>
    public static double ProbabilidadDeErrorDeSimbolo(double s, int q)
    {
        if (s <= 0) return 1.0 - (1.0 / q);
        var hasta = s + (12 * Math.Sqrt(s + 1)) + 40;
        const int Pasos = 4000;
        var paso = hasta / Pasos;
        double acierto = 0;
        for (var i = 0; i <= Pasos; i++)
        {
            var z = i * paso;
            var logDensidad = -(z + s) + LogBesselI0(2 * Math.Sqrt(s * z));
            var todasDebajo = z <= 0 ? 0 : Math.Exp((q - 1) * Math.Log(-double.ExpM1(-z)));
            var peso = i == 0 || i == Pasos ? 0.5 : 1.0;
            acierto += peso * Math.Exp(logDensidad) * todasDebajo;
        }
        return Math.Clamp(1 - (acierto * paso), 0, 1);
    }

    /// <summary>
    /// Verosimilitud de una palabra frente al ruido: suma, posicion a posicion, del logaritmo de
    /// la razon de verosimilitudes de que su tono lleve la senal.
    /// </summary>
    private double Puntuar(ReadOnlySpan<float> verosimilitud, ReadOnlySpan<byte> dura, ReadOnlySpan<byte> candidata, out int corregidas)
    {
        double puntuacion = 0;
        corregidas = 0;
        for (var i = 0; i < _n; i++)
        {
            if (candidata[i] != dura[i]) corregidas++;
            puntuacion += verosimilitud[(i * _q) + candidata[i]];
        }
        return puntuacion;
    }

    private static void OrdenarPorClave(Span<double> claves, Span<int> orden)
    {
        // Insercion: 63 elementos casi siempre y sin reservar nada.
        for (var i = 1; i < orden.Length; i++)
        {
            var k = claves[i];
            var o = orden[i];
            var j = i - 1;
            while (j >= 0 && claves[j] > k)
            {
                claves[j + 1] = claves[j];
                orden[j + 1] = orden[j];
                j--;
            }
            claves[j + 1] = k;
            orden[j + 1] = o;
        }
    }
}
