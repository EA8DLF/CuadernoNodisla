using System.Numerics;
using System.Text;
using Nodisla.Cuaderno.Modos.Tablas;

namespace Nodisla.Cuaderno.Modos.Wspr;

/// <summary>
/// Resumen de 15 bits de un indicativo compuesto, el que viaja en los mensajes de tipo 3.
/// </summary>
/// <remarks>
/// <para>
/// Un indicativo con prefijo o sufijo no cabe en los 28 bits del campo de indicativo. WSPR lo
/// resuelve en dos transmisiones: en la primera (tipo 2) va el indicativo entero sin
/// localizador, y en la segunda (tipo 3) va el localizador de seis caracteres con este resumen
/// del indicativo, que el receptor coteja con los que ya ha visto enteros.
/// </para>
/// <para>
/// La funcion es <i>lookup3</i> (<c>hashlittle</c>) de Bob Jenkins, de 2006, que su autor puso
/// en dominio publico. Se aplica a los caracteres del indicativo con la semilla
/// <see cref="TablasWspr.SemillaDelResumen"/> y se queda con los 15 bits bajos. Esta escrita
/// aqui desde la descripcion publica de la funcion.
/// </para>
/// </remarks>
public static class HashDeIndicativo
{
    /// <summary>Resumen de 15 bits del indicativo.</summary>
    /// <param name="indicativo">Indicativo compuesto, tal cual se escribe (<c>PJ4/K1ABC</c>).</param>
    public static int Calcular(string indicativo)
    {
        ArgumentNullException.ThrowIfNull(indicativo);
        var bytes = Encoding.ASCII.GetBytes(indicativo.Trim().ToUpperInvariant());
        return (int)(Lookup3(bytes, TablasWspr.SemillaDelResumen) & 0x7FFF);
    }

    /// <summary>La funcion <c>hashlittle</c> de lookup3 sobre una tira de bytes.</summary>
    /// <param name="clave">Bytes a resumir.</param>
    /// <param name="semilla">Valor inicial.</param>
    public static uint Lookup3(ReadOnlySpan<byte> clave, uint semilla)
    {
        var longitud = clave.Length;
        uint a, b, c;
        a = b = c = 0xdeadbeef + (uint)longitud + semilla;

        var k = 0;
        while (longitud > 12)
        {
            a += Leer(clave, k);
            b += Leer(clave, k + 4);
            c += Leer(clave, k + 8);
            Mezclar(ref a, ref b, ref c);
            longitud -= 12;
            k += 12;
        }

        // Ultimo bloque, de 0 a 12 bytes: cada byte que queda se suma en su sitio.
        switch (longitud)
        {
            case 12: c += (uint)clave[k + 11] << 24; goto case 11;
            case 11: c += (uint)clave[k + 10] << 16; goto case 10;
            case 10: c += (uint)clave[k + 9] << 8; goto case 9;
            case 9: c += clave[k + 8]; goto case 8;
            case 8: b += (uint)clave[k + 7] << 24; goto case 7;
            case 7: b += (uint)clave[k + 6] << 16; goto case 6;
            case 6: b += (uint)clave[k + 5] << 8; goto case 5;
            case 5: b += clave[k + 4]; goto case 4;
            case 4: a += (uint)clave[k + 3] << 24; goto case 3;
            case 3: a += (uint)clave[k + 2] << 16; goto case 2;
            case 2: a += (uint)clave[k + 1] << 8; goto case 1;
            case 1: a += clave[k]; break;
            case 0: return c;
        }

        Rematar(ref a, ref b, ref c);
        return c;
    }

    private static uint Leer(ReadOnlySpan<byte> clave, int i) =>
        clave[i] | ((uint)clave[i + 1] << 8) | ((uint)clave[i + 2] << 16) | ((uint)clave[i + 3] << 24);

    private static void Mezclar(ref uint a, ref uint b, ref uint c)
    {
        a -= c; a ^= BitOperations.RotateLeft(c, 4); c += b;
        b -= a; b ^= BitOperations.RotateLeft(a, 6); a += c;
        c -= b; c ^= BitOperations.RotateLeft(b, 8); b += a;
        a -= c; a ^= BitOperations.RotateLeft(c, 16); c += b;
        b -= a; b ^= BitOperations.RotateLeft(a, 19); a += c;
        c -= b; c ^= BitOperations.RotateLeft(b, 4); b += a;
    }

    private static void Rematar(ref uint a, ref uint b, ref uint c)
    {
        c ^= b; c -= BitOperations.RotateLeft(b, 14);
        a ^= c; a -= BitOperations.RotateLeft(c, 11);
        b ^= a; b -= BitOperations.RotateLeft(a, 25);
        c ^= b; c -= BitOperations.RotateLeft(b, 16);
        a ^= c; a -= BitOperations.RotateLeft(c, 4);
        b ^= a; b -= BitOperations.RotateLeft(a, 14);
        c ^= b; c -= BitOperations.RotateLeft(b, 24);
    }
}
