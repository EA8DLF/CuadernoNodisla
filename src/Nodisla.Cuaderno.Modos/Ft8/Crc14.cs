namespace Nodisla.Cuaderno.Modos.Ft8;

/// <summary>
/// El CRC de 14 bits que lleva todo mensaje de FT8 y FT4.
/// </summary>
/// <remarks>
/// <para>
/// <b>Esta es la pieza que impide que el cuaderno se llene de indicativos inventados.</b> El
/// corrector de errores LDPC siempre devuelve <i>algo</i>: dele ruido puro y le devolvera una
/// palabra de codigo valida, que al desempaquetarse dara un indicativo con toda la pinta de ser
/// real. Lo unico que separa una decodificacion de verdad de una fantasia es que estos catorce
/// bits cuadren.
/// </para>
/// <para>
/// Catorce bits significan que una palabra falsa tiene una probabilidad de aproximadamente una
/// entre dieciseis mil de colarse. Como en una ventana se prueban unos cientos de candidatos,
/// salen unas pocas falsas por cada mil ventanas: pocas, pero no cero. Por eso el decodificador
/// exige ademas que el mensaje se desempaquete a algo con sentido antes de darlo por bueno.
/// </para>
/// <para>
/// El calculo se hace sobre los 77 bits del mensaje seguidos de cinco ceros, es decir sobre 82
/// bits. Esos cinco ceros no son un capricho: el protocolo coloca el mensaje en un hueco de 96
/// bits y el CRC ocupa los catorce ultimos, de manera que 96 menos 14 son los 82 que se
/// protegen.
/// </para>
/// </remarks>
public static class Crc14
{
    /// <summary>Bits del mensaje antes del CRC.</summary>
    public const int BitsDelMensaje = 77;

    /// <summary>Anchura del CRC.</summary>
    public const int BitsDelCrc = 14;

    /// <summary>Bits que salen del mensaje mas su CRC, que son los que entran al LDPC.</summary>
    public const int BitsConCrc = BitsDelMensaje + BitsDelCrc;

    /// <summary>Bits sobre los que se calcula: los 77 del mensaje mas cinco ceros.</summary>
    public const int BitsProtegidos = 82;

    /// <summary>
    /// Polinomio generador, sin el bit de grado catorce que va implicito.
    /// </summary>
    /// <remarks>
    /// Es el que fija el protocolo. Un polinomio de CRC no se elige al azar: se escoge para que
    /// detecte todas las rafagas de errores cortas y el mayor numero posible de patrones
    /// sueltos. Cambiarlo por otro «equivalente» dejaria al modem incapaz de hablar con nadie.
    /// </remarks>
    public const ushort Polinomio = 0x2757;

    /// <summary>
    /// Calcula el CRC de un mensaje dado como bits sueltos, cada uno en un byte con valor 0 o 1.
    /// </summary>
    /// <param name="bitsDelMensaje">Los 77 bits del mensaje.</param>
    /// <returns>El CRC, en los catorce bits bajos.</returns>
    public static ushort Calcular(ReadOnlySpan<byte> bitsDelMensaje)
    {
        if (bitsDelMensaje.Length != BitsDelMensaje)
            throw new ArgumentException($"El mensaje debe tener {BitsDelMensaje} bits y tiene {bitsDelMensaje.Length}.", nameof(bitsDelMensaje));

        // Division larga en modulo dos, en la forma DIRECTA: cada bit del mensaje entra por
        // ARRIBA del registro (se suma al bit que va a salir) y, si ese bit sale a uno, se resta
        // el polinomio. Asi se calcula M(x)·x^14 mod P(x), que es el CRC del protocolo.
        //
        // OJO, fallo real del 27-09-2026: antes los bits entraban por ABAJO del registro sin
        // anadir despues los 14 ceros. Eso calcula M(x) mod P(x), otro numero. Nuestro emisor y
        // nuestro receptor usaban el mismo calculo y se entendian entre ellos —todas las pruebas
        // pasaban—, pero con el aire de verdad el LDPC sacaba palabras validas y el CRC las tiraba
        // todas: cero decodificaciones con la cascada llena de senales. La prueba que lo fija
        // compara contra mensajes de FT8 reales con su CRC conocido.
        const int BitDeArriba = 1 << (BitsDelCrc - 1);
        var resto = 0;
        for (var i = 0; i < BitsProtegidos; i++)
        {
            // Los bits del 77 al 81 son los cinco ceros de relleno.
            var bit = i < bitsDelMensaje.Length ? bitsDelMensaje[i] : (byte)0;
            resto ^= bit << (BitsDelCrc - 1);
            var desborda = (resto & BitDeArriba) != 0;
            resto = (resto << 1) & 0x3FFF;
            if (desborda) resto ^= Polinomio;
        }
        return (ushort)(resto & 0x3FFF);
    }

    /// <summary>
    /// Pega el CRC detras del mensaje y devuelve los 91 bits que entran al corrector de errores.
    /// </summary>
    /// <param name="bitsDelMensaje">Los 77 bits del mensaje.</param>
    public static byte[] AnadirA(ReadOnlySpan<byte> bitsDelMensaje)
    {
        var crc = Calcular(bitsDelMensaje);
        var salida = new byte[BitsConCrc];
        bitsDelMensaje.CopyTo(salida);
        for (var i = 0; i < BitsDelCrc; i++)
            salida[BitsDelMensaje + i] = (byte)((crc >> (BitsDelCrc - 1 - i)) & 1);
        return salida;
    }

    /// <summary>
    /// Comprueba unos 91 bits recien corregidos.
    /// </summary>
    /// <param name="bitsConCrc">Los 91 bits: 77 de mensaje y 14 de CRC.</param>
    /// <returns>
    /// Cierto solo si el CRC cuadra. <b>Si devuelve falso hay que tirar el candidato</b>, no
    /// intentar aprovecharlo: un mensaje con el CRC malo no es un mensaje con una errata, es
    /// ruido que ha pasado por el corrector.
    /// </returns>
    public static bool EsValido(ReadOnlySpan<byte> bitsConCrc)
    {
        if (bitsConCrc.Length != BitsConCrc) return false;
        var esperado = Calcular(bitsConCrc[..BitsDelMensaje]);
        var leido = 0;
        for (var i = 0; i < BitsDelCrc; i++)
            leido = (leido << 1) | bitsConCrc[BitsDelMensaje + i];
        return leido == esperado;
    }
}
