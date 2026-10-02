using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nodisla.Cuaderno.Ui.Soporte;
using Serilog;

namespace Nodisla.Cuaderno.Ui.Vistas;

/// <summary>
/// Pinta un capitulo de la ayuda (ya leido con <see cref="DocumentoMarkdown"/>) en un
/// <see cref="FlowDocument"/>: el visor de la ayuda sin navegador ni dependencias.
/// </summary>
/// <remarks>
/// Los colores van por referencia a los recursos del tema (<c>Texto</c>, <c>Acento</c>…), asi
/// que el capitulo abierto cambia con F9 igual que el resto de la ventana.
/// </remarks>
public static class ConstructorDeLaAyuda
{
    private static readonly FontFamily LetraDeMaquina = new("Consolas, Cascadia Mono, Courier New");

    /// <summary>Construye el documento.</summary>
    /// <param name="bloques">El capitulo ya leido.</param>
    /// <param name="libro">De donde salen las capturas.</param>
    /// <param name="alPulsarEnlace">Orden que atiende los enlaces (recibe el destino).</param>
    /// <param name="busqueda">Palabras que se resaltan, o nulo.</param>
    /// <param name="letra">Tamaño de la letra del texto corrido; los titulos van en proporcion.</param>
    /// <param name="anclas">Titulo de cada apartado, por su ancla, para poder bajar a el.</param>
    /// <returns>El documento.</returns>
    public static FlowDocument Construir(
        IReadOnlyList<BloqueMd> bloques,
        LibroDeAyuda libro,
        ICommand? alPulsarEnlace,
        string? busqueda,
        double letra,
        out Dictionary<string, Block> anclas)
    {
        ArgumentNullException.ThrowIfNull(bloques);
        ArgumentNullException.ThrowIfNull(libro);

        var documento = new FlowDocument
        {
            FontFamily = new FontFamily("Segoe UI"),
            PagePadding = new Thickness(18, 10, 18, 24),
            TextAlignment = TextAlignment.Left,
            IsHyphenationEnabled = false,
            LineHeight = double.NaN,
            FontSize = letra,
        };
        documento.SetResourceReference(TextElement.ForegroundProperty, "Texto");

        var contexto = new Contexto(libro, alPulsarEnlace, LibroDeAyuda.Palabras(busqueda), letra);
        anclas = contexto.Anclas;
        foreach (var bloque in bloques) documento.Blocks.Add(Bloque(bloque, contexto));
        return documento;
    }

    private sealed record Contexto(LibroDeAyuda Libro, ICommand? Enlace, string[] Palabras, double Letra)
    {
        public Dictionary<string, Block> Anclas { get; } = new(StringComparer.Ordinal);
    }

    private static Block Bloque(BloqueMd bloque, Contexto c)
    {
        switch (bloque)
        {
            case TituloMd titulo:
            {
                var p = Parrafo(titulo.Trozos, c);
                p.FontWeight = titulo.Nivel <= 2 ? FontWeights.Bold : FontWeights.SemiBold;
                p.FontSize = c.Letra * titulo.Nivel switch { 1 => 1.9, 2 => 1.42, 3 => 1.17, _ => 1.05 };
                p.Margin = titulo.Nivel switch
                {
                    1 => new Thickness(0, 0, 0, 10),
                    2 => new Thickness(0, 18, 0, 6),
                    _ => new Thickness(0, 12, 0, 4),
                };
                if (titulo.Nivel <= 2) p.SetResourceReference(TextElement.ForegroundProperty, "Acento");
                if (titulo.Nivel == 2)
                {
                    p.SetResourceReference(Block.BorderBrushProperty, "BordeSuave");
                    p.BorderThickness = new Thickness(0, 0, 0, 1);
                    p.Padding = new Thickness(0, 0, 0, 3);
                }

                c.Anclas.TryAdd(titulo.Ancla, p);
                return p;
            }

            case ParrafoMd parrafo:
            {
                var p = Parrafo(parrafo.Trozos, c);
                p.Margin = new Thickness(0, 0, 0, 9);
                p.LineHeight = c.Letra * 1.6;
                return p;
            }

            case ListaMd lista:
                return Lista(lista, c);

            case CitaMd cita:
            {
                var seccion = new Section
                {
                    Margin = new Thickness(0, 4, 0, 12),
                    Padding = new Thickness(12, 8, 10, 0),
                    BorderThickness = new Thickness(4, 0, 0, 0),
                };
                seccion.SetResourceReference(Block.BorderBrushProperty, "Acento");
                seccion.SetResourceReference(TextElement.BackgroundProperty, "FondoAlterno");
                foreach (var b in cita.Bloques) seccion.Blocks.Add(Bloque(b, c));
                return seccion;
            }

            case CodigoMd codigo:
            {
                var p = new Paragraph(new Run(codigo.Texto))
                {
                    FontFamily = LetraDeMaquina,
                    FontSize = c.Letra * 0.95,
                    Padding = new Thickness(10, 8, 10, 8),
                    Margin = new Thickness(0, 2, 0, 12),
                    BorderThickness = new Thickness(1),
                };
                p.SetResourceReference(TextElement.BackgroundProperty, "FondoAlterno");
                p.SetResourceReference(Block.BorderBrushProperty, "BordeSuave");
                return p;
            }

            case TablaMd tabla:
                return Tabla(tabla, c);

            case ImagenMd imagen:
                return Imagen(imagen, c);

            case RayaMd:
            {
                var raya = new Paragraph { Margin = new Thickness(0, 6, 0, 12), BorderThickness = new Thickness(0, 0, 0, 1), FontSize = 2 };
                raya.SetResourceReference(Block.BorderBrushProperty, "BordeSuave");
                return raya;
            }

            default:
                return new Paragraph();
        }
    }

