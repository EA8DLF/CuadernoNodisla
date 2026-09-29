namespace Nodisla.Cuaderno.Modos.Msk144;

/// <summary>
/// Convierte una trama de bits en el audio MSK que se manda al equipo.
/// </summary>
/// <remarks>
/// <para>
/// Se construye como OQPSK con pulsos de medio seno, que es exactamente MSK visto desde otro
/// lado. Cada bit es un pulso de medio seno de un milisegundo centrado en su instante; los bits
/// de indice par van al canal en cuadratura y los de indice impar al canal en fase, con lo que
/// los pulsos de un canal caen justo entre los del otro y la envolvente sale constante. Un bit
/// a uno es un pulso positivo y un bit a cero uno negativo. La senal de audio es
/// <c>I(t)·cos(ωt) − Q(t)·sin(ωt)</c> con la portadora en 1500 Hz.
/// </para>
/// <para>
/// La misma trama se repite sin parar mientras dure la emision, porque nadie sabe cuando va a
/// caer el ping. El pulso del bit cero empieza medio milisegundo antes del comienzo de la trama,
/// y el del ultimo bit acaba medio milisegundo despues: en la repeticion continua encajan unos
/// con otros sin costura.
/// </para>
/// </remarks>
public static class ModuladorMsk
{
    /// <summary>Duracion de un bit, en segundos.</summary>
    public const double DuracionDeBit = 1.0 / ParametrosMsk144.Baudios;

    /// <summary>
    /// Sintetiza una trama repetida el numero de veces indicado.
    /// </summary>
    /// <param name="trama">Bits de la trama, 144 o 40.</param>
    /// <param name="portadoraHz">Portadora, 1500 Hz por convenio.</param>
    /// <param name="frecuenciaDeMuestreo">Muestras por segundo de la salida.</param>
    /// <param name="repeticiones">Cuantas veces se repite la trama.</param>
    /// <param name="amplitud">Amplitud de pico, de 0 a 1.</param>
    /// <returns>El audio, sin silencio delante ni detras.</returns>
    public static float[] Sintetizar(
        ReadOnlySpan<byte> trama,
        double portadoraHz,
        int frecuenciaDeMuestreo,
        int repeticiones,
        double amplitud = 0.5)
    {
        if (trama.Length == 0) throw new ArgumentException("La trama está vacía.", nameof(trama));
        if (frecuenciaDeMuestreo <= 0) throw new ArgumentOutOfRangeException(nameof(frecuenciaDeMuestreo));
        if (repeticiones <= 0) throw new ArgumentOutOfRangeException(nameof(repeticiones));
        if (amplitud is <= 0 or > 1) throw new ArgumentOutOfRangeException(nameof(amplitud));

        var bits = trama.Length * repeticiones;
        var muestras = (int)Math.Round(bits * DuracionDeBit * frecuenciaDeMuestreo);
        var enFase = new double[muestras];
        var cuadratura = new double[muestras];

        // Cada bit deja su medio seno en su canal. El pulso dura dos bits y esta centrado en el
        // instante del bit, asi que se reparte medio bit a cada lado.
        var muestrasPorBit = DuracionDeBit * frecuenciaDeMuestreo;
        for (var i = 0; i < bits; i++)
        {
            var bit = trama[i % trama.Length];
            var signo = bit != 0 ? 1.0 : -1.0;
            var canal = (i & 1) == 0 ? cuadratura : enFase;
            var centro = i * muestrasPorBit;
            var desde = Math.Max(0, (int)Math.Ceiling(centro - muestrasPorBit));
            var hasta = Math.Min(muestras - 1, (int)Math.Floor(centro + muestrasPorBit));
            for (var n = desde; n <= hasta; n++)
            {
                var t = (n - centro) / muestrasPorBit; // de -1 a 1, en bits
                canal[n] += signo * Math.Cos(Math.PI * t / 2);
            }
        }

        var senal = new float[muestras];
        var avance = 2 * Math.PI * portadoraHz / frecuenciaDeMuestreo;
        var fase = 0.0;
        for (var n = 0; n < muestras; n++)
        {
            senal[n] = (float)(amplitud * ((enFase[n] * Math.Cos(fase)) - (cuadratura[n] * Math.Sin(fase))));
            fase += avance;
            if (fase > 2 * Math.PI) fase -= 2 * Math.PI;
        }

        SuavizarExtremos(senal, (int)Math.Round(muestrasPorBit * 2));
        return senal;
    }

    /// <summary>Sintetiza tramas repetidas hasta cubrir la duracion pedida.</summary>
    public static float[] Sintetizar(ReadOnlySpan<byte> trama, double portadoraHz, int frecuenciaDeMuestreo, double segundos, double amplitud = 0.5)
    {
        if (segundos <= 0) throw new ArgumentOutOfRangeException(nameof(segundos));
        var porTrama = trama.Length * DuracionDeBit;
        var repeticiones = Math.Max(1, (int)Math.Floor(segundos / porTrama));
        return Sintetizar(trama, portadoraHz, frecuenciaDeMuestreo, repeticiones, amplitud);
    }

    private static void SuavizarExtremos(float[] senal, int muestrasDeRampa)
    {
        if (muestrasDeRampa <= 0 || senal.Length < 2 * muestrasDeRampa) return;
        for (var i = 0; i < muestrasDeRampa; i++)
        {
            var envolvente = (float)((1 - Math.Cos(Math.PI * i / muestrasDeRampa)) / 2);
            senal[i] *= envolvente;
            senal[senal.Length - 1 - i] *= envolvente;
        }
    }
}
