using Nodisla.Cuaderno.Modos.Convolucional;
using Nodisla.Cuaderno.Modos.Jt65;

namespace Nodisla.Cuaderno.Modos.Jt9;

/// <summary>
/// Convierte un mensaje en los 85 tonos de JT9, y deshace el entrelazado a la vuelta.
/// </summary>
/// <remarks>
/// <para>
/// El camino de ida: 72 bits (el mismo empaquetado que JT65, <see cref="MensajeDe72Bits"/>)
/// mas 31 ceros de cola → codigo convolucional K=32 r=1/2 (el de WSPR,
/// <see cref="CodigoConvolucional.DeWsprYJt9"/>) → 206 bits → entrelazado por inversion de
/// bits → 69 simbolos de 3 bits (el bit 207 es un cero de relleno) → codigo de Gray → tonos 1
/// a 8, intercalados con los 16 simbolos de sincronismo en el tono 0.
/// </para>
/// </remarks>
public sealed class CodificadorJt9
{
    /// <summary>Bits de entrada al codigo convolucional: 72 de mensaje y 31 de cola.</summary>
    public const int BitsConCola = MensajeDe72Bits.Bits + 31;

    /// <summary>Codifica un mensaje a tonos.</summary>
    /// <param name="texto">Mensaje.</param>
    /// <param name="tonos">85 tonos, de 0 a 8.</param>
    /// <param name="motivo">Por que no se pudo, si no se pudo.</param>
    public bool TryCodificar(string? texto, out byte[] tonos, out string motivo)
    {
        tonos = [];
        if (!MensajeDe72Bits.TryEmpaquetar(texto, out var bits, out motivo)) return false;
        tonos = TonosDe(bits);
        return true;
    }

    /// <summary>Los 206 bits codificados de un mensaje de 72 bits, en el orden del codificador.</summary>
    public static byte[] BitsCodificadosDe(ReadOnlySpan<byte> bits72)
    {
        if (bits72.Length != MensajeDe72Bits.Bits) throw new ArgumentException($"Hacen falta {MensajeDe72Bits.Bits} bits.", nameof(bits72));
        var conCola = new byte[BitsConCola];
        bits72.CopyTo(conCola);
        var codificados = CodigoConvolucional.DeWsprYJt9.Codificar(conCola);
        if (codificados.Length != TablasJt9.BitsCodificados)
            throw new InvalidOperationException("El codigo convolucional no saco 206 bits.");
        return codificados;
    }

    /// <summary>Tonos de un mensaje ya empaquetado en 72 bits.</summary>
    public static byte[] TonosDe(ReadOnlySpan<byte> bits72)
    {
        var codificados = BitsCodificadosDe(bits72);

        // Entrelazado: el bit codificado p cae en la posicion PosicionDeCadaBit[p] de la
        // secuencia que se reparte en simbolos de tres bits, el mas significativo primero.
        Span<byte> entrelazados = stackalloc byte[TablasJt9.BitsCodificados + 1];
        for (var p = 0; p < TablasJt9.BitsCodificados; p++)
            entrelazados[TablasJt9.PosicionDeCadaBit[p]] = codificados[p];
        entrelazados[TablasJt9.BitsCodificados] = 0;

        var tonos = new byte[TablasJt9.Simbolos];
        for (var m = 0; m < TablasJt9.SimbolosDeDatos; m++)
        {
            var simbolo = (entrelazados[3 * m] << 2) | (entrelazados[(3 * m) + 1] << 1) | entrelazados[(3 * m) + 2];
            tonos[TablasJt9.PosicionesDeDatos[m]] = (byte)(1 + TablasJt9.Gray[simbolo]);
        }
        return tonos;
    }

    /// <summary>
    /// Deshace el entrelazado de los valores blandos: de un valor por posicion de la secuencia
    /// de 207 bits (por simbolo y bit, el mas significativo primero) a un valor por bit
    /// codificado, en el orden en que los quiere el decodificador de Fano.
    /// </summary>
    public static void Desentrelazar(ReadOnlySpan<byte> porPosicion, Span<byte> porBit)
    {
        if (porPosicion.Length < TablasJt9.BitsCodificados || porBit.Length != TablasJt9.BitsCodificados)
            throw new ArgumentException("Hacen falta 206 valores a cada lado.");
        for (var p = 0; p < TablasJt9.BitsCodificados; p++) porBit[p] = porPosicion[TablasJt9.PosicionDeCadaBit[p]];
    }
}
