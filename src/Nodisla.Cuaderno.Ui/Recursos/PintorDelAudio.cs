using Nodisla.Cuaderno.Audio.Cascada;

namespace Nodisla.Cuaderno.Ui.Recursos;

/// <summary>
/// Los dos paneles de abajo de la pantalla MULTI del FT-710: el osciloscopio y el AF-FFT del
/// audio de recepcion.
/// </summary>
/// <remarks>
/// <para>
/// Segun el manual de operacion del FT-710 (pag. 23, «MULTI»): «In addition to the scope
/// display, the oscilloscope and AF-FFT are also presented». En la foto del manual el
/// osciloscopio va a la izquierda, en cian, a 10 ms por division, y el AF-FFT a la derecha,
/// relleno en azul, de 0 a 4 kHz.
/// </para>
/// <para>
/// Aqui se calculan con el audio del codec USB de la radio, el mismo que ya lee el modem: no es
/// la señal interna de la radio, pero es lo mismo que sale por su altavoz. Codigo puro, sin
/// WPF, para poder probarlo.
/// </para>
/// </remarks>
public sealed class PintorDelAudio
{
    /// <summary>Ancho de cada panel, en puntos (la mitad del analizador).</summary>
    public const int Ancho = 425;

    /// <summary>Alto de cada panel.</summary>
    public const int Alto = 100;

    /// <summary>Divisiones del osciloscopio.</summary>
    public const int Divisiones = 10;

    /// <summary>Milisegundos por division del osciloscopio (lo que rotula la radio).</summary>
    public const int MilisegundosPorDivision = 10;

    /// <summary>Hasta donde llega el AF-FFT, en hercios (la escala de la radio: 0 a 4 kHz).</summary>
    public const int TopeDelAfFft = 4_000;

    /// <summary>Muestras de la transformada.</summary>
    public const int PuntosDeLaTransformada = 4096;

    private const int Fondo = unchecked((int)0xFF05070A);
    private const int Rejilla = unchecked((int)0xFF1C242E);
    private const int Cian = unchecked((int)0xFF2FE6F0);
    private const int AzulRelleno = unchecked((int)0xFF5A6FE0);
    private const int AzulBorde = unchecked((int)0xFFB8C6FF);

    private static readonly float[] Hann = VentanaDeHann.Crear(PuntosDeLaTransformada);
    private readonly double[] _espectro = new double[Ancho];
    private double _escala = 0.05;
    private bool _hayEspectro;

    /// <summary>Monta el pintor con los paneles vacios.</summary>
    public PintorDelAudio()
    {
        Array.Fill(Osciloscopio, Fondo);
        Array.Fill(AfFft, Fondo);
    }

    /// <summary>Puntos del osciloscopio, Bgra32.</summary>
    public int[] Osciloscopio { get; } = new int[Ancho * Alto];

    /// <summary>Puntos del AF-FFT, Bgra32.</summary>
    public int[] AfFft { get; } = new int[Ancho * Alto];

    /// <summary>Frecuencia (Hz) del pico mas alto del ultimo AF-FFT.</summary>
    public double FrecuenciaDelPico { get; private set; }

    /// <summary>Muestras que hacen falta para pintar los dos paneles.</summary>
    /// <param name="frecuenciaDeMuestreo">Muestras por segundo.</param>
    /// <returns>Cuantas de las ultimas muestras se usan.</returns>
    public static int MuestrasNecesarias(int frecuenciaDeMuestreo) =>
        Math.Max(PuntosDeLaTransformada, frecuenciaDeMuestreo * Divisiones * MilisegundosPorDivision / 1000);

    /// <summary>Pinta los dos paneles con las ultimas muestras.</summary>
    /// <param name="ultimas">Las ultimas muestras, de -1 a 1, la mas nueva al final.</param>
    /// <param name="frecuenciaDeMuestreo">Muestras por segundo.</param>
    public void Pintar(ReadOnlySpan<float> ultimas, int frecuenciaDeMuestreo)
    {
        if (ultimas.IsEmpty || frecuenciaDeMuestreo <= 0) return;
        PintarElOsciloscopio(ultimas, frecuenciaDeMuestreo);
        PintarElAfFft(ultimas, frecuenciaDeMuestreo);
    }

