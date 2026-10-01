namespace Nodisla.Cuaderno.Modos.ReedSolomon;

/// <summary>
/// Un codigo Reed-Solomon RS(n, k) sobre un cuerpo de Galois, con codificador sistematico y
/// decodificador algebraico de errores y borrados.
/// </summary>
/// <remarks>
/// <para>
/// <b>Convenio de posiciones.</b> La palabra de codigo es un polinomio <c>c(x)</c> de grado
/// menor que <c>n</c>, y <c>palabra[i]</c> es el coeficiente de <c>x^i</c>. Los simbolos del
/// mensaje van en los grados altos (<c>palabra[n−k..n−1]</c>, con <c>mensaje[j]</c> en
/// <c>x^(n−k+j)</c>) y la paridad en los bajos (<c>palabra[0..n−k−1]</c>). Es el convenio con el
/// que JT65 saca los simbolos al aire —los 51 de paridad primero y los 12 del mensaje al
/// final— y asi no hay que dar la vuelta a nada entre el codigo y el modulador.
/// </para>
/// <para>
/// <b>Generador.</b> <c>g(x) = (x − α^fcr)(x − α^(fcr+1))···(x − α^(fcr+2t−1))</c>. Toda palabra
/// de codigo es multiplo de <c>g(x)</c>, y por tanto se anula en esas <c>2t</c> raices; eso es lo
/// que miden los sindromes.
/// </para>
/// <para>
/// <b>Decodificacion.</b> Es la clasica de los libros, sin atajos: sindromes; polinomio
/// localizador de borrados; sindromes modificados de Forney y Berlekamp-Massey sobre ellos
/// para encontrar el localizador de errores; busqueda de Chien para hallar las posiciones;
/// formula de Forney para los valores. Con <c>e</c> borrados y <c>t'</c> errores se
/// corrige mientras <c>e + 2t' ≤ 2t</c>.
/// </para>
/// <para>
/// El decodificador esta pensado para que el decodificador blando lo llame miles de veces
/// sobre la misma palabra con distintos borrados: los sindromes se calculan una sola vez y se
/// pasan ya hechos, y no se reserva memoria en el bucle.
/// </para>
/// <para>
/// Escrito desde los libros (Lin y Costello, <i>Error Control Coding</i>, cap. 7; Blahut,
/// <i>Algebraic Codes for Data Transmission</i>, cap. 7); no se ha mirado codigo de terceros.
/// </para>
/// </remarks>
public sealed class CodigoReedSolomon
{
    private readonly int[] _logDelGenerador;

    /// <summary>Crea el codigo.</summary>
    /// <param name="campo">Cuerpo de los simbolos.</param>
    /// <param name="simbolosDeMensaje">Simbolos del mensaje, <c>k</c>.</param>
    /// <param name="primeraRaiz">Exponente de la primera raiz del generador, <c>fcr</c>.</param>
    /// <param name="longitud">Longitud de la palabra, <c>n</c>; por omision el orden del cuerpo (codigo primitivo).</param>
    public CodigoReedSolomon(CampoDeGalois campo, int simbolosDeMensaje, int primeraRaiz, int? longitud = null)
    {
        ArgumentNullException.ThrowIfNull(campo);
        Campo = campo;
        Longitud = longitud ?? campo.Orden;
        if (Longitud > campo.Orden) throw new ArgumentOutOfRangeException(nameof(longitud));
        if (simbolosDeMensaje <= 0 || simbolosDeMensaje >= Longitud) throw new ArgumentOutOfRangeException(nameof(simbolosDeMensaje));
        SimbolosDeMensaje = simbolosDeMensaje;
        Raices = Longitud - simbolosDeMensaje;
        PrimeraRaiz = primeraRaiz;

        // g(x) se construye multiplicando los factores (x - alfa^i) uno a uno. Se guarda con el
        // coeficiente de x^0 en la posicion 0; el de x^(2t) es 1 y no hace falta guardarlo.
        var generador = new int[Raices + 1];
        generador[0] = 1;
        for (var i = 0; i < Raices; i++)
        {
            var raiz = campo.Exp(primeraRaiz + i);
            for (var j = i + 1; j > 0; j--)
                generador[j] = generador[j - 1] ^ campo.Multiplicar(generador[j], raiz);
            generador[0] = campo.Multiplicar(generador[0], raiz);
        }
        _logDelGenerador = new int[Raices];
        for (var i = 0; i < Raices; i++) _logDelGenerador[i] = campo.Log(generador[i]);
    }

