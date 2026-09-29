using Nodisla.Cuaderno.Modos.Ft8;

namespace Nodisla.Cuaderno.Modos.Banco;

/// <summary>
/// Fabrica ventanas de audio con senales de relacion senal-ruido conocida.
/// </summary>
/// <remarks>
/// <para>
/// Es la pieza que convierte «decodifica bien» en una cifra. Sin esto solo se puede opinar: se
/// prueba un cambio, parece que sale mas, y no hay manera de saber si ha mejorado de verdad o
/// es que esa tarde habia buena propagacion.
/// </para>
/// <para>
/// <b>Que significa la relacion senal-ruido en FT8.</b> Se da siempre referida a un ancho de
/// banda de 2500 hercios, que es lo que ocupa una emision de voz. Una senal de FT8 ocupa solo
/// 50, asi que cuando el informe dice −20 dB la senal es en realidad mas fuerte que el ruido
/// <i>dentro de su propio hueco</i>; lo que pasa es que se la compara con el ruido de un hueco
/// cincuenta veces mas ancho. Por eso FT8 decodifica cosas que el oido no distingue del ruido.
/// </para>
/// <para>
/// El ruido que se genera es blanco y gaussiano. No es el ruido de una banda de verdad —ahi hay
/// tormentas, portadoras, ruido de plasma del vecino— pero es el patron con el que se miden
/// todos los modos digitales, y es lo que permite comparar cifras con las publicadas. Las
/// grabaciones reales son el siguiente escalon, y este banco esta hecho para poder meterlas.
/// </para>
/// </remarks>
public static class GeneradorDeSenal
{
    /// <summary>Ancho de banda de referencia de los informes de FT8, en hercios.</summary>
    public const double AnchoDeBandaDeReferencia = 2500.0;

    /// <summary>
    /// Fabrica una ventana con una senal a la relacion pedida.
    /// </summary>
    /// <param name="parametros">Parametros del modo.</param>
    /// <param name="tonos">Tonos del mensaje, tal y como los deja el codificador.</param>
    /// <param name="tonoBaseHz">Frecuencia del tono cero.</param>
    /// <param name="desfaseSegundos">Cuanto se adelanta o se atrasa respecto al comienzo previsto.</param>
    /// <param name="decibelios">Relacion senal-ruido referida a 2500 Hz.</param>
    /// <param name="frecuenciaDeMuestreo">Muestras por segundo de la ventana.</param>
    /// <param name="azar">Generador de numeros al azar, para que la prueba se pueda repetir.</param>
    public static float[] Ventana(
        ParametrosDelModo parametros,
        ReadOnlySpan<byte> tonos,
        double tonoBaseHz,
        double desfaseSegundos,
        double decibelios,
        int frecuenciaDeMuestreo,
        Random azar)
    {
        ArgumentNullException.ThrowIfNull(parametros);
        ArgumentNullException.ThrowIfNull(azar);

        var muestrasDeLaVentana = (int)Math.Round(parametros.PeriodoSegundos * frecuenciaDeMuestreo);
        var ventana = new float[muestrasDeLaVentana];

        const double Amplitud = 0.35;
        var senal = Modulador.Sintetizar(parametros, tonos, tonoBaseHz, frecuenciaDeMuestreo, Amplitud);

        var comienzo = (int)Math.Round((parametros.ComienzoNominalSegundos + desfaseSegundos) * frecuenciaDeMuestreo);
        for (var i = 0; i < senal.Length; i++)
        {
            var j = comienzo + i;
            if (j >= 0 && j < ventana.Length) ventana[j] += senal[i];
        }

        AnadirRuido(ventana, PotenciaMedia(senal), decibelios, frecuenciaDeMuestreo, azar);
        return ventana;
    }

    /// <summary>
    /// Anade ruido blanco a una ventana hasta dejarla en la relacion pedida.
    /// </summary>
    /// <param name="ventana">Ventana a la que anadir el ruido, se modifica en el sitio.</param>
    /// <param name="potenciaDeLaSenal">Potencia media de la senal mientras suena.</param>
    /// <param name="decibelios">Relacion senal-ruido referida a 2500 Hz.</param>
    /// <param name="frecuenciaDeMuestreo">Muestras por segundo.</param>
    /// <param name="azar">Generador de numeros al azar.</param>
    /// <remarks>
    /// La cuenta es directa: el ruido blanco reparte su potencia por igual entre los cero y los
    /// <paramref name="frecuenciaDeMuestreo"/> medios hercios que caben, asi que para saber
    /// cuanto cae dentro de los 2500 de referencia basta con la proporcion. De ahi se despeja la
    /// varianza que hay que darle.
    /// </remarks>
    public static void AnadirRuido(float[] ventana, double potenciaDeLaSenal, double decibelios, int frecuenciaDeMuestreo, Random azar)
    {
        ArgumentNullException.ThrowIfNull(ventana);
        ArgumentNullException.ThrowIfNull(azar);

        var razon = Math.Pow(10, decibelios / 10);
        var anchoTotal = frecuenciaDeMuestreo / 2.0;
        var varianza = potenciaDeLaSenal * anchoTotal / (AnchoDeBandaDeReferencia * razon);
        var desviacion = Math.Sqrt(varianza);

        for (var i = 0; i < ventana.Length; i++)
            ventana[i] += (float)(desviacion * Gaussiana(azar));
    }

    /// <summary>Potencia media de un trozo de audio.</summary>
    public static double PotenciaMedia(ReadOnlySpan<float> muestras)
    {
        if (muestras.Length == 0) return 0;
        double suma = 0;
        foreach (var v in muestras) suma += (double)v * v;
        return suma / muestras.Length;
    }

    /// <summary>
    /// Un numero al azar con distribucion normal de media cero y desviacion uno.
    /// </summary>
    /// <remarks>
    /// Metodo de Box y Muller: dos numeros repartidos por igual entre cero y uno se convierten
    /// en dos numeros con campana de Gauss. Se descarta el segundo para no tener que guardar
    /// estado, que a esta escala no cuesta nada y deja la funcion sin efectos colaterales.
    /// </remarks>
    private static double Gaussiana(Random azar)
    {
        var u1 = 1.0 - azar.NextDouble();
        var u2 = azar.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }
}
