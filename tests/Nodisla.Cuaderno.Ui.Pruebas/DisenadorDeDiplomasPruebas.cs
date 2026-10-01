using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Impresion.Diplomas;
using Nodisla.Cuaderno.Servicios.Correo;
using Nodisla.Cuaderno.Ui.Desarrollo;
using Nodisla.Cuaderno.Ui.Qsl;
using Nodisla.Cuaderno.Ui.VistaModelos;
using Nodisla.Cuaderno.Ui.Vistas.Qsl;
using PdfSharp.Pdf.IO;

namespace Nodisla.Cuaderno.Ui.Pruebas;

/// <summary>
/// El diseñador de diplomas de punta a punta: dibujo, numeracion correlativa, PDF, correo DE
/// MENTIRA (buzon en disco), historial y la pantalla pintada fuera de la vista. Todos los datos
/// son ficticios (EA8ZZZ, EA1TST…). No sale ningun correo.
/// </summary>
/// <remarks>
/// Con <c>CUADERNO_CAPTURAS_DIPLOMAS</c> apuntando a una carpeta deja alli las capturas
/// <c>diplomas-*.png</c> para la ayuda.
/// </remarks>
[Collection(nameof(ColeccionDeLaVentana))]
public sealed class DisenadorDeDiplomasPruebas : IDisposable
{
    private readonly string _carpeta = Path.Combine(Path.GetTempPath(), "cuaderno-diplomas-ui-" + Guid.NewGuid().ToString("N"));

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
    public Task El_diploma_sale_del_tamano_del_papel_y_la_tabla_larga_va_a_anexos() =>
        HiloDeVentana.Ejecutar(() =>
        {
            var diseno = PlantillasDeDiplomaDeFabrica.Clasico();
            var datos = DatosFicticios(3);
            var v = VariablesDeDiploma.Para(datos, diseno, null);

            var a4 = DibujanteDeDiplomas.Dibujar(diseno, v, datos.Filas, _ => null, 200);
            a4.PixelWidth.Should().Be(2339, "297 mm a 200 ppp");
            a4.PixelHeight.Should().Be(1654, "210 mm a 200 ppp");

            diseno.CambiarFormato(PapelDeDiploma.Carta, vertical: true);
            var carta = DibujanteDeDiplomas.Dibujar(diseno, v, datos.Filas, _ => null, 100);
            carta.PixelWidth.Should().Be(850);
            carta.PixelHeight.Should().Be(1100);

            var sobrio = PlantillasDeDiplomaDeFabrica.Sobrio();
            var caben = DibujanteDeDiplomas.Capacidad(sobrio.Tabla, sobrio.Tabla.AltoMm, sobrio.Tabla.Bloques);
            var muchas = DatosFicticios(caben * 3);
            var hojas = DibujanteDeDiplomas.Hojas(sobrio, VariablesDeDiploma.Para(muchas, sobrio, null), muchas.Filas, _ => null, 72);
            hojas.Count.Should().BeGreaterThan(1, "lo que no cabe va a hojas de anexo");
            hojas.SelectMany(h => h.Textos).Select(t => t.Texto).Should()
                .Contain(t => t.Contains("REF-001", StringComparison.Ordinal))
                .And.Contain(t => t.Contains($"REF-{caben * 3:000}", StringComparison.Ordinal), "la ultima referencia sale en un anexo");

            sobrio.Tabla.Anexo = false;
            DibujanteDeDiplomas.Hojas(sobrio, VariablesDeDiploma.Para(muchas, sobrio, null), muchas.Filas, _ => null, 72)
                .Should().ContainSingle("sin anexo, una hoja y «… y N más»")
                .Which.Textos.Should().Contain(t => t.Texto.Contains("más", StringComparison.Ordinal));
            return Task.CompletedTask;
        });

