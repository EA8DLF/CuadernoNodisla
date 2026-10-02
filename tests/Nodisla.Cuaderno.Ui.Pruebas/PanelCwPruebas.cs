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

            // «Buscar» vuelve a AUTO.
            modelo.FijarEn(650);
            Pulsar(panel, "Buscar");
            await Asentar();
            modelo.Fijo.Should().BeFalse();

            // El tono a mano: se escribe y Intro (la orden de la casilla) lo fija, acotado.
            modelo.TonoEscrito = "1500";
            modelo.AplicarTonoEscritoCommand.Execute(null);
            modelo.Fijo.Should().BeTrue();
            modelo.TonoHz.Should().Be(1200);
            modelo.Aviso.Should().Contain("1200");
            modelo.SubirTonoCommand.Execute(null);
            modelo.TonoHz.Should().Be(1200, "no pasa del máximo");
            modelo.BajarTonoCommand.Execute(null);
            modelo.TonoHz.Should().Be(1190);
            modelo.TonoEscrito = "250";
            modelo.AplicarTonoEscritoCommand.Execute(null);
            modelo.TonoHz.Should().Be(300);

            // Grabar: empieza y cuenta.
            Pulsar(panel, "Grabar 60 s");
            await Asentar();
            modelo.Grabando.Should().BeTrue();

            Todos<ButtonBase>(panel).Where(b => b is not ToggleButton && b.Command is null && b.TemplatedParent is null).Should().BeEmpty("cada botón lleva su orden");
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
            var equipo = new EquipoSimulado();
            var tx = new VistaModeloTransmisionCw(new AjustesDelPrograma(), new Nodisla.Cuaderno.Radio.Cw.EmisorCw(equipo, new VigilantePttDeDesarrollo(equipo)), equipo,
                cw: modelo, carpeta: Path.Combine(Path.GetTempPath(), "cuaderno-cw-capturas"), conReloj: false);
            var pagina = new PestanaCw { DataContext = new PrincipalDePrueba(modelo, tx) };
            var ventana = Ventana(pagina, 1340, 860);
            var linea = new PanelCwReducido { DataContext = modelo, Margin = new Thickness(8) };
            var bordeLinea = new Border { Child = linea, Padding = new Thickness(4), VerticalAlignment = VerticalAlignment.Top };
            bordeLinea.SetResourceReference(Border.BackgroundProperty, "FondoPanel");
            var ventanaLinea = Ventana(bordeLinea, 1340, 60);
            try
            {
                Alimentar(entrada, modelo, 36);
                await Asentar();

                // La rejilla del glosario genera sus filas en una pasada de diseño aparte.
                for (var i = 0; i < 4; i++)
                {
                    pagina.UpdateLayout();
                    await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                    await Task.Delay(50);
                }

                Guardar(pagina, new Rect(0, 0, pagina.ActualWidth, pagina.ActualHeight), Path.Combine(capturas, $"cw-pagina{sufijo}.png"));
                Retratar(linea, Path.Combine(capturas, $"cw-cabina{sufijo}.png"));
            }
            finally
            {
                ventana.Close();
                ventanaLinea.Close();
            }
        }
    });

    [Fact]
    public Task LaPaginaCwNoTieneEnlacesRotosYElHuecoDeTransmisionEstaListo() => HiloDeVentana.Ejecutar(async () =>
    {
        var errores = new StringBuilder();
        using var oyente = new OyenteDeEnlacesCw(errores);
        var entrada = new EntradaDeAudioSimulada { SinReloj = true };
        var modelo = new VistaModeloCw(new AjustesDelPrograma(), entrada, conReloj: false) { MiIndicativo = "EA8DLF" };

        // Con todo enganchado: la transmisión de verdad sobre el equipo simulado (montarla no
        // transmite nada; aquí no se pulsa ninguna macro).
        var equipo = new EquipoSimulado();
        var vigilante = new VigilantePttDeDesarrollo(equipo);
        var carpeta = Path.Combine(Path.GetTempPath(), "cuaderno-cw-" + Guid.NewGuid().ToString("N"));
        var tx = new VistaModeloTransmisionCw(new AjustesDelPrograma(), new Nodisla.Cuaderno.Radio.Cw.EmisorCw(equipo, vigilante), equipo, cw: modelo, carpeta: carpeta, conReloj: false);
        var pagina = new PestanaCw { DataContext = new PrincipalDePrueba(modelo, tx) };
        var ventana = Ventana(pagina, 1340, 900);
        try
        {
            await Asentar();
            Alimentar(entrada, modelo, 20);
            await Asentar();

            // El glosario está en la página, con su significado.
            var glosario = (ListBox)pagina.FindName("Glosario");
            glosario.Items.Count.Should().Be(modelo.Glosario.Count).And.BeGreaterThan(80);

            // El hueco de la transmisión lleva las macros; vacío, dice lo que irá.
            var rotulo = (TextBlock)pagina.FindName("RotuloDelHueco");
            pagina.HuecoDeTransmision.Content.Should().BeOfType<MacrosCw>();
            ((MacrosCw)pagina.HuecoDeTransmision.Content).DataContext.Should().BeSameAs(tx);
            rotulo.Visibility.Should().Be(Visibility.Collapsed);
            pagina.HuecoDeTransmision.Content = null;
            rotulo.Visibility.Should().Be(Visibility.Visible);

            errores.ToString().Should().BeEmpty("cada enlace roto es un control o un dato muerto");
        }
        finally
        {
            ventana.Close();
        }
    });

    [Fact]
    public Task LaLineaDeLaCabinaAbreLaPagina() => HiloDeVentana.Ejecutar(async () =>
    {
        var errores = new StringBuilder();
        using var oyente = new OyenteDeEnlacesCw(errores);
        var modelo = new VistaModeloCw(new AjustesDelPrograma(), new EntradaDeAudioSimulada { SinReloj = true }, conReloj: false) { MostrarAMano = true };
        var abierta = false;
        modelo.AbrirPaginaPedido += (_, _) => abierta = true;
        var linea = new PanelCwReducido { DataContext = modelo };
        var ventana = Ventana(linea, 1300, 60);
        try
        {
            await Asentar();
            Pulsar(linea, "Abrir CW");
            await Asentar();
            abierta.Should().BeTrue();
            Pulsar(linea, "Ocultar");
            await Asentar();
            modelo.MostrarAMano.Should().BeFalse();
            errores.ToString().Should().BeEmpty();
        }
        finally
        {
            ventana.Close();
        }
    });

    [Fact]
    public void LaPaginaCwEstaEnElGrupoOperarConSuCapitulo()
    {
        VistaModeloPrincipal.Grupos["Operar"].Should().Equal(
            VistaModeloPrincipal.PaginaOperar, VistaModeloPrincipal.PaginaDigital, VistaModeloPrincipal.PaginaCw,
            VistaModeloPrincipal.PaginaSatelites, VistaModeloPrincipal.PaginaRonda);
        VistaModeloPrincipal.GrupoDe(VistaModeloPrincipal.PaginaCw).Should().Be("Operar");
        VistaModeloPrincipal.CapituloDeCadaPagina[VistaModeloPrincipal.PaginaCw].Should().Be("17-cw");
    }

    // ── Glosario, resaltado y ayuda ──────────────────────────────────────

    [Theory]
    [InlineData("QRZ", GrupoDelGlosarioCw.CodigoQ, "¿Quién me llama?")]
    [InlineData("QTH", GrupoDelGlosarioCw.CodigoQ, "Ubicación")]
    [InlineData("TU", GrupoDelGlosarioCw.Abreviatura, "Gracias")]
    [InlineData("73", GrupoDelGlosarioCw.Abreviatura, "Saludos cordiales")]
    [InlineData("5NN", GrupoDelGlosarioCw.Abreviatura, "599")]
    [InlineData("?", GrupoDelGlosarioCw.Abreviatura, "repita")]
    [InlineData("<SK>", GrupoDelGlosarioCw.Prosigno, "Fin del contacto")]
    [InlineData("<KN>", GrupoDelGlosarioCw.Prosigno, "solo la estación llamada")]
    public void ElGlosarioDiceQueSignificaCadaCosa(string texto, GrupoDelGlosarioCw grupo, string trozo)
    {
        var entrada = GlosarioCw.Buscar(texto);
        entrada.Should().NotBeNull();
        entrada!.Grupo.Should().Be(grupo);
        entrada.Significado.Should().Contain(trozo);
    }

    [Fact]
    public void ElGlosarioTieneTodoEnLosSeisIdiomasYLosNumerosCortos()
    {
        GlosarioCw.Entradas.Should().Contain(e => e.Grupo == GrupoDelGlosarioCw.NumeroCorto && e.Texto == "N" && e.Significado.StartsWith('9'));
        GlosarioCw.Buscar("N").Should().BeNull("una N suelta no se traduce en el texto: es ambigua");
        // Se leen los recursos de cada idioma directamente, sin cambiar el idioma del programa
        // (cambiarlo avisa a las ventanas de otras pruebas, que viven en otro hilo).
        var recursos = new System.Resources.ResourceManager(
            "Nodisla.Cuaderno.Idiomas.Recursos.Digital", typeof(Nodisla.Cuaderno.Idiomas.Textos).Assembly);
        foreach (var codigo in Nodisla.Cuaderno.Idiomas.Textos.Idiomas.Select(i => i.Codigo))
        {
            var cultura = new System.Globalization.CultureInfo(codigo);
            var conjunto = recursos.GetResourceSet(cultura, createIfNotExists: true, tryParents: codigo == "es");
            conjunto.Should().NotBeNull(codigo);
            foreach (var e in GlosarioCw.Entradas)
                conjunto!.GetString(e.Clave).Should().NotBeNullOrWhiteSpace($"falta el significado de {e.Texto} en {codigo}");
        }

        recursos.GetString("Digital.CwAbrev.TU", new System.Globalization.CultureInfo("en")).Should().Be("Thank you");
        recursos.GetString("Digital.CwAbrev.TU", new System.Globalization.CultureInfo("de")).Should().Be("Danke");
    }

    [Fact]
    public void LasAbreviaturasSeResaltanConSuSignificadoYLaTraduccion()
    {
        var modelo = new VistaModeloCw(new AjustesDelPrograma(), null, conReloj: false) { MiIndicativo = "EA8DLF" };
        foreach (var c in "TU 73 QRZ? DE EA8DLF <SK> ") modelo.Principal.Anadir(c.ToString());

        var palabras = modelo.Principal.Palabras;
        palabras.Should().Contain(p => p.Texto == "TU" && p.Tipo == TipoDePalabraCw.Abreviatura && p.Ayuda == "Gracias");
        palabras.Should().Contain(p => p.Texto == "73" && p.Tipo == TipoDePalabraCw.Abreviatura);
        palabras.Should().Contain(p => p.Texto == "QRZ?" && p.Tipo == TipoDePalabraCw.Llamada && p.TieneAyuda);
        modelo.Principal.Traduccion.Should().Contain("TU = Gracias").And.Contain("73 = Saludos cordiales");
    }

    [Fact]
    public void ElEstadoDeLaBusquedaYElIndicadorDeEntrada()
    {
        var entrada = new EntradaDeAudioSimulada { SinReloj = true };
        var modelo = new VistaModeloCw(new AjustesDelPrograma(), entrada, conReloj: false) { MostrarAMano = true };
        modelo.EstadoDeLaBusqueda.Should().Contain("Buscando");
        modelo.FijarEn(640);
        modelo.EstadoDeLaBusqueda.Should().Contain("640");

        // El audio simulado llega con nivel: «bien».
        entrada.Adelantar(1);
        modelo.Refrescar();
        modelo.NivelDeEntrada.Should().Be(NivelDeEntradaCw.Bien);
        modelo.EntradaTexto.Should().Contain("dBFS");
    }

    /// <summary>Lo que la página CW toma de la ventana principal, sin montar la ventana entera.</summary>
    private sealed class PrincipalDePrueba(VistaModeloCw cw, VistaModeloTransmisionCw? txCw = null)
    {
        public VistaModeloCw Cw { get; } = cw;

        public VistaModeloTransmisionCw? TxCw { get; } = txCw;

        public object? Equipo => null;

        public bool FrontalDibujadoVisible => false;

        public bool FrontalDesplegado { get; set; }

        public bool ListaDeMandosVisible { get; set; }

        public string TextoDeLaListaDeMandos => string.Empty;

        public string TextoDelPliegueDelFrontal => string.Empty;
    }

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
        panel.Height = 320;
        return (Ventana(borde, ancho, 360), panel);
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
