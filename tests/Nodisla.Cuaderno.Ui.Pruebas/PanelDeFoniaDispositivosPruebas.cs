using System.IO;
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
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Audio.Fonia;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Radio.Control;
using Nodisla.Cuaderno.Radio.Ptt;
using Nodisla.Cuaderno.Ui.Ajustes;
using Nodisla.Cuaderno.Ui.VistaModelos;
using Nodisla.Cuaderno.Ui.Vistas;

namespace Nodisla.Cuaderno.Ui.Pruebas;

/// <summary>
/// El desplegable «Dispositivos...» del panel de fonia DE VERDAD —el mismo control, el mismo
/// Popup, el mismo ComboBox— pintado fuera de la pantalla y manejado por los caminos de la
/// accesibilidad: si la lista no llega con dispositivos, o el Popup de fuera se cierra solo al
/// abrir el desplegable de un combo (el fallo clasico de un Popup anidado dentro de otro con
/// <c>StaysOpen="False"</c>), aqui se ve sin que le cueste el ordenador a Jose.
/// </summary>
/// <remarks>
/// Jose reportó, probando la 0.2.17 de verdad, que no podía elegir micrófono ni altavoces desde
/// este desplegable. El XAML del Popup de dispositivos no cambió una coma en el rediseño de hoy
/// (commit 3bc8a84): esta prueba comprueba el camino de verdad —lista con contenido, desplegable
/// que abre sin tirar el Popup que lo contiene, y la elección llegando al modelo— para dejar
/// constancia de si el fallo es de interacción real o de otra capa (p. ej. la enumeración de
/// WASAPI en la máquina de Jose, fuera del alcance de esta prueba).
/// </remarks>
[Collection(nameof(ColeccionDeLaVentana))]
public sealed class PanelDeFoniaDispositivosPruebas
{
    private static readonly DispositivoDeAudio CodecIn = new("codec-in", "Micrófono (USB Audio Device)", true, true);
    private static readonly DispositivoDeAudio MicroPc = new("micro-pc", "Micrófono (Realtek)", true, false);
    private static readonly DispositivoDeAudio CodecOut = new("codec-out", "Altavoces (USB Audio Device)", false, true);
    private static readonly DispositivoDeAudio AltavocesPc = new("altavoces-pc", "Altavoces (Realtek)", false, false);

