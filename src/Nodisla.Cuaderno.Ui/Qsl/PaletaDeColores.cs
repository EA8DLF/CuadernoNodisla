using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Media;
using Nodisla.Cuaderno.Idiomas;

namespace Nodisla.Cuaderno.Ui.Qsl;

/// <summary>
/// Quien sabe qué colores usa ya la plantilla abierta: la fila «Colores del diseño» del selector.
/// </summary>
/// <remarks>
/// Lo cumplen los editores de la tarjeta QSL y de diplomas. El selector lo pregunta al abrir el
/// desplegable, así que no hace falta avisar de cada cambio.
/// </remarks>
public interface IColoresDelDiseno
{
    /// <summary>Los colores tal como están guardados en la plantilla, en su orden; vacíos y repetidos valen.</summary>
    /// <returns>Los colores.</returns>
    IEnumerable<string?> ColoresEnUso();
}

/// <summary>Un color con nombre, para una muestra clicable del selector.</summary>
/// <remarks>
/// El nombre se lee en el idioma del programa y cambia solo al cambiar de idioma: el de la paleta
/// sale de su clave y el de los demás colores se describe (<see cref="PaletaDeColores.Nombrar"/>).
/// </remarks>
public sealed class MuestraDeColor : System.ComponentModel.INotifyPropertyChanged
{
    private readonly string? _clave;

    /// <summary>Crea la muestra con el nombre descrito a partir del color.</summary>
    /// <param name="color">El color.</param>
    public MuestraDeColor(Color color)
        : this(null, color)
    {
    }

    /// <summary>Crea una muestra de la paleta.</summary>
    /// <param name="clave">Clave del nombre; nula, el nombre se describe a partir del color.</param>
    /// <param name="color">El color.</param>
    internal MuestraDeColor(string? clave, Color color)
    {
        _clave = clave;
        Color = color;
        Textos.AlCambiar(this, static m => m.PropertyChanged?.Invoke(m, new System.ComponentModel.PropertyChangedEventArgs(nameof(Nombre))));
        Codigo = PaletaDeColores.Formatear(color);
        var pincel = new SolidColorBrush(color);
        pincel.Freeze();
        Pincel = pincel;
        Tinta = PaletaDeColores.EsOscuro(color) ? Brushes.White : Brushes.Black;
    }

    /// <inheritdoc />
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Nombre del color, en el idioma del programa.</summary>
    public string Nombre => _clave is null ? PaletaDeColores.Nombrar(Color) : Textos.T(_clave);

    /// <summary>El color.</summary>
    public Color Color { get; }

    /// <summary>El color en el formato con que se guarda (<c>#RRGGBB</c> o <c>#AARRGGBB</c>).</summary>
    public string Codigo { get; }

    /// <summary>Pincel de la muestra.</summary>
    public Brush Pincel { get; }

    /// <summary>Blanco o negro, lo que se lea encima de la muestra (la marca de elegido).</summary>
    public Brush Tinta { get; }

    /// <inheritdoc />
    public override string ToString() => Nombre;
}

/// <summary>
/// La paleta del selector de color, los nombres de los colores y el formato con que se guardan.
/// </summary>
/// <remarks>
/// El formato guardado NO cambia: <c>#RRGGBB</c> si es opaco y <c>#AARRGGBB</c> si lleva
/// transparencia, lo mismo que se escribía a mano. Lo que ya hay en una plantilla (incluidos los
/// nombres en inglés como <c>White</c>) se lee igual y solo se reescribe cuando el operador elige
/// otro color.
/// </remarks>
public static class PaletaDeColores
{
    /// <summary>Los 40 colores de la paleta, en cuatro filas de diez.</summary>
    public static IReadOnlyList<MuestraDeColor> Basica { get; } =
    [
        // Negros y grises.
        M("Qsl.Color.Nombre.Negro", "#000000"), M("Qsl.Color.Nombre.Tinta", "#222222"), M("Qsl.Color.Nombre.Grafito", "#3C3C3C"), M("Qsl.Color.Nombre.GrisOscuro", "#555555"), M("Qsl.Color.Nombre.Gris", "#7F7F7F"),
        M("Qsl.Color.Nombre.GrisMedio", "#A0A0A0"), M("Qsl.Color.Nombre.GrisClaro", "#C8C8C8"), M("Qsl.Color.Nombre.Plata", "#E0E0E0"), M("Qsl.Color.Nombre.Humo", "#F2F2F2"), M("Qsl.Color.Nombre.Blanco", "#FFFFFF"),

        // Los del tema NODISLA y los de las plantillas de fábrica.
        M("Qsl.Color.Nombre.AzulNODISLA", "#0F5FA8"), M("Qsl.Color.Nombre.AzulCieloNODISLA", "#69B4F5"), M("Qsl.Color.Nombre.CianNODISLA", "#00B4D8"), M("Qsl.Color.Nombre.TurquesaNODISLA", "#35D0E8"),
        M("Qsl.Color.Nombre.AguamarinaNODISLA", "#79E8DC"), M("Qsl.Color.Nombre.VerdeNODISLA", "#22C55E"), M("Qsl.Color.Nombre.NocheNODISLA", "#0A0E1A"), M("Qsl.Color.Nombre.AzulMarino", "#1B3A5C"),
        M("Qsl.Color.Nombre.OroViejo", "#B08D57"), M("Qsl.Color.Nombre.Pergamino", "#F5EBD7"),

        // Vivos.
        M("Qsl.Color.Nombre.Rojo", "#D62828"), M("Qsl.Color.Nombre.Carmesi", "#9B111E"), M("Qsl.Color.Nombre.Naranja", "#F77F00"), M("Qsl.Color.Nombre.Ambar", "#FCBF49"), M("Qsl.Color.Nombre.Amarillo", "#FFD60A"),
        M("Qsl.Color.Nombre.VerdeHierba", "#2B9348"), M("Qsl.Color.Nombre.VerdeBosque", "#1B5E20"), M("Qsl.Color.Nombre.Azul", "#1565C0"), M("Qsl.Color.Nombre.Violeta", "#6A1B9A"), M("Qsl.Color.Nombre.Magenta", "#C2185B"),

        // Pastel.
        M("Qsl.Color.Nombre.RosaPastel", "#F8BBD0"), M("Qsl.Color.Nombre.Melocoton", "#FFD8B1"), M("Qsl.Color.Nombre.Vainilla", "#FFF3B0"), M("Qsl.Color.Nombre.Menta", "#C8F2D5"), M("Qsl.Color.Nombre.VerdeAgua", "#B2EBF2"),
        M("Qsl.Color.Nombre.Celeste", "#BBDEFB"), M("Qsl.Color.Nombre.Lavanda", "#D1C4E9"), M("Qsl.Color.Nombre.Lila", "#E1BEE7"), M("Qsl.Color.Nombre.Arena", "#E8D8C4"), M("Qsl.Color.Nombre.Marfil", "#FFFFF0"),
    ];

