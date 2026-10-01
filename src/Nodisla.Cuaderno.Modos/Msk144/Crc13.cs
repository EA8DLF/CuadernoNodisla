namespace Nodisla.Cuaderno.Modos.Msk144;

/// <summary>
/// El CRC de 13 bits de los mensajes de MSK144.
/// </summary>
/// <remarks>
/// <para>
/// Hace el mismo papel que el de 14 bits de FT8: es lo unico que separa una decodificacion de
/// verdad de una palabra valida del codigo a la que el corrector ha llegado desde ruido. Con
/// trece bits, una palabra falsa se cuela una de cada ocho mil veces; y como MSK144 hace muchos
/// mas intentos por periodo que FT8 —cada trozo de audio puede probarse en decenas de posiciones
/// y varias frecuencias— el decodificador tiene que ser mas exigente antes de llegar aqui.
/// </para>
/// <para>
/// El calculo es la division larga en modulo dos de siempre, sobre los 77 bits del mensaje mas
/// seis ceros: el protocolo coloca el mensaje en un hueco de 96 bits con el CRC en los trece
/// ultimos, y 96 menos 13 son los 83 bits protegidos. Polinomio 0x15D7 (sin el bit de grado
/// trece, que va implicito). Fuente: constantes del protocolo.
/// </para>
/// </remarks>
public static class Crc13
{
    /// <summary>Bits del mensaje.</summary>
    public const int BitsDelMensaje = 77;

    /// <summary>Anchura del CRC.</summary>
    public const int BitsDelCrc = 13;

    /// <summary>Bits que salen del mensaje mas su CRC.</summary>
    public const int BitsConCrc = BitsDelMensaje + BitsDelCrc;

    /// <summary>Bits sobre los que se calcula: los 77 del mensaje mas seis ceros.</summary>
    public const int BitsProtegidos = 83;

    /// <summary>Polinomio generador, sin el bit de grado trece.</summary>
    public const ushort Polinomio = 0x15D7;

    private const int Mascara = (1 << BitsDelCrc) - 1;

    /// <summary>Calcula el CRC de un mensaje de 77 bits sueltos.</summary>
    public static ushort Calcular(ReadOnlySpan<byte> bitsDelMensaje)
    {
        if (bitsDelMensaje.Length != BitsDelMensaje)
            throw new ArgumentException($"El mensaje debe tener {BitsDelMensaje} bits y tiene {bitsDelMensaje.Length}.", nameof(bitsDelMensaje));

        const int BitDeArriba = 1 << (BitsDelCrc - 1);
        var resto = 0;
        for (var i = 0; i < BitsProtegidos; i++)
        {
            // Forma directa (el bit entra por arriba): calcula M(x)·x^13 mod P(x). Antes entraba
            // por abajo sin los 13 ceros finales, el mismo fallo que tumbaba FT8 contra el aire
            // (ver Ft8/Crc14.cs, 27-09-2026).
            var bit = i < bitsDelMensaje.Length ? bitsDelMensaje[i] & 1 : 0;
            resto ^= bit << (BitsDelCrc - 1);
            var desborda = (resto & BitDeArriba) != 0;
            resto = (resto << 1) & Mascara;
            if (desborda) resto ^= Polinomio;
        }
        return (ushort)(resto & Mascara);
    }

    /// <summary>Pega el CRC detras del mensaje y devuelve los 90 bits que entran al corrector.</summary>
    public static byte[] AnadirA(ReadOnlySpan<byte> bitsDelMensaje)
    {
        var crc = Calcular(bitsDelMensaje);
        var salida = new byte[BitsConCrc];
        bitsDelMensaje.CopyTo(salida);
        for (var i = 0; i < BitsDelCrc; i++)
            salida[BitsDelMensaje + i] = (byte)((crc >> (BitsDelCrc - 1 - i)) & 1);
        return salida;
    }

    /// <summary>Comprueba unos 90 bits recien corregidos. Si es falso, el candidato se tira.</summary>
    public static bool EsValido(ReadOnlySpan<byte> bitsConCrc)
    {
        if (bitsConCrc.Length != BitsConCrc) return false;
        var esperado = Calcular(bitsConCrc[..BitsDelMensaje]);
        var leido = 0;
        for (var i = 0; i < BitsDelCrc; i++)
            leido = (leido << 1) | (bitsConCrc[BitsDelMensaje + i] & 1);
        return leido == esperado;
    }
}
