namespace Nodisla.Cuaderno.Modos.Jt65;

/// <summary>
/// Convierte los 126 tonos de JT65 en audio.
/// </summary>
/// <remarks>
/// <para>
/// JT65 es FSK de fase continua sin suavizado: cada simbolo es un seno puro a su frecuencia y
/// la fase se lleva acumulada muestra a muestra, de modo que el cambio de tono no da salto de
/// fase. Con simbolos de 372 ms el espectro de cada tono es tan estrecho (2,7 Hz) que no hace
/// falta el filtro gaussiano de FT8.
/// </para>
/// <para>
/// Los extremos llevan una rampa de medio coseno de 20 ms para no chasquear al encender ni al
/// apagar. Tambien va aqui, y no en el modem, para que cualquier salida (antena, fichero, banco
/// de pruebas) lleve la misma senal.
/// </para>
/// </remarks>
public static class ModuladorJt65
{
    /// <summary>Duracion de las rampas de encendido y apagado, en segundos.</summary>
    public const double RampaSegundos = 0.020;

    /// <summary>
    /// Sintetiza el audio de una secuencia de tonos.
    /// </summary>
    /// <param name="parametros">Submodo.</param>
    /// <param name="tonos">126 tonos, de 0 a 65.</param>
    /// <param name="tonoBaseHz">Frecuencia del tono de sincronismo, por ejemplo 1270,5 Hz.</param>
    /// <param name="frecuenciaDeMuestreo">Muestras por segundo de la salida.</param>
    /// <param name="amplitud">Amplitud de pico, de 0 a 1.</param>
    /// <returns>El audio del mensaje, sin silencio delante ni detras: 46,8 segundos.</returns>
    public static float[] Sintetizar(
        ParametrosJt65 parametros,
        ReadOnlySpan<byte> tonos,
        double tonoBaseHz,
        int frecuenciaDeMuestreo = 48000,
        double amplitud = 0.5)
    {
        ArgumentNullException.ThrowIfNull(parametros);
        if (tonos.Length != TablasJt65.Simbolos)
            throw new ArgumentException($"Hacen falta {TablasJt65.Simbolos} tonos y llegan {tonos.Length}.", nameof(tonos));
        if (frecuenciaDeMuestreo <= 0) throw new ArgumentOutOfRangeException(nameof(frecuenciaDeMuestreo));
        if (amplitud is <= 0 or > 1) throw new ArgumentOutOfRangeException(nameof(amplitud));
        foreach (var t in tonos)
            if (t >= TablasJt65.TonosTotales) throw new ArgumentException($"El tono {t} no existe.", nameof(tonos));

        var muestras = (int)Math.Round(parametros.DuracionDeLaSenalSegundos * frecuenciaDeMuestreo);
        var senal = new float[muestras];
        var espaciado = parametros.EspaciadoDeTonosHz;
        var fase = 0.0;
        var muestrasPorSimbolo = ParametrosJt65.DuracionDeSimboloSegundos * frecuenciaDeMuestreo;

        for (var n = 0; n < muestras; n++)
        {
            // El simbolo se decide por el tiempo, no por un contador de muestras enteras: a 48000
            // muestras por segundo un simbolo son 17832,8 muestras, y redondearlas acumularia
            // medio simbolo de deriva al final del minuto.
            var k = Math.Min(TablasJt65.Simbolos - 1, (int)(n / muestrasPorSimbolo));
            var frecuencia = tonoBaseHz + (tonos[k] * espaciado);
            senal[n] = (float)(amplitud * Math.Sin(fase));
            fase += 2 * Math.PI * frecuencia / frecuenciaDeMuestreo;
            if (fase > 2 * Math.PI) fase -= 2 * Math.PI;
        }

        SuavizarExtremos(senal, (int)Math.Round(RampaSegundos * frecuenciaDeMuestreo));
        return senal;
    }

    private static void SuavizarExtremos(float[] senal, int muestrasDeRampa)
    {
        if (muestrasDeRampa <= 0 || 2 * muestrasDeRampa > senal.Length) return;
        for (var i = 0; i < muestrasDeRampa; i++)
        {
            var envolvente = (float)((1 - Math.Cos(Math.PI * i / muestrasDeRampa)) / 2);
            senal[i] *= envolvente;
            senal[senal.Length - 1 - i] *= envolvente;
        }
    }
}
