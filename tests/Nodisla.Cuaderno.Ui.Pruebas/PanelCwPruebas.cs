using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FluentAssertions;
using Nodisla.Cuaderno.Ui.Ajustes;
using Nodisla.Cuaderno.Ui.Desarrollo;
using Nodisla.Cuaderno.Ui.Digital;
using Nodisla.Cuaderno.Ui.VistaModelos;
using Nodisla.Cuaderno.Ui.Vistas;

namespace Nodisla.Cuaderno.Ui.Pruebas;

/// <summary>
/// El panel de telegrafía de la cabina: la lógica sin ventana y el panel DE VERDAD pintado fuera
/// de la pantalla, sin activar, pulsado por los caminos de la accesibilidad. Con
/// <c>CUADERNO_CAPTURAS_CW</c> (una carpeta) deja además las capturas de la ayuda.
/// </summary>
[Collection(nameof(ColeccionDeLaVentana))]
public sealed class PanelCwPruebas
{
    // ── Indicativos y llamadas en el texto ───────────────────────────────

    [Theory]
    [InlineData("EA8DLF", TipoDePalabraCw.Propio)]
    [InlineData("EA8DLF/P", TipoDePalabraCw.Propio)]
    [InlineData("DL1ABC", TipoDePalabraCw.Indicativo)]
    [InlineData("G4XYZ", TipoDePalabraCw.Indicativo)]
    [InlineData("EA8/DL1ABC", TipoDePalabraCw.Indicativo)]
    [InlineData("3B8CF", TipoDePalabraCw.Indicativo)]
    [InlineData("CQ", TipoDePalabraCw.Llamada)]
    [InlineData("QRZ?", TipoDePalabraCw.Llamada)]
    [InlineData("<AR>", TipoDePalabraCw.Prosigno)]
    [InlineData("5NN", TipoDePalabraCw.Normal)]
    [InlineData("599", TipoDePalabraCw.Normal)]
    [InlineData("FT710", TipoDePalabraCw.Normal)]
    [InlineData("RST", TipoDePalabraCw.Normal)]
    [InlineData("TNX", TipoDePalabraCw.Normal)]
    public void SeReconocenIndicativosLlamadasYProsignos(string palabra, TipoDePalabraCw tipo) =>
        PalabrasCw.Clasificar(palabra, "EA8DLF").Should().Be(tipo);

    [Fact]
    public void LosProsignosPegadosSeSeparan() =>
        PalabrasCw.Partir("HW?<KN> 73<SK>").Should().Equal("HW?", "<KN>", "73", "<SK>");

    // ── El modelo, sin ventana ───────────────────────────────────────────

    [Fact]
    public void SaleConElEquipoEnCwYFijaAlPitchDelEquipo()
    {
        var modelo = new VistaModeloCw(new AjustesDelPrograma(), new EntradaDeAudioSimulada(), conReloj: false);
        modelo.Visible.Should().BeFalse();

        modelo.SeguirAlEquipo("USB", 600);
        modelo.Visible.Should().BeFalse("en fonía no estorba");
        modelo.Escuchando.Should().BeFalse();

        modelo.SeguirAlEquipo("CW", 650);
        modelo.Visible.Should().BeTrue();
        modelo.Escuchando.Should().BeTrue("se ve y no está en pausa");
        modelo.RotuloDeFijar.Should().Contain("650");

        modelo.FijarOAuto();
        modelo.Fijo.Should().BeTrue();
        modelo.TonoHz.Should().Be(650);
        modelo.Decodificador.TonoFijoHz.Should().Be(650);

        modelo.FijarOAuto();
        modelo.Fijo.Should().BeFalse();
        modelo.Decodificador.TonoFijoHz.Should().BeNull();

        modelo.AlternarPausa();
        modelo.Escuchando.Should().BeFalse("en pausa no se escucha el audio");
        modelo.SeguirAlEquipo("LSB", null);
        modelo.Visible.Should().BeFalse();
    }

