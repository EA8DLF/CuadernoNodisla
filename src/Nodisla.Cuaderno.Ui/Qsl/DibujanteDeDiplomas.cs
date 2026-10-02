using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nodisla.Cuaderno.Idiomas;
using Nodisla.Cuaderno.Impresion.Diplomas;
using Nodisla.Cuaderno.Impresion.Qsl;

namespace Nodisla.Cuaderno.Ui.Qsl;

/// <summary>Una hoja del diploma ya dibujada, con su texto para la capa oculta del PDF.</summary>
/// <param name="Imagen">La hoja.</param>
/// <param name="Textos">Los textos que lleva, en milimetros.</param>
public sealed record HojaDeDiploma(BitmapSource Imagen, IReadOnlyList<TextoDePagina> Textos);

/// <summary>
/// Dibuja un diploma con WPF. Es el mismo motor que <see cref="DibujanteDeQsl"/>: los textos se
/// trazan con <see cref="DibujanteDeQsl.DibujarCampo"/>, el fondo se coloca con el mismo ajuste y
/// las imagenes salen de la misma cache. La pantalla, el PNG y el PDF son el mismo dibujo.
/// </summary>
/// <remarks>
/// La tabla de referencias se reparte en bloques; lo que no cabe va a hojas de anexo con la misma
/// orla, que solo salen en el PDF.
/// </remarks>
public static class DibujanteDeDiplomas
{
    /// <summary>Resolucion de la vista previa del editor.</summary>
    public const double PppDePantalla = 72;

    /// <summary>Resolucion del PNG y del PDF.</summary>
    public const double PppDeImprenta = 200;

    private const double SeparacionDeBloquesMm = 5;
    private const double MmPorPunto = 25.4 / 72.0;

    /// <summary>La primera hoja del diploma.</summary>
    /// <param name="diseno">La plantilla.</param>
    /// <param name="variables">Variables del diploma (<see cref="VariablesDeDiploma.Para"/>).</param>
    /// <param name="filas">Filas de la tabla.</param>
    /// <param name="rutaDeImagen">Traduce el nombre de una imagen de la plantilla a su ruta.</param>
    /// <param name="ppp">Resolucion.</param>
    /// <returns>La hoja.</returns>
    public static BitmapSource Dibujar(
        DisenoDeDiploma diseno,
        IReadOnlyDictionary<string, string> variables,
        IReadOnlyList<FilaDeJustificante> filas,
        Func<string?, string?> rutaDeImagen,
        double ppp) =>
        Hojas(diseno, variables, filas, rutaDeImagen, ppp, conAnexos: false)[0].Imagen;

    /// <summary>Todas las hojas: el diploma y, si la tabla no cabe, sus anexos.</summary>
    /// <param name="diseno">La plantilla.</param>
    /// <param name="variables">Variables del diploma.</param>
    /// <param name="filas">Filas de la tabla.</param>
    /// <param name="rutaDeImagen">Traduce el nombre de una imagen a su ruta.</param>
    /// <param name="ppp">Resolucion.</param>
    /// <param name="conAnexos">Dibujar las hojas de anexo.</param>
    /// <returns>Las hojas, la primera es el diploma.</returns>
    public static IReadOnlyList<HojaDeDiploma> Hojas(
        DisenoDeDiploma diseno,
        IReadOnlyDictionary<string, string> variables,
        IReadOnlyList<FilaDeJustificante> filas,
        Func<string?, string?> rutaDeImagen,
        double ppp,
        bool conAnexos = true)
    {
        ArgumentNullException.ThrowIfNull(diseno);
        ArgumentNullException.ThrowIfNull(variables);
        ArgumentNullException.ThrowIfNull(filas);
        ArgumentNullException.ThrowIfNull(rutaDeImagen);

        var tabla = diseno.Tabla;
        var caben = tabla.Visible ? Capacidad(tabla, tabla.AltoMm, tabla.Bloques) : 0;
        var conAnexo = tabla.Visible && tabla.Anexo && filas.Count > caben;

        // Si va a haber anexo, la ultima linea de la tabla lo anuncia en vez de una fila.
        var enLaPrimera = !tabla.Visible ? 0 : filas.Count <= caben ? filas.Count : Math.Max(0, caben - 1);
        var hojas = new List<HojaDeDiploma> { Primera(diseno, variables, filas, enLaPrimera, rutaDeImagen, ppp, conAnexo) };
        if (!conAnexo || !conAnexos) return hojas;

        var (x, y, ancho, alto) = HuecoDelAnexo(diseno);
        var bloques = diseno.AnchoMm > diseno.AltoMm ? 3 : 2;
        var porHoja = Math.Max(1, Capacidad(tabla, alto, bloques));
        var resto = filas.Skip(enLaPrimera).ToList();
        var total = (int)Math.Ceiling(resto.Count / (double)porHoja);
        for (var i = 0; i < total; i++)
        {
            var tramo = resto.Skip(i * porHoja).Take(porHoja).ToList();
            hojas.Add(Anexo(diseno, variables, tramo, enLaPrimera + (i * porHoja), i + 1, total, x, y, ancho, alto, bloques, ppp));
        }

        return hojas;
    }

