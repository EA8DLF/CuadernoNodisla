using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nodisla.Cuaderno.Impresion.Qsl;

namespace Nodisla.Cuaderno.Ui.Qsl;

/// <summary>Formato de la imagen de la tarjeta.</summary>
public enum FormatoDeImagen
{
    /// <summary>JPG: pesa poco, es lo que conviene para el correo.</summary>
    Jpg = 0,

    /// <summary>PNG: sin perdida, para archivar o retocar.</summary>
    Png,
}

/// <summary>
/// Dibuja una tarjeta QSL con WPF: la misma rutina para la vista previa del editor, la imagen del
/// correo y el PDF.
/// </summary>
/// <remarks>
/// <para>
/// <b>Un solo dibujo para todo.</b> El editor enseña este mapa de bits; el correo lleva este mapa
/// de bits a 200 ppp; el PDF lleva este mapa de bits a 300 ppp. Asi es imposible que la tarjeta
/// que llega al corresponsal no sea la que se ve en pantalla.
/// </para>
/// <para>
/// Se dibuja directamente en pixeles (milimetros por pixeles-por-milimetro y puntos por
/// pixeles-por-punto) en vez de escalar un dibujo en milimetros: con letra de 3,5 unidades y un
/// zoom de doce, el trazado del texto de WPF redondea mal.
/// </para>
/// </remarks>
public static class DibujanteDeQsl
{
    /// <summary>Resolucion de la vista previa del editor.</summary>
    public const double PppDePantalla = 150;

    /// <summary>Resolucion de la imagen del correo: se lee bien y pesa unos cientos de KB.</summary>
    public const double PppDeCorreo = 200;

    /// <summary>Resolucion para imprimir.</summary>
    public const double PppDeImprenta = 300;

    /// <summary>Aire alrededor del texto en los recuadros, en milimetros.</summary>
    private const double AireDelRecuadroMm = 1.0;

    private static readonly ConcurrentDictionary<string, BitmapSource> Fondos = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Dibuja la tarjeta.</summary>
    /// <param name="diseno">La plantilla.</param>
    /// <param name="variables">Los valores de las variables (de <see cref="VariablesDeQsl.Para"/>).</param>
    /// <param name="rutaDelFondo">La imagen de fondo, si la hay.</param>
    /// <param name="ppp">Puntos por pulgada.</param>
    /// <param name="soloMisDatos">Quitar los campos del contacto (la imagen para eQSL).</param>
    /// <returns>El mapa de bits, congelado.</returns>
    public static BitmapSource Dibujar(
        DisenoDeQsl diseno,
        IReadOnlyDictionary<string, string> variables,
        string? rutaDelFondo,
        double ppp,
        bool soloMisDatos = false)
    {
        ArgumentNullException.ThrowIfNull(diseno);
        ArgumentNullException.ThrowIfNull(variables);

        var k = ppp / 25.4;
        var ancho = Math.Max(1, (int)Math.Round(diseno.AnchoMm * k));
        var alto = Math.Max(1, (int)Math.Round(diseno.AltoMm * k));

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var tarjeta = new Rect(0, 0, ancho, alto);
            dc.DrawRectangle(Pincel(diseno.ColorDeFondo, Colors.White), null, tarjeta);

            if (Fondo(rutaDelFondo) is { } imagen)
            {
                dc.PushClip(new RectangleGeometry(tarjeta));
                dc.DrawImage(imagen, Colocar(imagen, tarjeta, diseno.Ajuste));
                dc.Pop();
            }

            foreach (var campo in diseno.Campos)
            {
                if (!campo.Visible || (soloMisDatos && campo.EsDelContacto)) continue;
                var texto = VariablesDeQsl.Sustituir(campo.Texto, variables);
                if (string.IsNullOrWhiteSpace(texto)) continue;

                var (formato, izquierda) = Formatear(campo, texto, k, ppp);
                var arriba = campo.YMm * k;
                if (!string.IsNullOrWhiteSpace(campo.ColorDeRecuadro))
                {
                    var aire = AireDelRecuadroMm * k;
                    dc.DrawRoundedRectangle(
                        Pincel(campo.ColorDeRecuadro, Colors.Transparent), null,
                        new Rect(izquierda - aire, arriba - (aire / 2), formato.WidthIncludingTrailingWhitespace + (2 * aire), formato.Height + aire),
                        aire * 0.8, aire * 0.8);
                }

                dc.DrawText(formato, new Point(izquierda, arriba));
            }
        }