    [Fact]
    public void LeeElAudioSimuladoYResaltaLlamadaIndicativosYElPropio()
    {
        var entrada = new EntradaDeAudioSimulada();
        var modelo = new VistaModeloCw(new AjustesDelPrograma(), entrada, conReloj: false) { MiIndicativo = "EA8DLF" };
        modelo.MostrarAMano = true;

        string? elegido = null;
        modelo.IndicativoElegido += (_, i) => elegido = i;

        Alimentar(entrada, modelo, 40);

        var texto = modelo.Principal.Texto;
        texto.Should().Contain("EA5XYZ").And.Contain("DE");
        modelo.Wpm.Should().BeApproximately(22, 3);
        modelo.TonoHz.Should().BeApproximately(700, 15);
        modelo.Principal.Palabras.Should().Contain(p => p.Texto == "EA5XYZ" && p.Tipo == TipoDePalabraCw.Indicativo);
        modelo.Principal.Palabras.Should().Contain(p => p.Texto == "CQ" && p.Tipo == TipoDePalabraCw.Llamada);
        modelo.Principal.Palabras.Should().Contain(p => p.Texto == "EA8DLF" && p.Tipo == TipoDePalabraCw.Propio);
        modelo.Otras.Should().Contain(l => l.Texto.Contains("DL1ABC"), "la de 940 Hz sale en su línea");

        modelo.PasarIndicativo("EA5XYZ");
        elegido.Should().Be("EA5XYZ");

        modelo.Borrar();
        modelo.Principal.Palabras.Should().BeEmpty();
        modelo.SinTexto.Should().BeTrue();
    }

    [Fact]
    public void LosAjustesDeCwSeGuardanSeAcotanYAvisan()
    {
        var carpeta = Path.Combine(Path.GetTempPath(), "cuaderno-cw-" + Guid.NewGuid().ToString("N"));
        try
        {
            var ajustes = new AjustesDelPrograma();
            var audio = new VistaModeloAjustesAudio(ajustes, carpeta, new EntradaDeAudioSimulada(), new SalidaDeAudioSimulada());
            var avisos = 0;
            audio.AjustesDeCwGuardados += (_, _) => avisos++;

            audio.CwTonoHz = 620;
            audio.CwAnchoHz = 75;
            audio.CwSensibilidad = 9;
            audio.CwWpmMinima = 10;
            audio.CwWpmMaxima = 99;
            audio.CwSenales = 2;
            audio.Guardar();

            avisos.Should().Be(1);
            audio.CwWpmMaxima.Should().Be(60, "se acota y la pantalla enseña lo guardado");
            var leidos = AjustesDelPrograma.Leer(carpeta);
            leidos.Cw.TonoPorOmisionHz.Should().Be(620);
            leidos.Cw.AnchoDelFiltroHz.Should().Be(75);
            leidos.Cw.Sensibilidad.Should().Be(9);
            leidos.Cw.WpmMinima.Should().Be(10);
            leidos.Cw.WpmMaxima.Should().Be(60);
            leidos.Cw.Opciones().CanalesMaximos.Should().Be(2);

            var modelo = new VistaModeloCw(leidos, null, conReloj: false);
            modelo.TonoHz.Should().Be(620);
            modelo.Decodificador.Opciones.AnchoDelFiltroHz.Should().Be(75);
        }
        finally
        {
            if (Directory.Exists(carpeta)) Directory.Delete(carpeta, true);
        }
    }

    // ── El panel de verdad ───────────────────────────────────────────────

