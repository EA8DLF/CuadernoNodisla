using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Idiomas;
using Nodisla.Cuaderno.Modos.Rtty;
using Nodisla.Cuaderno.Ui.Ajustes;
using Nodisla.Cuaderno.Ui.Digital;
using Nodisla.Cuaderno.Ui.Desarrollo;
using Nodisla.Cuaderno.Ui.VistaModelos;
using Nodisla.Cuaderno.Ui.Vistas;

namespace Nodisla.Cuaderno.Ui.Pruebas;

/// <summary>
/// RTTY transmite con el mismo candado («no volver a preguntar») que CW y la fonía, y sus ajustes
/// (tono, desplazamiento, parada e inversión) sobreviven a cerrar el programa, igual que los de
/// CW. Con <c>CUADERNO_CAPTURAS_RTTY</c> (una carpeta) deja además las capturas de la ayuda.
/// </summary>
[Collection(nameof(ColeccionDeLaVentana))]
public sealed class PanelRttyPruebas
{
    [Fact]
    public async Task ElCandadoDeConfirmacionFunciona()
    {
        var ajustes = new AjustesDelPrograma();
        ajustes.Digital.PedirConfirmacionAlTransmitir = true;
        var salida = new SalidaDeAudioSimulada();
        var vigilante = new VigilanteDeMentira();
        var emisor = new EmisorRtty(salida, vigilante);
        var modelo = new VistaModeloRtty(ajustes, emisor: emisor, salida: salida, conReloj: false)
        {
            TextoAEmitir = "CQ CQ DE EA8DLF",
        };

        // Sin pregunta puesta: no se transmite.
        modelo.ConfirmarQueVaATransmitir = null;
        await modelo.EmitirCommand.ExecuteAsync(null);
        vigilante.Pedidas.Should().Be(0);
        modelo.TextoAEmitir.Should().Be("CQ CQ DE EA8DLF", "no se ha tocado: no se ha mandado nada");

        // Diciendo que no: tampoco.
        modelo.ConfirmarQueVaATransmitir = _ => false;
        await modelo.EmitirCommand.ExecuteAsync(null);
        vigilante.Pedidas.Should().Be(0);
        modelo.Aviso.Should().Be(Textos.T("Cabina.TxCw.NoConfirmado"));

        // «No volver a preguntar» (el mismo ajuste que CW y la fonía): ya no se pregunta.
        modelo.NoVolverAPreguntarAlTransmitir();
        ajustes.Digital.PedirConfirmacionAlTransmitir.Should().BeFalse();
        await modelo.EmitirCommand.ExecuteAsync(null);
        vigilante.Pedidas.Should().Be(1, "ya no hace falta confirmar, y esta vez sí se transmite");
        modelo.TextoAEmitir.Should().BeEmpty();
    }

    [Fact]
    public void ClasificaIndicativosLlamadaPropioYAbreviaturasComoEnCw()
    {
        var modelo = new VistaModeloRtty(new AjustesDelPrograma(), conReloj: false) { MiIndicativo = "EA8DLF" };

        modelo.Simular("CQ DE EA5XYZ EA5XYZ K\nEA5XYZ DE EA8DLF RST 599 TU 73\n");

        modelo.Principal.Palabras.Should().Contain(p => p.Texto == "CQ" && p.Tipo == TipoDePalabraCw.Llamada);
        modelo.Principal.Palabras.Should().Contain(p => p.Texto == "EA5XYZ" && p.Tipo == TipoDePalabraCw.Indicativo);
        modelo.Principal.Palabras.Should().Contain(p => p.Texto == "EA8DLF" && p.Tipo == TipoDePalabraCw.Propio);
        modelo.Principal.Palabras.Should().Contain(p => p.Texto == "TU" && p.Tipo == TipoDePalabraCw.Abreviatura);
        modelo.Principal.Traduccion.Should().Contain("TU").And.Contain("73");
        modelo.SinTexto.Should().BeFalse();

        string? elegido = null;
        modelo.IndicativoElegido += (_, i) => elegido = i;
        modelo.PasarIndicativoCommand.Execute("EA5XYZ");
        elegido.Should().Be("EA5XYZ");

        modelo.Borrar();
        modelo.Principal.Palabras.Should().BeEmpty();
        modelo.SinTexto.Should().BeTrue();
    }

