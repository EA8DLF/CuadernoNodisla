namespace Nodisla.Cuaderno.Modos.Ft8;

/// <summary>
/// Utilidades para mover campos dentro de una tira de bits.
/// </summary>
/// <remarks>
/// Todo el modem trabaja con los bits sueltos, un byte por bit, en vez de con bytes
/// empaquetados. Gasta ocho veces mas memoria —174 bytes en lugar de 22— y a cambio se ve de un
/// vistazo lo que hace cada linea, que en un codigo corrector de errores vale mucho mas que
/// esos bytes. El orden es siempre <b>el bit mas significativo primero</b>, que es como el
/// protocolo numera los campos.
/// </remarks>
public static class EmpaquetadoDeBits
{
    /// <summary>Escribe un numero en <paramref name="anchura"/> bits a partir de una posicion.</summary>
    /// <param name="destino">Tira de bits donde escribir.</param>
    /// <param name="posicion">Primer bit que se ocupa.</param>
    /// <param name="anchura">Cuantos bits ocupa el campo.</param>
    /// <param name="valor">Valor a escribir; debe caber en la anchura.</param>
    public static void Escribir(Span<byte> destino, int posicion, int anchura, long valor)
    {
        if (anchura is < 1 or > 63) throw new ArgumentOutOfRangeException(nameof(anchura));
        if (posicion < 0 || posicion + anchura > destino.Length)
            throw new ArgumentOutOfRangeException(nameof(posicion), "El campo no cabe en la tira de bits.");
        if (valor < 0 || (anchura < 63 && valor >= 1L << anchura))
            throw new ArgumentOutOfRangeException(nameof(valor), $"El valor {valor} no cabe en {anchura} bits.");

        for (var i = 0; i < anchura; i++)
            destino[posicion + i] = (byte)((valor >> (anchura - 1 - i)) & 1);
    }

    /// <summary>Lee un numero de <paramref name="anchura"/> bits a partir de una posicion.</summary>
    public static long Leer(ReadOnlySpan<byte> origen, int posicion, int anchura)
    {
        if (anchura is < 1 or > 63) throw new ArgumentOutOfRangeException(nameof(anchura));
        if (posicion < 0 || posicion + anchura > origen.Length)
            throw new ArgumentOutOfRangeException(nameof(posicion), "El campo se sale de la tira de bits.");

        long valor = 0;
        for (var i = 0; i < anchura; i++)
            valor = (valor << 1) | origen[posicion + i];
        return valor;
    }

    /// <summary>
    /// Escribe un campo que no cabe en un entero de 64 bits.
    /// </summary>
    /// <remarks>
    /// El texto libre de FT8 son trece caracteres de un alfabeto de 42, y eso da un numero de 71
    /// bits: ocho mas de los que caben en el entero mayor de la maquina. Por eso estos dos
    /// metodos existen aparte, y por eso el texto libre se trata con el entero de 128 bits que
    /// trae .NET 8 en vez de con acrobacias de dos mitades.
    /// </remarks>
    /// <param name="destino">Tira de bits donde escribir.</param>
    /// <param name="posicion">Primer bit que se ocupa.</param>
    /// <param name="anchura">Cuantos bits ocupa el campo, hasta 127.</param>
    /// <param name="valor">Valor a escribir.</param>
    public static void EscribirGrande(Span<byte> destino, int posicion, int anchura, UInt128 valor)
    {
        if (anchura is < 1 or > 127) throw new ArgumentOutOfRangeException(nameof(anchura));
        if (posicion < 0 || posicion + anchura > destino.Length)
            throw new ArgumentOutOfRangeException(nameof(posicion), "El campo no cabe en la tira de bits.");
        if (valor >= UInt128.One << anchura)
            throw new ArgumentOutOfRangeException(nameof(valor), $"El valor no cabe en {anchura} bits.");

        for (var i = 0; i < anchura; i++)
            destino[posicion + i] = (byte)((valor >> (anchura - 1 - i)) & UInt128.One);
    }

    /// <summary>Lee un campo de mas de 64 bits.</summary>
    /// <param name="origen">Tira de bits de la que leer.</param>
    /// <param name="posicion">Primer bit del campo.</param>
    /// <param name="anchura">Cuantos bits ocupa, hasta 127.</param>
    public static UInt128 LeerGrande(ReadOnlySpan<byte> origen, int posicion, int anchura)
    {
        if (anchura is < 1 or > 127) throw new ArgumentOutOfRangeException(nameof(anchura));
        if (posicion < 0 || posicion + anchura > origen.Length)
            throw new ArgumentOutOfRangeException(nameof(posicion), "El campo se sale de la tira de bits.");

        UInt128 valor = 0;
        for (var i = 0; i < anchura; i++)
            valor = (valor << 1) | origen[posicion + i];
        return valor;
    }

    /// <summary>Convierte bytes empaquetados en bits sueltos, el mas significativo primero.</summary>
    /// <param name="bytes">Bytes de origen.</param>
    /// <param name="bits">Cuantos bits interesan; el resto del ultimo byte se descarta.</param>
    public static byte[] DesdeBytes(ReadOnlySpan<byte> bytes, int bits)
    {
        var salida = new byte[bits];
        for (var i = 0; i < bits; i++)
            salida[i] = (byte)((bytes[i / 8] >> (7 - (i % 8))) & 1);
        return salida;
    }

    /// <summary>Convierte bits sueltos en bytes empaquetados, rellenando el ultimo con ceros.</summary>
    public static byte[] ABytes(ReadOnlySpan<byte> bits)
    {
        var salida = new byte[(bits.Length + 7) / 8];
        for (var i = 0; i < bits.Length; i++)
            if (bits[i] != 0)
                salida[i / 8] |= (byte)(1 << (7 - (i % 8)));
        return salida;
    }
}
