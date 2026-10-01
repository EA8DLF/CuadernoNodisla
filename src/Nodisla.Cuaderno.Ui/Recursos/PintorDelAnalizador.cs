namespace Nodisla.Cuaderno.Ui.Recursos;

/// <summary>
/// Pinta el analizador del equipo como lo pinta la pantalla del FT-710: la traza arriba,
/// blanca azulada sobre negro con rejilla gris, y la cascada debajo en rojos.
/// </summary>
/// <remarks>
/// <para>
/// Trabaja con puntos en memoria (formato Bgra32, un <c>int</c> por punto) y no sabe nada de
/// WPF: el modelo de vista los copia a sus imagenes. Asi se prueba sin hilo de interfaz.
/// </para>
/// <para>
/// La escala se ajusta sola al suelo de ruido, que se sigue con una media lenta: el nivel que
/// manda la radio depende de la banda, del preamplificador y del atenuador, y con una escala
/// fija una banda tranquila saldria negra entera.
/// </para>
/// </remarks>
public sealed class PintorDelAnalizador
{
    /// <summary>Puntos de ancho: uno por cada punto de la traza del equipo.</summary>
    public const int Ancho = 850;

    /// <summary>Alto de la traza, en puntos.</summary>
    public const int AltoDeTraza = 110;

    /// <summary>Filas de cascada que se guardan.</summary>
    public const int AltoDeCascada = 120;

    /// <summary>Alto de la vista 3DSS: ocupa lo de la traza y la cascada juntas.</summary>
    public const int AltoTresD = AltoDeTraza + AltoDeCascada;

    /// <summary>Pasadas que se ven en fila en la vista 3DSS.</summary>
    public const int PasadasTresD = 36;

    /// <summary>
    /// Filas de cascada por cada pasada que llega, segun SPEED (SS00). La radio manda unas once
    /// tramas por segundo con cualquier SPEED (medido el 29-09-2026), asi que la velocidad de la
    /// cascada se hace aqui: SLOW1 una fila cada cuatro pasadas … FAST3 tres por pasada; STOP
    /// congela la pantalla, como en la radio.
    /// </summary>
    public static IReadOnlyList<double> FilasPorPasada { get; } = [0.25, 0.5, 1, 2, 3, 0];

    private const int Fondo = unchecked((int)0xFF05070A);
    private const int RejillaColor = unchecked((int)0xFF1C242E);
    private const int Relleno = unchecked((int)0xFF15356A);
    private const int Linea = unchecked((int)0xFFDDEEFF);

    private static readonly int[] Paleta = CrearPaleta();

    private readonly double[] _media = new double[Ancho];
    private readonly byte[][] _historia = new byte[AltoDeCascada][];
    private int _filasDeHistoria;
    private double _filasPendientes;
    private bool _hayMedia;
    private double _suelo = double.NaN;

    /// <summary>Monta el pintor con la pantalla vacia.</summary>
    public PintorDelAnalizador()
    {
        Array.Fill(Traza, Fondo);
        Array.Fill(Cascada, Paleta[0]);
        Array.Fill(VistaTresD, Fondo);
    }

    /// <summary>SPEED de la radio (0 SLOW1 … 4 FAST3, 5 STOP).</summary>
    public int Velocidad { get; set; } = 2;

    /// <summary>Se pinta tambien la vista 3DSS.</summary>
    public bool TresD { get; set; }

    /// <summary>Puntos de la vista 3DSS, fila a fila de arriba abajo.</summary>
    public int[] VistaTresD { get; } = new int[Ancho * AltoTresD];

    /// <summary>Filas que se han añadido a la cascada desde que se monto el pintor.</summary>
    public long FilasAnadidas { get; private set; }

    /// <summary>Puntos de la traza, fila a fila de arriba abajo.</summary>
    public int[] Traza { get; } = new int[Ancho * AltoDeTraza];

    /// <summary>Puntos de la cascada; la fila mas nueva arriba.</summary>
    public int[] Cascada { get; } = new int[Ancho * AltoDeCascada];

    /// <summary>Suelo de ruido que se esta usando (0–255).</summary>
    public double Suelo => double.IsNaN(_suelo) ? 0 : _suelo;