    [Fact]
    public Task Emitir_numera_correlativo_por_serie_y_guarda_lo_impreso_en_el_historial() =>
        HiloDeVentana.Ejecutar(async () =>
        {
            var (servicio, _, _, _) = await MontarAsync();
            var diseno = PlantillasDeDiplomaDeFabrica.Clasico();
            diseno.Serie = "CLUB";

            (await servicio.SiguienteNumeroAsync("CLUB")).Should().Be(1);
            var primero = await servicio.EmitirAsync(diseno, DatosFicticios(2), OrigenDeDiplomaEmitido.Emitido);
            var segundoDatos = DatosFicticios(5);
            segundoDatos.Indicativo = "ea2tst";
            var segundo = await servicio.EmitirAsync(diseno, segundoDatos, OrigenDeDiplomaEmitido.Emitido);
            var otraSerie = PlantillasDeDiplomaDeFabrica.Sobrio();
            otraSerie.Serie = "PROPIOS";
            var propio = await servicio.EmitirAsync(otraSerie, DatosFicticios(1), OrigenDeDiplomaEmitido.Conseguido);
            var tercero = await servicio.EmitirAsync(diseno, DatosFicticios(1), OrigenDeDiplomaEmitido.Emitido);

            primero.Numero.Should().Be(1);
            segundo.Numero.Should().Be(2);
            tercero.Numero.Should().Be(3, "la numeracion sigue en su serie");
            propio.Numero.Should().Be(1, "cada serie numera por su cuenta");
            segundoDatos.Numero.Should().Be(2, "los datos quedan con su numero");
            segundo.Indicativo.Should().Be("EA2TST");
            segundo.Qsos.Should().Be(5);

            var historial = await servicio.HistorialAsync();
            historial.Select(h => h.Numero).Should().Equal(3, 1, 2, 1);
            var reimpreso = ServicioDeDiplomas.DatosDe(historial.Single(h => h.Id == segundo.Id))!;
            reimpreso.Numero.Should().Be(2);
            reimpreso.Filas.Should().HaveCount(5);
            reimpreso.Indicativo.Should().Be("ea2tst");

            var sinIndicativo = DatosFicticios(1);
            sinIndicativo.Indicativo = " ";
            var emitir = () => servicio.EmitirAsync(diseno, sinIndicativo, OrigenDeDiplomaEmitido.Emitido);
            await emitir.Should().ThrowAsync<ArgumentException>();
            (await servicio.SiguienteNumeroAsync("CLUB")).Should().Be(4, "un intento fallido no gasta numero");
        });

    [Fact]
    public Task El_PDF_tiene_sus_paginas_del_tamano_del_papel_y_el_texto_presente() =>
        HiloDeVentana.Ejecutar(async () =>
        {
            var (servicio, _, _, _) = await MontarAsync();
            var diseno = PlantillasDeDiplomaDeFabrica.Sobrio();
            diseno.PrefijoDeNumero = "EA8-";
            var datos = DatosFicticios(3);
            await servicio.EmitirAsync(diseno, datos, OrigenDeDiplomaEmitido.Emitido);
            var yo = await servicio.Qsl.MiEstacionAsync();

            var pdf = servicio.Pdf(diseno, datos, yo);

            pdf.Paginas.Should().Be(1);
            pdf.NombreSugerido.Should().Be("Diploma_NODISLA_EA8-0001_EA1TST.pdf");
            using var leido = PdfReader.Open(new MemoryStream(pdf.Bytes), PdfDocumentOpenMode.Import);
            leido.Pages[0].Width.Millimeter.Should().BeApproximately(210, 0.1);
            leido.Pages[0].Height.Millimeter.Should().BeApproximately(297, 0.1);
            leido.Info.Title.Should().Be("Diploma de las Islas Ficticias nº EA8-0001 — EA1TST");
            var texto = TextoDe(pdf.Bytes);
            texto.Should().Contain("EA1TST").And.Contain("Pepa Prueba").And.Contain("EA8-0001").And.Contain("REF-002").And.Contain("EA8ZZZ");

            var png = servicio.Png(diseno, datos, yo);
            png.Take(4).Should().Equal(0x89, 0x50, 0x4E, 0x47);

            // Una tabla que no cabe saca hojas de anexo en el PDF.
            var muchas = DatosFicticios(DibujanteDeDiplomas.Capacidad(diseno.Tabla, diseno.Tabla.AltoMm, diseno.Tabla.Bloques) + 10);
            servicio.Pdf(diseno, muchas, yo).Paginas.Should().Be(2);
        });

