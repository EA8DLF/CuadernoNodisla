namespace Nodisla.Cuaderno.Audio.Cascada;

/// <summary>
/// Transformada rapida de Fourier, en el sitio y de base dos.
/// </summary>
/// <remarks>
/// No hay dependencia externa a proposito: el algoritmo cabe en una pagina, no cambia nunca y
/// asi la cascada no arrastra una biblioteca de calculo entera. Trabaja en el sitio sobre dos
/// vectores —parte real y parte imaginaria— del mismo tamano, que tiene que ser potencia de dos.
/// </remarks>
public static class TransformadaRapida
{
    /// <summary>Dice si un numero es potencia de dos, que es lo que admite la transformada.</summary>
    /// <param name="n">Numero a comprobar.</param>
    /// <returns>Verdadero si es potencia de dos y mayor que cero.</returns>
    public static bool EsPotenciaDeDos(int n) => n > 0 && (n & (n - 1)) == 0;

    /// <summary>
    /// Transforma en el sitio la senal que llega en <paramref name="real"/> e
    /// <paramref name="imaginaria"/>.
    /// </summary>
    /// <param name="real">Parte real; sale con la parte real del espectro.</param>
    /// <param name="imaginaria">Parte imaginaria; sale con la parte imaginaria del espectro.</param>
    /// <exception cref="ArgumentException">
    /// Si los dos vectores no miden lo mismo o el tamano no es potencia de dos.
    /// </exception>
    public static void Transformar(Span<float> real, Span<float> imaginaria)
    {
        if (real.Length != imaginaria.Length)
        {
            throw new ArgumentException(
                "La parte real y la imaginaria tienen que medir lo mismo.",
                nameof(imaginaria));
        }

        var n = real.Length;
        if (!EsPotenciaDeDos(n))
        {
            throw new ArgumentException(
                "El tamano de la transformada tiene que ser potencia de dos.",
                nameof(real));
        }

        if (n == 1)
        {
            return;
        }

        OrdenarPorBitsAlReves(real, imaginaria);

        // Mariposas: de dos en dos, de cuatro en cuatro, y asi hasta el tamano entero.
        for (var salto = 2; salto <= n; salto <<= 1)
        {
            var mitad = salto >> 1;
            var paso = -2.0 * Math.PI / salto;

            for (var bloque = 0; bloque < n; bloque += salto)
            {
                for (var k = 0; k < mitad; k++)
                {
                    var angulo = paso * k;
                    var coseno = Math.Cos(angulo);
                    var seno = Math.Sin(angulo);

                    var i = bloque + k;
                    var j = i + mitad;

                    var realJ = real[j];
                    var imaginariaJ = imaginaria[j];

                    var realGiro = (realJ * coseno) - (imaginariaJ * seno);
                    var imaginariaGiro = (realJ * seno) + (imaginariaJ * coseno);

                    real[j] = (float)(real[i] - realGiro);
                    imaginaria[j] = (float)(imaginaria[i] - imaginariaGiro);
                    real[i] = (float)(real[i] + realGiro);
                    imaginaria[i] = (float)(imaginaria[i] + imaginariaGiro);
                }
            }
        }
    }

    /// <summary>Coloca las muestras en el orden que pide el algoritmo: el indice al reves.</summary>
    private static void OrdenarPorBitsAlReves(Span<float> real, Span<float> imaginaria)
    {
        var n = real.Length;
        var j = 0;

        for (var i = 1; i < n; i++)
        {
            var bit = n >> 1;
            while ((j & bit) != 0)
            {
                j ^= bit;
                bit >>= 1;
            }

            j |= bit;

            if (i < j)
            {
                (real[i], real[j]) = (real[j], real[i]);
                (imaginaria[i], imaginaria[j]) = (imaginaria[j], imaginaria[i]);
            }
        }
    }
}