    /// <summary>Pinta una traza nueva y le añade una fila a la cascada.</summary>
    /// <param name="niveles">Niveles de 0 a 255, de frecuencia baja a alta.</param>
    public void Pintar(ReadOnlySpan<byte> niveles)
    {
        if (niveles.IsEmpty) return;
        var filas = FilasPorPasada[Math.Clamp(Velocidad, 0, FilasPorPasada.Count - 1)];
        if (filas <= 0) return; // STOP: la pantalla se queda quieta.

        var remuestreados = new byte[Ancho];
        for (var x = 0; x < Ancho; x++) remuestreados[x] = niveles[(int)((long)x * niveles.Length / Ancho)];

        SeguirElSuelo(remuestreados);

        // La traza se suaviza un poco entre pasadas (la radio tambien promedia la suya); la
        // cascada no, para no emborronar lo que dura una sola pasada.
        var suavizada = new byte[Ancho];
        for (var x = 0; x < Ancho; x++)
        {
            _media[x] = _hayMedia ? (_media[x] * 0.5) + (remuestreados[x] * 0.5) : remuestreados[x];
            suavizada[x] = (byte)Math.Round(_media[x]);
        }

        _hayMedia = true;
        PintarLaTraza(suavizada);

        _filasPendientes += filas;
        while (_filasPendientes >= 1)
        {
            AnadirFila(remuestreados);
            _filasPendientes -= 1;
        }

        if (TresD) PintarTresD();
    }

    /// <summary>Color de la cascada para un nivel, ya ajustado al suelo.</summary>
    /// <param name="nivel">Nivel de 0 a 255.</param>
    /// <returns>Color Bgra32.</returns>
    public int ColorDeCascada(byte nivel)
    {
        // El ruido se queda en granate oscuro, como en la radio; lo que asoma 40 puntos por
        // encima ya es rojo vivo y lo fuerte llega al amarillo blanquecino.
        var t = (nivel - (Suelo - 6)) / 60.0;
        return Paleta[(int)Math.Clamp(t * 255, 0, 255)];
    }

    private void SeguirElSuelo(byte[] niveles)
    {
        var ordenados = (byte[])niveles.Clone();
        Array.Sort(ordenados);
        var mediana = ordenados[ordenados.Length / 2];
        _suelo = double.IsNaN(_suelo) ? mediana : (_suelo * 0.9) + (mediana * 0.1);
    }

    private void PintarLaTraza(byte[] niveles)
    {
        Array.Fill(Traza, Fondo);

        // Rejilla: diez columnas y cinco filas, como la pantalla del equipo.
        for (var i = 1; i < 10; i++)
        {
            var x = i * Ancho / 10;
            for (var y = 0; y < AltoDeTraza; y++) Traza[(y * Ancho) + x] = RejillaColor;
        }

        for (var j = 1; j < 5; j++)
        {
            var y = j * AltoDeTraza / 5;
            Array.Fill(Traza, RejillaColor, y * Ancho, Ancho);
        }

        // El suelo de ruido cae a un cuarto de la altura; la escala es de 150 puntos de nivel.
        var anterior = -1;
        for (var x = 0; x < Ancho; x++)
        {
            var altura = (niveles[x] - Suelo) / 150.0 * AltoDeTraza + (AltoDeTraza * 0.25);
            var y = AltoDeTraza - 1 - (int)Math.Clamp(altura, 0, AltoDeTraza - 1);

            for (var f = y + 1; f < AltoDeTraza; f++) Traza[(f * Ancho) + x] = Relleno;

            // Une con el punto anterior para que la linea no quede a trozos.
            var desde = anterior < 0 ? y : Math.Min(anterior, y);
            var hasta = anterior < 0 ? y : Math.Max(anterior, y);
            for (var f = desde; f <= hasta; f++) Traza[(f * Ancho) + x] = Linea;
            anterior = y;
        }
    }

    private void AnadirFila(byte[] niveles)
    {
        Array.Copy(Cascada, 0, Cascada, Ancho, Cascada.Length - Ancho);
        for (var x = 0; x < Ancho; x++) Cascada[x] = ColorDeCascada(niveles[x]);

        // La historia de niveles, para la vista 3DSS: la mas nueva en la posicion 0.
        var ultima = _historia[^1] ?? new byte[Ancho];
        Array.Copy(_historia, 0, _historia, 1, _historia.Length - 1);
        Array.Copy(niveles, ultima, Ancho);
        _historia[0] = ultima;
        _filasDeHistoria = Math.Min(_filasDeHistoria + 1, _historia.Length);
        FilasAnadidas++;
    }

