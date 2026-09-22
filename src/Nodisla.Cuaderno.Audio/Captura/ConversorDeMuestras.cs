using System.Buffers.Binary;

namespace Nodisla.Cuaderno.Audio.Captura;

/// <summary>
/// Pasa de los bytes que da la tarjeta a muestras de un canal en coma flotante, y al reves.
/// </summary>
/// <remarks>
/// Windows entrega el audio en el formato que tenga puesto el dispositivo —casi siempre coma
/// flotante de 32 bits y dos canales—, y el modem trabaja siempre igual: un canal, coma
/// flotante de -1 a 1. Esta clase es la aduana entre los dos mundos, y esta aparte de la
/// tarjeta a proposito, para poder probarla sin tener ninguna.
/// </remarks>
public static class ConversorDeMuestras
{
    /// <summary>Cuantas muestras de un canal salen de un bloque de bytes.</summary>
    /// <param name="bytes">Bytes que hay.</param>
    /// <param name="canales">Canales entrelazados.</param>
    /// <param name="bitsPorMuestra">Bits de cada muestra de cada canal.</param>
    /// <returns>El numero de muestras de un canal.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Si los canales o los bits no valen.</exception>
    public static int CuantasMuestras(int bytes, int canales, int bitsPorMuestra)
    {
        Comprobar(canales, bitsPorMuestra);
        var bytesPorCuadro = canales * (bitsPorMuestra / 8);
        return bytes / bytesPorCuadro;
    }

    /// <summary>
    /// Convierte bytes entrelazados en muestras de un solo canal.
    /// </summary>
    /// <param name="crudo">Los bytes tal y como llegan del dispositivo.</param>
    /// <param name="canales">Canales entrelazados.</param>
    /// <param name="bitsPorMuestra">Bits de cada muestra.</param>
    /// <param name="esComaFlotante">Verdadero si las muestras vienen en coma flotante.</param>
    /// <param name="destino">Donde se dejan las muestras de un canal.</param>
    /// <returns>Cuantas muestras se escribieron.</returns>
    /// <remarks>
    /// Los canales se promedian en vez de quedarse con el izquierdo: si el equipo entrega el
    /// audio por un solo canal, quedarse con el que no es deja el modem sordo, y promediar
    /// funciona en los dos casos.
    /// </remarks>
    /// <exception cref="ArgumentException">Si el destino se queda corto.</exception>
    /// <exception cref="NotSupportedException">Si el formato no es de los conocidos.</exception>
    public static int AMono(
        ReadOnlySpan<byte> crudo,
        int canales,
        int bitsPorMuestra,
        bool esComaFlotante,
        Span<float> destino)
    {
        Comprobar(canales, bitsPorMuestra);

        var bytesPorMuestra = bitsPorMuestra / 8;
        var bytesPorCuadro = canales * bytesPorMuestra;
        var cuadros = crudo.Length / bytesPorCuadro;

        if (destino.Length < cuadros)
        {
            throw new ArgumentException(
                "El destino no tiene sitio para todas las muestras.",
                nameof(destino));
        }

        for (var cuadro = 0; cuadro < cuadros; cuadro++)
        {
            var suma = 0.0;
            var baseDelCuadro = cuadro * bytesPorCuadro;

            for (var canal = 0; canal < canales; canal++)
            {
                suma += LeerUna(crudo.Slice(baseDelCuadro + (canal * bytesPorMuestra), bytesPorMuestra), esComaFlotante);
            }

            destino[cuadro] = (float)(suma / canales);
        }

        return cuadros;
    }