    /// <summary>Cuántos colores del diseño se enseñan como mucho.</summary>
    public const int MaximoDelDiseno = 20;

    /// <summary>El color en el formato guardado: <c>#RRGGBB</c> si es opaco, <c>#AARRGGBB</c> si no.</summary>
    /// <param name="color">El color.</param>
    /// <returns>El texto.</returns>
    public static string Formatear(Color color) => color.A == 255
        ? string.Create(CultureInfo.InvariantCulture, $"#{color.R:X2}{color.G:X2}{color.B:X2}")
        : string.Create(CultureInfo.InvariantCulture, $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}");

    /// <summary>Lee un color guardado (<c>#RGB</c>, <c>#RRGGBB</c>, <c>#AARRGGBB</c> o un nombre en inglés).</summary>
    /// <param name="texto">El texto.</param>
    /// <param name="color">El color leído.</param>
    /// <returns>Si se ha entendido.</returns>
    public static bool TryLeer(string? texto, out Color color)
    {
        color = Colors.Transparent;
        if (string.IsNullOrWhiteSpace(texto)) return false;
        try
        {
            if (ColorConverter.ConvertFromString(texto.Trim()) is not Color leido) return false;
            color = leido;
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>Si un color es oscuro (para elegir tinta blanca o negra encima).</summary>
    /// <param name="color">El color.</param>
    /// <returns>Oscuro o no.</returns>
    public static bool EsOscuro(Color color)
    {
        if (color.A < 110) return false;
        var luminancia = (0.299 * color.R) + (0.587 * color.G) + (0.114 * color.B);
        return luminancia < 140;
    }

    /// <summary>
    /// El nombre de un color: el de la paleta si es uno de ellos; si no, uno descriptivo
    /// («Azul oscuro», «Gris claro»…). La transparencia se dice aparte («… al 90 %»).
    /// </summary>
    /// <param name="color">El color.</param>
    /// <returns>El nombre.</returns>
    public static string Nombrar(Color color)
    {
        var opaco = Color.FromRgb(color.R, color.G, color.B);
        if (color.A == 0) return Textos.T("Qsl.Color.Nombre.Transparente");
        var nombre = Basica.FirstOrDefault(m => m.Color == opaco)?.Nombre ?? Describir(opaco);
        if (color.A == 255) return nombre;
        return Textos.F("Qsl.Color.AlPorcentaje", nombre, Math.Round(color.A * 100.0 / 255));
    }

    /// <summary>Las muestras de los colores que ya usa la plantilla: sin repetir, en su orden.</summary>
    /// <param name="colores">Los colores guardados.</param>
    /// <returns>Las muestras (como mucho <see cref="MaximoDelDiseno"/>).</returns>
    public static IReadOnlyList<MuestraDeColor> DelDiseno(IEnumerable<string?>? colores)
    {
        var salida = new List<MuestraDeColor>();
        if (colores is null) return salida;
        var vistos = new HashSet<Color>();
        foreach (var texto in colores)
        {
            if (!TryLeer(texto, out var color) || color.A == 0 || !vistos.Add(color)) continue;
            salida.Add(new MuestraDeColor(color));
            if (salida.Count == MaximoDelDiseno) break;
        }

        return salida;
    }

    /// <summary>Pasa de tono (0–360), saturación (0–1) y brillo (0–1) a color opaco.</summary>
    /// <param name="tono">Tono en grados.</param>
    /// <param name="saturacion">Saturación.</param>
    /// <param name="brillo">Brillo.</param>
    /// <param name="alfa">Opacidad 0–255.</param>
    /// <returns>El color.</returns>
    public static Color DeTsb(double tono, double saturacion, double brillo, byte alfa = 255)
    {
        tono = ((tono % 360) + 360) % 360;
        saturacion = Math.Clamp(saturacion, 0, 1);
        brillo = Math.Clamp(brillo, 0, 1);
        var c = brillo * saturacion;
        var x = c * (1 - Math.Abs((tono / 60 % 2) - 1));
        var m = brillo - c;
        var (r, g, b) = (int)(tono / 60) switch
        {
            0 => (c, x, 0d),
            1 => (x, c, 0d),
            2 => (0d, c, x),
            3 => (0d, x, c),
            4 => (x, 0d, c),
            _ => (c, 0d, x),
        };
        return Color.FromArgb(alfa, Byte(r + m), Byte(g + m), Byte(b + m));
    }

    /// <summary>Pasa un color a tono (0–360), saturación (0–1) y brillo (0–1).</summary>
    /// <param name="color">El color.</param>
    /// <returns>Tono, saturación y brillo.</returns>
    public static (double Tono, double Saturacion, double Brillo) ATsb(Color color)
    {
        double r = color.R / 255.0, g = color.G / 255.0, b = color.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var d = max - min;
        double tono;
        if (d == 0) tono = 0;
        else if (max == r) tono = 60 * (((g - b) / d) % 6);
        else if (max == g) tono = 60 * (((b - r) / d) + 2);
        else tono = 60 * (((r - g) / d) + 4);
        if (tono < 0) tono += 360;
        return (tono, max == 0 ? 0 : d / max, max);
    }

    private static string Describir(Color color)
    {
        var (tono, saturacion, brillo) = ATsb(color);
        if (brillo < 0.08) return Textos.T("Qsl.Color.Nombre.Negro");
        if (saturacion < 0.12)
        {
            if (brillo > 0.96) return Textos.T("Qsl.Color.Nombre.Blanco");
            return Textos.T(brillo < 0.35 ? "Qsl.Color.Nombre.GrisOscuro" : brillo > 0.75 ? "Qsl.Color.Nombre.GrisClaro" : "Qsl.Color.Nombre.Gris");
        }

        var familia = tono switch
        {
            < 15 => "Qsl.Color.Nombre.Rojo",
            < 40 => "Qsl.Color.Nombre.Naranja",
            < 65 => "Qsl.Color.Nombre.Amarillo",
            < 90 => "Qsl.Color.Nombre.VerdeLima",
            < 155 => "Qsl.Color.Nombre.Verde",
            < 185 => "Qsl.Color.Nombre.Turquesa",
            < 200 => "Qsl.Color.Nombre.Cian",
            < 255 => "Qsl.Color.Nombre.Azul",
            < 290 => "Qsl.Color.Nombre.Violeta",
            < 335 => "Qsl.Color.Nombre.Fucsia",
            _ => "Qsl.Color.Nombre.Rojo",
        };
        var nombre = Textos.T(familia);
        if (brillo < 0.45) return Textos.F("Qsl.Color.Oscuro", nombre);
        if (saturacion < 0.4 && brillo > 0.8) return Textos.F("Qsl.Color.Pastel", nombre);
        return saturacion < 0.45 ? Textos.F("Qsl.Color.Apagado", nombre) : nombre;
    }

    private static byte Byte(double valor) => (byte)Math.Clamp(Math.Round(valor * 255), 0, 255);

    private static MuestraDeColor M(string clave, string codigo) =>
        new(clave, (Color)ColorConverter.ConvertFromString(codigo));
}

/// <summary>Los últimos colores elegidos, el más reciente primero, sin repetir.</summary>
/// <remarks>
/// Son de la sesión: los comparten todos los selectores del programa (elegir un azul para un
/// texto lo deja a mano para la orla), pero no se guardan en disco.
/// </remarks>
public sealed class ColoresRecientes
{
    /// <summary>Cuántos se recuerdan.</summary>
    public const int Maximo = 10;

    /// <summary>Los de todo el programa.</summary>
    public static ColoresRecientes Compartidos { get; } = new();

    /// <summary>Las muestras, la más reciente primero.</summary>
    public ObservableCollection<MuestraDeColor> Muestras { get; } = [];

    /// <summary>Apunta un color usado: sube al principio y se quita si ya estaba.</summary>
    /// <param name="color">El color.</param>
    public void Anotar(Color color)
    {
        if (color.A == 0) return;
        for (var i = Muestras.Count - 1; i >= 0; i--)
        {
            if (Muestras[i].Color == color) Muestras.RemoveAt(i);
        }

        Muestras.Insert(0, new MuestraDeColor(color));
        while (Muestras.Count > Maximo) Muestras.RemoveAt(Muestras.Count - 1);
    }

    /// <summary>Olvida todos.</summary>
    public void Vaciar() => Muestras.Clear();
}
