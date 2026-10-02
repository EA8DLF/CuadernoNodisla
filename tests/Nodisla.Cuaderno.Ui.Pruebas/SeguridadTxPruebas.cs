using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FluentAssertions;
using Nodisla.Cuaderno.Radio.Control;
using Nodisla.Cuaderno.Radio.Ptt;
using Nodisla.Cuaderno.Ui.Ajustes;
using Nodisla.Cuaderno.Ui.VistaModelos;
using Nodisla.Cuaderno.Ui.Vistas;

namespace Nodisla.Cuaderno.Ui.Pruebas;

/// <summary>
/// El apartado de salvaguardas de transmision: se guarda, se le da al vigilante, liberar una
/// banda pide confirmacion y la vista no tiene enlaces rotos. Con <c>CUADERNO_CAPTURAS_TXCW</c>
/// deja tambien su captura.
/// </summary>
[Collection(nameof(ColeccionDeLaVentana))]
public sealed class SeguridadTxPruebas
{
    private static VigilantePtt Vigilante() =>
        new(new ControlEquipoConmutable(), new OpcionesDelVigilante { EngancharseAlCierreDelProceso = false });

    [Fact]
    public async Task Se_guarda_se_aplica_al_vigilante_y_liberar_pide_confirmacion()
    {
        var carpeta = Path.Combine(Path.GetTempPath(), "cuaderno-segtx-" + Guid.NewGuid().ToString("N"));
        await using var vigilante = Vigilante();
        try
        {
            var vm = new VistaModeloSeguridadTx(vigilante, carpeta);
            vm.BloquearFueraDeBanda.Should().BeTrue("de fabrica, todo puesto");
            vm.VigilarRoe.Should().BeTrue();

            // Sin pregunta, o diciendo que no, la banda no se libera.
            var cb = vm.Filas.Single(f => f.Banda == "27 MHz");
            cb.Liberada = true;
            cb.Liberada.Should().BeFalse();
            string? preguntado = null;
            vm.ConfirmarLiberarBanda = p => { preguntado = p; return false; };
            cb.Liberada = true;
            cb.Liberada.Should().BeFalse();
            preguntado.Should().Contain("27 MHz");

            vm.ConfirmarLiberarBanda = _ => true;
            cb.Liberada = true;
            cb.Liberada.Should().BeTrue();
            vm.Filas.Single(f => f.Banda == "6m").PotenciaMaxima = "25";
            vm.RoeMaxima = 9;
            vm.Guardar();

            vigilante.Seguridad.BandasLiberadas.Should().Equal("27 MHz");
            vigilante.Seguridad.PotenciaMaximaPorBanda.Should().ContainKey("6m").WhoseValue.Should().Be(25);
            vigilante.Seguridad.RoeMaxima.Should().Be(3.0, "se acota");

            var leidos = AjustesDeSeguridadTx.Leer(carpeta);
            leidos.BandasLiberadas.Should().Equal("27 MHz");
            leidos.PotenciaMaximaPorBanda["6m"].Should().Be(25);
            new VistaModeloSeguridadTx(vigilante, carpeta).Filas.Single(f => f.Banda == "27 MHz").Liberada.Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(carpeta)) Directory.Delete(carpeta, true);
        }
    }

    [Fact]
    public async Task Las_fuentes_de_PTT_se_dan_de_alta_en_el_vigilante()
    {
        await using var vigilante = Vigilante();
        var altas = Telegrafia.FuentesDePtt.Conectar(vigilante, null, null, null, null);
        altas.Should().BeEmpty();
        vigilante.FuentesPulsadas().Should().BeEmpty();

        var pulsada = true;
        using var alta = vigilante.RegistrarFuente("Prueba", () => pulsada);
        vigilante.FuentesPulsadas().Should().Equal("Prueba");
        pulsada = false;
        vigilante.FuentesPulsadas().Should().BeEmpty();
    }

    [Fact]
    public Task La_vista_no_tiene_enlaces_rotos_y_deja_su_captura() => HiloDeVentana.Ejecutar(async () =>
    {
        var errores = new StringBuilder();
        using var oyente = new OyenteSeg(errores);
        await using var vigilante = Vigilante();
        var vm = new VistaModeloSeguridadTx(vigilante, null);
        vm.Filas.Single(f => f.Banda == "6m").PotenciaMaxima = "25";
        var vista = new SeguridadTx { DataContext = vm, Margin = new Thickness(8) };
        var borde = new Border { Child = vista, Padding = new Thickness(4) };
        borde.SetResourceReference(Border.BackgroundProperty, "FondoPanel");
        var ventana = new Window
        {
            Content = borde,
            Width = 760,
            SizeToContent = SizeToContent.Height,
            WindowStyle = WindowStyle.None,
            ShowActivated = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -10000,
            Top = 0,
        };
        ventana.SetResourceReference(TextElement.ForegroundProperty, "Texto");
        ventana.Show();
        try
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            errores.ToString().Should().BeEmpty();
            if (Environment.GetEnvironmentVariable("CUADERNO_CAPTURAS_TXCW") is { Length: > 0 } capturas)
            {
                var ancho = (int)Math.Ceiling(borde.ActualWidth);
                var alto = (int)Math.Ceiling(borde.ActualHeight);
                var imagen = new RenderTargetBitmap(ancho, alto, 96, 96, PixelFormats.Pbgra32);
                imagen.Render(borde);
                var png = new PngBitmapEncoder();
                png.Frames.Add(BitmapFrame.Create(imagen));
                Directory.CreateDirectory(capturas);
                using var fichero = File.Create(Path.Combine(capturas, "seguridad-tx.png"));
                png.Save(fichero);
            }
        }
        finally
        {
            ventana.Close();
        }
    });

    private sealed class OyenteSeg : TraceListener
    {
        private readonly StringBuilder _errores;

        public OyenteSeg(StringBuilder errores)
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
