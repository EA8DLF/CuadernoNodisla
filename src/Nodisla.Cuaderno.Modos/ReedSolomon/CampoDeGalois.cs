namespace Nodisla.Cuaderno.Modos.ReedSolomon;

/// <summary>
/// Un cuerpo finito de caracteristica dos, GF(2^m), con sus tablas de logaritmos.
/// </summary>
/// <remarks>
/// <para>
/// Es la aritmetica sobre la que se construye un codigo Reed-Solomon: los simbolos de la
/// palabra son elementos del cuerpo, y sumar, multiplicar y dividir simbolos es lo que hace el
/// codificador y el decodificador todo el rato. En caracteristica dos la suma es la o exclusiva
/// bit a bit; la multiplicacion se hace por tablas de logaritmos y antilogaritmos respecto a un
/// elemento primitivo <c>alfa</c>, exactamente como se multiplica con una tabla de logaritmos
/// de toda la vida: se suman los exponentes y se vuelve atras.
/// </para>
/// <para>
/// El cuerpo queda definido por su polinomio primitivo. Para JT65 es GF(64) con
/// <c>x^6 + x + 1</c> (0x43): es una constante del protocolo, no una eleccion nuestra, porque
/// dos implementaciones con polinomios distintos generan palabras de codigo distintas y no se
/// entienden entre si.
/// </para>
/// <para>
/// Escrito desde los libros (Lin y Costello, <i>Error Control Coding</i>; Blahut,
/// <i>Algebraic Codes for Data Transmission</i>); no se ha mirado ningun codigo de terceros.
/// </para>
/// </remarks>
public sealed class CampoDeGalois
{
    private readonly int[] _exp;
    private readonly int[] _log;

    /// <summary>Crea el cuerpo GF(2^<paramref name="bits"/>) con el polinomio primitivo dado.</summary>
    /// <param name="bits">Bits por simbolo, de 2 a 12.</param>
    /// <param name="polinomioPrimitivo">
    /// Polinomio primitivo con el bit <paramref name="bits"/> puesto, por ejemplo 0x43 para
    /// <c>x^6 + x + 1</c>.
    /// </param>
    /// <exception cref="ArgumentException">Si el polinomio no es primitivo (no genera el cuerpo entero).</exception>
    public CampoDeGalois(int bits, int polinomioPrimitivo)
    {
        if (bits is < 2 or > 12) throw new ArgumentOutOfRangeException(nameof(bits));
        if ((polinomioPrimitivo >> bits) != 1)
            throw new ArgumentException("El polinomio tiene que tener grado igual a los bits por simbolo.", nameof(polinomioPrimitivo));

        Bits = bits;
        Tamano = 1 << bits;
        Orden = Tamano - 1;
        _exp = new int[2 * Orden];
        _log = new int[Tamano];
        Array.Fill(_log, -1);

        // Se van generando las potencias de alfa multiplicando por x y reduciendo por el
        // polinomio. Si el polinomio es primitivo, las Orden primeras potencias recorren todos
        // los elementos no nulos sin repetir ninguno.
        var valor = 1;
        for (var i = 0; i < Orden; i++)
        {
            if (_log[valor] != -1)
                throw new ArgumentException("El polinomio no es primitivo: las potencias de alfa se repiten antes de tiempo.", nameof(polinomioPrimitivo));
            _exp[i] = valor;
            _log[valor] = i;
            valor <<= 1;
            if ((valor & Tamano) != 0) valor ^= polinomioPrimitivo;
        }
        if (valor != 1)
            throw new ArgumentException("El polinomio no es primitivo.", nameof(polinomioPrimitivo));

        // La tabla de antilogaritmos se duplica para poder sumar dos logaritmos sin reducir
        // modulo el orden en cada multiplicacion.
        for (var i = Orden; i < _exp.Length; i++) _exp[i] = _exp[i - Orden];
    }

    /// <summary>Bits por simbolo.</summary>
    public int Bits { get; }

    /// <summary>Elementos del cuerpo, 2^bits.</summary>
    public int Tamano { get; }

    /// <summary>Orden del grupo multiplicativo, 2^bits − 1.</summary>
    public int Orden { get; }

    /// <summary>GF(64) con <c>x^6 + x + 1</c>, el cuerpo de JT65.</summary>
    public static CampoDeGalois Gf64 { get; } = new(6, 0x43);

    /// <summary>Alfa elevado a <paramref name="exponente"/>; el exponente puede ser negativo o mayor que el orden.</summary>
    public int Exp(int exponente)
    {
        var e = exponente % Orden;
        if (e < 0) e += Orden;
        return _exp[e];
    }

    /// <summary>Logaritmo en base alfa. Cero no tiene logaritmo.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Si se pide el logaritmo de cero.</exception>
    public int Log(int valor) =>
        valor > 0 && valor < Tamano && _log[valor] >= 0
            ? _log[valor]
            : throw new ArgumentOutOfRangeException(nameof(valor), valor, "Cero no tiene logaritmo.");

    /// <summary>Producto de dos elementos.</summary>
    public int Multiplicar(int a, int b) =>
        a == 0 || b == 0 ? 0 : _exp[_log[a] + _log[b]];

    /// <summary>Producto sabiendo ya el logaritmo del segundo factor. Es el bucle interior del decodificador.</summary>
    public int MultiplicarPorLog(int a, int logaritmoDeB) =>
        a == 0 ? 0 : _exp[_log[a] + logaritmoDeB];

    /// <summary>Inverso multiplicativo.</summary>
    /// <exception cref="DivideByZeroException">Si se pide el inverso de cero.</exception>
    public int Inverso(int a) =>
        a == 0 ? throw new DivideByZeroException("Cero no tiene inverso.") : _exp[Orden - _log[a]];

    /// <summary>Cociente <paramref name="a"/> / <paramref name="b"/>.</summary>
    public int Dividir(int a, int b) =>
        b == 0 ? throw new DivideByZeroException()
        : a == 0 ? 0
        : _exp[_log[a] + Orden - _log[b]];

    /// <summary><paramref name="a"/> elevado a <paramref name="n"/>.</summary>
    public int Potencia(int a, int n)
    {
        if (a == 0) return n == 0 ? 1 : 0;
        var e = (long)_log[a] * n % Orden;
        if (e < 0) e += Orden;
        return _exp[e];
    }
}
