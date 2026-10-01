using System.Numerics;

namespace Nodisla.Cuaderno.Modos.Convolucional;

/// <summary>
/// Codigo convolucional de tasa 1/2 con dos polinomios sobre un registro de 32 bits.
/// </summary>
/// <remarks>
/// <para>
/// Es el codigo que comparten WSPR y JT9 (longitud de restriccion 32, polinomios 0xF2D05351 y
/// 0xE4613C47): por eso vive aqui y no dentro de la carpeta de WSPR. La clase es generica en
/// los polinomios, asi que si algun dia hace falta otro codigo del mismo corte, se construye
/// con otros dos numeros.
/// </para>
/// <para>
/// <b>Como funciona.</b> Los bits del mensaje entran de uno en uno por la parte baja de un
/// registro de desplazamiento de 32 bits. Por cada bit que entra salen dos: la paridad del
/// registro enmascarado con cada polinomio. Como el registro arrastra los 31 bits anteriores,
/// cada bit de salida depende de 32 bits de entrada, y eso es lo que da al codigo su fuerza:
/// una longitud de restriccion tan larga hace que un decodificador de Viterbi sea impensable
/// (dos mil millones de estados) y obliga a decodificar de forma secuencial, que es lo que hace
/// <see cref="DecodificadorDeFano"/>.
/// </para>
/// <para>
/// El mensaje termina con 31 ceros de cola que vacian el registro. El decodificador sabe que
/// estan ahi y los usa: en la cola solo hay un camino posible, y eso es una comprobacion
/// gratuita de 62 bits que ayuda mucho a no inventar mensajes.
/// </para>
/// </remarks>
public sealed class CodigoConvolucional
{
    /// <summary>Crea el codigo con sus dos polinomios.</summary>
    /// <param name="polinomioA">Polinomio del primer bit de salida.</param>
    /// <param name="polinomioB">Polinomio del segundo bit de salida.</param>
    public CodigoConvolucional(uint polinomioA, uint polinomioB)
    {
        PolinomioA = polinomioA;
        PolinomioB = polinomioB;
    }

    /// <summary>El codigo de WSPR y JT9.</summary>
    public static CodigoConvolucional DeWsprYJt9 { get; } =
        new(Tablas.TablasWspr.PolinomioA, Tablas.TablasWspr.PolinomioB);

    /// <summary>Polinomio del primer bit de salida.</summary>
    public uint PolinomioA { get; }

    /// <summary>Polinomio del segundo bit de salida.</summary>
    public uint PolinomioB { get; }

    /// <summary>Bits que ve cada bit de salida.</summary>
    public int LongitudDeRestriccion => 32;

    /// <summary>Ceros de cola que hay que anadir al mensaje para vaciar el registro.</summary>
    public int BitsDeCola => LongitudDeRestriccion - 1;

    /// <summary>
    /// Codifica una secuencia de bits, ya con su cola de ceros incluida.
    /// </summary>
    /// <param name="bits">Bits de entrada, uno por byte, con valor 0 o 1.</param>
    /// <returns>El doble de bits: por cada bit de entrada, el del polinomio A y luego el del B.</returns>
    public byte[] Codificar(ReadOnlySpan<byte> bits)
    {
        var salida = new byte[bits.Length * 2];
        uint registro = 0;
        for (var i = 0; i < bits.Length; i++)
        {
            registro = Avanzar(registro, bits[i]);
            salida[2 * i] = BitA(registro);
            salida[(2 * i) + 1] = BitB(registro);
        }
        return salida;
    }

    /// <summary>Mete un bit por la parte baja del registro.</summary>
    /// <param name="registro">Estado actual.</param>
    /// <param name="bit">Bit que entra, 0 o 1.</param>
    public static uint Avanzar(uint registro, int bit) => (registro << 1) | (uint)(bit & 1);

    /// <summary>Bit que saca el polinomio A para un estado del registro.</summary>
    public byte BitA(uint registro) => Paridad(registro & PolinomioA);

    /// <summary>Bit que saca el polinomio B para un estado del registro.</summary>
    public byte BitB(uint registro) => Paridad(registro & PolinomioB);

    /// <summary>Paridad de un numero: 1 si tiene un numero impar de unos.</summary>
    public static byte Paridad(uint valor) => (byte)(BitOperations.PopCount(valor) & 1);
}