    private void PintarElOsciloscopio(ReadOnlySpan<float> ultimas, int frecuencia)
    {
        var cuantas = Math.Min(ultimas.Length, frecuencia * Divisiones * MilisegundosPorDivision / 1000);
        var tramo = ultimas[^cuantas..];

        // La escala se ajusta sola, despacio, al pico: el nivel del codec depende del AF GAIN.
        var pico = 0f;
        foreach (var m in tramo) pico = Math.Max(pico, Math.Abs(m));
        _escala = Math.Max(0.01, (_escala * 0.8) + (pico * 0.2));

        Array.Fill(Osciloscopio, Fondo);
        for (var d = 1; d < Divisiones; d++) Columna(Osciloscopio, d * Ancho / Divisiones, Rejilla);
        Array.Fill(Osciloscopio, Rejilla, Alto / 2 * Ancho, Ancho);

        var mitad = (Alto - 1) / 2.0;
        for (var x = 0; x < Ancho; x++)
        {
            var desde = (int)((long)x * cuantas / Ancho);
            var hasta = Math.Max(desde + 1, (int)((long)(x + 1) * cuantas / Ancho));
            float minimo = 1, maximo = -1;
            for (var i = desde; i < hasta && i < cuantas; i++)
            {
                minimo = Math.Min(minimo, tramo[i]);
                maximo = Math.Max(maximo, tramo[i]);
            }

            var arriba = (int)Math.Clamp(mitad - (maximo / _escala * mitad * 0.85), 0, Alto - 1);
            var abajo = (int)Math.Clamp(mitad - (minimo / _escala * mitad * 0.85), 0, Alto - 1);
            for (var y = arriba; y <= abajo; y++) Osciloscopio[(y * Ancho) + x] = Cian;
        }
    }

    private void PintarElAfFft(ReadOnlySpan<float> ultimas, int frecuencia)
    {
        var real = new float[PuntosDeLaTransformada];
        var imaginaria = new float[PuntosDeLaTransformada];
        var cuantas = Math.Min(ultimas.Length, PuntosDeLaTransformada);
        var tramo = ultimas[^cuantas..];
        for (var i = 0; i < cuantas; i++) real[PuntosDeLaTransformada - cuantas + i] = tramo[i] * Hann[PuntosDeLaTransformada - cuantas + i];
        TransformadaRapida.Transformar(real, imaginaria);

        var hzPorPunto = frecuencia / (double)PuntosDeLaTransformada;
        var mejor = double.MinValue;
        for (var x = 0; x < Ancho; x++)
        {
            var desde = (int)(x * TopeDelAfFft / (double)Ancho / hzPorPunto);
            var hasta = Math.Max(desde + 1, (int)((x + 1) * TopeDelAfFft / (double)Ancho / hzPorPunto));
            var potencia = 1e-12;
            for (var k = desde; k < hasta && k < PuntosDeLaTransformada / 2; k++)
            {
                potencia = Math.Max(potencia, (real[k] * real[k]) + (imaginaria[k] * imaginaria[k]));
            }

            var db = 10 * Math.Log10(potencia);
            _espectro[x] = _hayEspectro ? (_espectro[x] * 0.5) + (db * 0.5) : db;
            if (db > mejor)
            {
                mejor = db;
                FrecuenciaDelPico = (desde + hasta) / 2.0 * hzPorPunto;
            }
        }

        _hayEspectro = true;

        // 70 dB de escala por debajo del pico de este panel, como una pantalla con nivel automatico.
        var techo = _espectro.Max();
        Array.Fill(AfFft, Fondo);
        for (var k = 1; k < 4; k++) Columna(AfFft, k * Ancho / 4, Rejilla);
        for (var x = 0; x < Ancho; x++)
        {
            var altura = Math.Clamp((_espectro[x] - (techo - 70)) / 70.0, 0, 1) * (Alto - 2);
            var y = Alto - 1 - (int)altura;
            for (var f = y + 1; f < Alto; f++) AfFft[(f * Ancho) + x] = AzulRelleno;
            AfFft[(y * Ancho) + x] = AzulBorde;
        }
    }

    private static void Columna(int[] puntos, int x, int color)
    {
        for (var y = 0; y < Alto; y++) puntos[(y * Ancho) + x] = color;
    }
}