    [Fact]
    public Task Enviar_manda_el_PDF_por_el_correo_de_las_QSL_y_lo_apunta() =>
        HiloDeVentana.Ejecutar(async () =>
        {
            var (servicio, buzon, _, _) = await MontarAsync();
            var diseno = PlantillasDeDiplomaDeFabrica.Clasico();
            var datos = DatosFicticios(2);
            var emitido = await servicio.EmitirAsync(diseno, datos, OrigenDeDiplomaEmitido.Emitido);

            await servicio.EnviarAsync(diseno, datos, emitido, "ea1tst@ejemplo.es");

            var correo = buzon.Enviados.Should().ContainSingle().Which;
            correo.Para.Should().Be("ea1tst@ejemplo.es");
            correo.Asunto.Should().Be("Diploma de las Islas Ficticias nº 0001 - EA1TST");
            correo.Texto.Should().Contain("Pepa Prueba").And.Contain("EA8ZZZ");
            correo.Adjuntos.Should().ContainSingle().Which.TipoDeMedio.Should().Be("application/pdf");
            var apunte = (await servicio.HistorialAsync()).Single();
            apunte.Correo.Should().Be("ea1tst@ejemplo.es");
            apunte.EnviadoUtc.Should().NotBeNull();

            var malo = () => servicio.EnviarAsync(diseno, datos, emitido, "no-es-un-correo");
            await malo.Should().ThrowAsync<ErrorDeCorreo>();
        });

    [Fact]
    public Task Desde_el_cuaderno_y_desde_el_modulo_de_diplomas_se_rellena_solo() =>
        HiloDeVentana.Ejecutar(async () =>
        {
            var (servicio, _, _, _) = await MontarAsync();

            var qsos = await servicio.ContactosConAsync("ea1tst");
            qsos.Should().HaveCount(2);
            var datos = ServicioDeDiplomas.DesdeContactos("ea1tst", qsos, PlantillasDeDiplomaDeFabrica.Clasico());
            datos.Indicativo.Should().Be("EA1TST");
            datos.Nombre.Should().Be("Pepa Prueba");
            datos.Correo.Should().Be("ea1tst@ejemplo.es");
            datos.NombreDelDiploma.Should().Be("Diploma NODISLA");
            datos.Filas.Select(f => f.Banda).Should().Equal("20m", "40m");
            datos.Filas[0].Referencia.Should().Be("ISL-07", "la referencia del corresponsal");

            var conseguidos = await servicio.ConseguidosAsync();
            var dme = conseguidos.Should().ContainSingle().Which;
            dme.Completo.Should().BeTrue();
            var yo = await servicio.Qsl.MiEstacionAsync();
            var certificado = await servicio.DesdeDiplomaAsync(dme, yo);
            certificado.Indicativo.Should().Be("EA8ZZZ");
            certificado.NombreDelDiploma.Should().Be("Diploma Ficticio de Islas");
            certificado.Categoria.Should().Be("Mixto");
            certificado.Entidad.Should().Be("Radioclub Ficticio");
            certificado.Referencias.Should().Be(2);
            certificado.Filas.Select(f => f.Referencia).Should().Equal("ISL-07", "ISL-09");
            certificado.Filas[0].Indicativo.Should().Be("EA1TST", "sale del primer contacto de la referencia");
        });

    [Fact]
    public Task El_disenador_busca_emite_guarda_el_PDF_y_la_plantilla() =>
        HiloDeVentana.Ejecutar(async () =>
        {
            var (servicio, _, _, _) = await MontarAsync();
            var abiertos = new List<string>();
            var vm = new VistaModeloDisenadorDeDiplomas(servicio)
            {
                AbrirFichero = abiertos.Add,
                ElegirDondeGuardar = (nombre, _) => Path.Combine(_carpeta, nombre),
            };
            await vm.RefrescarAsync();

            vm.Diseno!.Id.Should().Be("nodisla-clasico");
            vm.TextoDelNumero.Should().Contain("0001");
            vm.Indicativo = "ea1tst";
            await vm.BuscarContactosAsync();
            vm.Contactos.Should().HaveCount(2);
            vm.Nombre.Should().Be("Pepa Prueba");
            vm.Correo.Should().Be("ea1tst@ejemplo.es");
            vm.Contactos[1].Elegido = false;
            vm.RedibujarYa();
            vm.VistaPrevia.Should().NotBeNull();
            vm.Datos.Filas.Should().ContainSingle("el contacto desmarcado no cuenta");

            await vm.EmitirYGuardarPdfAsync();
            vm.Historial.Should().ContainSingle().Which.Emitido.Numero.Should().Be(1);
            abiertos.Should().ContainSingle().Which.Should().EndWith("Diploma_NODISLA_0001_EA1TST.pdf");
            File.Exists(abiertos[0]).Should().BeTrue();
            vm.TextoDelNumero.Should().Contain("Emitido con el nº 0001");

            await vm.EmitirYGuardarPdfAsync();
            vm.Historial.Select(h => h.Emitido.Numero).Should().Equal(2, 1);

            // Mover un texto y guardar: la plantilla de fabrica queda guardada con el cambio.
            var indicativo = vm.Campos.Single(c => c.Nombre == "Indicativo");
            var x = indicativo.XMm;
            indicativo.Mover(10, 0, vm.AnchoMm, vm.AltoMm);
            vm.HayCambios.Should().BeTrue();
            vm.Guardar();
            new VistaModeloDisenadorDeDiplomas(servicio).Campos.Single(c => c.Nombre == "Indicativo").XMm.Should().Be(x + 10);

            // Duplicar y exportar/importar desde la pantalla.
            vm.DuplicarPlantilla();
            vm.Diseno!.Nombre.Should().EndWith("(copia)");
            vm.Guardar();
            vm.ExportarPlantilla();
            var exportada = Directory.GetFiles(_carpeta, "*" + AlmacenDeDisenosDeDiploma.ExtensionExportada).Should().ContainSingle().Which;
            vm.ElegirFichero = _ => exportada;
            var antes = vm.Disenos.Count;
            vm.ImportarPlantilla();
            vm.Disenos.Should().HaveCount(antes + 1);
        });