    /// <summary>Cuantas filas caben en una caja de tabla.</summary>
    /// <param name="tabla">La tabla.</param>
    /// <param name="altoMm">Alto disponible.</param>
    /// <param name="bloques">Bloques uno al lado de otro.</param>
    /// <returns>Filas que caben, sin contar cabeceras.</returns>
    public static int Capacidad(TablaDeDiploma tabla, double altoMm, int bloques)
    {
        ArgumentNullException.ThrowIfNull(tabla);
        var fila = AltoDeFila(tabla);
        var util = altoMm - AltoDelTitulo(tabla) - fila;
        return Math.Max(0, (int)Math.Floor(util / fila)) * Math.Clamp(bloques, 1, 4);
    }

    private static HojaDeDiploma Primera(
        DisenoDeDiploma diseno,
        IReadOnlyDictionary<string, string> variables,
        IReadOnlyList<FilaDeJustificante> filas,
        int enLaTabla,
        Func<string?, string?> rutaDeImagen,
        double ppp,
        bool conAnexo)
    {
        var k = ppp / 25.4;
        var textos = new List<TextoDePagina>();
        var imagen = Pintar(diseno, ppp, dc =>
        {
            if (DibujanteDeQsl.Fondo(rutaDeImagen(diseno.ImagenDeFondo)) is { } fondo)
            {
                var hoja = new Rect(0, 0, diseno.AnchoMm * k, diseno.AltoMm * k);
                dc.PushClip(new RectangleGeometry(hoja));
                dc.DrawImage(fondo, DibujanteDeQsl.Colocar(fondo, hoja, diseno.Ajuste));
                dc.Pop();
            }

            Orla(dc, diseno.Marco, diseno.AnchoMm, diseno.AltoMm, k);

            foreach (var i in diseno.ImagenesColocadas.Where(i => i.Visible))
            {
                ImagenColocada(dc, i, rutaDeImagen(i.Fichero), k);
            }

            if (diseno.Tabla.Visible)
            {
                var t = diseno.Tabla;
                var pie = filas.Count > enLaTabla
                    ? conAnexo
                        ? Textos.F("Qsl.Disenador.Impreso.SigueEnAnexo", filas.Count - enLaTabla)
                        : Textos.F("Qsl.Disenador.Impreso.YMas", filas.Count - enLaTabla)
                    : null;
                Tabla(dc, t, VariablesDeDiploma.Sustituir(t.Titulo, variables), filas.Take(enLaTabla).ToList(), 0, pie, t.XMm, t.YMm, t.AnchoMm, t.Bloques, Capacidad(t, t.AltoMm, 1), k, ppp, textos);
            }

            foreach (var campo in diseno.Campos.Where(c => c.Visible))
            {
                var texto = VariablesDeDiploma.Sustituir(campo.Texto, variables);
                if (string.IsNullOrWhiteSpace(texto)) continue;
                DibujanteDeQsl.DibujarCampo(dc, campo, texto, k, ppp);
                var caja = DibujanteDeQsl.CajaDelTexto(campo, texto);
                textos.Add(new TextoDePagina(texto, caja.X + 1, caja.Y + 0.5, campo.TamanoPt));
            }
        });
        return new HojaDeDiploma(imagen, textos);
    }