    [Fact]
    public void LosAjustesDeRttySeGuardanYSeLeen()
    {
        var carpeta = Path.Combine(Path.GetTempPath(), "cuaderno-rtty-" + Guid.NewGuid().ToString("N"));
        try
        {
            var ajustes = new AjustesDelPrograma();
            var modelo = new VistaModeloRtty(ajustes, conReloj: false)
            {
                TonoHz = 1600,
                DesplazamientoHz = 850,
                BitsDeParada = 2,
                Invertido = true,
            };

            modelo.GuardarLoElegido(carpeta);

            var leidos = AjustesDelPrograma.Leer(carpeta);
            leidos.Rtty.TonoHz.Should().Be(1600);
            leidos.Rtty.DesplazamientoHz.Should().Be(850);
            leidos.Rtty.BitsDeParada.Should().Be(2);
            leidos.Rtty.Invertido.Should().BeTrue();

            // Lo leído se refleja al montar el modelo de nuevo, sin tocar nada a mano.
            var otraVez = new VistaModeloRtty(leidos, conReloj: false);
            otraVez.TonoHz.Should().Be(1600);
            otraVez.DesplazamientoHz.Should().Be(850);
            otraVez.BitsDeParada.Should().Be(2);
            otraVez.Invertido.Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(carpeta)) Directory.Delete(carpeta, true);
        }
    }

    [Fact]
    public Task Capturas() => HiloDeVentana.Ejecutar(async () =>
    {
        if (Environment.GetEnvironmentVariable("CUADERNO_CAPTURAS_RTTY") is not { Length: > 0 } capturas) return;

        var ajustes = new AjustesDelPrograma();
        var entrada = new EntradaDeAudioSimulada();
        var salida = new SalidaDeAudioSimulada();
        var vigilante = new VigilanteDeMentira();
        var emisor = new EmisorRtty(salida, vigilante);
        var modelo = new VistaModeloRtty(ajustes, entrada, emisor, salida, conReloj: false) { PaginaVisible = true };

        // Texto recibido de mentira, sin pasar audio de verdad por el canal: solo hace falta que
        // se vea la terminal con algo escrito, clasificado como en CW (indicativos, llamada y
        // propio resaltados, con la traducción debajo).
        modelo.MiIndicativo = "EA8DLF";
        modelo.Simular("CQ CQ CQ DE EA5XYZ EA5XYZ K\nEA5XYZ DE EA8DLF EA8DLF RST 599 599 NAME JOSE QTH TENERIFE TU 73\n");

        var panel = new PanelRtty { DataContext = modelo, Margin = new Thickness(8) };
        var borde = new Border { Child = panel, Padding = new Thickness(4), VerticalAlignment = VerticalAlignment.Top };
        borde.SetResourceReference(Border.BackgroundProperty, "FondoPanel");
        var ventana = Ventana(borde, 1100, 520);
        try
        {
            await Asentar();
            Guardar(panel, Path.Combine(capturas, "rtty-pagina.png"));

            // Un estado «en el aire» de mentira, sin tocar el PTT de verdad: solo la propiedad.
            modelo.EnAntena = true;
            modelo.Enviando = "CQ CQ CQ DE EA8DLF EA8DLF K";
            await Asentar();
            Guardar(panel, Path.Combine(capturas, "rtty-en-antena.png"));
        }
        finally
        {
            ventana.Close();
        }
    });

    private static async Task Asentar()
    {
        await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
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

    private static void Guardar(FrameworkElement raiz, string ruta)
    {
        var ancho = (int)Math.Ceiling(raiz.ActualWidth);
        var alto = (int)Math.Ceiling(raiz.ActualHeight);
        var dibujo = new DrawingVisual();
        using (var lienzo = dibujo.RenderOpen())
        {
            lienzo.DrawRectangle((Brush)Application.Current.Resources["FondoPanel"], null, new Rect(0, 0, ancho, alto));
            lienzo.DrawRectangle(new VisualBrush(raiz) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top }, null, new Rect(0, 0, raiz.ActualWidth, raiz.ActualHeight));
        }

        var imagen = new RenderTargetBitmap(ancho, alto, 96, 96, PixelFormats.Pbgra32);
        imagen.Render(dibujo);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(imagen));
        Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);
        using var fichero = File.Create(ruta);
        png.Save(fichero);
    }

    private sealed class VigilanteDeMentira : IVigilantePtt
    {
        public int Pedidas { get; private set; }

        public TimeSpan TiempoMaximo => TimeSpan.FromMinutes(3);

        public TimeSpan TiempoSinLatido => TimeSpan.FromSeconds(10);

        public bool EnAntena { get; private set; }

        public event EventHandler<MotivoDeSuelta>? PttSoltado;

        public Task<ITransmisionEnCurso> PedirAntenaAsync(string motivo, CancellationToken ct = default)
        {
            Pedidas++;
            EnAntena = true;
            _ = PttSoltado;
            return Task.FromResult<ITransmisionEnCurso>(new TransmisionDeMentira(this));
        }

        public Task SoltarYaAsync(MotivoDeSuelta motivo = MotivoDeSuelta.Panico)
        {
            EnAntena = false;
            return Task.CompletedTask;
        }

        private sealed class TransmisionDeMentira(VigilanteDeMentira vigilante) : ITransmisionEnCurso
        {
            public bool EnAntena => vigilante.EnAntena;

            public void Latir()
            {
            }

            public ValueTask DisposeAsync()
            {
                vigilante.EnAntena = false;
                return ValueTask.CompletedTask;
            }
        }
    }
}
