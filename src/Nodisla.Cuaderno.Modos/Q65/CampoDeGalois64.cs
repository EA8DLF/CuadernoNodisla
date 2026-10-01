namespace Nodisla.Cuaderno.Modos.Q65;

/// <summary>
/// El cuerpo finito de 64 elementos con el que trabaja el codigo de Q65.
/// </summary>
/// <remarks>
/// <para>
/// Cada simbolo de Q65 son seis bits, y el codigo corrector los trata como elementos de GF(64):
/// se suman con o-exclusivo y se multiplican modulo el polinomio primitivo x^6 + x + 1, que es
/// el que usa el protocolo. Con un cuerpo tan pequeno lo mas rapido es tabular las potencias de
/// alfa y sus logaritmos una vez, y multiplicar sumando logaritmos.
/// </para>
/// <para>
/// Es propio de Q65 a proposito, aunque en el modulo haya otro cuerpo para Reed-Solomon: cada
/// modo vive en su carpeta y no depende de lo que hagan los demas.
/// </para>
/// </remarks>
public static class CampoDeGalois64
{
    /// <summary>Elementos del cuerpo.</summary>
    public const int Orden = 64;

    /// <summary>Bits por elemento.</summary>
    public const int BitsPorElemento = 6;

    /// <summary>x^6 + x + 1, escrito con el bit 6 puesto.</summary>
    public const int PolinomioPrimitivo = 0b1000011;

    // Las potencias se guardan dos veces seguidas para que un producto de logaritmos no
    // necesite reducir modulo 63.
    private static readonly byte[] Potencias = new byte[2 * (Orden - 1)];
    private static readonly byte[] Logaritmos = new byte[Orden];

    static CampoDeGalois64()
    {
        var v = 1;
        for (var i = 0; i < Orden - 1; i++)
        {
            Potencias[i] = (byte)v;
            Potencias[i + Orden - 1] = (byte)v;
            Logaritmos[v] = (byte)i;
            v <<= 1;
            if ((v & Orden) != 0) v ^= PolinomioPrimitivo;
        }
    }

    /// <summary>Alfa elevado al exponente indicado.</summary>
    public static int Potencia(int exponente)
    {
        var e = exponente % (Orden - 1);
        if (e < 0) e += Orden - 1;
        return Potencias[e];
    }

    /// <summary>Logaritmo en base alfa de un elemento distinto de cero.</summary>
    public static int Logaritmo(int elemento)
    {
        if (elemento is <= 0 or >= Orden) throw new ArgumentOutOfRangeException(nameof(elemento));
        return Logaritmos[elemento];
    }

    /// <summary>Producto de dos elementos.</summary>
    public static int Multiplicar(int a, int b)
    {
        if (a == 0 || b == 0) return 0;
        return Potencias[Logaritmos[a] + Logaritmos[b]];
    }

    /// <summary>Inverso multiplicativo de un elemento distinto de cero.</summary>
    public static int Inverso(int a)
    {
        if (a is <= 0 or >= Orden) throw new ArgumentOutOfRangeException(nameof(a));
        return Potencias[(Orden - 1) - Logaritmos[a]];
    }
}