    [Fact]
    public Task El_disenador_se_pinta_sin_enlaces_rotos() =>
        HiloDeVentana.Ejecutar(async () =>
        {
            var errores = new StringBuilder();
            using var oyente = new OyenteDeEnlaces(errores);
            var (servicio, _, _, _) = await MontarAsync();
            var capturas = Environment.GetEnvironmentVariable("CUADERNO_CAPTURAS_DIPLOMAS");

            var vm = new VistaModeloDisenadorDeDiplomas(servicio) { AbrirFichero = _ => { }, ElegirDondeGuardar = (n, _) => Path.Combine(_carpeta, n) };
            var logo = Path.Combine(_carpeta, "logo.png");
            var firma = Path.Combine(_carpeta, "firma.png");
            Directory.CreateDirectory(_carpeta);
            File.WriteAllBytes(logo, LogoFicticio());
            File.WriteAllBytes(firma, FirmaFicticia());

            var panel = new DisenadorDeDiplomas { DataContext = vm };
            var ventana = Ventana(panel, 1600, 900);
            try
            {
                await vm.RefrescarAsync();
                vm.ElementoElegido = vm.Imagenes.Single(i => i.Uso == UsoDeImagen.Logo);
                vm.ElegirImagen = () => logo;
                vm.ElegirImagenDelElemento();
                vm.ElementoElegido = vm.Imagenes.Single(i => i.Uso == UsoDeImagen.Firma);
                vm.ElegirImagen = () => firma;
                vm.ElegirImagenDelElemento();
                vm.NombreDelGestor = "Ana Gestora";
                vm.Indicativo = "EA1TST";
                await vm.BuscarContactosAsync();
                vm.NombreDelDiploma = "Diploma de las Islas Ficticias";
                vm.Categoria = "Oro";
                vm.ElementoElegido = vm.Campos.Single(c => c.Nombre == "Indicativo");
                vm.RedibujarYa();
                await Asentar();
                errores.ToString().Should().BeEmpty("cada enlace roto es un control muerto");
                if (capturas is { Length: > 0 }) Retratar(panel, Path.Combine(capturas, "diplomas-disenador.png"));

                await vm.EmitirYGuardarPdfAsync();
                if (capturas is { Length: > 0 })
                {
                    File.WriteAllBytes(Path.Combine(capturas, "diplomas-diploma-emitido.png"), servicio.Png(vm.Diseno!, vm.Datos, await servicio.Qsl.MiEstacionAsync()));
                }

                // El certificado de un diploma conseguido, con la plantilla vertical y su tabla.
                vm.Diseno = vm.Disenos.Single(d => d.Id == "nodisla-sobrio");
                vm.EsCertificadoPropio = true;
                vm.Conseguido = vm.Conseguidos.Single();
                await vm.CargarConseguidoAsync();
                vm.ElementoElegido = null;
                vm.RedibujarYa();
                await Asentar();
                errores.ToString().Should().BeEmpty("cada enlace roto es un control muerto");
                vm.Datos.Filas.Should().HaveCount(2);
                if (capturas is { Length: > 0 }) Retratar(panel, Path.Combine(capturas, "diplomas-certificado.png"));
            }
            finally
            {
                ventana.Close();
            }
        });

