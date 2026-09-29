namespace Nodisla.Cuaderno.Modos.Jt9;

/// <summary>
/// Convierte los 85 tonos de JT9 en audio: FSK de fase continua, sin suavizado, con rampas de
/// medio coseno en los extremos.
/// </summary>
/// <remarks>
/// Los tonos de JT9 duran 576 ms y estan a 1,7 Hz unos de otros: el espectro de cada uno es tan
/// estrecho que la senal entera cabe en 16 Hz sin ningun filtro. La fase se lleva acumulada
/// muestra a muestra para que el cambio de tono no de salto.
/// </remarks>
public static class ModuladorJt9
{
    /// <summary>Duracion de las rampas de encendido y apagado, en segundos.</summary>
    public const double RampaSegundos = 0.020;

    /// <summary>Sintetiza el audio de una secuencia de tonos.</summary>
    /// <param name="tonos">85 tonos, de 0 a 8.</param>
    /// <param name="tonoBaseHz">Frecuencia del tono 0 (el de sincronismo).</param>
    /// <param name="frecuenciaDeMuestreo">Muestras por segundo de la salida.</param>
    /// <param name="amplitud">Amplitud de pico, de 0 a 1.</param>
    /// <returns>El audio del mensaje, sin silencio delante ni detras: 48,96 segundos.</returns>
    public static float[] Sintetizar(ReadOnlySpan<byte> tonos, double tonoBaseHz, int frecuenciaDeMuestreo = 48000, double amplitud = 0.5)
    {
        if (tonos.Length != TablasJt9.Simbolos)
            throw new ArgumentException($"Hacen falta {TablasJt9.Simbolos} tonos y llegan {tonos.Length}.", nameof(tonos));
        if (frecuenciaDeMuestreo <= 0) throw new ArgumentOutOfRangeException(nameof(frecuenciaDeMuestreo));
        if (amplitud is <= 0 or > 1) throw new ArgumentOutOfRangeException(nameof(amplitud));
        foreach (var t in tonos)
            if (t >= TablasJt9.Tonos) throw new ArgumentException($"El tono {t} no existe.", nameof(tonos));

        var muestras = (int)Math.Round(ParametrosJt9.DuracionDeLaSenalSegundos * frecuenciaDeMuestreo);
        var senal = new float[muestras];
        var muestrasPorSimbolo = ParametrosJt9.DuracionDeSimboloSegundos * frecuenciaDeMuestreo;
        var fase = 0.0;
        for (var n = 0; n < muestras; n++)
        {
            var k = Math.Min(TablasJt9.Simbolos - 1, (int)(n / muestrasPorSimbolo));
            var frecuencia = tonoBaseHz + (tonos[k] * ParametrosJt9.EspaciadoDeTonosHz);
            senal[n] = (float)(amplitud * Math.Sin(fase));
            fase += 2 * Math.PI * frecuencia / frecuenciaDeMuestreo;
            if (fase > 2 * Math.PI) fase -= 2 * Math.PI;
        }

        var rampa = (int)Math.Round(RampaSegundos * frecuenciaDeMuestreo);
        for (var i = 0; i < rampa && 2 * rampa <= senal.Length; i++)
        {
            var envolvente = (float)((1 - Math.Cos(Math.PI * i / rampa)) / 2);
            senal[i] *= envolvente;
            senal[senal.Length - 1 - i] *= envolvente;
        }
        return senal;
    }
}
