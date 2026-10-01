using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nodisla.Cuaderno.Aplicacion.Puertos;

namespace Nodisla.Cuaderno.Ui.Recursos;

/// <summary>Las paletas de la cascada.</summary>
public enum PaletaDeCascada
{
    /// <summary>La de casa: negro, azul, verde, ambar, blanco.</summary>
    Nodisla,

    /// <summary>Escala de grises.</summary>
    Gris,

    /// <summary>Negro, rojo, amarillo, blanco.</summary>
    Fuego,

    /// <summary>Negro, azul, cian, blanco.</summary>
    Azul,
}

/// <summary>
/// Pinta la cascada: convierte las columnas de espectro que saca el modem en una imagen que
/// va bajando.
/// </summary>
/// <remarks>
/// <para>
/// La imagen tiene un tamaño fijo en puntos y se estira hasta donde llegue el hueco. Se hace
/// asi porque el modem entrega columnas a su propio ritmo —varias por segundo— y no al ritmo
/// al que el operador cambia el tamaño de la ventana: si la imagen siguiera al hueco, cada
/// tiron del raton tiraria el historial entero.
/// </para>
/// <para>
/// <b>La escala de color se ajusta sola.</b> El nivel absoluto que llega depende del volumen
/// del codec, del atenuador y de la banda, y una escala fija pintaria negro entero en una
/// banda tranquila y blanco entero en 20 metros por la tarde. Lo que se fija no es el nivel
/// sino el <i>contraste</i>: el suelo se pega al ruido medido y el techo va treinta y tantos
/// decibelios por encima, que es donde vive lo que interesa. Encima de eso van los mandos de
/// WSJT-X: <see cref="GananciaDb"/> sube el brillo, <see cref="CeroDb"/> mueve el negro,
/// <see cref="PromedioDeColumnas"/> suaviza y saca las señales debiles, y
/// <see cref="AnchoVisibleHz"/> decide hasta donde se pinta.
/// </para>
/// </remarks>
public sealed class PintorDeCascada
{
    /// <summary>Puntos de ancho de la imagen. Uno por cada trocito de frecuencia.</summary>
    public const int Ancho = 500;

    /// <summary>Filas de historial que se guardan.</summary>
    public const int Alto = 260;

    /// <summary>Ancho visible de fabrica, en hercios.</summary>
    /// <remarks>
    /// Tres mil: es donde acaba el filtro de un equipo en banda lateral. Pintar hasta 4.000
    /// seria pintar una quinta parte de la imagen con lo que el equipo ya ha cortado.
    /// </remarks>
    public const double TopeHz = 3000;

    /// <summary>Recorrido en decibelios entre el negro y el blanco.</summary>
    private const double RecorridoDb = 36;

    /// <summary>Lo deprisa que el suelo de color sigue al ruido: 0 nunca, 1 de golpe.</summary>
    private const double SeguimientoDelSuelo = 0.08;

    private readonly int[] _puntos = new int[Ancho * Alto];
    private readonly double[] _fila = new double[Ancho];
    private readonly double[] _acumulada = new double[Ancho];
    private readonly Int32Rect _todo = new(0, 0, Ancho, Alto);

    private WriteableBitmap? _imagen;
    private double _suelo = double.NaN;
    private int _acumuladas;
    private double _anchoVisibleHz = TopeHz;

    /// <summary>La imagen de la cascada, o nula mientras no haya llegado ninguna columna.</summary>
    public WriteableBitmap? Imagen => _imagen;

    /// <summary>Columnas que se han pintado desde que se arranco.</summary>
    public long Columnas { get; private set; }

    /// <summary>Ganancia en decibelios: mas brillo. Es el «Gain» de WSJT-X.</summary>
    public double GananciaDb { get; set; }

    /// <summary>Cero en decibelios: donde empieza el negro respecto al ruido. Es el «Zero» de WSJT-X.</summary>
    public double CeroDb { get; set; }

    /// <summary>Columnas que se promedian antes de pintar una fila. Es el «N Avg» de WSJT-X.</summary>
    public int PromedioDeColumnas { get; set; } = 1;

    /// <summary>Paleta.</summary>
    public PaletaDeCascada Paleta { get; set; } = PaletaDeCascada.Nodisla;

    /// <summary>Hasta que frecuencia se pinta, en hercios. Cambiarlo limpia el historial.</summary>
    public double AnchoVisibleHz
    {
        get => _anchoVisibleHz;
        set
        {
            var nuevo = Math.Clamp(value, 500, 6000);
            if (Math.Abs(nuevo - _anchoVisibleHz) < 0.5) return;
            _anchoVisibleHz = nuevo;
            Limpiar();
        }
    }

    /// <summary>Suelta el historial y vuelve a empezar en negro.</summary>
    public void Limpiar()
    {
        Array.Clear(_puntos);
        Array.Clear(_acumulada);
        _acumuladas = 0;
        _suelo = double.NaN;
        Columnas = 0;
        _imagen?.WritePixels(_todo, _puntos, Ancho * 4, 0);
    }

