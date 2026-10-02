using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
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
using Nodisla.Cuaderno.Servidores;
using Nodisla.Cuaderno.Ui.VistaModelos;
using Nodisla.Cuaderno.Ui.Vistas;

namespace Nodisla.Cuaderno.Ui.Pruebas;

/// <summary>
/// Configuracion › «Servidor para otros programas»: abrir a la red y dejar transmitir piden
/// confirmacion, se guarda y se aplica, los clientes salen en vivo y la vista no tiene enlaces
/// rotos. Solo en 127.0.0.1 y con el equipo vacio: nada sale al aire. Con
/// <c>CUADERNO_CAPTURAS_SERVIDOR</c> deja su captura.
/// </summary>
[Collection(nameof(ColeccionDeLaVentana))]
public sealed class ServidoresExternosPruebas
{
    private static (VigilantePtt Vigilante, ServidoresParaOtrosProgramas Servidores) Montar()
    {
        var control = new ControlEquipoConmutable();
        var vigilante = new VigilantePtt(control, new OpcionesDelVigilante { EngancharseAlCierreDelProceso = false });
        var radio = new RadioCompartida(control, vigilante, () => false);
        return (vigilante, new ServidoresParaOtrosProgramas(radio));
    }

    private static int PuertoLibre()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var puerto = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return puerto;
    }

    [Fact]
    public async Task Abrir_la_red_y_dejar_transmitir_piden_confirmacion_y_se_guarda_y_aplica()
    {
        var carpeta = Path.Combine(Path.GetTempPath(), "cuaderno-servidor-" + Guid.NewGuid().ToString("N"));
        var (vigilante, servidores) = Montar();
        await using (vigilante)
        await using (servidores)
        {
            try
            {
                var vm = new VistaModeloServidores(servidores, carpeta);
                vm.RigctldActivo.Should().BeFalse("de fabrica todo apagado");
                vm.TciActivo.Should().BeFalse();
                vm.PermitirTx.Should().BeFalse();
                vm.PuertoRigctld.Should().Be("4532");
                vm.PuertoTci.Should().Be("40001");

                // Sin pregunta, o diciendo que no, no se abre ni se deja transmitir.
                vm.AbrirALaRedLocal = true;
                vm.AbrirALaRedLocal.Should().BeFalse();
                vm.Confirmar = (_, _) => false;
                vm.PermitirTx = true;
                vm.PermitirTx.Should().BeFalse();
                string? preguntado = null;
                vm.Confirmar = (titulo, _) => { preguntado = titulo; return true; };
                vm.PermitirTx = true;
                vm.PermitirTx.Should().BeTrue();
                preguntado.Should().NotBeNullOrEmpty();

                // Puerto malo: no se guarda.
                vm.PuertoRigctld = "80";
                await vm.GuardarAsync();
                File.Exists(Path.Combine(carpeta, OpcionesDeServidores.Fichero)).Should().BeFalse();

                var puerto = PuertoLibre();
                vm.PuertoRigctld = puerto.ToString(System.Globalization.CultureInfo.InvariantCulture);
                vm.RigctldActivo = true;
                await vm.GuardarAsync();
                servidores.Rigctld.Encendido.Should().BeTrue();
                servidores.Rigctld.Punto!.Address.Should().Be(IPAddress.Loopback);
                vm.HayAlgunoActivo.Should().BeTrue();
                vm.EstadoRigctld.Should().Contain("127.0.0.1");

                var leidas = OpcionesDeServidores.Leer(carpeta);
                leidas.RigctldActivo.Should().BeTrue();
                leidas.PermitirTx.Should().BeTrue();
                leidas.PuertoRigctld.Should().Be(puerto);

                // Un cliente en vivo y desconectarlo desde la pantalla.
                using var cliente = new TcpClient();
                await cliente.ConnectAsync(IPAddress.Loopback, puerto);
                await cliente.GetStream().WriteAsync(Encoding.ASCII.GetBytes("t\n"));
                await EsperarAsync(() => servidores.Radio.Clientes.Count == 1);
                vm.Refrescar();
                vm.Clientes.Should().ContainSingle().Which.Protocolo.Should().Be("rigctld");
                vm.IndicadorTexto.Should().Contain("1");
                await vm.DesconectarAsync(vm.Clientes[0]);
                vm.Clientes.Should().BeEmpty();
            }
            finally
            {
                if (Directory.Exists(carpeta)) Directory.Delete(carpeta, true);
            }
        }
    }

    [Fact]
    public Task La_vista_no_tiene_enlaces_rotos_y_deja_su_captura() => HiloDeVentana.Ejecutar(async () =>
    {
        var errores = new StringBuilder();
        using var oyente = new OyenteSrv(errores);
        var (vigilante, servidores) = Montar();
        await using (vigilante)
        await using (servidores)
        {
            var puerto = PuertoLibre();
            await servidores.AplicarAsync(new OpcionesDeServidores { RigctldActivo = true, PuertoRigctld = puerto, PermitirTx = false });
            var vm = new VistaModeloServidores(servidores, null) { RigctldActivo = true, PuertoRigctld = puerto.ToString(System.Globalization.CultureInfo.InvariantCulture) };

            // Un programa conectado que pide PTT con la TX externa apagada: queda en el registro.
            using var cliente = new TcpClient();
            await cliente.ConnectAsync(IPAddress.Loopback, puerto);
            var flujo = cliente.GetStream();
            await flujo.WriteAsync(Encoding.ASCII.GetBytes("T 1\n"));
            var bufer = new byte[64];
            _ = await flujo.ReadAsync(bufer);
            vm.Refrescar();
            vm.Clientes.Should().ContainSingle();
            vm.Peticiones.Should().ContainSingle();

            var vista = new ServidoresExternos { DataContext = vm, Margin = new Thickness(8) };
            var borde = new Border { Child = vista, Padding = new Thickness(4) };
            borde.SetResourceReference(Border.BackgroundProperty, "FondoPanel");
            var ventana = new Window
            {
                Content = borde,
                Width = 820,
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
                if (Environment.GetEnvironmentVariable("CUADERNO_CAPTURAS_SERVIDOR") is { Length: > 0 } capturas)
                {
                    var imagen = new RenderTargetBitmap((int)Math.Ceiling(borde.ActualWidth), (int)Math.Ceiling(borde.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                    imagen.Render(borde);
                    var png = new PngBitmapEncoder();
                    png.Frames.Add(BitmapFrame.Create(imagen));
                    Directory.CreateDirectory(capturas);
                    using var fichero = File.Create(Path.Combine(capturas, "servidor-otros-programas.png"));
                    png.Save(fichero);
                }
            }
            finally
            {
                ventana.Close();
            }
        }
    });

    private static async Task EsperarAsync(Func<bool> condicion)
    {
        var tope = DateTime.UtcNow.AddSeconds(3);
        while (!condicion() && DateTime.UtcNow < tope) await Task.Delay(10);
        condicion().Should().BeTrue();
    }

    private sealed class OyenteSrv : TraceListener
    {
        private readonly StringBuilder _errores;

        public OyenteSrv(StringBuilder errores)
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
