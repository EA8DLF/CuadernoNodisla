using Nodisla.Cuaderno.Modos.Convolucional;
using Nodisla.Cuaderno.Modos.Tablas;

namespace Nodisla.Cuaderno.Modos.Wspr;

/// <summary>
/// De un mensaje a los 162 tonos que se emiten.
/// </summary>
/// <remarks>
/// La cadena es: 50 bits de mensaje, 31 ceros de cola, codigo convolucional que los convierte
/// en 162 bits, entrelazado por inversion de bits, y por ultimo cada bit se junta con el bit de
/// sincronismo de su posicion para formar un tono de cuatro: <c>tono = sincronismo + 2 × dato</c>.
/// </remarks>
public static class CodificadorWspr
{
    /// <summary>Bits que entran al codigo convolucional: el mensaje mas la cola.</summary>
    public const int BitsCodificados = MensajeWspr.Bits + 31;

    /// <summary>Tonos de un mensaje, listos para el modulador.</summary>
    /// <param name="mensaje">Mensaje a emitir.</param>
    public static byte[] Tonos(MensajeWspr mensaje)
    {
        ArgumentNullException.ThrowIfNull(mensaje);
        return TonosDeLosBits(mensaje.Empaquetar());
    }

    /// <summary>Tonos a partir de los 50 bits ya empaquetados.</summary>
    /// <param name="bits50">Los 50 bits, uno por byte.</param>
    public static byte[] TonosDeLosBits(ReadOnlySpan<byte> bits50)
    {
        if (bits50.Length != MensajeWspr.Bits)
            throw new ArgumentException($"Hacen falta {MensajeWspr.Bits} bits y llegan {bits50.Length}.", nameof(bits50));

        var conCola = new byte[BitsCodificados];
        bits50.CopyTo(conCola);
        var codificados = CodigoConvolucional.DeWsprYJt9.Codificar(conCola);
        if (codificados.Length != ParametrosWspr.Simbolos)
            throw new InvalidOperationException("El codigo convolucional no saco 162 bits.");

        var sincronismo = TablasWspr.VectorDeSincronismo;
        var posiciones = ParametrosWspr.PosicionDeCadaBit;
        var tonos = new byte[ParametrosWspr.Simbolos];
        for (var p = 0; p < ParametrosWspr.Simbolos; p++)
        {
            var j = posiciones[p];
            tonos[j] = (byte)(sincronismo[j] + (2 * codificados[p]));
        }
        return tonos;
    }

    /// <summary>
    /// Deshace el entrelazado: de los valores blandos por simbolo a los valores blandos por bit
    /// codificado, en el orden en que los quiere el decodificador.
    /// </summary>
    /// <param name="porSimbolo">Un valor por simbolo emitido.</param>
    /// <param name="porBit">Salida: un valor por bit codificado.</param>
    public static void Desentrelazar(ReadOnlySpan<byte> porSimbolo, Span<byte> porBit)
    {
        if (porSimbolo.Length != ParametrosWspr.Simbolos || porBit.Length != ParametrosWspr.Simbolos)
            throw new ArgumentException("Hacen falta 162 valores a cada lado.");
        var posiciones = ParametrosWspr.PosicionDeCadaBit;
        for (var p = 0; p < ParametrosWspr.Simbolos; p++) porBit[p] = porSimbolo[posiciones[p]];
    }
}
