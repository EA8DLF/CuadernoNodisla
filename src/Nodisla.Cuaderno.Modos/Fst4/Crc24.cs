namespace Nodisla.Cuaderno.Modos.Fst4;

/// <summary>
/// El CRC de 24 bits de FST4 y FST4W.
/// </summary>
/// <remarks>
/// <para>
/// Veinticuatro bits son diez mas que en FT8: una palabra de ruido que cuadre las ecuaciones
/// del codigo tiene una entre diecisiete millones de colarse, en vez de una entre dieciseis
/// mil. Es lo que permite que en FST4 se pueda apretar mas al corrector —mas vueltas, la
/// recuperacion profunda con menos frenos— sin que el cuaderno se llene de contactos falsos.
/// </para>
/// <para>
/// Polinomio 0x100065B con valor inicial cero, calculado sobre los bits del mensaje tal cual,
/// sin relleno: 77 en FST4 (ya revueltos con la mezcla) y 50 en FST4W. Fuente: Quick-Start
/// Guide to FST4 and FST4W, apendice A.
/// </para>
/// </remarks>
public static class Crc24
{
    /// <summary>Anchura del CRC.</summary>
    public const int BitsDelCrc = 24;

    /// <summary>Polinomio generador, sin el bit de grado veinticuatro.</summary>
    public const uint Polinomio = 0x00065B;

    private const uint Mascara = (1u << BitsDelCrc) - 1;

    /// <summary>Calcula el CRC de un mensaje de bits sueltos, de la longitud que sea.</summary>
    public static uint Calcular(ReadOnlySpan<byte> bitsDelMensaje)
    {
        const uint BitDeArriba = 1u << (BitsDelCrc - 1);
        uint resto = 0;
        foreach (var b in bitsDelMensaje)
        {
            // Forma directa (el bit entra por arriba): calcula M(x)·x^24 mod P(x). Antes entraba
            // por abajo sin los 24 ceros finales, el mismo fallo que tumbaba FT8 contra el aire
            // (ver Ft8/Crc14.cs, 27-09-2026).
            resto ^= (uint)(b & 1) << (BitsDelCrc - 1);
            var desborda = (resto & BitDeArriba) != 0;
            resto = (resto << 1) & Mascara;
            if (desborda) resto ^= Polinomio;
        }
        return resto & Mascara;
    }

    /// <summary>Pega el CRC detras del mensaje.</summary>
    public static byte[] AnadirA(ReadOnlySpan<byte> bitsDelMensaje)
    {
        var crc = Calcular(bitsDelMensaje);
        var salida = new byte[bitsDelMensaje.Length + BitsDelCrc];
        bitsDelMensaje.CopyTo(salida);
        for (var i = 0; i < BitsDelCrc; i++)
            salida[bitsDelMensaje.Length + i] = (byte)((crc >> (BitsDelCrc - 1 - i)) & 1);
        return salida;
    }

    /// <summary>Comprueba un mensaje con su CRC detras. Si es falso, el candidato se tira.</summary>
    public static bool EsValido(ReadOnlySpan<byte> bitsConCrc)
    {
        if (bitsConCrc.Length <= BitsDelCrc) return false;
        var bits = bitsConCrc.Length - BitsDelCrc;
        var esperado = Calcular(bitsConCrc[..bits]);
        uint leido = 0;
        for (var i = 0; i < BitsDelCrc; i++)
            leido = (leido << 1) | (uint)(bitsConCrc[bits + i] & 1);
        return leido == esperado;
    }
}