    [Fact]
    public Task ElPopupAbreConLaListaYSePuedeElegirOtroMicrofono() => HiloDeVentana.Ejecutar(async () =>
    {
        var (ventana, panel, fonia) = Montar();
        try
        {
            await Asentar();

            var desplegar = (ToggleButton)panel.FindName("Desplegar");
            var popup = TodosEnLogico<Popup>(panel).Single(p => ReferenceEquals(p.PlacementTarget, desplegar));
            popup.IsOpen.Should().BeFalse("antes de pulsar el boton no tiene que estar abierto");

            // El mismo gesto que el operador: pulsar «Dispositivos...».
            desplegar.IsChecked = true;
            await Asentar();

            popup.IsOpen.Should().BeTrue("el boton tiene que abrir el Popup");
            var contenido = (FrameworkElement)popup.Child;
            var combos = TodosEnVisual<ComboBox>(contenido).ToList();
            combos.Should().HaveCount(4, "microfono, altavoces, entrada del equipo y salida del equipo");

            var comboMicro = combos[0];
            var comboAltavoces = combos[1];
            comboMicro.Items.Count.Should().Be(2, "la lista de entradas tiene que llegar con los dispositivos reales");
            comboAltavoces.Items.Count.Should().Be(2, "la lista de salidas tiene que llegar con los dispositivos reales");

            var capturas = Path.Combine(Path.GetTempPath(), "cuaderno-fonia-dispositivos-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(capturas);
            Capturar(contenido, Path.Combine(capturas, "popup-abierto.png"));

            fonia.Ajustes.Microfono.Should().Be(MicroPc, "el del equipo no puede salir elegido para el microfono del PC");

            // Abre el desplegable del combo DE VERDAD (su propio Popup, anidado dentro del
            // primero): si el Popup de fuera (StaysOpen="False") lo tratase como "fuera", se
            // cerraria solo y el operador no podria ni ver la lista.
            var peerCombo = (ComboBoxAutomationPeer)UIElementAutomationPeer.CreatePeerForElement(comboMicro)!;
            ((IExpandCollapseProvider)peerCombo).Expand();
            await Asentar();

            popup.IsOpen.Should().BeTrue("el desplegable del combo no puede cerrar el Popup que lo contiene");
            comboMicro.IsDropDownOpen.Should().BeTrue();

            ((IExpandCollapseProvider)peerCombo).Collapse();
            comboMicro.SelectedItem = CodecIn;
            await Asentar();

            fonia.Ajustes.Microfono.Should().Be(CodecIn, "elegir en el combo tiene que cambiar el dispositivo del modelo");
            popup.IsOpen.Should().BeTrue("elegir un dispositivo no cierra el Popup");

            Capturar(contenido, Path.Combine(capturas, "popup-tras-elegir-otro-microfono.png"));

            Console.WriteLine($"CAPTURAS: {capturas}");
        }
        finally
        {
            ventana.Close();
        }
    });

    /// <summary>
    /// Jose probo la 0.2.18 con dispositivos reales y no le gusta que el Popup se quede fijo
    /// hasta pulsar «Dispositivos...» otra vez: pide que se cierre solo al tocar fuera, como
    /// cualquier desplegable, sin que vuelva el fallo de cerrarse de mas al elegir un combo (la
    /// prueba de arriba). Aqui se comprueba el cierre de verdad: un clic simulado claramente
    /// fuera del Popup y del boton tiene que cerrarlo.
    /// </summary>
    [Fact]
    public Task UnClicFueraDelPopupLoCierra() => HiloDeVentana.Ejecutar(async () =>
    {
        var (ventana, panel, _) = Montar();
        try
        {
            await Asentar();

            var desplegar = (ToggleButton)panel.FindName("Desplegar");
            var popup = TodosEnLogico<Popup>(panel).Single(p => ReferenceEquals(p.PlacementTarget, desplegar));

            desplegar.IsChecked = true;
            await Asentar();
            popup.IsOpen.Should().BeTrue("el boton tiene que abrir el Popup");

            // Un clic claramente fuera: directamente en la ventana, nada que ver con el Popup
            // ni con el boton que lo abre.
            var clicFuera = new System.Windows.Input.MouseButtonEventArgs(
                System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left)
            {
                RoutedEvent = UIElement.PreviewMouseDownEvent,
            };
            ventana.RaiseEvent(clicFuera);
            await Asentar();

            popup.IsOpen.Should().BeFalse("un clic de verdad fuera del Popup y del boton tiene que cerrarlo");
            desplegar.IsChecked.Should().Be(false);
        }
        finally
        {
            ventana.Close();
        }
    });

    // ── Montaje ──────────────────────────────────────────────────────────

    private static (Window Ventana, PanelDeFonia Panel, VistaModeloFonia Fonia) Montar()
    {
        var radio = new EquipoDeMentira();
        var vigilante = new VigilantePtt(radio, new OpcionesDelVigilante { EngancharseAlCierreDelProceso = false });
        var equipo = new VistaModeloEquipo(radio, vigilante);
        equipo.ConectarAsync().GetAwaiter().GetResult();
        radio.PonerModo("USB");

        var rx = new PuenteDeMentira();
        var tx = new PuenteDeMentira();
        var control = new ControlDeFonia(vigilante, rx, tx, reloj: new RelojQuieto());
        var ajustes = new AjustesDelPrograma();

        var deAjustes = new VistaModeloAjustesFonia(
            ajustes,
            carpeta: null,
            () => [CodecIn, MicroPc],
            () => [CodecOut, AltavocesPc],
            deEntrada => deEntrada ? MicroPc.Id : AltavocesPc.Id);

        var fonia = new VistaModeloFonia(deAjustes, control, equipo, radio);

        var panel = new PanelDeFonia { DataContext = fonia };
        var ventana = new Window
        {
            Content = panel,
            Width = 1300,
            Height = 200,
            WindowStyle = WindowStyle.None,
            ShowActivated = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -9000,
            Top = 0,
        };
        ventana.SetResourceReference(TextElement.ForegroundProperty, "Texto");
        ventana.Show();
        ventana.UpdateLayout();
        return (ventana, panel, fonia);
    }

    private static async Task Asentar()
    {
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
    }

    private static IEnumerable<T> TodosEnLogico<T>(DependencyObject raiz) where T : DependencyObject
    {
        foreach (var hijo in LogicalTreeHelper.GetChildren(raiz).OfType<DependencyObject>())
        {
            if (hijo is T t) yield return t;
            foreach (var nieto in TodosEnLogico<T>(hijo)) yield return nieto;
        }
    }

    private static IEnumerable<T> TodosEnVisual<T>(DependencyObject raiz) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(raiz); i++)
        {
            var hijo = VisualTreeHelper.GetChild(raiz, i);
            if (hijo is T t) yield return t;
            foreach (var nieto in TodosEnVisual<T>(hijo)) yield return nieto;
        }
    }

    private static void Capturar(FrameworkElement elemento, string ruta)
    {
        var ancho = Math.Max(1, (int)Math.Ceiling(elemento.ActualWidth));
        var alto = Math.Max(1, (int)Math.Ceiling(elemento.ActualHeight));
        var mapa = new RenderTargetBitmap(ancho, alto, 96, 96, PixelFormats.Pbgra32);
        mapa.Render(elemento);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(mapa));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(ruta))!);
        using var fichero = File.Create(ruta);
        png.Save(fichero);
    }

    private sealed class RelojQuieto : TimeProvider
    {
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            new Quieto();

        private sealed class Quieto : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;

            public void Dispose()
            {
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class PuenteDeMentira : IPuenteDeAudio
    {
        public (string Entrada, string Salida)? Abierto { get; private set; }

        public bool EstaAbierto => Abierto is not null;

        public float Ganancia { get; set; } = 1f;

        public bool Silenciado { get; set; }

        public double Nivel => 0;

        public bool Saturando => false;

        public DateTimeOffset? UltimoAvanceUtc { get; set; }

        public Nodisla.Cuaderno.Audio.Procesado.IProcesadorDeAudio? AntesDeLaGanancia { get; set; }

        public Nodisla.Cuaderno.Audio.Procesado.IProcesadorDeAudio? TrasLaGanancia { get; set; }

        public event EventHandler<Exception>? Fallo;

        public void Romper(Exception fallo)
        {
            Abierto = null;
            Fallo?.Invoke(this, fallo);
        }

        public Task AbrirAsync(string idEntrada, string idSalida, CancellationToken ct = default)
        {
            Abierto = (idEntrada, idSalida);
            return Task.CompletedTask;
        }

        public Task CerrarAsync()
        {
            Abierto = null;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => new(CerrarAsync());
    }

    private sealed class EquipoDeMentira : IControlEquipo
    {
        private EstadoDelEquipo _estado = EstadoDelEquipo.Desconectado;

        public bool Ptt { get; private set; }

        public ViaDeControl Via => ViaDeControl.CatNativo;

        public EstadoDelEquipo Estado => _estado;

        public event EventHandler<EstadoDelEquipo>? EstadoCambiado;

        public void PonerModo(string modoDelEquipo) =>
            Cambiar(_estado with { Modo = TraductorDeModos.PorOmision.DesdeElEquipo(modoDelEquipo) });

        public Task ConectarAsync(CancellationToken ct = default)
        {
            Cambiar(_estado with { Conectado = true, Frecuencia = Frecuencia.DesdeHercios(14_200_000), LeidoUtc = DateTimeOffset.UnixEpoch });
            return Task.CompletedTask;
        }

        public Task DesconectarAsync(CancellationToken ct = default)
        {
            Cambiar(EstadoDelEquipo.Desconectado);
            return Task.CompletedTask;
        }

        public Task PonerFrecuenciaAsync(Frecuencia frecuencia, CancellationToken ct = default) => Task.CompletedTask;

        public Task PonerModoAsync(Modo modo, CancellationToken ct = default) => Task.CompletedTask;

        public Task PonerPttAsync(bool transmitir, CancellationToken ct = default)
        {
            Ptt = transmitir;
            Cambiar(_estado with { Transmitiendo = transmitir });
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private void Cambiar(EstadoDelEquipo nuevo)
        {
            _estado = nuevo;
            EstadoCambiado?.Invoke(this, nuevo);
        }
    }
}
