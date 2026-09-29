using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Radio.Espectro;
using Nodisla.Cuaderno.Ui.Recursos;
using Nodisla.Cuaderno.Ui.VistaModelos;
using Nodisla.Cuaderno.Ui.Vistas;

namespace Nodisla.Cuaderno.Ui.Pruebas;

/// <summary>
/// El analizador de la propia radio en la pantalla del frontal, con tramas REALES del FT-710
/// de EA8DLF (28-09-2026: 18.100.000, CENTER, span 200 kHz). Sin radio y sin reloj.
/// </summary>
public class AnalizadorDelEquipoPruebas
{
    private static IReadOnlyList<TrazaDeEspectro> TrazasReales()
    {
        var carpeta = Path.Combine(AppContext.BaseDirectory, "Capturas");
        return Directory.GetFiles(carpeta, "espectro-ft710-*.bin")
            .Order(StringComparer.Ordinal)
            .Select(f => TramaDelAnalizadorFt710.IntentarDescifrar(File.ReadAllBytes(f), out var t) ? t! : null)
            .OfType<TrazaDeEspectro>()
            .ToList();
    }

    [Fact]
    public void Hay_tramas_reales_y_son_de_la_radio_del_operador()
    {
        var trazas = TrazasReales();
        trazas.Should().NotBeEmpty();
        trazas[0].VfoHz.Should().Be(18_100_000);
        trazas[0].SpanHz.Should().Be(200_000);
        trazas[0].Modo.Should().Be(ModoDelAnalizador.Centro);
    }

    [Fact]
    public void El_rotulo_es_el_de_la_pantalla_del_equipo()
    {
        VistaModeloAnalizador.RotuloDe(TrazasReales()[0]).Should().Be("CENTER  SPEED FAST1  SPAN 200kHz");
        VistaModeloAnalizador.RotuloDe(TrazasReales()[0] with { SpanHz = 1_000_000 }).Should().EndWith("SPAN 1MHz");
    }

    /// <summary>Tramas de la radio real con cada modo del analizador puesto (29-09-2026).</summary>
    private static TrazaDeEspectro TrazaDeModo(string modo)
    {
        var fichero = Path.Combine(AppContext.BaseDirectory, "Capturas", $"analizador-ft710-{modo}.bin");
        TramaDelAnalizadorFt710.IntentarDescifrar(File.ReadAllBytes(fichero), out var traza).Should().BeTrue();
        return traza!;
    }

    [Theory]
    [InlineData("span50k", "CENTER  SPEED SLOW2  SPAN 50kHz", 50_000, false, false, 1)]
    [InlineData("slow1", "CENTER  SPEED SLOW1  SPAN 50kHz", 50_000, false, false, 0)]
    [InlineData("cursor", "CURSOR  SPEED SLOW1  SPAN 50kHz", 50_000, false, false, 0)]
    [InlineData("fix", "FIX 18068kHz  SPEED SLOW1  SPAN 100kHz", 100_000, false, false, 0)]
    [InlineData("expand", "CENTER  SPEED SLOW1  SPAN 50kHz", 50_000, false, true, 0)]
    [InlineData("3dss", "CENTER  SPEED SLOW1  SPAN 50kHz", 50_000, true, false, 0)]
    public void Lo_que_se_cambia_en_la_radio_llega_en_la_trama_y_se_dibuja(
        string modo, string rotulo, double span, bool tresD, bool ampliado, int velocidad)
    {
        EnHiloDeInterfaz(() =>
        {
            var modelo = new VistaModeloAnalizador(null);
            modelo.Pintar(TrazasReales()[0]);
            modelo.Pintar(TrazaDeModo(modo));

            modelo.Rotulo.Should().Be(rotulo);
            modelo.SpanHz.Should().Be(span);
            modelo.EnTresD.Should().Be(tresD);
            modelo.Ampliado.Should().Be(ampliado);
            modelo.Velocidad.Should().Be(velocidad);
            modelo.InicioFijoHz.Should().Be(modo == "fix" ? 18_068_000 : 0);
        });
    }

