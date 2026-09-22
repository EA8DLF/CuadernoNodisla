namespace Nodisla.Cuaderno.Audio.Cascada;

/// <summary>
/// La ventana de Hann que se aplica a cada tramo antes de transformarlo.
/// </summary>
/// <remarks>
/// Sin ventana, cortar el audio en tramos es multiplicarlo por un rectangulo, y un rectangulo
/// reparte la energia de cada tono por todo el espectro: en la cascada eso se ve como rayas
/// que tapan las senales debiles. La de Hann ensancha un poco cada tono a cambio de bajar mucho
/// esa fuga, que es justo el trato que interesa para ver FT8.
/// </remarks>
public static class VentanaDeHann
{
    /// <summary>Crea los coeficientes de una ventana de Hann periodica.</summary>
    /// <param name="tamano">Numero de muestras del tramo.</param>
    /// <returns>Los coeficientes, de cero a uno.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Si el tamano no es positivo.</exception>
    public static float[] Crear(int tamano)
    {
        if (tamano <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(tamano),
                tamano,
                "El tamaño de la ventana tiene que ser positivo.");
        }

        var coeficientes = new float[tamano];
        for (var i = 0; i < tamano; i++)
        {
            // Periodica —se divide entre el tamano, no entre el tamano menos uno—, que es la
            // que corresponde cuando se analiza un flujo continuo troceado con solape.
            coeficientes[i] = (float)(0.5 - (0.5 * Math.Cos(2.0 * Math.PI * i / tamano)));
        }

        return coeficientes;
    }

    /// <summary>
    /// Suma de los coeficientes, que es lo que hace falta para normalizar la magnitud.
    /// </summary>
    /// <param name="coeficientes">Coeficientes de la ventana.</param>
    /// <returns>La suma.</returns>
    public static double Suma(ReadOnlySpan<float> coeficientes)
    {
        var suma = 0.0;
        foreach (var coeficiente in coeficientes)
        {
            suma += coeficiente;
        }

        return suma;
    }
}
