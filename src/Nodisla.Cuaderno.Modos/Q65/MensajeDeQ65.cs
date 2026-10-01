using Nodisla.Cuaderno.Modos.Ft8;

namespace Nodisla.Cuaderno.Modos.Q65;

/// <summary>
/// Del mensaje de 77 bits a los 85 tonos de Q65, y vuelta.
/// </summary>
/// <remarks>
/// <para>
/// Q65 usa el mismo empaquetado de 77 bits que FT8, asi que aqui no se vuelve a escribir: se
/// reutiliza <see cref="MensajeDe77Bits"/>. Lo propio de Q65 empieza despues: los 77 bits se
/// completan con un cero hasta 78 y se leen como 13 simbolos de seis bits, con el primer bit
/// de cada simbolo como el mas significativo; se les anade el sello de 12 bits (dos simbolos
/// mas), se codifican en 65, se recortan los dos del sello y los 63 que quedan se reparten por
/// las 63 posiciones de datos de la trama. Las otras 22 llevan el tono cero de sincronismo, y
/// cada simbolo de datos <i>s</i> se emite en el tono <i>s + 1</i>.
/// </para>
/// </remarks>
public static class MensajeDeQ65
{
    /// <summary>Simbolos que ocupa el mensaje de 77 bits.</summary>
    public const int SimbolosDelMensaje = 13;

    /// <summary>Simbolos de datos que se emiten: los 65 de la palabra menos los 2 del sello.</summary>
    public const int SimbolosEmitidos = CodigoQra.Longitud - Crc12DeQ65.SimbolosDelSello;

    /// <summary>Tonos distintos de la senal: el de sincronismo y los 64 de datos.</summary>
    public const int Tonos = 65;

    /// <summary>Los 77 bits, leidos de seis en seis, como 13 simbolos.</summary>
    public static int[] SimbolosDe(ReadOnlySpan<byte> bits77)
    {
        if (bits77.Length != MensajeDe77Bits.Bits)
            throw new ArgumentException($"Hacen falta {MensajeDe77Bits.Bits} bits.", nameof(bits77));
        var simbolos = new int[SimbolosDelMensaje];
        for (var s = 0; s < SimbolosDelMensaje; s++)
        {
            var valor = 0;
            for (var b = 0; b < CampoDeGalois64.BitsPorElemento; b++)
            {
                var indice = (s * CampoDeGalois64.BitsPorElemento) + b;
                var bit = indice < bits77.Length ? bits77[indice] : 0;
                valor = (valor << 1) | bit;
            }
            simbolos[s] = valor;
        }
        return simbolos;
    }

    /// <summary>Los 13 simbolos de vuelta a los 77 bits. El bit de relleno se descarta.</summary>
    public static byte[] BitsDe(ReadOnlySpan<int> simbolos13)
    {
        if (simbolos13.Length != SimbolosDelMensaje)
            throw new ArgumentException($"Hacen falta {SimbolosDelMensaje} simbolos.", nameof(simbolos13));
        var bits = new byte[MensajeDe77Bits.Bits];
        for (var i = 0; i < bits.Length; i++)
        {
            var s = i / CampoDeGalois64.BitsPorElemento;
            var b = i % CampoDeGalois64.BitsPorElemento;
            bits[i] = (byte)((simbolos13[s] >> (CampoDeGalois64.BitsPorElemento - 1 - b)) & 1);
        }
        return bits;
    }

    /// <summary>Los 15 simbolos de informacion: los 13 del mensaje y los 2 del sello.</summary>
    public static int[] InformacionDe(ReadOnlySpan<byte> bits77, int polinomioDelCrc)
    {
        var simbolos = SimbolosDe(bits77);
        var (bajo, alto) = Crc12DeQ65.Calcular(simbolos, polinomioDelCrc);
        return [.. simbolos, bajo, alto];
    }

    /// <summary>
    /// Comprueba el sello de 15 simbolos de informacion y, si cuadra, devuelve los 77 bits.
    /// </summary>
    public static bool TryBitsDeLaInformacion(ReadOnlySpan<int> informacion, int polinomioDelCrc, out byte[] bits77)
    {
        bits77 = [];
        if (!Crc12DeQ65.EsValido(informacion, polinomioDelCrc)) return false;
        // El bit de relleno del ultimo simbolo tiene que ser cero: si no lo es, la palabra no
        // la emitio nadie que hable Q65.
        if ((informacion[SimbolosDelMensaje - 1] & 1) != 0) return false;
        bits77 = BitsDe(informacion[..SimbolosDelMensaje]);
        return true;
    }

    /// <summary>Indice en la palabra de codigo del simbolo emitido en la posicion de datos indicada.</summary>
    public static int IndiceEnLaPalabra(int indiceEmitido) =>
        indiceEmitido < SimbolosDelMensaje ? indiceEmitido : indiceEmitido + Crc12DeQ65.SimbolosDelSello;

    /// <summary>Los 85 tonos de la trama a partir de la palabra de codigo.</summary>
    public static byte[] TonosDe(ReadOnlySpan<int> palabra, TablasDeQ65 tablas)
    {
        ArgumentNullException.ThrowIfNull(tablas);
        if (palabra.Length != CodigoQra.Longitud)
            throw new ArgumentException($"Hacen falta {CodigoQra.Longitud} simbolos.", nameof(palabra));
        var tonos = new byte[TablasDeQ65.SimbolosDeLaTrama];
        for (var t = 0; t < SimbolosEmitidos; t++)
            tonos[tablas.PosicionesDeDatos[t]] = (byte)(palabra[IndiceEnLaPalabra(t)] + 1);
        return tonos;
    }

    /// <summary>Convierte un mensaje escrito en los 85 tonos que hay que emitir.</summary>
    public static bool TryTonosDe(string? texto, CodigoQra codigo, out byte[] tonos, out string motivo)
    {
        ArgumentNullException.ThrowIfNull(codigo);
        tonos = [];
        if (!MensajeDe77Bits.TryEmpaquetar(texto, out var bits, out motivo)) return false;
        var informacion = InformacionDe(bits, codigo.Tablas.PolinomioDelCrc);
        tonos = TonosDe(codigo.Codificar(informacion), codigo.Tablas);
        return true;
    }
}