    // ── Montaje ───────────────────────────────────────────────────────────

    private async Task<(ServicioDeDiplomas Servicio, BuzonDeSalidaEnDisco Buzon, RepositorioQsoEnMemoria Cuaderno, RepositorioDiplomasEmitidosEnMemoria Emitidos)> MontarAsync()
    {
        var a = Contacto(1, "EA1TST", new DateTimeOffset(2026, 3, 1, 10, 0, 0, TimeSpan.Zero), "20m", 14.2m, "SSB");
        a.Name = "Pepa Prueba";
        a.Email = "ea1tst@ejemplo.es";
        a.Referencias.Add(new QsoReferencia { Codigo = "ISL-07", Lado = LadoDeReferencia.Corresponsal, Descripcion = "Isla Siete" });
        var b = Contacto(2, "EA1TST", new DateTimeOffset(2026, 4, 2, 11, 0, 0, TimeSpan.Zero), "40m", 7.074m, "FT8");
        var c = Contacto(3, "EA9TST", new DateTimeOffset(2026, 5, 3, 12, 0, 0, TimeSpan.Zero), "20m", 14.074m, "FT8");

        Directory.CreateDirectory(_carpeta);
        var cuaderno = new RepositorioQsoEnMemoria([a, b, c]);
        var estaciones = new RepositorioEstacionEnMemoria(conPerfilesDeEjemplo: false);
        var id = await estaciones.AnadirAsync(new Estacion
        {
            NombrePerfil = "Ficticio",
            StationCallsign = Indicativo.Parse("EA8ZZZ"),
            MyName = "Operador Ficticio",
            MyCity = "Isla Ficticia",
            MyGridsquare = Locator.Parse("IL18AA"),
            Predeterminado = true,
        });
        var buzon = new BuzonDeSalidaEnDisco(Path.Combine(_carpeta, "enviados"));
        var qsl = new ServicioDeQsl(_carpeta, buzon, new AlmacenDeMentira(), cuaderno, estaciones) { EstacionActiva = () => id };
        var emitidos = new RepositorioDiplomasEmitidosEnMemoria();
        var servicio = new ServicioDeDiplomas(_carpeta, qsl, emitidos, cuaderno, new DiplomasFicticios());
        return (servicio, buzon, cuaderno, emitidos);
    }

    private static Qso Contacto(long id, string indicativo, DateTimeOffset cuando, string banda, decimal mhz, string modo) => new()
    {
        Id = id,
        Call = Indicativo.Parse(indicativo),
        StationCallsign = Indicativo.Parse("EA8ZZZ"),
        InicioUtc = cuando,
        Freq = Frecuencia.DesdeMegahercios(mhz),
        Band = Banda.Parse(banda),
        Mode = Modo.Parse(modo),
        RstSent = Informe.Parse(modo == "SSB" ? "59" : "-10"),
    };

    private static DatosDeDiploma DatosFicticios(int filas) => new()
    {
        Indicativo = "ea1tst",
        Nombre = "Pepa Prueba",
        NombreDelDiploma = "Diploma de las Islas Ficticias",
        Categoria = "Oro",
        Fecha = new DateTimeOffset(2026, 9, 30, 10, 0, 0, TimeSpan.Zero),
        Filas = Enumerable.Range(1, filas).Select(i => new FilaDeJustificante
        {
            Referencia = $"REF-{i:000}",
            NombreDeReferencia = $"Isla {i}",
            Indicativo = $"EA8T{(char)('A' + (i % 26))}",
            FechaUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddDays(i),
            Banda = i % 2 == 0 ? "20m" : "40m",
            Modo = i % 3 == 0 ? "CW" : "SSB",
        }).ToList(),
    };

    private static string TextoDe(byte[] pdf)
    {
        var bruto = Encoding.Latin1.GetString(pdf);
        var salida = new StringBuilder();
        foreach (Match m in Regex.Matches(bruto, @"\((?<t>(?:\\.|[^\\)])*)\)\s*Tj|<(?<h>[0-9A-Fa-f]+)>\s*Tj"))
        {
            salida.Append(m.Groups["t"].Success ? m.Groups["t"].Value : Encoding.Latin1.GetString(Convert.FromHexString(m.Groups["h"].Value))).Append('\n');
        }

        return salida.ToString();
    }

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