    private static List Lista(ListaMd lista, Contexto c)
    {
        var l = new List
        {
            MarkerStyle = lista.Numerada ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
            Margin = new Thickness(0, 0, 0, 9),
            Padding = new Thickness(22, 0, 0, 0),
        };
        foreach (var elemento in lista.Elementos)
        {
            var item = new ListItem();
            var p = Parrafo(elemento.Trozos, c);
            p.Margin = new Thickness(0, 0, 0, 3);
            p.LineHeight = c.Letra * 1.6;
            item.Blocks.Add(p);
            foreach (var hijo in elemento.Hijos)
            {
                var b = Bloque(hijo, c);
                if (b is List sub) sub.Margin = new Thickness(0, 0, 0, 3);
                item.Blocks.Add(b);
            }

            l.ListItems.Add(item);
        }

        return l;
    }

    private static Table Tabla(TablaMd tabla, Contexto c)
    {
        var t = new Table { CellSpacing = 0, Margin = new Thickness(0, 2, 0, 12), BorderThickness = new Thickness(1) };
        t.SetResourceReference(Block.BorderBrushProperty, "BordeSuave");
        for (var i = 0; i < tabla.Cabecera.Count; i++)
        {
            // La primera columna (la tecla, el campo…) suele ser corta: que no se coma la mitad.
            t.Columns.Add(new TableColumn { Width = i == 0 && tabla.Cabecera.Count > 1 ? new GridLength(1, GridUnitType.Star) : new GridLength(2.4, GridUnitType.Star) });
        }

        var grupo = new TableRowGroup();
        var cabecera = new TableRow { FontWeight = FontWeights.SemiBold };
        cabecera.SetResourceReference(TextElement.BackgroundProperty, "FondoCabecera");
        foreach (var celda in tabla.Cabecera) cabecera.Cells.Add(Celda(celda, c));
        grupo.Rows.Add(cabecera);

        var alterna = false;
        foreach (var fila in tabla.Filas)
        {
            var f = new TableRow();
            if (alterna) f.SetResourceReference(TextElement.BackgroundProperty, "FondoAlterno");
            alterna = !alterna;
            foreach (var celda in fila.Take(tabla.Cabecera.Count)) f.Cells.Add(Celda(celda, c));
            grupo.Rows.Add(f);
        }

        t.RowGroups.Add(grupo);
        return t;
    }

    private static TableCell Celda(IReadOnlyList<TrozoMd> trozos, Contexto c)
    {
        var p = Parrafo(trozos, c);
        p.Margin = new Thickness(0);
        var celda = new TableCell(p) { Padding = new Thickness(8, 4, 8, 4), BorderThickness = new Thickness(0, 0, 1, 1) };
        celda.SetResourceReference(TableCell.BorderBrushProperty, "BordeSuave");
        return celda;
    }

