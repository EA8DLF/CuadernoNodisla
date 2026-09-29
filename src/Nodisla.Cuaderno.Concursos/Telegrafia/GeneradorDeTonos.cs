namespace Nodisla.Cuaderno.Concursos.Telegrafia;

/// <summary>
/// Convierte los puntos y las rayas en muestras de audio.
/// </summary>
/// <remarks>
/// <para>
/// Lo unico delicado aqui son los <b>flancos</b>. Un tono que arranca y para de golpe no suena
/// como un punto: suena como un chasquido que se oye varios kilohercios a los lados, y en un
/// concurso eso es el operador al que todo el mundo le dice que tiene clics. Por eso cada tono
/// entra y sale con una rampa de coseno alzado de unos milisegundos, que es lo que hace un
/// equipo decente.
/// </para>
/// <para>
/// La rampa no puede pasar de la mitad del elemento mas corto: a sesenta palabras por minuto un
/// punto dura veinte milisegundos, y una rampa de cinco por cada lado ya se come la mitad. Se
/// recorta sola en ese caso.
/// </para>
/// <para>
/// Esta clase no emite: devuelve muestras. Quien las saque por la antena tiene que pasar por el
/// vigilante del PTT.
/// </para>
/// </remarks>
public sealed class GeneradorDeTonos
{
    /// <summary>Crea un generador.</summary>
    /// <param name="frecuenciaDeMuestreo">Muestras por segundo.</param>
    /// <param name="tono">Tono de escucha en hercios.</param>
    /// <param name="amplitud">Amplitud del tono, de 0 a 1.</param>
    /// <param name="flanco">Cuanto tarda el tono en subir y en bajar.</param>
    /// <exception cref="ArgumentOutOfRangeException">Algun valor esta fuera de lo razonable.</exception>
    public GeneradorDeTonos(
        int frecuenciaDeMuestreo = 48000,
        double tono = 700,
        double amplitud = 0.5,
        TimeSpan? flanco = null)
    {
        if (frecuenciaDeMuestreo is < 8000 or > 192000)
            throw new ArgumentOutOfRangeException(nameof(frecuenciaDeMuestreo), frecuenciaDeMuestreo, "Frecuencia de muestreo fuera de lo admitido.");
        if (tono is < 100 or > 3000)
            throw new ArgumentOutOfRangeException(nameof(tono), tono, "El tono de escucha va de 100 a 3000 Hz.");
        if (amplitud is <= 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(amplitud), amplitud, "La amplitud va de 0 a 1.");

        FrecuenciaDeMuestreo = frecuenciaDeMuestreo;
        Tono = tono;
        Amplitud = amplitud;
        Flanco = flanco is { } f && f > TimeSpan.Zero ? f : TimeSpan.FromMilliseconds(5);
    }

    /// <summary>Muestras por segundo.</summary>
    public int FrecuenciaDeMuestreo { get; }

    /// <summary>Tono de escucha en hercios.</summary>
    public double Tono { get; }

    /// <summary>Amplitud del tono.</summary>
    public double Amplitud { get; }

    /// <summary>Lo que tarda el tono en subir y en bajar.</summary>
    public TimeSpan Flanco { get; }

    /// <summary>Genera el audio de una lista de piezas manipuladas.</summary>
    /// <param name="piezas">Puntos, rayas y silencios con su duracion.</param>
    /// <returns>Las muestras, en coma flotante y un solo canal.</returns>
    public float[] Generar(IReadOnlyList<ElementoMorse> piezas)
    {
        ArgumentNullException.ThrowIfNull(piezas);
        if (piezas.Count == 0) return [];

        var total = 0;
        foreach (var pieza in piezas) total += Muestras(pieza.Duracion);
        var salida = new float[total];

        var fase = 0.0;
        var paso = 2.0 * Math.PI * Tono / FrecuenciaDeMuestreo;
        var escrito = 0;

        foreach (var pieza in piezas)
        {
            var cuantas = Muestras(pieza.Duracion);
            if (!pieza.ConTono)
            {
                // El silencio no adelanta la fase: al volver el tono arranca donde lo dejo y
                // no hay salto de fase audible entre elementos de la misma letra.
                escrito += cuantas;
                continue;
            }

            var rampa = Math.Min(Muestras(Flanco), cuantas / 2);
            for (var i = 0; i < cuantas; i++)
            {
                var envolvente = 1.0;
                if (rampa > 0)
                {
                    if (i < rampa) envolvente = 0.5 - 0.5 * Math.Cos(Math.PI * i / rampa);
                    else if (i >= cuantas - rampa) envolvente = 0.5 - 0.5 * Math.Cos(Math.PI * (cuantas - 1 - i) / rampa);
                }
                salida[escrito + i] = (float)(Amplitud * envolvente * Math.Sin(fase));
                fase += paso;
                if (fase > 2.0 * Math.PI) fase -= 2.0 * Math.PI;
            }
            escrito += cuantas;
        }

        return salida;
    }

    /// <summary>Genera el audio de un texto.</summary>
    /// <param name="manipulador">Manipulador que pone la velocidad.</param>
    /// <param name="texto">Texto a manipular.</param>
    /// <returns>Las muestras.</returns>
    public float[] Generar(Manipulador manipulador, string? texto)
    {
        ArgumentNullException.ThrowIfNull(manipulador);
        return Generar(manipulador.Manipular(texto));
    }

    private int Muestras(TimeSpan duracion) =>
        (int)Math.Round(duracion.TotalSeconds * FrecuenciaDeMuestreo, MidpointRounding.AwayFromZero);
}