    /// <summary>Un emblema inventado: circulo azul con una estrella. No es el logo de nadie.</summary>
    private static byte[] LogoFicticio() => Png(400, 400, dc =>
    {
        dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0x1B, 0x3A, 0x5C)), new Pen(new SolidColorBrush(Color.FromRgb(0xB0, 0x8D, 0x57)), 18), new Point(200, 200), 180, 180);
        var estrella = new StreamGeometry();
        using (var g = estrella.Open())
        {
            for (var i = 0; i < 10; i++)
            {
                var r = i % 2 == 0 ? 120 : 50;
                var a = (Math.PI / 5 * i) - (Math.PI / 2);
                var p = new Point(200 + (r * Math.Cos(a)), 200 + (r * Math.Sin(a)));
                if (i == 0) g.BeginFigure(p, true, true);
                else g.LineTo(p, true, false);
            }
        }

        dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(0xB0, 0x8D, 0x57)), null, estrella);
    });

    /// <summary>Una rubrica inventada.</summary>
    private static byte[] FirmaFicticia() => Png(600, 200, dc =>
    {
        var trazo = new StreamGeometry();
        using (var g = trazo.Open())
        {
            g.BeginFigure(new Point(20, 140), false, false);
            g.BezierTo(new Point(120, 10), new Point(160, 190), new Point(240, 90), true, true);
            g.BezierTo(new Point(300, 20), new Point(330, 170), new Point(400, 110), true, true);
            g.BezierTo(new Point(450, 70), new Point(520, 150), new Point(580, 60), true, true);
        }

        dc.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromRgb(0x22, 0x2A, 0x44)), 7) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, trazo);
    });

    private static byte[] Png(int ancho, int alto, Action<DrawingContext> dibujar)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen()) dibujar(dc);
        var mapa = new RenderTargetBitmap(ancho, alto, 96, 96, PixelFormats.Pbgra32);
        mapa.Render(visual);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(mapa));
        using var salida = new MemoryStream();
        png.Save(salida);
        return salida.ToArray();
    }

    /// <summary>Un modulo de diplomas de mentira con un diploma ficticio conseguido.</summary>
    private sealed class DiplomasFicticios : IDiplomas
    {
        private static readonly Diploma Islas = new("DFI", "Diploma Ficticio de Islas", ClaseDeDiploma.PorReferencia, "Radioclub Ficticio", null);

        public Task<IReadOnlyList<Diploma>> CatalogoAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Diploma>>([Islas]);

        public Task<IReadOnlyList<VarianteDeDiploma>> VariantesAsync(string codigo, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<VarianteDeDiploma>>([]);

        public Task<ProgresoDeDiploma> ProgresoAsync(string codigo, string variante, CancellationToken ct = default) => Task.FromResult(Progreso());

        public Task<IReadOnlyList<string>> MisDiplomasAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>(["DFI"]);

        public Task FijarMisDiplomasAsync(IReadOnlyList<string> codigos, CancellationToken ct = default) => Task.CompletedTask;

        public Task<IReadOnlyList<ProgresoDeDiploma>> ProgresoDeMisDiplomasAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ProgresoDeDiploma>>([Progreso(), new ProgresoDeDiploma("DFI", "CW", 1, 0, 2, DateTimeOffset.UtcNow)]);

        public Task<Pagina<EstadoDeReferencia>> DetalleAsync(string codigo, string variante, int desplazamiento, int limite, CancellationToken ct = default)
        {
            EstadoDeReferencia[] todas =
            [
                new("ISL-07", "Isla Siete", true, true, 1),
                new("ISL-08", "Isla Ocho", true, false, 3),
                new("ISL-09", "Isla Nueve", true, true, null),
            ];
            return Task.FromResult(new Pagina<EstadoDeReferencia>(todas.Skip(desplazamiento).Take(limite).ToList(), todas.Length, desplazamiento));
        }

        public Task<IReadOnlyList<string>> QueAportaAsync(Indicativo indicativo, Banda banda, Modo modo, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>([]);

        public Task RecalcularAsync(IProgress<ProgresoDeSincronizacion>? progreso = null, CancellationToken ct = default) => Task.CompletedTask;

        private static ProgresoDeDiploma Progreso() => new("DFI", "Mixto", 3, 2, 2, DateTimeOffset.UtcNow);
    }

    /// <summary>Recoge los errores de enlace de WPF mientras vive.</summary>
    private sealed class OyenteDeEnlaces : TraceListener
    {
        private readonly StringBuilder _errores;

        public OyenteDeEnlaces(StringBuilder errores)
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
