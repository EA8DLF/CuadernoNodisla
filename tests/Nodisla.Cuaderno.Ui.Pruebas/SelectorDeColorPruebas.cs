using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FluentAssertions;
using Nodisla.Cuaderno.Impresion.Diplomas;
using Nodisla.Cuaderno.Impresion.Qsl;
using Nodisla.Cuaderno.Ui.Qsl;
using Nodisla.Cuaderno.Ui.VistaModelos;
using Nodisla.Cuaderno.Ui.Vistas.Controles;

namespace Nodisla.Cuaderno.Ui.Pruebas;

/// <summary>
/// El selector de color de los editores de QSL y diplomas: paleta, colores del diseño,
/// recientes, el formato guardado de ida y vuelta y el control asignando el color al elemento.
/// </summary>
/// <remarks>
/// Con <c>CUADERNO_CAPTURAS_SELECTOR</c> apuntando a una carpeta deja allí las capturas
/// <c>selector-de-color-*.png</c> con el desplegable abierto, pintadas fuera de la pantalla.
/// </remarks>
[Collection(nameof(ColeccionDeLaVentana))]
public sealed class SelectorDeColorPruebas
{
    [Fact]
    public void La_paleta_tiene_40_colores_distintos_y_con_nombre()
    {
        PaletaDeColores.Basica.Should().HaveCount(40);
        PaletaDeColores.Basica.Select(m => m.Color).Should().OnlyHaveUniqueItems();
        PaletaDeColores.Basica.Select(m => m.Nombre).Should().OnlyHaveUniqueItems().And.NotContain(string.Empty);
        PaletaDeColores.Basica.Should().Contain(m => m.Codigo == "#1B3A5C" && m.Nombre == "Azul marino", "el azul de las orlas de fábrica");
        PaletaDeColores.Basica.Should().Contain(m => m.Codigo == "#0F5FA8", "el acento del tema NODISLA");
    }

    [Theory]
    [InlineData("#1B3A5C", "#1B3A5C")]
    [InlineData("#1b3a5c", "#1B3A5C")]
    [InlineData("#E6FFFFFF", "#E6FFFFFF")]
    [InlineData("#FF000000", "#000000")]
    [InlineData("White", "#FFFFFF")]
    [InlineData("Navy", "#000080")]
    [InlineData("#ABC", "#AABBCC")]
    public void El_formato_guardado_va_y_vuelve(string guardado, string reescrito)
    {
        PaletaDeColores.TryLeer(guardado, out var color).Should().BeTrue();
        var texto = PaletaDeColores.Formatear(color);
        texto.Should().Be(reescrito);
        PaletaDeColores.TryLeer(texto, out var otraVez).Should().BeTrue();
        otraVez.Should().Be(color, "lo que se escribe se vuelve a leer igual");
        DibujanteDeQsl.AColor(texto, Colors.Transparent).Should().Be(color, "y el dibujante de la tarjeta lo entiende igual");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("#12")]
    [InlineData("azulito")]
    public void Lo_que_no_es_un_color_no_se_entiende(string? texto) =>
        PaletaDeColores.TryLeer(texto, out _).Should().BeFalse();

    [Fact]
    public void Todos_los_colores_de_las_plantillas_de_fabrica_se_leen()
    {
        var colores = new List<string?>();
        foreach (var d in new[] { PlantillasDeDiplomaDeFabrica.Clasico(), PlantillasDeDiplomaDeFabrica.Sobrio() })
        {
            colores.AddRange([d.ColorDeFondo, d.Marco.Color, d.Marco.ColorSecundario, d.Tabla.ColorDeTexto, d.Tabla.ColorDeCabecera, d.Tabla.ColorDeLineas]);
            colores.AddRange(d.Campos.Select(c => c.Color));
            colores.AddRange(d.Campos.Select(c => c.ColorDeRecuadro).Where(c => !string.IsNullOrEmpty(c)));
            colores.AddRange(d.ImagenesColocadas.Select(i => i.ColorDeLinea));
        }

        colores.Should().NotBeEmpty();
        colores.Where(c => !PaletaDeColores.TryLeer(c, out var _)).Should().BeEmpty();
    }