    [Fact]
    public void En_fix_la_marca_del_vfo_cae_donde_cae_en_la_escala_fija()
    {
        EnHiloDeInterfaz(() =>
        {
            var modelo = new VistaModeloAnalizador(null);
            modelo.Pintar(TrazaDeModo("fix"));

            // VFO 18.128.100 en una escala de 18.068.000 a 18.168.000.
            modelo.PosicionDelVfo.Should().BeApproximately(0.601, 0.001);
        });
    }

    [Fact]
    public void Lo_pulsado_manda_sobre_la_trama_que_ya_venia_y_luego_manda_la_radio()
    {
        EnHiloDeInterfaz(() =>
        {
            long ahora = 1_000;
            var modelo = new VistaModeloAnalizador(null, () => ahora);
            modelo.Pintar(TrazasReales()[0]);

            modelo.Ajustar(new AjusteDelAnalizador(500_000, 3, ModoDelAnalizador.Centro, TresD: true, Ampliado: false));
            modelo.Rotulo.Should().Be("CENTER  SPEED FAST2  SPAN 500kHz");
            modelo.EnTresD.Should().BeTrue();

            // Trama vieja, de antes de que la radio lo aplicara: no deshace lo pulsado.
            ahora += 300;
            modelo.Pintar(TrazasReales()[0]);
            modelo.SpanHz.Should().Be(500_000);
            modelo.EnTresD.Should().BeTrue();

            // Pasado el plazo, lo que diga la radio.
            ahora += (long)VistaModeloAnalizador.PrioridadDelCat.TotalMilliseconds;
            modelo.Pintar(TrazasReales()[0]);
            modelo.SpanHz.Should().Be(200_000);
            modelo.EnTresD.Should().BeFalse();
            modelo.Rotulo.Should().Be("CENTER  SPEED FAST1  SPAN 200kHz");
        });
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(1, 4)]
    [InlineData(2, 8)]
    [InlineData(3, 16)]
    [InlineData(4, 24)]
    [InlineData(5, 0)]
    public void Speed_cambia_lo_que_corre_la_cascada(int velocidad, int filas)
    {
        var pintor = new PintorDelAnalizador { Velocidad = velocidad };
        for (var i = 0; i < 8; i++) pintor.Pintar(TrazasReales()[0].Niveles);
        pintor.FilasAnadidas.Should().Be(filas);
    }

    [Fact]
    public void En_3dss_se_pinta_la_vista_en_perspectiva()
    {
        var pintor = new PintorDelAnalizador { TresD = true };
        foreach (var traza in Enumerable.Repeat(TrazasReales(), 20).SelectMany(t => t)) pintor.Pintar(traza.Niveles);

        var fondo = pintor.VistaTresD[0];
        pintor.VistaTresD.Count(p => p != fondo).Should().BeGreaterThan(PintorDelAnalizador.Ancho * 20, "hay muchas pasadas en fila");

        // La pasada de delante llega a los bordes, abajo; arriba del todo no hay nada.
        var ancho = PintorDelAnalizador.Ancho;
        var abajoIzquierda = Enumerable.Range(PintorDelAnalizador.AltoTresD - 60, 60)
            .SelectMany(f => Enumerable.Range(0, 10).Select(x => pintor.VistaTresD[(f * ancho) + x]));
        abajoIzquierda.Should().Contain(p => p != fondo);
        pintor.VistaTresD.Take(ancho * 10).Should().OnlyContain(p => p == fondo);
    }

    [Fact]
    public void El_pintor_deja_el_ruido_oscuro_y_las_señales_claras()
    {
        var pintor = new PintorDelAnalizador();
        foreach (var traza in TrazasReales()) pintor.Pintar(traza.Niveles);

        var ruido = Brillo(pintor.ColorDeCascada((byte)pintor.Suelo));
        var señal = Brillo(pintor.ColorDeCascada((byte)Math.Min(255, pintor.Suelo + 40)));
        ruido.Should().BeLessThan(0.25);
        señal.Should().BeGreaterThan(ruido * 2);

        // La fila nueva de la cascada va arriba y no es negra entera.
        pintor.Cascada.Take(PintorDelAnalizador.Ancho).Distinct().Count().Should().BeGreaterThan(5);
    }

