namespace Nodisla.Cuaderno.Modos.Wspr;

/// <summary>
/// Convierte los 162 tonos de WSPR en audio.
/// </summary>
/// <remarks>
/// <para>
/// Es una modulacion de cuatro tonos con fase continua: la fase se lleva acumulada muestra a
/// muestra y solo cambia la velocidad a la que gira, asi que al pasar de un tono a otro no hay
/// ningun salto. Con simbolos de dos tercios de segundo y tonos a 1,46 Hz, eso basta para que
/// la senal ocupe sus seis hercios y nada mas; WSPR no suaviza el cambio de tono como hace FT8.
/// </para>
/// <para>
/// Lo unico que se suaviza es el principio y el final: una subida y una bajada de volumen en
/// coseno alzado, para que encender y apagar la portadora no de un chasquido que se oiga en
/// toda la banda.
/// </para>
/// <para>
/// La frecuencia que se pide es el <b>centro</b> de los cuatro tonos, que es la que se apunta
/// en un spot; los tonos caen a ±0,73 y ±2,2 Hz de ella.
/// </para>
/// </remarks>
public static class ModuladorWspr
{
    /// <summary>Duracion de la subida y la bajada de volumen, en segundos.</summary>
    public const double RampaSegundos = 0.02;

    /// <summary>
    /// Sintetiza el audio de una secuencia de tonos.
    /// </summary>
    /// <param name="tonos">Los 162 tonos, de 0 a 3.</param>
    /// <param name="frecuenciaCentralHz">Centro de los cuatro tonos dentro del audio.</param>
    /// <param name="frecuenciaDeMuestreo">Muestras por segundo de la salida.</param>
    /// <param name="amplitud">Amplitud de pico, de 0 a 1.</param>
    /// <param name="derivaHz">
    /// Deriva lineal de frecuencia a lo largo de la transmision, para fabricar en el banco las
    /// senales de equipos que se van calentando. Cero al emitir de verdad.
    /// </param>
    /// <returns>El audio del mensaje, 110,6 segundos, sin silencio delante ni detras.</returns>
    public static float[] Sintetizar(
        ReadOnlySpan<byte> tonos,
        double frecuenciaCentralHz,
        int frecuenciaDeMuestreo = 48000,
        double amplitud = 0.5,
        double derivaHz = 0)
    {
        if (tonos.Length != ParametrosWspr.Simbolos)
            throw new ArgumentException($"Hacen falta {ParametrosWspr.Simbolos} tonos y llegan {tonos.Length}.", nameof(tonos));
        if (frecuenciaDeMuestreo <= 0) throw new ArgumentOutOfRangeException(nameof(frecuenciaDeMuestreo));
        if (amplitud is <= 0 or > 1) throw new ArgumentOutOfRangeException(nameof(amplitud));
        foreach (var t in tonos)
            if (t > 3) throw new ArgumentException("Los tonos van de 0 a 3.", nameof(tonos));

        var muestras = (int)Math.Round(ParametrosWspr.DuracionDeLaSenalSegundos * frecuenciaDeMuestreo);
        var salida = new float[muestras];
        var muestrasPorSimbolo = ParametrosWspr.DuracionDeSimboloSegundos * frecuenciaDeMuestreo;
        var muestrasDeRampa = Math.Max(1, (int)Math.Round(RampaSegundos * frecuenciaDeMuestreo));

        double fase = 0;
        for (var i = 0; i < muestras; i++)
        {
            var simbolo = Math.Min(ParametrosWspr.Simbolos - 1, (int)(i / muestrasPorSimbolo));
            var frecuencia = frecuenciaCentralHz
                + ((tonos[simbolo] - 1.5) * ParametrosWspr.EspaciadoDeTonosHz)
                + (derivaHz * (((double)i / muestras) - 0.5));

            var envolvente = 1.0;
            if (i < muestrasDeRampa) envolvente = 0.5 * (1 - Math.Cos(Math.PI * i / muestrasDeRampa));
            else if (i >= muestras - muestrasDeRampa) envolvente = 0.5 * (1 - Math.Cos(Math.PI * (muestras - 1 - i) / muestrasDeRampa));

            salida[i] = (float)(amplitud * envolvente * Math.Sin(fase));
            fase += 2 * Math.PI * frecuencia / frecuenciaDeMuestreo;
            if (fase > 2 * Math.PI) fase -= 2 * Math.PI;
        }
        return salida;
    }
}