    [Fact]
    public void Los_colores_del_diseno_salen_sin_repetir_y_en_su_orden()
    {
        var muestras = PaletaDeColores.DelDiseno(["#FFFFFF", "#1b3a5c", null, "", "no-es-color", "#1B3A5C", "White", "#00FFFFFF", "#E6FFFFFF", "#B08D57"]);

        muestras.Select(m => m.Codigo).Should().Equal("#FFFFFF", "#1B3A5C", "#E6FFFFFF", "#B08D57");
        muestras[1].Nombre.Should().Be("Azul marino", "un color de la paleta lleva su nombre");
        muestras[2].Nombre.Should().Be("Blanco al 90 %", "la transparencia se dice");
        PaletaDeColores.DelDiseno(Enumerable.Range(0, 60).Select(i => PaletaDeColores.Formatear(Color.FromRgb((byte)i, 10, 10))))
            .Should().HaveCount(PaletaDeColores.MaximoDelDiseno);
    }

    [Theory]
    [InlineData("#0D2B4E", "Azul oscuro")]
    [InlineData("#9A9A9A", "Gris")]
    [InlineData("#F4C2C2", "Rojo pastel")]
    [InlineData("#2E8B57", "Verde")]
    public void Un_color_sin_nombre_se_describe(string codigo, string nombre)
    {
        PaletaDeColores.TryLeer(codigo, out var color).Should().BeTrue();
        PaletaDeColores.Nombrar(color).Should().Be(nombre);
    }

    [Fact]
    public void Tono_saturacion_y_brillo_van_y_vuelven()
    {
        foreach (var m in PaletaDeColores.Basica)
        {
            var (t, s, b) = PaletaDeColores.ATsb(m.Color);
            PaletaDeColores.DeTsb(t, s, b).Should().Be(m.Color, m.Nombre);
        }
    }

    [Fact]
    public void Los_recientes_van_primero_sin_repetir_y_con_tope()
    {
        var recientes = new ColoresRecientes();
        recientes.Anotar(Colors.Red);
        recientes.Anotar(Colors.Blue);
        recientes.Anotar(Colors.Red);
        recientes.Anotar(Colors.Transparent);

        recientes.Muestras.Select(m => m.Color).Should().Equal(Colors.Red, Colors.Blue);
        for (var i = 0; i < 20; i++) recientes.Anotar(Color.FromRgb((byte)(i * 10), 0, 0));
        recientes.Muestras.Should().HaveCount(ColoresRecientes.Maximo);
        recientes.Muestras[0].Color.Should().Be(Color.FromRgb(190, 0, 0));
    }