    [Fact]
    public Task NingunEnlaceRotoYTodosLosBotonesHacenAlgo() => HiloDeVentana.Ejecutar(async () =>
    {
        var errores = new StringBuilder();
        using var oyente = new OyenteDeEnlacesCw(errores);
        var entrada = new EntradaDeAudioSimulada { SinReloj = true };
        var modelo = new VistaModeloCw(new AjustesDelPrograma(), entrada, conReloj: false) { MiIndicativo = "EA8DLF" };
        modelo.MostrarAMano = true;
        string? elegido = null;
        modelo.IndicativoElegido += (_, i) => elegido = i;

        var (ventana, panel) = Pintar(modelo, 1300);
        try
        {
            await Asentar();
            Buscar<Button>(panel, "Escuchar").IsVisible.Should().BeTrue("la entrada está cerrada");
            Pulsar(panel, "Escuchar");
            await Asentar();
            modelo.HayAudio.Should().BeTrue();

            Alimentar(entrada, modelo, 30);
            await Asentar();

            // Los indicativos son botones; pulsar uno lo pasa al contacto nuevo.
            var indicativo = Todos<Button>(panel).First(b => b.IsVisible && Equals(b.Content, "EA5XYZ"));
            ((IInvokeProvider)new ButtonAutomationPeer(indicativo)).Invoke();
            await Asentar();
            elegido.Should().Be("EA5XYZ");

            // Clic en el espectro: fija el tono ahí.
            var espectro = Todos<EspectroCw>(panel).Single();
            espectro.PulsarEn(espectro.ActualWidth / 2);
            await Asentar();
            modelo.Fijo.Should().BeTrue();
            modelo.TonoHz.Should().BeApproximately(espectro.HerciosEn(espectro.ActualWidth / 2), 0.5);

            Pulsar(panel, "Auto");
            await Asentar();
            modelo.Fijo.Should().BeFalse();
            Pulsar(panel, "Pausa");
            await Asentar();
            modelo.Pausado.Should().BeTrue();
            Pulsar(panel, "Seguir");
            await Asentar();
            modelo.Pausado.Should().BeFalse();
            Pulsar(panel, "Borrar");
            await Asentar();
            modelo.Principal.Palabras.Should().BeEmpty();
            Pulsar(panel, "Ocultar");
            await Asentar();
            modelo.MostrarAMano.Should().BeFalse();

            Todos<ButtonBase>(panel).Where(b => b.Command is null && b.TemplatedParent is null).Should().BeEmpty("cada botón lleva su orden");
            errores.ToString().Should().BeEmpty("cada enlace roto es un control o un dato muerto");
        }
        finally
        {
            ventana.Close();
        }
    });

    [Fact]
    public Task ElApartadoDeAjustesNoTieneEnlacesRotos() => HiloDeVentana.Ejecutar(async () =>
    {
        var errores = new StringBuilder();
        using var oyente = new OyenteDeEnlacesCw(errores);
        var carpeta = Path.Combine(Path.GetTempPath(), "cuaderno-cw-" + Guid.NewGuid().ToString("N"));
        var audio = new VistaModeloAjustesAudio(new AjustesDelPrograma(), carpeta, new EntradaDeAudioSimulada(), new SalidaDeAudioSimulada());
        var vista = new AjustesDeAudio { DataContext = audio, Width = 760 };
        var ventana = Ventana(new ScrollViewer { Content = vista }, 800, 1400);
        try
        {
            await Asentar();
            errores.ToString().Should().BeEmpty();
            var seccion = (FrameworkElement)vista.FindName("AjustesDeCw");
            seccion.Should().NotBeNull();
            if (Environment.GetEnvironmentVariable("CUADERNO_CAPTURAS_CW") is { Length: > 0 } capturas)
            {
                RetratarDesde(vista, seccion, Path.Combine(capturas, "configuracion-cw.png"));
            }
        }
        finally
        {
            audio.Detener();
            ventana.Close();
        }
    });

    [Fact]
    public Task Capturas() => HiloDeVentana.Ejecutar(async () =>
    {
        if (Environment.GetEnvironmentVariable("CUADERNO_CAPTURAS_CW") is not { Length: > 0 } capturas) return;

        foreach (var (tema, sufijo) in new[] { ("Tema.Oscuro", string.Empty), ("Tema.Claro", "-claro") })
        {
            using var conTema = new ConTemaCw(tema);
            var entrada = new EntradaDeAudioSimulada { SinReloj = true };
            var modelo = new VistaModeloCw(new AjustesDelPrograma(), entrada, conReloj: false) { MiIndicativo = "EA8DLF" };
            modelo.SeguirAlEquipo("CW", 700);
            await entrada.AbrirAsync("simulado-ft710-entrada");
            var (ventana, panel) = Pintar(modelo, 1340);
            try
            {
                Alimentar(entrada, modelo, 36);
                await Asentar();
                Retratar(panel, Path.Combine(capturas, $"cw-cabina{sufijo}.png"));
            }
            finally
            {
                ventana.Close();
            }
        }
    });

    // ── Utilidades ───────────────────────────────────────────────────────

    /// <summary>Adelanta el audio simulado y lo vuelca en el panel cada 100 ms.</summary>
    private static void Alimentar(EntradaDeAudioSimulada entrada, VistaModeloCw modelo, double segundos)
    {
        for (var t = 0.0; t < segundos; t += 0.1)
        {
            entrada.Adelantar(0.1);
            modelo.Refrescar();
        }
    }