    /// <summary>
    /// Convierte muestras de un canal al formato que pide el dispositivo, repitiendolas en
    /// todos los canales.
    /// </summary>
    /// <param name="mono">Muestras de un canal, de -1 a 1.</param>
    /// <param name="canales">Canales que espera el dispositivo.</param>
    /// <param name="bitsPorMuestra">Bits de cada muestra.</param>
    /// <param name="esComaFlotante">Verdadero si el dispositivo quiere coma flotante.</param>
    /// <param name="destino">Donde se dejan los bytes.</param>
    /// <returns>Cuantos bytes se escribieron.</returns>
    /// <exception cref="ArgumentException">Si el destino se queda corto.</exception>
    /// <exception cref="NotSupportedException">Si el formato no es de los conocidos.</exception>
    public static int DesdeMono(
        ReadOnlySpan<float> mono,
        int canales,
        int bitsPorMuestra,
        bool esComaFlotante,
        Span<byte> destino)
    {
        Comprobar(canales, bitsPorMuestra);

        var bytesPorMuestra = bitsPorMuestra / 8;
        var bytesPorCuadro = canales * bytesPorMuestra;
        var necesarios = mono.Length * bytesPorCuadro;

        if (destino.Length < necesarios)
        {
            throw new ArgumentException(
                "El destino no tiene sitio para todos los bytes.",
                nameof(destino));
        }

        for (var i = 0; i < mono.Length; i++)
        {
            // Se recorta a la escala: pasarse de uno en coma flotante suena a chasquido y en
            // entero da la vuelta al signo, que suena mucho peor.
            var valor = Math.Clamp(mono[i], -1f, 1f);

            for (var canal = 0; canal < canales; canal++)
            {
                var sitio = destino.Slice((i * bytesPorCuadro) + (canal * bytesPorMuestra), bytesPorMuestra);
                EscribirUna(valor, sitio, esComaFlotante);
            }
        }

        return necesarios;
    }

    /// <summary>Lee una muestra suelta y la deja entre -1 y 1.</summary>
    private static double LeerUna(ReadOnlySpan<byte> crudo, bool esComaFlotante)
    {
        if (esComaFlotante)
        {
            return crudo.Length switch
            {
                4 => BitConverter.ToSingle(crudo),
                8 => BitConverter.ToDouble(crudo),
                _ => throw FormatoDesconocido(crudo.Length * 8, true),
            };
        }

        return crudo.Length switch
        {
            2 => BinaryPrimitives.ReadInt16LittleEndian(crudo) / 32768.0,
            3 => LeerDe24Bits(crudo) / 8388608.0,
            4 => BinaryPrimitives.ReadInt32LittleEndian(crudo) / 2147483648.0,
            _ => throw FormatoDesconocido(crudo.Length * 8, false),
        };
    }

    /// <summary>Escribe una muestra suelta en el formato del dispositivo.</summary>
    private static void EscribirUna(float valor, Span<byte> destino, bool esComaFlotante)
    {
        if (esComaFlotante)
        {
            switch (destino.Length)
            {
                case 4:
                    BinaryPrimitives.WriteSingleLittleEndian(destino, valor);
                    return;
                case 8:
                    BinaryPrimitives.WriteDoubleLittleEndian(destino, valor);
                    return;
                default:
                    throw FormatoDesconocido(destino.Length * 8, true);
            }
        }

        switch (destino.Length)
        {
            case 2:
                BinaryPrimitives.WriteInt16LittleEndian(destino, (short)Math.Round(valor * 32767.0));
                return;
            case 3:
                EscribirDe24Bits((int)Math.Round(valor * 8388607.0), destino);
                return;
            case 4:
                BinaryPrimitives.WriteInt32LittleEndian(destino, (int)Math.Round(valor * 2147483647.0));
                return;
            default:
                throw FormatoDesconocido(destino.Length * 8, false);
        }
    }

    /// <summary>Lee un entero de 24 bits con signo, que viene en tres bytes.</summary>
    private static int LeerDe24Bits(ReadOnlySpan<byte> crudo)
    {
        var valor = crudo[0] | (crudo[1] << 8) | (crudo[2] << 16);

        // Se estira el bit de signo a los 32 bits.
        return (valor & 0x800000) != 0 ? valor | unchecked((int)0xFF000000) : valor;
    }

    /// <summary>Escribe un entero de 24 bits con signo en tres bytes.</summary>
    private static void EscribirDe24Bits(int valor, Span<byte> destino)
    {
        destino[0] = (byte)(valor & 0xFF);
        destino[1] = (byte)((valor >> 8) & 0xFF);
        destino[2] = (byte)((valor >> 16) & 0xFF);
    }

    private static void Comprobar(int canales, int bitsPorMuestra)
    {
        if (canales <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(canales),
                canales,
                "Tiene que haber al menos un canal.");
        }

        if (bitsPorMuestra <= 0 || bitsPorMuestra % 8 != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bitsPorMuestra),
                bitsPorMuestra,
                "Los bits por muestra tienen que ser un múltiplo de ocho.");
        }
    }

    private static NotSupportedException FormatoDesconocido(int bits, bool esComaFlotante) =>
        new($"No se sabe manejar audio de {bits} bits {(esComaFlotante ? "en coma flotante" : "en enteros")}.");
}