    /// <summary>Cuerpo de los simbolos.</summary>
    public CampoDeGalois Campo { get; }

    /// <summary>Simbolos por palabra, <c>n</c>.</summary>
    public int Longitud { get; }

    /// <summary>Simbolos de mensaje por palabra, <c>k</c>.</summary>
    public int SimbolosDeMensaje { get; }

    /// <summary>Simbolos de paridad, <c>n − k</c>, que son tambien las raices del generador.</summary>
    public int Raices { get; }

    /// <summary>Exponente de la primera raiz del generador.</summary>
    public int PrimeraRaiz { get; }

    /// <summary>Errores que corrige sin borrados, <c>t = (n − k) / 2</c>.</summary>
    public int ErroresCorregibles => Raices / 2;

    /// <summary>
    /// El RS(63,12) de JT65 sobre GF(64): 12 simbolos de mensaje, 51 de paridad, generador con
    /// raices <c>α^3..α^53</c>.
    /// </summary>
    /// <remarks>
    /// Los parametros (cuerpo <c>x^6+x+1</c>, primera raiz 3, raices consecutivas) son
    /// constantes del protocolo JT65, tomadas de su descripcion publicada. Ver
    /// <c>Tablas/LEEME-jt65-jt9.md</c>.
    /// </remarks>
    public static CodigoReedSolomon Jt65 { get; } = new(CampoDeGalois.Gf64, 12, 3);

    /// <summary>
    /// Codifica un mensaje de forma sistematica.
    /// </summary>
    /// <param name="mensaje"><c>k</c> simbolos.</param>
    /// <param name="palabra">Destino de los <c>n</c> simbolos: paridad en las posiciones bajas, mensaje en las altas.</param>
    public void Codificar(ReadOnlySpan<byte> mensaje, Span<byte> palabra)
    {
        if (mensaje.Length != SimbolosDeMensaje) throw new ArgumentException($"Hacen falta {SimbolosDeMensaje} simbolos.", nameof(mensaje));
        if (palabra.Length != Longitud) throw new ArgumentException($"Hacen falta {Longitud} posiciones.", nameof(palabra));

        // Division larga de x^(n-k)·m(x) entre g(x): el resto es la paridad. Se procesa el
        // mensaje del grado mas alto al mas bajo, como en un registro de desplazamiento.
        Span<int> resto = stackalloc int[Raices];
        resto.Clear();
        for (var j = SimbolosDeMensaje - 1; j >= 0; j--)
        {
            var realimentacion = mensaje[j] ^ resto[Raices - 1];
            for (var i = Raices - 1; i > 0; i--)
                resto[i] = resto[i - 1] ^ Campo.MultiplicarPorLog(realimentacion, _logDelGenerador[i]);
            resto[0] = Campo.MultiplicarPorLog(realimentacion, _logDelGenerador[0]);
        }

        for (var i = 0; i < Raices; i++) palabra[i] = (byte)resto[i];
        for (var j = 0; j < SimbolosDeMensaje; j++) palabra[Raices + j] = mensaje[j];
    }

    /// <summary>Los simbolos de mensaje de una palabra (las posiciones altas).</summary>
    public void ExtraerMensaje(ReadOnlySpan<byte> palabra, Span<byte> mensaje)
    {
        for (var j = 0; j < SimbolosDeMensaje; j++) mensaje[j] = palabra[Raices + j];
    }

    /// <summary>
    /// Calcula los sindromes de una palabra recibida: <c>S_j = r(α^(fcr+j))</c>.
    /// </summary>
    /// <param name="recibida"><c>n</c> simbolos.</param>
    /// <param name="sindromes">Destino de los <c>2t</c> sindromes.</param>
    /// <returns>Verdadero si todos son cero, es decir, si la palabra ya es de codigo.</returns>
    public bool Sindromes(ReadOnlySpan<byte> recibida, Span<int> sindromes)
    {
        if (recibida.Length != Longitud) throw new ArgumentException($"Hacen falta {Longitud} simbolos.", nameof(recibida));
        if (sindromes.Length < Raices) throw new ArgumentException($"Hacen falta {Raices} sindromes.", nameof(sindromes));

        var todosCero = true;
        for (var j = 0; j < Raices; j++)
        {
            // Horner desde el grado mas alto.
            var logRaiz = (PrimeraRaiz + j) % Campo.Orden;
            var s = 0;
            for (var i = Longitud - 1; i >= 0; i--)
                s = Campo.MultiplicarPorLog(s, logRaiz) ^ recibida[i];
            sindromes[j] = s;
            if (s != 0) todosCero = false;
        }
        return todosCero;
    }