    /// <summary>
    /// Mete una columna nueva arriba y empuja hacia abajo lo que habia.
    /// </summary>
    /// <param name="columna">La columna tal y como la saca el modem.</param>
    /// <remarks>
    /// Tiene que llamarse en el hilo de la ventana: la imagen es suya.
    /// </remarks>
    public void Anadir(ColumnaDeCascada columna)
    {
        ArgumentNullException.ThrowIfNull(columna);

        var magnitudes = columna.Magnitudes.Span;
        if (magnitudes.Length == 0 || columna.HzPorCasilla <= 0) return;

        Resumir(magnitudes, columna.HzPorCasilla);

        // El promedio de columnas: se acumulan N y se pinta una sola fila con la media.
        var promedio = Math.Max(1, PromedioDeColumnas);
        for (var x = 0; x < Ancho; x++) _acumulada[x] += _fila[x];
        _acumuladas++;
        if (_acumuladas < promedio) return;

        for (var x = 0; x < Ancho; x++)
        {
            _fila[x] = _acumulada[x] / _acumuladas;
            _acumulada[x] = 0;
        }

        _acumuladas = 0;

        // El suelo persigue al ruido de fondo, pero despacio: si saltara con cada columna, la
        // cascada parpadearia con cada rafaga de estatica.
        var medio = Media(_fila);
        _suelo = double.IsNaN(_suelo) ? medio : (_suelo * (1 - SeguimientoDelSuelo)) + (medio * SeguimientoDelSuelo);

        // Se empuja todo una fila hacia abajo y se escribe la nueva en la de arriba.
        Array.Copy(_puntos, 0, _puntos, Ancho, (Alto - 1) * Ancho);
        for (var x = 0; x < Ancho; x++)
        {
            _puntos[x] = Colorear((_fila[x] - _suelo + 3 - CeroDb + GananciaDb) / RecorridoDb);
        }

        _imagen ??= new WriteableBitmap(Ancho, Alto, 96, 96, PixelFormats.Bgra32, null);
        _imagen.WritePixels(_todo, _puntos, Ancho * 4, 0);
        Columnas++;
    }

    /// <summary>
    /// Reparte las casillas de frecuencia entre los puntos de la imagen, quedandose con la
    /// mas fuerte de cada grupo.
    /// </summary>
    /// <remarks>
    /// Con la maxima y no con la media a proposito: una señal de FT8 ocupa 50 Hz y, si se
    /// promediara con el ruido de los lados, las mas debiles —que son justo las que importan—
    /// desaparecerian del dibujo aunque el decodificador si las viera.
    /// </remarks>
    private void Resumir(ReadOnlySpan<float> magnitudes, double hzPorCasilla)
    {
        var casillasPorPunto = _anchoVisibleHz / hzPorCasilla / Ancho;

        for (var x = 0; x < Ancho; x++)
        {
            var desde = (int)(x * casillasPorPunto);
            var hasta = Math.Max(desde + 1, (int)((x + 1) * casillasPorPunto));

            if (desde >= magnitudes.Length)
            {
                _fila[x] = double.IsNaN(_suelo) ? -120 : _suelo;
                continue;
            }

            hasta = Math.Min(hasta, magnitudes.Length);
            var mayor = double.NegativeInfinity;
            for (var i = desde; i < hasta; i++) mayor = Math.Max(mayor, magnitudes[i]);
            _fila[x] = mayor;
        }
    }

    private static double Media(double[] fila)
    {
        var suma = 0.0;
        foreach (var valor in fila) suma += valor;
        return suma / fila.Length;
    }

    /// <summary>
    /// Pasa de «cuanto sobresale del ruido» a color segun la paleta. La de casa va de negro a
    /// blanco pasando por azul, verde y ambar: es la escala de siempre en este oficio, y por
    /// eso se reconoce.
    /// </summary>
    private int Colorear(double fuerza)
    {
        var f = Math.Clamp(fuerza, 0, 1);

        return Paleta switch
        {
            PaletaDeCascada.Gris => Mezclar(0x00, 0x00, 0x00, 0xFF, 0xFF, 0xFF, f),
            PaletaDeCascada.Fuego => f switch
            {
                < 0.40 => Mezclar(0x00, 0x00, 0x00, 0xB0, 0x10, 0x00, f / 0.40),
                < 0.75 => Mezclar(0xB0, 0x10, 0x00, 0xFF, 0xD0, 0x00, (f - 0.40) / 0.35),
                _ => Mezclar(0xFF, 0xD0, 0x00, 0xFF, 0xFF, 0xFF, (f - 0.75) / 0.25),
            },
            PaletaDeCascada.Azul => f switch
            {
                < 0.45 => Mezclar(0x00, 0x00, 0x10, 0x00, 0x40, 0xC0, f / 0.45),
                < 0.80 => Mezclar(0x00, 0x40, 0xC0, 0x30, 0xE0, 0xFF, (f - 0.45) / 0.35),
                _ => Mezclar(0x30, 0xE0, 0xFF, 0xFF, 0xFF, 0xFF, (f - 0.80) / 0.20),
            },
            _ => f switch
            {
                < 0.25 => Mezclar(0x06, 0x08, 0x09, 0x10, 0x30, 0x55, f / 0.25),
                < 0.50 => Mezclar(0x10, 0x30, 0x55, 0x1F, 0x7A, 0x5A, (f - 0.25) / 0.25),
                < 0.75 => Mezclar(0x1F, 0x7A, 0x5A, 0xE8, 0xB3, 0x3A, (f - 0.50) / 0.25),
                _ => Mezclar(0xE8, 0xB3, 0x3A, 0xFF, 0xFF, 0xFF, (f - 0.75) / 0.25),
            },
        };
    }

    private static int Mezclar(int r1, int v1, int a1, int r2, int v2, int a2, double t)
    {
        var r = (int)(r1 + ((r2 - r1) * t));
        var v = (int)(v1 + ((v2 - v1) * t));
        var a = (int)(a1 + ((a2 - a1) * t));

        // Bgra32 sin transparencia: el alfa va a tope.
        return (0xFF << 24) | (r << 16) | (v << 8) | a;
    }
}
