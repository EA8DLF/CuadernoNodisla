using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Impresion.Qsl;
using Nodisla.Cuaderno.Servicios.Correo;
using Nodisla.Cuaderno.Ui.Desarrollo;
using Nodisla.Cuaderno.Ui.Qsl;
using Nodisla.Cuaderno.Ui.VistaModelos;
using Nodisla.Cuaderno.Ui.Vistas;

namespace Nodisla.Cuaderno.Ui.Pruebas;

/// <summary>
/// La tarjeta QSL propia de punta a punta: dibujo, envio por un correo DE MENTIRA, marca en el
/// cuaderno y las dos pantallas pintadas fuera de la vista. No sale ningun correo.
/// </summary>
/// <remarks>
/// Con <c>CUADERNO_CAPTURAS_QSL</c> apuntando a una carpeta, deja alli las capturas del editor,
/// de la ventana de envio y una tarjeta generada, para la ayuda.
/// </remarks>
[Collection(nameof(ColeccionDeLaVentana))]
public sealed class TarjetaQslPruebas : IDisposable
{
    private readonly string _carpeta = Path.Combine(Path.GetTempPath(), "cuaderno-qsl-ui-" + Guid.NewGuid().ToString("N"));

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_carpeta)) Directory.Delete(_carpeta, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public Task La_tarjeta_sale_de_140_por_90_mm_a_la_resolucion_pedida() =>
        HiloDeVentana.Ejecutar(() =>
        {
            var diseno = DisenoDeQsl.PorOmision();
            var v = VariablesDeQsl.Para(Contacto(1, "EA1ABC"), null);

            var imprenta = DibujanteDeQsl.Dibujar(diseno, v, null, 300);
            imprenta.PixelWidth.Should().Be(1654);
            imprenta.PixelHeight.Should().Be(1063);

            diseno.Vertical = true;
            var vertical = DibujanteDeQsl.Dibujar(diseno, v, null, 200);
            vertical.PixelWidth.Should().BeLessThan(vertical.PixelHeight);

            var jpg = DibujanteDeQsl.Codificar(imprenta, FormatoDeImagen.Jpg);
            jpg.Take(2).Should().Equal(0xFF, 0xD8);
            DibujanteDeQsl.Codificar(imprenta, FormatoDeImagen.Png).Take(4).Should().Equal(0x89, 0x50, 0x4E, 0x47);
            return Task.CompletedTask;
        });

    [Fact]
    public Task Enviar_manda_una_tarjeta_por_contacto_y_los_apunta_como_enviados() =>
        HiloDeVentana.Ejecutar(async () =>
        {
            var (servicio, cuaderno, buzon, _) = Montar();
            servicio.Ajustes.Asunto = "QSL {miindicativo} - {indicativo} {fecha}";
            var avisados = new List<long>();
            servicio.QslEnviadas += (_, ids) => avisados.AddRange(ids);

            var qsos = await servicio.TraerAsync([1, 2]);
            await servicio.EnviarAsync("ea1abc@ejemplo.es", qsos, DisenoDeQsl.PorOmision(), await servicio.MiEstacionAsync(), servicio.Ajustes.Asunto, "Tnx {nombre}\n{contactos}");

            var correo = buzon.Enviados.Should().ContainSingle().Which;
            correo.Para.Should().Be("ea1abc@ejemplo.es");
            correo.Asunto.Should().Be("QSL EA8DLF - EA1ABC 2026-09-29");
            correo.Texto.Should().Contain("Tnx Pepe").And.Contain("2026-09-29 14:32 UTC  20m FT8").And.Contain("15:00 UTC  40m SSB");
            correo.Adjuntos.Should().HaveCount(2);
            correo.Adjuntos[0].Nombre.Should().Be("QSL_EA8DLF_EA1ABC_20260929_1432.jpg");
            correo.Adjuntos[0].TipoDeMedio.Should().Be("image/jpeg");

            avisados.Should().BeEquivalentTo([1L, 2L]);
            foreach (var id in new long[] { 1, 2 })
            {
                var guardado = (await cuaderno.ObtenerAsync(id))!;
                var tarjeta = guardado.Confirmaciones.Single(c => c.Medio == MedioDeConfirmacion.Papel);
                tarjeta.Enviado.Should().Be(EstadoDeConfirmacion.Confirmado, "QSL_SENT=Y");
                tarjeta.Via.Should().Be(ViaDeEnvio.Electronico, "QSL_SENT_VIA=E");
                tarjeta.EnviadoUtc.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
            }

            new FilaDeQso((await cuaderno.ObtenerAsync(1))!).Papel.Should().NotBe(EstadoDePastilla.Nada, "la columna QSL del cuaderno lo refleja");
        });

    [Fact]
    public Task Si_el_servidor_falla_no_se_apunta_nada() =>
        HiloDeVentana.Ejecutar(async () =>
        {
            var (servicio, cuaderno, _, _) = Montar(new EnviadorQueFalla(esDelDestinatario: false));
            var qsos = await servicio.TraerAsync([1]);

            var envio = () => servicio.EnviarAsync("ea1abc@ejemplo.es", qsos, DisenoDeQsl.PorOmision(), DatosDeMiEstacion.Vacios, "a", "b");
            await envio.Should().ThrowAsync<ErrorDeCorreo>();

            (await cuaderno.ObtenerAsync(1))!.Confirmaciones.Should().NotContain(c => c.Medio == MedioDeConfirmacion.Papel);
        });

    [Fact]
    public Task La_ventana_de_envio_agrupa_por_estacion_salta_la_que_no_tiene_correo_y_para_si_falla_el_servidor() =>
        HiloDeVentana.Ejecutar(async () =>
        {
            var (servicio, cuaderno, buzon, _) = Montar();
            var envio = new VistaModeloEnvioQsl(servicio, [1, 2, 3]);
            await envio.CargarAsync();

            envio.Destinatarios.Should().HaveCount(2);
            var abc = envio.Destinatarios.Single(d => d.Indicativo == "EA1ABC");
            abc.Contactos.Should().HaveCount(2);
            abc.Origen.Should().Be(OrigenDelCorreo.DelContacto);
            abc.Enviar.Should().BeTrue();
            var sin = envio.Destinatarios.Single(d => d.Indicativo == "DL1XYZ");
            sin.Origen.Should().Be(OrigenDelCorreo.Ninguno);
            sin.Enviar.Should().BeFalse("sin direccion no se marca");
            envio.VistaPrevia.Should().NotBeNull();

            // Marcada a mano y sin direccion: se salta con aviso, las demas salen.
            sin.Enviar = true;
            await envio.EnviarAsync();
            buzon.Enviados.Should().ContainSingle();
            sin.Estado.Should().Contain("sin dirección");
            abc.Enviado.Should().BeTrue();
            envio.Apuntados.Should().BeEquivalentTo([1L, 2L]);

            // Escrita a mano, sale.
            sin.Correo = "dl1xyz@ejemplo.de";
            sin.Origen.Should().Be(OrigenDelCorreo.Escrita);
            await envio.EnviarAsync();
            buzon.Enviados.Should().HaveCount(2);
            buzon.Enviados[1].Para.Should().Be("dl1xyz@ejemplo.de");

            // Con el servidor caido se para al primer fallo en vez de repetirlo con todos.
            var (servicioRoto, _, _, _) = Montar(new EnviadorQueFalla(esDelDestinatario: false));
            var roto = new VistaModeloEnvioQsl(servicioRoto, [1, 3]);
            await roto.CargarAsync();
            foreach (var d in roto.Destinatarios) { d.Correo = "x@ejemplo.es"; d.Enviar = true; }
            await roto.EnviarAsync();
            roto.Aviso.Should().StartWith("Parado");
            roto.Destinatarios.Count(d => d.Estado.Contains("tiempo agotado", StringComparison.Ordinal)).Should().Be(1, "tras el primer fallo del servidor no se intenta con el resto");
        });

    [Fact]
    public Task El_editor_mueve_un_campo_y_guarda_la_plantilla() =>
        HiloDeVentana.Ejecutar(async () =>
        {
            var (servicio, _, _, _) = Montar();
            var editor = new VistaModeloQsl(servicio);
            await editor.RefrescarAsync();

            editor.Diseno!.Id.Should().Be("nodisla");
            editor.Contacto!.Qso.Should().NotBeNull("se previsualiza con un contacto de verdad");
            var indicativo = editor.Campos[0];
            var x = indicativo.XMm;
            indicativo.Mover(10, 0, editor.AnchoMm, editor.AltoMm);
            indicativo.XMm.Should().Be(x + 10);
            editor.HayCambios.Should().BeTrue();

            editor.NombreDelDiseno = "La mía";
            editor.Guardar();
            editor.PonerPorOmision();
            editor.HayCambios.Should().BeFalse();

            var otra = new VistaModeloQsl(servicio);
            otra.Diseno!.Nombre.Should().Be("La mía");
            otra.Campos[0].XMm.Should().Be(x + 10);
        });

    [Fact]
    public Task Editor_y_ventana_de_envio_se_pintan_sin_enlaces_rotos() =>
        HiloDeVentana.Ejecutar(async () =>
        {
            var errores = new StringBuilder();
            using var oyente = new OyenteDeEnlacesDeQsl(errores);
            var (servicio, _, _, _) = Montar();

            var capturas = Environment.GetEnvironmentVariable("CUADERNO_CAPTURAS_QSL");
            var editor = new VistaModeloQsl(servicio);
            if (capturas is { Length: > 0 })
            {
                // Para la ayuda: un fondo de foto (cielo y el Teide), el aspecto que tendra con la del operador.
                Directory.CreateDirectory(_carpeta);
                var foto = Path.Combine(_carpeta, "teide.png");
                File.WriteAllBytes(foto, FotoDeEjemplo());
                editor.ElegirImagen = () => foto;
                editor.ElegirFondo();
                editor.ColorDeFondo = "#1B3A5C";
            }

            var panel = new PanelDeQsl { DataContext = editor };
            var ventana = Ventana(panel, 1600, 760);
            try
            {
                await editor.RefrescarAsync();
                editor.CampoElegido = editor.Campos[0];
                await Asentar();
                errores.ToString().Should().BeEmpty("cada enlace roto es un control muerto");
                if (capturas is { Length: > 0 })
                {
                    Retratar(panel, Path.Combine(capturas, "qsl-editor.png"));
                    File.WriteAllBytes(
                        Path.Combine(capturas, "qsl-tarjeta-generada.jpg"),
                        DibujanteDeQsl.Codificar(servicio.Dibujar(editor.Diseno!, editor.Contacto!.Qso, await servicio.MiEstacionAsync(), DibujanteDeQsl.PppDeCorreo), FormatoDeImagen.Jpg));
                    editor.Guardar();
                }
            }
            finally
            {
                ventana.Close();
            }

            var envio = new VistaModeloEnvioQsl(servicio, [1, 2, 3]);
            var dialogo = new VentanaDeEnvioDeQsl
            {
                DataContext = envio,
                ShowActivated = false,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -8000,
                Top = 0,
            };
            dialogo.Show();
            try
            {
                await envio.CargarAsync();
                await Asentar();
                errores.ToString().Should().BeEmpty("cada enlace roto es un control muerto");
                if (capturas is { Length: > 0 }) Retratar((FrameworkElement)dialogo.Content, Path.Combine(capturas, "qsl-enviar.png"));
            }
            finally
            {
                dialogo.Close();
            }
        });

    // ── Montaje ───────────────────────────────────────────────────────────

    private (ServicioDeQsl Servicio, RepositorioQsoEnMemoria Cuaderno, BuzonDeSalidaEnDisco Buzon, AlmacenDeMentira Credenciales) Montar(IEnviadorDeCorreo? enviador = null)
    {
        var abc1 = Contacto(1, "EA1ABC");
        abc1.Email = "ea1abc@ejemplo.es";
        abc1.Name = "Pepe";
        var abc2 = Contacto(2, "EA1ABC");
        abc2.InicioUtc = new DateTimeOffset(2026, 9, 29, 15, 0, 0, TimeSpan.Zero);
        abc2.Band = Banda.Parse("40m");
        abc2.Freq = Frecuencia.DesdeMegahercios(7.150m);
        abc2.Mode = Modo.Parse("SSB");
        abc2.RstSent = Informe.Parse("59");
        var dl = Contacto(3, "DL1XYZ");

        var cuaderno = new RepositorioQsoEnMemoria([abc1, abc2, dl]);
        var buzon = new BuzonDeSalidaEnDisco(Path.Combine(_carpeta, "enviados"));
        var credenciales = new AlmacenDeMentira();
        var servicio = new ServicioDeQsl(_carpeta, enviador ?? buzon, credenciales, cuaderno, new RepositorioEstacionEnMemoria())
        {
            EstacionActiva = () => 1,
        };
        return (servicio, cuaderno, buzon, credenciales);
    }

    private static Qso Contacto(long id, string indicativo) => new()
    {
        Id = id,
        Call = Indicativo.Parse(indicativo),
        StationCallsign = Indicativo.Parse("EA8DLF"),
        MyGridsquare = Locator.Parse("IL18SN"),
        InicioUtc = new DateTimeOffset(2026, 9, 29, 14, 32, 0, TimeSpan.Zero),
        Freq = Frecuencia.DesdeMegahercios(14.074m),
        Band = Banda.Parse("20m"),
        Mode = Modo.Parse("FT8"),
        RstSent = Informe.Parse("-10"),
    };

    private static Window Ventana(FrameworkElement contenido, double ancho, double alto)
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
            Left = -8000,
            Top = 0,
            Background = (Brush)Application.Current.Resources["FondoVentana"],
        };
        ventana.Show();
        ventana.UpdateLayout();
        return ventana;
    }

    private static async Task Asentar()
    {
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
    }

    private static void Retratar(FrameworkElement contenido, string ruta)
    {
        var fondo = (Brush)Application.Current.Resources["FondoVentana"];
        var dibujo = new DrawingVisual();
        using (var lienzo = dibujo.RenderOpen())
        {
            lienzo.DrawRectangle(fondo, null, new Rect(0, 0, contenido.ActualWidth, contenido.ActualHeight));
            lienzo.DrawRectangle(new VisualBrush(contenido), null, new Rect(0, 0, contenido.ActualWidth, contenido.ActualHeight));
        }

        var imagen = new RenderTargetBitmap((int)Math.Ceiling(contenido.ActualWidth), (int)Math.Ceiling(contenido.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        imagen.Render(dibujo);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(imagen));
        Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);
        using var fichero = File.Create(ruta);
        png.Save(fichero);
    }

    private static byte[] FotoDeEjemplo()
    {
        var dibujo = new DrawingVisual();
        using (var dc = dibujo.RenderOpen())
        {
            dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(0x1B, 0x3A, 0x5C), Color.FromRgb(0xF2, 0x9E, 0x4C), 90), null, new Rect(0, 0, 1400, 900));
            var teide = new StreamGeometry();
            using (var g = teide.Open())
            {
                g.BeginFigure(new Point(0, 820), true, true);
                g.LineTo(new Point(430, 640), true, false);
                g.LineTo(new Point(640, 430), true, false);
                g.LineTo(new Point(700, 420), true, false);
                g.LineTo(new Point(900, 610), true, false);
                g.LineTo(new Point(1400, 780), true, false);
                g.LineTo(new Point(1400, 900), true, false);
                g.LineTo(new Point(0, 900), true, false);
            }

            dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(0x2B, 0x22, 0x33)), null, teide);
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(0x90, 0x0E, 0x4D, 0x7A)), null, new Rect(0, 860, 1400, 40));
        }

        var mapa = new RenderTargetBitmap(1400, 900, 96, 96, PixelFormats.Pbgra32);
        mapa.Render(dibujo);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(mapa));
        using var salida = new MemoryStream();
        png.Save(salida);
        return salida.ToArray();
    }

    private sealed class EnviadorQueFalla(bool esDelDestinatario) : IEnviadorDeCorreo
    {
        public Task EnviarAsync(MensajeDeCorreo mensaje, ConfiguracionSmtp configuracion, string? contrasena, CancellationToken ct = default) =>
            throw new ErrorDeCorreo("El servidor de correo no contesta (tiempo agotado).") { EsDelDestinatario = esDelDestinatario };

        public Task ProbarAsync(ConfiguracionSmtp configuracion, string? contrasena, CancellationToken ct = default) => Task.CompletedTask;
    }

    /// <summary>Recoge los errores de enlace de WPF mientras vive.</summary>
    private sealed class OyenteDeEnlacesDeQsl : TraceListener
    {
        private readonly StringBuilder _errores;

        public OyenteDeEnlacesDeQsl(StringBuilder errores)
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