    private static Block Imagen(ImagenMd imagen, Contexto c)
    {
        var fuente = Cargar(imagen.Ruta, c.Libro);
        if (fuente is null)
        {
            // Una captura que falta se dice, no se esconde: es un fallo de la ayuda.
            var falta = new Paragraph(new Run(Nodisla.Cuaderno.Idiomas.Textos.F("Ayuda.FaltaCaptura", Path.GetFileName(imagen.Ruta), imagen.Texto)))
            {
                FontStyle = FontStyles.Italic,
                Margin = new Thickness(0, 0, 0, 9),
            };
            falta.SetResourceReference(TextElement.ForegroundProperty, "ErrorTexto");
            return falta;
        }

        var foto = new Image
        {
            Source = fuente,
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.DownOnly,
            HorizontalAlignment = HorizontalAlignment.Left,
            MaxWidth = fuente.PixelWidth,
            ToolTip = imagen.Texto,
        };
        RenderOptions.SetBitmapScalingMode(foto, BitmapScalingMode.HighQuality);
        System.Windows.Automation.AutomationProperties.SetName(foto, imagen.Texto);

        var marco = new Border { Child = foto, BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Left };
        marco.SetResourceReference(Border.BorderBrushProperty, "Borde");

        var pie = new TextBlock
        {
            Text = imagen.Texto,
            TextWrapping = TextWrapping.Wrap,
            FontStyle = FontStyles.Italic,
            FontSize = c.Letra * 0.92,
            Margin = new Thickness(0, 4, 0, 0),
        };
        pie.SetResourceReference(TextBlock.ForegroundProperty, "TextoTenue");

        var pila = new StackPanel { Margin = new Thickness(0, 2, 0, 0) };
        pila.Children.Add(marco);
        pila.Children.Add(pie);
        return new BlockUIContainer(pila) { Margin = new Thickness(0, 4, 0, 14) };
    }

    private static BitmapSource? Cargar(string ruta, LibroDeAyuda libro)
    {
        try
        {
            using var flujo = libro.AbrirImagen(ruta);
            if (flujo is null) return null;
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = flujo;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "No se ha podido cargar la captura {Ruta} de la ayuda.", ruta);
            return null;
        }
    }

    private static Paragraph Parrafo(IReadOnlyList<TrozoMd> trozos, Contexto c)
    {
        var p = new Paragraph();
        foreach (var trozo in trozos) p.Inlines.Add(EnLinea(trozo, c));
        return p;
    }

    private static Inline EnLinea(TrozoMd trozo, Contexto c)
    {
        var span = new Span();
        if (trozo.Codigo)
        {
            span.FontFamily = LetraDeMaquina;
            span.SetResourceReference(TextElement.BackgroundProperty, "FondoAlterno");
        }

        if (trozo.Negrita) span.FontWeight = FontWeights.SemiBold;
        if (trozo.Cursiva) span.FontStyle = FontStyles.Italic;

        var texto = trozo.Codigo ? " " + trozo.Texto + " " : trozo.Texto;
        foreach (var run in Resaltar(texto, c.Palabras)) span.Inlines.Add(run);

        if (trozo.Enlace is null) return span;

        var enlace = new Hyperlink(span)
        {
            Command = c.Enlace,
            CommandParameter = trozo.Enlace,
            ToolTip = trozo.Enlace.StartsWith("http", StringComparison.OrdinalIgnoreCase) || trozo.Enlace.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
                ? trozo.Enlace
                : null,
        };
        enlace.SetResourceReference(TextElement.ForegroundProperty, "Acento");
        return enlace;
    }

    /// <summary>Parte el texto para pintar de fondo las palabras buscadas.</summary>
    private static IEnumerable<Run> Resaltar(string texto, string[] palabras)
    {
        if (palabras.Length == 0)
        {
            yield return new Run(texto);
            yield break;
        }

        var normal = LibroDeAyuda.Normalizar(texto);
        var marcas = new bool[texto.Length];
        foreach (var palabra in palabras)
        {
            var desde = 0;
            while ((desde = normal.IndexOf(palabra, desde, StringComparison.Ordinal)) >= 0)
            {
                for (var k = desde; k < desde + palabra.Length && k < marcas.Length; k++) marcas[k] = true;
                desde += Math.Max(1, palabra.Length);
            }
        }

        var inicio = 0;
        while (inicio < texto.Length)
        {
            var fin = inicio;
            while (fin < texto.Length && marcas[fin] == marcas[inicio]) fin++;
            var run = new Run(texto[inicio..fin]);
            if (marcas[inicio])
            {
                run.SetResourceReference(TextElement.BackgroundProperty, "AvisoFondo");
                run.SetResourceReference(TextElement.ForegroundProperty, "AvisoTexto");
            }

            yield return run;
            inicio = fin;
        }
    }
}
