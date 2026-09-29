namespace Nodisla.Cuaderno.Modos.Senal;

/// <summary>
/// Transformada rapida de Fourier propia, de raiz dos y en el sitio.
/// </summary>
/// <remarks>
/// <para>
/// Es propia a proposito: el modem no depende de ninguna biblioteca de terceros, de modo que
/// no hay nada que actualizar, ni licencias que revisar, ni un binario nativo que pueda tumbar
/// el cuaderno. La FFT de raiz dos es de sobra para lo que hace falta aqui: las longitudes que
/// usa el modem (1920 muestras por simbolo en FT8, 576 en FT4) se rellenan con ceros hasta la
/// siguiente potencia de dos, que ademas es justo lo que se quiere para afinar la resolucion
/// en frecuencia.
/// </para>
/// <para>
/// La transformada se escribe <b>sobre</b> los vectores que se le pasan. Quien llama reutiliza
/// los mismos vectores ventana tras ventana y asi no se recolecta basura en mitad de una
/// decodificacion, que es cuando mas prisa hay.
/// </para>
/// </remarks>
public static class Fft
{
    /// <summary>
    /// Transforma en el sitio. Las partes real e imaginaria van en vectores separados porque
    /// el resto del modem trabaja con <c>float[]</c> y asi no hay que copiar a estructuras.
    /// </summary>
    /// <param name="real">Parte real; sale sustituida por la de la transformada.</param>
    /// <param name="imaginaria">Parte imaginaria; sale sustituida por la de la transformada.</param>
    /// <exception cref="ArgumentException">Si las longitudes no coinciden o no son potencia de dos.</exception>
    public static void Transformar(Span<float> real, Span<float> imaginaria)
    {
        if (real.Length != imaginaria.Length)
            throw new ArgumentException("Las partes real e imaginaria deben medir lo mismo.", nameof(imaginaria));
        var n = real.Length;
        if (n <= 1) return;
        if (!EsPotenciaDeDos(n))
            throw new ArgumentException($"La longitud debe ser potencia de dos y es {n}.", nameof(real));

        // Primero se reordenan las muestras invirtiendo los bits del indice. La FFT de raiz dos
        // va combinando parejas cada vez mas separadas; si se parte del orden invertido, todas
        // esas combinaciones caen en posiciones contiguas y no hace falta memoria auxiliar.
        ReordenarPorBitsInvertidos(real, imaginaria);

        for (var longitud = 2; longitud <= n; longitud <<= 1)
        {
            // Raiz de la unidad correspondiente a este tamano de bloque. Signo negativo porque
            // esta es la transformada directa (de tiempo a frecuencia).
            var angulo = -2.0 * Math.PI / longitud;
            var pasoReal = Math.Cos(angulo);
            var pasoImaginaria = Math.Sin(angulo);

            for (var inicio = 0; inicio < n; inicio += longitud)
            {
                // El giro se acumula multiplicando en vez de llamando a Cos y Sin en cada
                // mariposa: es lo que hace que la FFT sea rapida de verdad. Se recalcula en
                // cada bloque para que el error de redondeo no se arrastre por todo el vector.
                double giroReal = 1.0, giroImaginaria = 0.0;
                var mitad = longitud >> 1;
                for (var k = 0; k < mitad; k++)
                {
                    var i = inicio + k;
                    var j = i + mitad;

                    var pr = (float)(real[j] * giroReal - imaginaria[j] * giroImaginaria);
                    var pi = (float)(real[j] * giroImaginaria + imaginaria[j] * giroReal);

                    real[j] = real[i] - pr;
                    imaginaria[j] = imaginaria[i] - pi;
                    real[i] += pr;
                    imaginaria[i] += pi;

                    var siguienteReal = giroReal * pasoReal - giroImaginaria * pasoImaginaria;
                    giroImaginaria = giroReal * pasoImaginaria + giroImaginaria * pasoReal;
                    giroReal = siguienteReal;
                }
            }
        }
    }

    /// <summary>
    /// Transforma una senal real y devuelve la magnitud de cada casilla de frecuencia.
    /// </summary>
    /// <param name="muestras">Muestras de entrada; se copian, no se tocan.</param>
    /// <param name="longitudFft">Longitud de la transformada, potencia de dos y mayor o igual que las muestras.</param>
    /// <param name="magnitudes">
    /// Destino de las magnitudes. Basta con la mitad mas uno de la longitud: una senal real
    /// tiene el espectro simetrico y la otra mitad no anade informacion.
    /// </param>
    public static void MagnitudesDeSenalReal(ReadOnlySpan<float> muestras, int longitudFft, Span<float> magnitudes)
    {
        if (!EsPotenciaDeDos(longitudFft))
            throw new ArgumentException($"La longitud debe ser potencia de dos y es {longitudFft}.", nameof(longitudFft));
        if (muestras.Length > longitudFft)
            throw new ArgumentException("Hay mas muestras que longitud de transformada.", nameof(muestras));
        var utiles = (longitudFft / 2) + 1;
        if (magnitudes.Length < utiles)
            throw new ArgumentException($"Hacen falta al menos {utiles} casillas.", nameof(magnitudes));

        var real = new float[longitudFft];
        var imaginaria = new float[longitudFft];
        muestras.CopyTo(real);
        Transformar(real, imaginaria);

        for (var i = 0; i < utiles; i++)
            magnitudes[i] = MathF.Sqrt((real[i] * real[i]) + (imaginaria[i] * imaginaria[i]));
    }

    /// <summary>
    /// Transformada inversa en el sitio: de frecuencia a tiempo.
    /// </summary>
    /// <remarks>
    /// Se hace con la directa, cambiando de signo la parte imaginaria antes y despues y
    /// dividiendo por la longitud. Es un truco conocido y evita mantener dos rutinas que
    /// tendrian que ir siempre a la par.
    /// </remarks>
    /// <param name="real">Parte real; sale sustituida.</param>
    /// <param name="imaginaria">Parte imaginaria; sale sustituida.</param>
    public static void TransformarInversa(Span<float> real, Span<float> imaginaria)
    {
        for (var i = 0; i < imaginaria.Length; i++) imaginaria[i] = -imaginaria[i];
        Transformar(real, imaginaria);
        var escala = 1f / real.Length;
        for (var i = 0; i < real.Length; i++)
        {
            real[i] *= escala;
            imaginaria[i] *= -escala;
        }
    }

    /// <summary>Un numero es potencia de dos si solo tiene un bit puesto.</summary>
    public static bool EsPotenciaDeDos(int n) => n > 0 && (n & (n - 1)) == 0;

    /// <summary>Menor potencia de dos que llega a <paramref name="n"/>.</summary>
    public static int PotenciaDeDosQueCubre(int n)
    {
        if (n <= 1) return 1;
        var p = 1;
        while (p < n) p <<= 1;
        return p;
    }

    private static void ReordenarPorBitsInvertidos(Span<float> real, Span<float> imaginaria)
    {
        var n = real.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            var bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i >= j) continue;
            (real[i], real[j]) = (real[j], real[i]);
            (imaginaria[i], imaginaria[j]) = (imaginaria[j], imaginaria[i]);
        }
    }
}