    [Fact]
    public void Sin_analizador_lo_dice_en_vez_de_dejar_un_hueco()
    {
        EnHiloDeInterfaz(() =>
        {
            var modelo = new VistaModeloAnalizador(null);
            modelo.Recibiendo.Should().BeFalse();
            modelo.TituloDelAviso.Should().Be("SIN ANALIZADOR DEL EQUIPO");
        });
    }

    [Fact]
    public void Con_tramas_reales_pinta_traza_cascada_y_marca_del_vfo()
    {
        EnHiloDeInterfaz(() =>
        {
            var modelo = new VistaModeloAnalizador(null);
            foreach (var traza in TrazasReales()) modelo.Pintar(traza);

            modelo.Recibiendo.Should().BeTrue();
            modelo.SpanHz.Should().Be(200_000);
            modelo.PosicionDelVfo.Should().BeApproximately(0.5, 0.001);
            modelo.ImagenDeLaTraza!.PixelWidth.Should().Be(850);
            modelo.ImagenDeLaCascada!.PixelHeight.Should().Be(PintorDelAnalizador.AltoDeCascada);
            modelo.ImagenTresD!.PixelHeight.Should().Be(PintorDelAnalizador.AltoTresD);

            // Con CUADERNO_CAPTURA_ANALIZADOR se guarda el retrato del control con las tramas reales.
            if (Environment.GetEnvironmentVariable("CUADERNO_CAPTURA_ANALIZADOR") is { Length: > 0 } ruta)
            {
                // Con CUADERNO_TRAMAS_ANALIZADOR, una tirada larga grabada de la radio para la cascada.
                var tirada = Environment.GetEnvironmentVariable("CUADERNO_TRAMAS_ANALIZADOR") is { Length: > 0 } carpeta
                    ? Directory.GetFiles(carpeta, "*.bin").Order(StringComparer.Ordinal)
                        .Select(f => TramaDelAnalizadorFt710.IntentarDescifrar(File.ReadAllBytes(f), out var t) ? t : null)
                        .OfType<TrazaDeEspectro>().ToList()
                    : [.. Enumerable.Repeat(TrazasReales(), 40).SelectMany(t => t)];
                foreach (var traza in tirada) modelo.Pintar(traza);

                GuardarRetrato(modelo, ruta);
            }
        });
    }

    private static void GuardarRetrato(VistaModeloAnalizador modelo, string ruta)
    {
        var control = new AnalizadorDelEquipo { DataContext = modelo, Width = 422, Height = 140 };
        var marco = new Border { Background = Brushes.Black, Child = control, Width = 422, Height = 140 };
        var ventana = new Window
        {
            Content = marco,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStyle = WindowStyle.None,
            ShowActivated = false,
            ShowInTaskbar = false,
            Left = -6000,
            Top = 0,
        };
        ventana.Show();
        marco.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Render);

        var escala = 3.0;
        var mapa = new RenderTargetBitmap((int)(422 * escala), (int)(140 * escala), 96 * escala, 96 * escala, PixelFormats.Pbgra32);
        mapa.Render(marco);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(mapa));
        using (var fichero = File.Create(ruta)) png.Save(fichero);
        ventana.Close();
    }

    private static double Brillo(int color) =>
        (((color >> 16) & 0xFF) + ((color >> 8) & 0xFF) + (color & 0xFF)) / (3 * 255.0);

    private static void EnHiloDeInterfaz(Action accion)
    {
        Exception? fallo = null;
        var hilo = new Thread(() =>
        {
            try { accion(); }
            catch (Exception ex) { fallo = ex; }
        });
        hilo.SetApartmentState(ApartmentState.STA);
        hilo.Start();
        hilo.Join();
        if (fallo is not null) throw new InvalidOperationException(fallo.Message, fallo);
    }
}