    /// <summary>
    /// La vista 3DSS: las ultimas pasadas en fila, de la mas vieja (al fondo, arriba, estrecha y
    /// apagada) a la mas nueva (delante, abajo, ancha). Cada pasada tapa lo que queda detras.
    /// </summary>
    private void PintarTresD()
    {
        Array.Fill(VistaTresD, Fondo);
        var centro = (Ancho - 1) / 2.0;
        var cuantas = Math.Min(PasadasTresD, _filasDeHistoria);
        var salto = Math.Max(1, _filasDeHistoria / PasadasTresD);

        for (var k = cuantas - 1; k >= 0; k--)
        {
            if (_historia[k * salto] is not { } niveles) continue;
            var fondo = PasadasTresD <= 1 ? 0 : k / (double)(PasadasTresD - 1);
            var base0 = (int)(AltoTresD - 2 - (fondo * AltoTresD * 0.58));
            var estrechez = 1 - (0.38 * fondo);
            var altura = AltoTresD * 0.42 * (1 - (0.45 * fondo));
            var brillo = 1 - (0.6 * fondo);
            var anterior = -1;
            var anteriorX = -1;

            for (var x = 0; x < Ancho; x++)
            {
                var px = (int)Math.Round(centro + ((x - centro) * estrechez));
                var alto = Math.Clamp((niveles[x] - Suelo + 8) / 150.0 * altura, 0, altura);
                var y = Math.Clamp(base0 - (int)alto, 0, AltoTresD - 1);

                // Tapa lo de detras hasta la base de esta pasada.
                for (var f = y + 1; f <= base0 && f < AltoTresD; f++) VistaTresD[(f * Ancho) + px] = Fondo;

                // El ruido en la linea azulada de la traza; lo que asoma, en los colores de la cascada.
                var color = niveles[x] > Suelo + 20
                    ? Apagar(ColorDeCascada(niveles[x]), brillo)
                    : Apagar(Linea, brillo * 0.55);
                var seguida = anterior >= 0 && (anteriorX == px - 1 || anteriorX == px);
                var desde = seguida ? Math.Min(anterior, y) : y;
                var hasta = seguida ? Math.Max(anterior, y) : y;
                for (var f = desde; f <= hasta; f++) VistaTresD[(f * Ancho) + px] = color;
                anterior = y;
                anteriorX = px;
            }
        }
    }

    private static int Apagar(int color, double brillo)
    {
        var r = (int)(((color >> 16) & 0xFF) * brillo);
        var g = (int)(((color >> 8) & 0xFF) * brillo);
        var b = (int)((color & 0xFF) * brillo);
        return unchecked((int)0xFF000000) | (r << 16) | (g << 8) | b;
    }

    private static int[] CrearPaleta()
    {
        // Negro granate → granate → rojo → naranja → amarillo blanquecino.
        (double P, int R, int G, int B)[] tramos =
        [
            (0.00, 0x10, 0x02, 0x02),
            (0.30, 0x4A, 0x0A, 0x08),
            (0.60, 0xC8, 0x1E, 0x16),
            (0.82, 0xFF, 0x78, 0x2E),
            (1.00, 0xFF, 0xF4, 0xC8),
        ];

        var paleta = new int[256];
        for (var i = 0; i < 256; i++)
        {
            var t = i / 255.0;
            var k = 0;
            while (k < tramos.Length - 2 && t > tramos[k + 1].P) k++;
            var (p0, r0, g0, b0) = tramos[k];
            var (p1, r1, g1, b1) = tramos[k + 1];
            var u = (t - p0) / (p1 - p0);
            var r = (int)(r0 + ((r1 - r0) * u));
            var g = (int)(g0 + ((g1 - g0) * u));
            var b = (int)(b0 + ((b1 - b0) * u));
            paleta[i] = unchecked((int)0xFF000000) | (r << 16) | (g << 8) | b;
        }

        return paleta;
    }
}