    private static (Window Ventana, PanelCw Panel) Pintar(VistaModeloCw modelo, double ancho)
    {
        var panel = new PanelCw { DataContext = modelo, Margin = new Thickness(8) };
        var borde = new Border { Child = panel, Padding = new Thickness(4), VerticalAlignment = VerticalAlignment.Top };
        borde.SetResourceReference(Border.BackgroundProperty, "FondoPanel");
        return (Ventana(borde, ancho, 260), panel);
    }

    private static Window Ventana(UIElement contenido, double ancho, double alto)
    {
        var ventana = new Window
        {
            Content = contenido,
            Width = ancho,
            Height = alto,
            WindowStyle = WindowStyle.None,
            ShowActivated = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -10000,
            Top = 0,
        };
        ventana.SetResourceReference(TextElement.ForegroundProperty, "Texto");
        ventana.Show();
        ventana.UpdateLayout();
        return ventana;
    }

    private static async Task Asentar()
    {
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
    }

    private static void Pulsar(DependencyObject raiz, string rotulo)
    {
        var boton = Buscar<Button>(raiz, rotulo);
        boton.IsEnabled.Should().BeTrue($"«{rotulo}» tiene que poder pulsarse");
        ((IInvokeProvider)new ButtonAutomationPeer(boton)).Invoke();
    }

    private static T Buscar<T>(DependencyObject raiz, string rotulo) where T : ContentControl =>
        Todos<T>(raiz).First(c => c.IsVisible && string.Equals(c.Content?.ToString(), rotulo, StringComparison.Ordinal));

    private static IEnumerable<T> Todos<T>(DependencyObject raiz) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(raiz); i++)
        {
            var hijo = VisualTreeHelper.GetChild(raiz, i);
            if (hijo is T t) yield return t;
            foreach (var nieto in Todos<T>(hijo)) yield return nieto;
        }
    }

    private static void Retratar(FrameworkElement elemento, string ruta)
    {
        var raiz = (FrameworkElement)VisualTreeHelper.GetParent(elemento);
        var ancho = (int)Math.Ceiling(raiz.ActualWidth);
        var alto = (int)Math.Ceiling(raiz.ActualHeight);
        Guardar(raiz, new Rect(0, 0, ancho, alto), ruta);
    }

    private static void RetratarDesde(FrameworkElement vista, FrameworkElement seccion, string ruta)
    {
        // Desde el rótulo «Telegrafía (CW)» (dos elementos antes de la sección) hasta su final.
        var arriba = seccion.TranslatePoint(new Point(0, 0), vista).Y - 52;
        var abajo = seccion.TranslatePoint(new Point(0, seccion.ActualHeight), vista).Y + 8;
        Guardar(vista, new Rect(0, Math.Max(0, arriba), vista.ActualWidth, abajo - Math.Max(0, arriba)), ruta);
    }

    private static void Guardar(FrameworkElement raiz, Rect zona, string ruta)
    {
        var dibujo = new DrawingVisual();
        using (var lienzo = dibujo.RenderOpen())
        {
            lienzo.DrawRectangle((Brush)Application.Current.Resources["FondoPanel"], null, new Rect(0, 0, zona.Width, zona.Height));
            lienzo.PushTransform(new TranslateTransform(-zona.X, -zona.Y));
            lienzo.DrawRectangle(new VisualBrush(raiz) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top }, null, new Rect(0, 0, raiz.ActualWidth, raiz.ActualHeight));
            lienzo.Pop();
        }

        var imagen = new RenderTargetBitmap((int)Math.Ceiling(zona.Width), (int)Math.Ceiling(zona.Height), 96, 96, PixelFormats.Pbgra32);
        imagen.Render(dibujo);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(imagen));
        Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);
        using var fichero = File.Create(ruta);
        png.Save(fichero);
    }

    private sealed class ConTemaCw : IDisposable
    {
        private readonly int _indice = -1;
        private readonly ResourceDictionary? _antes;

        public ConTemaCw(string tema)
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

    private sealed class OyenteDeEnlacesCw : TraceListener
    {
        private readonly StringBuilder _errores;

        public OyenteDeEnlacesCw(StringBuilder errores)
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