    [Fact]
    public Task El_control_asigna_el_color_al_elemento_y_se_pinta_sin_enlaces_rotos() =>
        HiloDeVentana.Ejecutar(async () =>
        {
            var errores = new StringBuilder();
            using var oyente = new OyenteDeEnlacesDelSelector(errores);
            var campo = new CampoDeQsl { Nombre = "Indicativo", Color = "White", ColorDeRecuadro = null };
            var cambios = 0;
            var editable = new CampoEditable(campo, () => cambios++);
            var origen = new OrigenFicticio(["#FFFFFF", "#1B3A5C", "#B08D57", "#1B3A5C"]);

            var (ventana, lienzo, editor) = Montar(editable, origen);
            try
            {
                await Asentar();
                var selectores = Descendientes<SelectorDeColor>(editor).ToList();
                selectores.Select(s => s.Titulo).Should().Contain(["Color del texto", "Recuadro detrás del texto"]);
                var texto = selectores.Single(s => s.Titulo == "Color del texto");
                var recuadro = selectores.Single(s => s.Titulo == "Recuadro detrás del texto");
                var recientes = new ColoresRecientes();
                texto.Recientes = recientes;
                recuadro.Recientes = recientes;

                texto.Color.Should().Be("White", "cargar no reescribe lo guardado");
                campo.Color.Should().Be("White");
                cambios.Should().Be(0);

                // Colores del diseño: los de la plantilla abierta, sin repetir.
                texto.PrepararDesplegable();
                texto.ColoresDelDiseno.Select(m => m.Codigo).Should().Equal("#FFFFFF", "#1B3A5C", "#B08D57");

                // Pulsar una muestra de la paleta escribe el color en el campo, en el formato de siempre.
                var contenido = Sacar(texto, lienzo);
                await Asentar();
                var rojo = Muestra(contenido, "Rojo");
                rojo.ToolTip.Should().Be("Rojo");
                AutomationName(rojo).Should().Be("Rojo");
                rojo.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                campo.Color.Should().Be("#D62828");
                editable.Color.Should().Be("#D62828");
                cambios.Should().Be(1);
                recientes.Muestras.Select(m => m.Codigo).Should().Equal("#D62828");

                // Una muestra del diseño, y el reciente sube al principio.
                texto.PrepararDesplegable();
                await Asentar();
                var delDiseno = Botones(contenido).Where(b => b.DataContext is MuestraDeColor m && texto.ColoresDelDiseno.Contains(m)).ToList();
                delDiseno.Should().HaveCount(3);
                delDiseno[1].RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                campo.Color.Should().Be("#1B3A5C");
                recientes.Muestras.Select(m => m.Codigo).Should().Equal("#1B3A5C", "#D62828");
                Devolver(texto, contenido, lienzo);

                // El recuadro admite opacidad y quedarse vacío.
                recuadro.Elegir(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF));
                campo.ColorDeRecuadro.Should().Be("#E6FFFFFF");
                recuadro.Elegir(PaletaDeColores.Basica.Single(m => m.Nombre == "Celeste"));
                campo.ColorDeRecuadro.Should().Be("#E6BBDEFB", "elegir en la paleta conserva la opacidad del recuadro");
                recuadro.Vaciar();
                campo.ColorDeRecuadro.Should().BeNull("sin recuadro se guarda como antes: nada");

                // El color del texto admite opacidad: se guarda con ella (#AARRGGBB, como antes).
                texto.Elegir(Color.FromArgb(0x80, 0x22, 0xC5, 0x5E));
                texto.AdmiteOpacidad.Should().BeTrue();
                campo.Color.Should().Be("#8022C55E");

                errores.ToString().Should().BeEmpty("cada enlace roto es un control muerto");
            }
            finally
            {
                ventana.Close();
            }
        });

    [Fact]
    public Task Un_campo_sin_opacidad_guarda_el_color_opaco() =>
        HiloDeVentana.Ejecutar(() =>
        {
            var selector = new SelectorDeColor { Color = "#FFFFFF", Recientes = new ColoresRecientes() };
            selector.Elegir(Color.FromArgb(0x40, 0x1B, 0x3A, 0x5C));
            selector.Color.Should().Be("#1B3A5C");
            selector.Vaciar();
            selector.Color.Should().Be("#1B3A5C", "sin «AdmiteVacio» no se puede dejar sin color");
            return Task.CompletedTask;
        });

    [Fact]
    public Task Capturas_con_el_desplegable_abierto() =>
        HiloDeVentana.Ejecutar(async () =>
        {
            var capturas = Environment.GetEnvironmentVariable("CUADERNO_CAPTURAS_SELECTOR");
            var errores = new StringBuilder();
            using var oyente = new OyenteDeEnlacesDelSelector(errores);
            var campo = new CampoDeQsl { Nombre = "Indicativo", Color = "#1B3A5C", ColorDeRecuadro = "#E6FFFFFF" };
            var editable = new CampoEditable(campo, () => { });
            var origen = new OrigenFicticio(["#FFFFFF", "#1B3A5C", "#B08D57", "#222222", "#B8B8B8", "#E6FFFFFF"]);

            foreach (var (tema, nombre) in new[] { ("Tema.Oscuro", "oscuro"), ("Tema.Claro", "claro") })
            {
                using var conTema = new ConTema(tema);
                var (ventana, lienzo, editor) = Montar(editable, origen);
                try
                {
                    await Asentar();
                    var selector = Descendientes<SelectorDeColor>(editor).Single(s => s.Titulo == "Color del texto");
                    var recientes = new ColoresRecientes();
                    recientes.Anotar((Color)ColorConverter.ConvertFromString("#D62828"));
                    recientes.Anotar((Color)ColorConverter.ConvertFromString("#0F5FA8"));
                    selector.Recientes = recientes;
                    selector.ColorExactoDesplegado = nombre == "oscuro";
                    selector.AvanzadoDesplegado = nombre == "claro";
                    selector.PrepararDesplegable();
                    var contenido = Sacar(selector, lienzo);
                    await Asentar();

                    Botones(contenido).Select(b => ((MuestraDeColor)b.DataContext).Nombre).Should().Contain("Azul marino");
                    errores.ToString().Should().BeEmpty("cada enlace roto es un control muerto");
                    if (capturas is { Length: > 0 }) Retratar((FrameworkElement)ventana.Content, Path.Combine(capturas, $"selector-de-color-{nombre}.png"));
                    Devolver(selector, contenido, lienzo);
                }
                finally
                {
                    ventana.Close();
                }
            }
        });

    // ── Montaje ───────────────────────────────────────────────────────────

    /// <summary>La ficha del campo (la plantilla compartida) en una ventana fuera de la pantalla.</summary>
    private static (Window Ventana, Canvas Lienzo, ContentControl Editor) Montar(CampoEditable editable, IColoresDelDiseno origen)
    {
        var editor = new ContentControl { Content = editable, Width = 320, Margin = new Thickness(16) };
        editor.Resources.MergedDictionaries.Add((ResourceDictionary)Application.LoadComponent(
            new Uri("/Nodisla.Cuaderno.Ui;component/Vistas/Qsl/EditorDeElementos.xaml", UriKind.Relative)));
        var lienzo = new Canvas();
        var raiz = new Grid { Width = 640, Height = 520, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        SelectorDeColor.SetOrigenDeColores(raiz, origen);
        editor.HorizontalAlignment = HorizontalAlignment.Left;
        editor.VerticalAlignment = VerticalAlignment.Top;
        raiz.Children.Add(editor);
        raiz.Children.Add(lienzo);
        raiz.SetResourceReference(TextElement.ForegroundProperty, "Texto");
        var ventana = new Window
        {
            Content = raiz,
            Width = 680,
            Height = 600,
            WindowStyle = WindowStyle.None,
            ShowActivated = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -8000,
            Top = 0,
        };
        ventana.Show();
        ventana.UpdateLayout();
        return (ventana, lienzo, editor);
    }

    /// <summary>
    /// Saca lo de dentro del desplegable y lo pone bajo el botón, en la misma ventana: un Popup
    /// de verdad se abriría en la pantalla del operador, y esto pinta lo mismo fuera de ella.
    /// </summary>
    private static FrameworkElement Sacar(SelectorDeColor selector, Canvas lienzo)
    {
        var contenido = selector.ContenidoDelDesplegable;
        selector.Desplegable.Child = null;
        var donde = selector.TranslatePoint(new Point(0, selector.ActualHeight), lienzo);
        Canvas.SetLeft(contenido, donde.X);
        Canvas.SetTop(contenido, donde.Y);
        lienzo.Children.Add(contenido);
        lienzo.UpdateLayout();
        return contenido;
    }

    private static void Devolver(SelectorDeColor selector, FrameworkElement contenido, Canvas lienzo)
    {
        lienzo.Children.Remove(contenido);
        selector.Desplegable.Child = contenido;
    }

    private static Button Muestra(FrameworkElement contenido, string nombre) =>
        Botones(contenido).First(b => b.DataContext is MuestraDeColor m && m.Nombre == nombre);

    private static IEnumerable<Button> Botones(DependencyObject raiz) =>
        Descendientes<Button>(raiz).Where(b => b.DataContext is MuestraDeColor && b.IsVisible);

    private static string AutomationName(DependencyObject d) => System.Windows.Automation.AutomationProperties.GetName(d);

    private static IEnumerable<T> Descendientes<T>(DependencyObject raiz)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(raiz); i++)
        {
            var hijo = VisualTreeHelper.GetChild(raiz, i);
            if (hijo is T t) yield return t;
            foreach (var dentro in Descendientes<T>(hijo)) yield return dentro;
        }
    }

    private static async Task Asentar()
    {
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
    }

    private static void Retratar(FrameworkElement raiz, string ruta)
    {
        // Lo que sobresale (el desplegable) también sale: el lienzo mide lo que ocupa todo.
        var limites = VisualTreeHelper.GetDescendantBounds(raiz);
        limites.Union(new Rect(0, 0, 1, 1));
        var ancho = (int)Math.Ceiling(Math.Max(limites.Right, 1) + 16);
        var alto = (int)Math.Ceiling(Math.Max(limites.Bottom, 1) + 16);
        var fondo = new DrawingVisual();
        using (var lienzo = fondo.RenderOpen())
        {
            lienzo.DrawRectangle((Brush)Application.Current.Resources["FondoPanel"], null, new Rect(0, 0, ancho, alto));
        }

        var imagen = new RenderTargetBitmap(ancho, alto, 96, 96, PixelFormats.Pbgra32);
        imagen.Render(fondo);
        imagen.Render(raiz);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(imagen));
        Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);
        using var fichero = File.Create(ruta);
        png.Save(fichero);
    }

    private sealed class OrigenFicticio(IEnumerable<string?> colores) : IColoresDelDiseno
    {
        public IEnumerable<string?> ColoresEnUso() => colores;
    }

    /// <summary>Cambia el tema de la aplicación de las pruebas mientras vive, y lo deja como estaba.</summary>
    private sealed class ConTema : IDisposable
    {
        private readonly int _indice = -1;
        private readonly ResourceDictionary? _antes;

        public ConTema(string tema)
        {
            var diccionarios = Application.Current.Resources.MergedDictionaries;
            for (var i = 0; i < diccionarios.Count; i++)
            {
                if (!diccionarios[i].Contains("FondoVentana") || !diccionarios[i].Contains("Acento")) continue;
                _indice = i;
                _antes = diccionarios[i];
                diccionarios[i] = (ResourceDictionary)Application.LoadComponent(new Uri($"/Nodisla.Cuaderno.Ui;component/Recursos/{tema}.xaml", UriKind.Relative));
                break;
            }
        }

        public void Dispose()
        {
            if (_indice >= 0 && _antes is not null) Application.Current.Resources.MergedDictionaries[_indice] = _antes;
        }
    }

    /// <summary>Recoge los errores de enlace de WPF mientras vive.</summary>
    private sealed class OyenteDeEnlacesDelSelector : TraceListener
    {
        private readonly StringBuilder _errores;

        public OyenteDeEnlacesDelSelector(StringBuilder errores)
        {
            _errores = errores;
            PresentationTraceSources.Refresh();
            PresentationTraceSources.DataBindingSource.Listeners.Add(this);
            PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
        }

        public override void Write(string? message) => _errores.Append(message);

        public override void WriteLine(string? message) => _errores.AppendLine(message);

        protected override void Dispose(bool disposing)
        {
            PresentationTraceSources.DataBindingSource.Listeners.Remove(this);
            base.Dispose(disposing);
        }
    }
}