        // A 96 ppp un DIP es un pixel, que es en lo que se ha dibujado. Con los ppp de verdad
        // aqui, WPF volveria a escalar el dibujo y la tarjeta saldria recortada.
        var mapa = new RenderTargetBitmap(ancho, alto, 96, 96, PixelFormats.Pbgra32);
        mapa.Render(visual);
        mapa.Freeze();
        return mapa;
    }

    /// <summary>
    /// El hueco que ocupa cada campo en la tarjeta, en milimetros, para ponerle encima el asa del editor.
    /// </summary>
    /// <param name="campo">El campo.</param>
    /// <param name="variables">Los valores de las variables.</param>
    /// <returns>El rectangulo; si el texto sale vacio, el de su plantilla sin rellenar, para poder cogerlo.</returns>
    public static Rect Caja(CampoDeQsl campo, IReadOnlyDictionary<string, string> variables)
    {
        ArgumentNullException.ThrowIfNull(campo);
        ArgumentNullException.ThrowIfNull(variables);
        const double Ppp = 96;
        var k = Ppp / 25.4;
        var texto = VariablesDeQsl.Sustituir(campo.Texto, variables);
        if (string.IsNullOrWhiteSpace(texto)) texto = string.IsNullOrWhiteSpace(campo.Texto) ? campo.Nombre : campo.Texto;
        var (formato, izquierda) = Formatear(campo, texto, k, Ppp);
        return new Rect(
            (izquierda / k) - AireDelRecuadroMm,
            campo.YMm - (AireDelRecuadroMm / 2),
            (formato.WidthIncludingTrailingWhitespace / k) + (2 * AireDelRecuadroMm),
            (formato.Height / k) + AireDelRecuadroMm);
    }

    /// <summary>Codifica la tarjeta.</summary>
    /// <param name="imagen">La tarjeta dibujada.</param>
    /// <param name="formato">PNG o JPG.</param>
    /// <returns>Los bytes del fichero.</returns>
    public static byte[] Codificar(BitmapSource imagen, FormatoDeImagen formato)
    {
        ArgumentNullException.ThrowIfNull(imagen);
        BitmapEncoder codificador = formato == FormatoDeImagen.Png
            ? new PngBitmapEncoder()
            : new JpegBitmapEncoder { QualityLevel = 92 };

        // El JPG no tiene transparencia: se aplana sobre blanco en vez de dejar que salga negro.
        var fuente = formato == FormatoDeImagen.Jpg ? new FormatConvertedBitmap(imagen, PixelFormats.Bgr24, null, 0) : imagen;
        codificador.Frames.Add(BitmapFrame.Create(fuente));
        using var salida = new MemoryStream();
        codificador.Save(salida);
        return salida.ToArray();
    }

    /// <summary>Tipo MIME y extension del formato.</summary>
    /// <param name="formato">El formato.</param>
    /// <returns>Tipo y extension con punto.</returns>
    public static (string Tipo, string Extension) Tipo(FormatoDeImagen formato) =>
        formato == FormatoDeImagen.Png ? ("image/png", ".png") : ("image/jpeg", ".jpg");

    /// <summary>Olvida la imagen de fondo guardada en memoria (al cambiarla).</summary>
    /// <param name="ruta">Su ruta.</param>
    public static void OlvidarFondo(string? ruta)
    {
        if (!string.IsNullOrEmpty(ruta)) Fondos.TryRemove(ruta, out _);
    }

    /// <summary>Convierte un color escrito en <c>#RRGGBB</c> o por su nombre.</summary>
    /// <param name="texto">El color.</param>
    /// <param name="siNoVale">El que se usa si no se entiende.</param>
    /// <returns>El color.</returns>
    public static Color AColor(string? texto, Color siNoVale)
    {
        if (string.IsNullOrWhiteSpace(texto)) return siNoVale;
        try
        {
            return ColorConverter.ConvertFromString(texto.Trim()) is Color c ? c : siNoVale;
        }
        catch (FormatException)
        {
            return siNoVale;
        }
    }

    private static (FormattedText Formato, double Izquierda) Formatear(CampoDeQsl campo, string texto, double k, double ppp)
    {
        var tipo = new Typeface(
            new FontFamily(string.IsNullOrWhiteSpace(campo.Fuente) ? "Arial" : campo.Fuente),
            campo.Cursiva ? FontStyles.Italic : FontStyles.Normal,
            campo.Negrita ? FontWeights.Bold : FontWeights.Normal,
            FontStretches.Normal);
        var tamano = Math.Clamp(campo.TamanoPt, 2, 400) * ppp / 72.0;
        var formato = new FormattedText(
            texto,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            tipo,
            tamano,
            Pincel(campo.Color, Colors.Black),
            1.0);

        var anchoDelTexto = formato.WidthIncludingTrailingWhitespace;

        // Con varias lineas, cada linea se alinea dentro del bloque igual que el bloque en la tarjeta.
        formato.MaxTextWidth = Math.Max(1, anchoDelTexto + 0.5);
        formato.TextAlignment = campo.Alineacion switch
        {
            AlineacionDeCampo.Centro => TextAlignment.Center,
            AlineacionDeCampo.Derecha => TextAlignment.Right,
            _ => TextAlignment.Left,
        };

        var ancla = campo.XMm * k;
        var izquierda = campo.Alineacion switch
        {
            AlineacionDeCampo.Centro => ancla - (anchoDelTexto / 2),
            AlineacionDeCampo.Derecha => ancla - anchoDelTexto,
            _ => ancla,
        };
        return (formato, izquierda);
    }

    private static Rect Colocar(BitmapSource imagen, Rect tarjeta, AjusteDeFondo ajuste)
    {
        if (ajuste == AjusteDeFondo.Estirar || imagen.PixelWidth == 0 || imagen.PixelHeight == 0) return tarjeta;
        var escalaX = tarjeta.Width / imagen.PixelWidth;
        var escalaY = tarjeta.Height / imagen.PixelHeight;
        var escala = ajuste == AjusteDeFondo.Rellenar ? Math.Max(escalaX, escalaY) : Math.Min(escalaX, escalaY);
        var ancho = imagen.PixelWidth * escala;
        var alto = imagen.PixelHeight * escala;
        return new Rect((tarjeta.Width - ancho) / 2, (tarjeta.Height - alto) / 2, ancho, alto);
    }

    private static BitmapSource? Fondo(string? ruta)
    {
        if (string.IsNullOrWhiteSpace(ruta) || !File.Exists(ruta)) return null;
        if (Fondos.TryGetValue(ruta, out var hecha)) return hecha;
        try
        {
            // Se lee entera a memoria y se suelta el fichero: si no, queda bloqueado y no se
            // puede cambiar la foto ni borrar la plantilla mientras el programa este abierto.
            var imagen = new BitmapImage();
            using (var flujo = File.OpenRead(ruta))
            {
                imagen.BeginInit();
                imagen.CacheOption = BitmapCacheOption.OnLoad;
                imagen.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                imagen.StreamSource = flujo;
                imagen.EndInit();
            }

            imagen.Freeze();
            Fondos[ruta] = imagen;
            return imagen;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or FileFormatException or UnauthorizedAccessException)
        {
            Serilog.Log.Warning(ex, "No se ha podido leer la imagen de fondo de la QSL {Ruta}.", ruta);
            return null;
        }
    }

    private static SolidColorBrush Pincel(string? color, Color siNoVale)
    {
        var pincel = new SolidColorBrush(AColor(color, siNoVale));
        pincel.Freeze();
        return pincel;
    }
}