    private static HojaDeDiploma Anexo(
        DisenoDeDiploma diseno,
        IReadOnlyDictionary<string, string> variables,
        IReadOnlyList<FilaDeJustificante> filas,
        int primera,
        int hoja,
        int hojas,
        double x,
        double y,
        double ancho,
        double alto,
        int bloques,
        double ppp)
    {
        var k = ppp / 25.4;
        var textos = new List<TextoDePagina>();
        var titulo = VariablesDeDiploma.Sustituir(Textos.F("Qsl.Disenador.Impreso.TituloDelAnexo"), variables)
            + " — " + Textos.F("Qsl.Disenador.Impreso.Anexo", hoja, hojas);
        var imagen = Pintar(diseno, ppp, dc =>
        {
            Orla(dc, diseno.Marco, diseno.AnchoMm, diseno.AltoMm, k);
            Tabla(dc, diseno.Tabla, titulo, filas, primera, null, x, y, ancho, bloques, Math.Max(1, Capacidad(diseno.Tabla, alto, 1)), k, ppp, textos);
        });
        return new HojaDeDiploma(imagen, textos);
    }

    private static (double X, double Y, double Ancho, double Alto) HuecoDelAnexo(DisenoDeDiploma diseno)
    {
        var margen = (diseno.Marco.Estilo == EstiloDeMarco.Ninguno ? 10 : diseno.Marco.MargenMm) + 10;
        return (margen, margen, diseno.AnchoMm - (2 * margen), diseno.AltoMm - (2 * margen));
    }

    private static BitmapSource Pintar(DisenoDeDiploma diseno, double ppp, Action<DrawingContext> dibujar)
    {
        var k = ppp / 25.4;
        var ancho = Math.Max(1, (int)Math.Round(diseno.AnchoMm * k));
        var alto = Math.Max(1, (int)Math.Round(diseno.AltoMm * k));
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(DibujanteDeQsl.Pincel(diseno.ColorDeFondo, Colors.White), null, new Rect(0, 0, ancho, alto));
            dibujar(dc);
        }

