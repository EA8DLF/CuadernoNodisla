using Nodisla.Cuaderno.Modos.ReedSolomon;

namespace Nodisla.Cuaderno.Modos.Jt65;

/// <summary>
/// Convierte un mensaje en la secuencia de 126 tonos de JT65, y deshace el camino.
/// </summary>
/// <remarks>
/// <para>
/// El camino de ida: 72 bits → 12 simbolos de 6 bits → Reed-Solomon (63,12) → 63 simbolos
/// entrelazados → codigo de Gray → tonos de datos, intercalados con los 63 de sincronismo
/// segun el vector pseudoaleatorio. El tono de datos <c>g</c> suena <c>2 + g</c> espaciados
/// por encima del de sincronismo; el hueco del tono 1 separa el sincronismo de los datos.
/// </para>
/// </remarks>
public sealed class CodificadorJt65
{
    private readonly CodigoReedSolomon _codigo = CodigoReedSolomon.Jt65;

    /// <summary>Codigo Reed-Solomon con el que trabaja.</summary>
    public CodigoReedSolomon Codigo => _codigo;

    /// <summary>
    /// Codifica un mensaje a tonos.
    /// </summary>
    /// <param name="texto">Mensaje.</param>
    /// <param name="tonos">126 tonos, de 0 a 65: 0 es el sincronismo, 2..65 los datos.</param>
    /// <param name="motivo">Por que no se pudo, si no se pudo.</param>
    public bool TryCodificar(string? texto, out byte[] tonos, out string motivo)
    {
        tonos = [];
        if (!MensajeDe72Bits.TryEmpaquetar(texto, out var bits, out motivo)) return false;
        tonos = TonosDe(bits);
        return true;
    }

    /// <summary>Tonos de un mensaje ya empaquetado en 72 bits.</summary>
    public byte[] TonosDe(ReadOnlySpan<byte> bits)
    {
        if (bits.Length != MensajeDe72Bits.Bits) throw new ArgumentException($"Hacen falta {MensajeDe72Bits.Bits} bits.", nameof(bits));

        Span<byte> simbolos = stackalloc byte[12];
        for (var i = 0; i < 12; i++)
        {
            var v = 0;
            for (var b = 0; b < 6; b++) v = (v << 1) | bits[(6 * i) + b];
            simbolos[i] = (byte)v;
        }

        Span<byte> palabra = stackalloc byte[63];
        _codigo.Codificar(simbolos, palabra);
        return TonosDePalabra(palabra);
    }

    /// <summary>Tonos de una palabra Reed-Solomon ya codificada (63 simbolos, paridad primero).</summary>
    public static byte[] TonosDePalabra(ReadOnlySpan<byte> palabra)
    {
        if (palabra.Length != 63) throw new ArgumentException("Hacen falta 63 simbolos.", nameof(palabra));
        var tonos = new byte[TablasJt65.Simbolos];
        var datos = TablasJt65.PosicionesDeDatos;
        var haciaElAire = TablasJt65.EntrelazadoHaciaElAire;
        for (var i = 0; i < 63; i++)
        {
            var intervalo = datos[haciaElAire[i]];
            tonos[intervalo] = (byte)(TablasJt65.PrimerTonoDeDatos + TablasJt65.Gray[palabra[i]]);
        }
        return tonos;
    }

    /// <summary>Los 72 bits de una palabra Reed-Solomon ya corregida.</summary>
    public byte[] BitsDePalabra(ReadOnlySpan<byte> palabra)
    {
        Span<byte> simbolos = stackalloc byte[12];
        _codigo.ExtraerMensaje(palabra, simbolos);
        var bits = new byte[MensajeDe72Bits.Bits];
        for (var i = 0; i < 12; i++)
            for (var b = 0; b < 6; b++)
                bits[(6 * i) + b] = (byte)((simbolos[i] >> (5 - b)) & 1);
        return bits;
    }
}