    /// <summary>
    /// Corrige una palabra recibida, con los sindromes ya calculados y una lista de posiciones borradas.
    /// </summary>
    /// <param name="palabra">Palabra recibida; si se corrige, sale corregida en el sitio.</param>
    /// <param name="sindromes">Sindromes de <paramref name="palabra"/> (ver <see cref="Sindromes"/>).</param>
    /// <param name="borrados">Posiciones cuyo simbolo se da por desconocido.</param>
    /// <param name="corregidas">Posiciones que cambiaron de valor.</param>
    /// <returns>
    /// Verdadero si se llego a una palabra de codigo dentro de la capacidad del codigo. Falso
    /// si no: la palabra queda como estaba.
    /// </returns>
    /// <remarks>
    /// No reserva memoria en el monton: el decodificador blando lo llama miles de veces por palabra.
    /// </remarks>
    public bool TryCorregir(Span<byte> palabra, ReadOnlySpan<int> sindromes, ReadOnlySpan<int> borrados, out int corregidas)
    {
        corregidas = 0;
        if (palabra.Length != Longitud) throw new ArgumentException($"Hacen falta {Longitud} simbolos.", nameof(palabra));
        var e = borrados.Length;
        if (e > Raices) return false;

        var campo = Campo;
        var orden = campo.Orden;
        var dosT = Raices;

        var todosCero = true;
        for (var j = 0; j < dosT; j++) if (sindromes[j] != 0) { todosCero = false; break; }
        if (todosCero) return true;

        // Localizador de borrados: Gamma(x) = prod (1 - X_l x), con X_l = alfa^posicion.
        Span<int> gamma = stackalloc int[dosT + 1];
        gamma.Clear();
        gamma[0] = 1;
        for (var l = 0; l < e; l++)
        {
            var x = campo.Exp(borrados[l]);
            for (var i = l + 1; i > 0; i--)
                gamma[i] ^= campo.Multiplicar(gamma[i - 1], x);
        }

        // Sindromes modificados (Forney): Xi(x) = Gamma(x) S(x) mod x^(2t). Los e primeros no
        // dicen nada de los errores; a partir de ahi la secuencia la genera solo el localizador
        // de errores, y ese es el que se busca con Berlekamp-Massey de toda la vida.
        Span<int> xi = stackalloc int[dosT];
        for (var j = 0; j < dosT; j++)
        {
            var suma = 0;
            for (var i = 0; i <= j && i <= e; i++)
                suma ^= campo.Multiplicar(gamma[i], sindromes[j - i]);
            xi[j] = suma;
        }

        Span<int> lambdaErrores = stackalloc int[dosT + 1];
        Span<int> b = stackalloc int[dosT + 1];
        Span<int> t = stackalloc int[dosT + 1];
        lambdaErrores.Clear();
        b.Clear();
        lambdaErrores[0] = 1;
        b[0] = 1;
        var longitudDelRegistro = 0;
        var cuantos = dosT - e;
        for (var m = 0; m < cuantos; m++)
        {
            var discrepancia = 0;
            for (var i = 0; i <= longitudDelRegistro && i <= m; i++)
                discrepancia ^= campo.Multiplicar(lambdaErrores[i], xi[e + m - i]);

            if (discrepancia == 0)
            {
                for (var i = dosT; i > 0; i--) b[i] = b[i - 1];
                b[0] = 0;
                continue;
            }

            var logDiscrepancia = campo.Log(discrepancia);
            t[0] = lambdaErrores[0];
            for (var i = 1; i <= dosT; i++)
                t[i] = lambdaErrores[i] ^ campo.MultiplicarPorLog(b[i - 1], logDiscrepancia);

            if (2 * longitudDelRegistro <= m)
            {
                var logInverso = orden - logDiscrepancia;
                for (var i = 0; i <= dosT; i++) b[i] = campo.MultiplicarPorLog(lambdaErrores[i], logInverso);
                longitudDelRegistro = m + 1 - longitudDelRegistro;
            }
            else
            {
                for (var i = dosT; i > 0; i--) b[i] = b[i - 1];
                b[0] = 0;
            }
            t.CopyTo(lambdaErrores);
        }

        var gradoDeErrores = 0;
        for (var i = dosT; i >= 0; i--) if (lambdaErrores[i] != 0) { gradoDeErrores = i; break; }
        // Con e borrados y nu errores hacen falta e + 2 nu <= 2t.
        if (gradoDeErrores != longitudDelRegistro || (2 * gradoDeErrores) + e > dosT) return false;

        // Localizador completo: Lambda(x) = Gamma(x) LambdaErrores(x), de grado e + nu.
        Span<int> lambda = stackalloc int[dosT + 1];
        lambda.Clear();
        for (var i = 0; i <= e; i++)
            for (var j = 0; j <= gradoDeErrores && i + j <= dosT; j++)
                lambda[i + j] ^= campo.Multiplicar(gamma[i], lambdaErrores[j]);

        var grado = e + gradoDeErrores;
        if (lambda[grado] == 0) return false;

        // Chien: se prueban todas las posiciones. Lambda(X^-1) = 0 marca una posicion errada.
        Span<int> logLambda = stackalloc int[dosT + 1];
        for (var i = 0; i <= grado; i++) logLambda[i] = lambda[i] == 0 ? -1 : campo.Log(lambda[i]);
        Span<int> posiciones = stackalloc int[dosT];
        var encontradas = 0;
        for (var p = 0; p < Longitud; p++)
        {
            var logXInverso = (orden - p) % orden;
            var suma = 0;
            for (var i = 0; i <= grado; i++)
                if (logLambda[i] >= 0) suma ^= campo.Exp(logLambda[i] + (i * logXInverso));
            if (suma == 0)
            {
                if (encontradas == grado) return false;
                posiciones[encontradas++] = p;
            }
        }
        if (encontradas != grado) return false;

        // Omega(x) = S(x) Lambda(x) mod x^(2t)
        Span<int> omega = stackalloc int[dosT];
        for (var i = 0; i < dosT; i++)
        {
            var suma = 0;
            for (var j = 0; j <= i && j <= grado; j++)
                suma ^= campo.Multiplicar(lambda[j], sindromes[i - j]);
            omega[i] = suma;
        }

        // Forney: e_l = X_l^(1-fcr) Omega(X_l^-1) / Lambda'(X_l^-1); en caracteristica dos la
        // derivada formal solo conserva los terminos de grado impar.
        Span<int> valores = stackalloc int[dosT];
        for (var l = 0; l < grado; l++)
        {
            var p = posiciones[l];
            var xInverso = campo.Exp(-p);
            var numerador = 0;
            var potencia = 1;
            for (var i = 0; i < dosT; i++)
            {
                numerador ^= campo.Multiplicar(omega[i], potencia);
                potencia = campo.Multiplicar(potencia, xInverso);
            }
            var denominador = 0;
            potencia = 1;
            var xInversoAlCuadrado = campo.Multiplicar(xInverso, xInverso);
            for (var i = 1; i <= grado; i += 2)
            {
                denominador ^= campo.Multiplicar(lambda[i], potencia);
                potencia = campo.Multiplicar(potencia, xInversoAlCuadrado);
            }
            if (denominador == 0) return false;
            var valor = campo.Dividir(numerador, denominador);
            valores[l] = campo.Multiplicar(valor, campo.Exp(p * (1 - PrimeraRaiz)));
        }

        // Se aplica y se comprueba de verdad: la palabra corregida tiene que tener todos los
        // sindromes a cero. Si no, algo se ha colado y se deja como estaba.
        for (var l = 0; l < grado; l++) palabra[posiciones[l]] ^= (byte)valores[l];
        Span<int> comprobacion = stackalloc int[dosT];
        if (!Sindromes(palabra, comprobacion))
        {
            for (var l = 0; l < grado; l++) palabra[posiciones[l]] ^= (byte)valores[l];
            return false;
        }

        for (var l = 0; l < grado; l++) if (valores[l] != 0) corregidas++;
        return true;
    }

    /// <summary>
    /// Corrige una palabra recibida calculando antes los sindromes.
    /// </summary>
    /// <param name="palabra">Palabra recibida; sale corregida en el sitio si se pudo.</param>
    /// <param name="borrados">Posiciones borradas.</param>
    /// <param name="corregidas">Posiciones que cambiaron de valor.</param>
    public bool TryCorregir(Span<byte> palabra, ReadOnlySpan<int> borrados, out int corregidas)
    {
        Span<int> sindromes = stackalloc int[Raices];
        Sindromes(palabra, sindromes);
        return TryCorregir(palabra, sindromes, borrados, out corregidas);
    }
}