        // A 96 ppp un DIP es un pixel, que es en lo que se ha dibujado (ver DibujanteDeQsl).
        var mapa = new RenderTargetBitmap(ancho, alto, 96, 96, PixelFormats.Pbgra32);
        mapa.Render(visual);
        mapa.Freeze();
        return mapa;
    }

    private static void Orla(DrawingContext dc, MarcoDeDiploma marco, double anchoMm, double altoMm, double k)
    {
        if (marco.Estilo == EstiloDeMarco.Ninguno) return;
        var color = DibujanteDeQsl.Pincel(marco.Color, Colors.Black);
        var segundo = DibujanteDeQsl.Pincel(marco.ColorSecundario, Colors.Gray);
        var m = Math.Max(0, marco.MargenMm);
        var g = Math.Clamp(marco.GrosorMm, 0.1, 10);
        Rect R(double inset) => new((m + inset) * k, (m + inset) * k, Math.Max(1, (anchoMm - (2 * (m + inset))) * k), Math.Max(1, (altoMm - (2 * (m + inset))) * k));
        Pen P(Brush b, double mm) => new(b, Math.Max(0.5, mm * k));

        switch (marco.Estilo)
        {
            case EstiloDeMarco.Filete:
                dc.DrawRectangle(null, P(color, g), R(g / 2));
                break;

            case EstiloDeMarco.DobleFilete:
                dc.DrawRectangle(null, P(color, g), R(g / 2));
                dc.DrawRectangle(null, P(segundo, g * 0.35), R(g + 1.6));
                break;

            case EstiloDeMarco.Clasico:
            {
                dc.DrawRectangle(null, P(color, g), R(g / 2));
                dc.DrawRectangle(null, P(segundo, Math.Max(0.25, g * 0.3)), R(g + 1.8));
                dc.DrawRectangle(null, P(color, 0.2), R(g + 3.2));

                // Cuadros en las esquinas: tapan el cruce de los filetes y rematan la orla.
                const double Lado = 7;
                foreach (var (cx, cy) in new[] { (m, m), (anchoMm - m - Lado, m), (m, altoMm - m - Lado), (anchoMm - m - Lado, altoMm - m - Lado) })
                {
                    var cuadro = new Rect(cx * k, cy * k, Lado * k, Lado * k);
                    dc.DrawRectangle(color, null, cuadro);
                    var dentro = new Rect((cx + 1.6) * k, (cy + 1.6) * k, (Lado - 3.2) * k, (Lado - 3.2) * k);
                    dc.DrawRectangle(segundo, null, dentro);
                }

                break;
            }

            case EstiloDeMarco.Escuadras:
            {
                dc.DrawRectangle(null, P(color, g), R(3));
                const double Brazo = 16;
                var lapiz = P(segundo, Math.Max(0.8, g * 3));
                foreach (var (cx, cy, dx, dy) in new[] { (m, m, 1, 1), (anchoMm - m, m, -1, 1), (m, altoMm - m, 1, -1), (anchoMm - m, altoMm - m, -1, -1) })
                {
                    dc.DrawLine(lapiz, new Point(cx * k, cy * k), new Point((cx + (dx * Brazo)) * k, cy * k));
                    dc.DrawLine(lapiz, new Point(cx * k, cy * k), new Point(cx * k, (cy + (dy * Brazo)) * k));
                }

                break;
            }
        }
    }

    private static void ImagenColocada(DrawingContext dc, ImagenDeDiploma i, string? ruta, double k)
    {
        var caja = new Rect(i.XMm * k, i.YMm * k, Math.Max(1, i.AnchoMm * k), Math.Max(1, i.AltoMm * k));
        if (DibujanteDeQsl.Fondo(ruta) is { } imagen)
        {
            dc.PushOpacity(Math.Clamp(i.Opacidad, 0, 1));
            dc.DrawImage(imagen, DibujanteDeQsl.Colocar(imagen, caja, AjusteDeFondo.Encajar));
            dc.Pop();
        }

        if (i.LineaDeFirma)
        {
            var lapiz = new Pen(DibujanteDeQsl.Pincel(i.ColorDeLinea, Colors.Gray), Math.Max(0.5, 0.3 * k));
            dc.DrawLine(lapiz, new Point(caja.Left, caja.Bottom), new Point(caja.Right, caja.Bottom));
        }
    }

    private static double AltoDeFila(TablaDeDiploma t) => Math.Clamp(t.TamanoPt, 4, 30) * MmPorPunto * 1.55;

    private static double AltoDelTitulo(TablaDeDiploma t) => string.IsNullOrWhiteSpace(t.Titulo) ? 0 : Math.Clamp(t.TamanoPt, 4, 30) * 1.25 * MmPorPunto * 1.7;

    private static void Tabla(
        DrawingContext dc,
        TablaDeDiploma t,
        string titulo,
        IReadOnlyList<FilaDeJustificante> filas,
        int primera,
        string? pie,
        double xMm,
        double yMm,
        double anchoMm,
        int bloques,
        int filasPorBloque,
        double k,
        double ppp,
        List<TextoDePagina> textos)
    {
        bloques = Math.Clamp(bloques, 1, 4);
        var tamano = Math.Clamp(t.TamanoPt, 4, 30);
        var fila = AltoDeFila(t);
        var cabecera = DibujanteDeQsl.Pincel(t.ColorDeCabecera, Colors.Black);
        var texto = DibujanteDeQsl.Pincel(t.ColorDeTexto, Colors.Black);
        var lineas = new Pen(DibujanteDeQsl.Pincel(t.ColorDeLineas, Colors.LightGray), Math.Max(0.5, 0.12 * k));
        var raya = new Pen(cabecera, Math.Max(0.6, 0.3 * k));
        var y = yMm;

        if (!string.IsNullOrWhiteSpace(titulo) && !string.IsNullOrWhiteSpace(t.Titulo))
        {
            var f = Formato(titulo, t.Fuente, tamano * 1.25, true, cabecera, anchoMm * k, ppp, TextAlignment.Left);
            dc.DrawText(f, new Point(xMm * k, y * k));
            textos.Add(new TextoDePagina(titulo, xMm, y, tamano * 1.25));
            y += AltoDelTitulo(t);
        }

        var columnas = t.Columnas.Count > 0 ? t.Columnas : [new ColumnaDeTabla { Titulo = Textos.T("Qsl.Disenador.Impreso.Referencia"), Texto = "{referencia}" }];
        var anchoDeBloque = (anchoMm - (SeparacionDeBloquesMm * (bloques - 1))) / bloques;
        var pesos = columnas.Sum(c => Math.Max(0.1, c.Ancho));
        // Se llena un bloque entero antes de pasar al siguiente, como las columnas de un periodico.
        var porBloque = Math.Max(1, filasPorBloque);

        for (var b = 0; b < bloques; b++)
        {
            var x0 = xMm + (b * (anchoDeBloque + SeparacionDeBloquesMm));
            var yFila = y;

            // Solo los bloques que llevan filas (el primero siempre, para que se vea la tabla).
            var desde = b * porBloque;
            if (b > 0 && desde >= filas.Count + (pie is null ? 0 : 1)) break;
            Celdas(dc, columnas.Select(c => c.Titulo).ToList(), columnas, x0, yFila, anchoDeBloque, pesos, t.Fuente, tamano, true, cabecera, k, ppp);
            yFila += fila;
            dc.DrawLine(raya, new Point(x0 * k, (yFila - 0.6) * k), new Point((x0 + anchoDeBloque) * k, (yFila - 0.6) * k));

            for (var i = desde; i < Math.Min(desde + porBloque, filas.Count); i++)
            {
                var v = VariablesDeDiploma.DeFila(filas[i], primera + i + 1);
                var valores = columnas.Select(c => VariablesDeDiploma.Sustituir(c.Texto, v)).ToList();
                Celdas(dc, valores, columnas, x0, yFila, anchoDeBloque, pesos, t.Fuente, tamano, false, texto, k, ppp);
                textos.Add(new TextoDePagina(string.Join("  ", valores.Where(s => s.Length > 0)), x0, yFila, tamano));
                yFila += fila;
                dc.DrawLine(lineas, new Point(x0 * k, (yFila - 0.6) * k), new Point((x0 + anchoDeBloque) * k, (yFila - 0.6) * k));
            }

            if (pie is not null && desde + porBloque > filas.Count)
            {
                var f = Formato(pie, t.Fuente, tamano, false, cabecera, anchoDeBloque * k, ppp, TextAlignment.Left);
                f.SetFontStyle(FontStyles.Italic);
                dc.DrawText(f, new Point(x0 * k, yFila * k));
                textos.Add(new TextoDePagina(pie, x0, yFila, tamano));
            }
        }
    }

    private static void Celdas(
        DrawingContext dc,
        IReadOnlyList<string> valores,
        IReadOnlyList<ColumnaDeTabla> columnas,
        double x0,
        double y,
        double ancho,
        double pesos,
        string fuente,
        double tamano,
        bool negrita,
        Brush pincel,
        double k,
        double ppp)
    {
        var x = x0;
        for (var c = 0; c < columnas.Count; c++)
        {
            var anchoDeCelda = ancho * Math.Max(0.1, columnas[c].Ancho) / pesos;
            var alineacion = columnas[c].Alineacion switch
            {
                AlineacionDeCampo.Centro => TextAlignment.Center,
                AlineacionDeCampo.Derecha => TextAlignment.Right,
                _ => TextAlignment.Left,
            };
            if (!string.IsNullOrEmpty(valores[c]))
            {
                var f = Formato(valores[c], fuente, tamano, negrita, pincel, Math.Max(1, (anchoDeCelda - 1) * k), ppp, alineacion);
                dc.DrawText(f, new Point(x * k, y * k));
            }

            x += anchoDeCelda;
        }
    }

    private static FormattedText Formato(string texto, string fuente, double tamanoPt, bool negrita, Brush pincel, double anchoPx, double ppp, TextAlignment alineacion)
    {
        var tipo = new Typeface(
            new FontFamily(string.IsNullOrWhiteSpace(fuente) ? "Segoe UI" : fuente),
            FontStyles.Normal,
            negrita ? FontWeights.SemiBold : FontWeights.Normal,
            FontStretches.Normal);
        return new FormattedText(texto, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, tipo, tamanoPt * ppp / 72.0, pincel, 1.0)
        {
            MaxTextWidth = Math.Max(1, anchoPx),
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis,
            TextAlignment = alineacion,
        };
    }
}
