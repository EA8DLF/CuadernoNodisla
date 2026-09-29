using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using FluentAssertions;
using Nodisla.Cuaderno.Ui.VistaModelos;
using Nodisla.Cuaderno.Ui.Vistas;
using static Nodisla.Cuaderno.Ui.Pruebas.MontajeDeLaPestanaDigital;

namespace Nodisla.Cuaderno.Ui.Pruebas;

/// <summary>
/// El panel del modem DE VERDAD, pintado en una ventana fuera de la pantalla y sin activar,
/// y pulsado boton a boton por los mismos caminos que usa la accesibilidad (los «peers» de
/// automatizacion): si un boton no tiene orden, su enlace esta roto o su CanExecute no se
/// alcanza, aqui se ve. Nada de raton ni de foco: no se le quita el ordenador a nadie.
/// </summary>
[Collection(nameof(ColeccionDeLaVentana))]
public sealed class PanelDelModemEnPantallaPruebas
{
    [Fact]
    public Task NingunEnlaceDelPanelEstaRoto() => HiloDeVentana.Ejecutar(async () =>
    {
        var errores = new StringBuilder();
        using var oyente = new OyenteDeEnlaces(errores);

        var modelo = Montar(out _, out _, out _, new SincronizadorDeMentira());
        var (ventana, panel) = Pintar(modelo, 1400, 700);
        try
        {
            // Los desplegables se abren para que su contenido tambien se enlace.
            foreach (var e in Todos<Expander>(panel)) e.IsExpanded = true;
            await Asentar();
            await modelo.EscucharCommand.ExecuteAsync(null);
            await Asentar();

            errores.ToString().Should().BeEmpty("cada enlace roto es un control o un dato muerto");
        }
        finally
        {
            ventana.Close();
        }
    });

    [Fact]
    public Task ElOyenteDeEnlacesDeVerdadVeUnEnlaceRoto() => HiloDeVentana.Ejecutar(async () =>
    {
        // Sin esto, «ningún enlace roto» podría ser solo un oyente sordo.
        var errores = new StringBuilder();
        using var oyente = new OyenteDeEnlaces(errores);

        var texto = new TextBlock { DataContext = new object() };
        texto.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("NoExiste"));
        var ventana = new Window { Content = texto, ShowActivated = false, ShowInTaskbar = false, Left = -8000, WindowStartupLocation = WindowStartupLocation.Manual };
        ventana.Show();
        await Asentar();
        ventana.Close();

        errores.ToString().Should().Contain("NoExiste");
    });

    [Fact]
    public Task TodosLosBotonesTienenOrdenYHacenAlgo() => HiloDeVentana.Ejecutar(async () =>
    {
        var modelo = Montar(out var modem, out var reloj, out _, new SincronizadorDeMentira());
        var (ventana, panel) = Pintar(modelo, 1400, 700);
        try
        {
            foreach (var e in Todos<Expander>(panel)) e.IsExpanded = true;
            await Asentar();

            // Todo boton del panel lleva orden: uno sin orden es un boton que no hace nada.
            var sinOrden = Todos<ButtonBase>(panel)
                .Where(b => b is not ToggleButton and not DataGridColumnHeader && b.Command is null && b.TemplatedParent is null)
                .Select(b => b.Content?.ToString())
                .ToList();
            sinOrden.Should().BeEmpty();

            Pulsar(panel, "Escuchar");
            await Asentar();
            modelo.Escuchando.Should().BeTrue();

            // El pestillo, a mano, y la pregunta dice que si: el modem es de mentira.
            Alternar(panel, "Permitir transmitir en esta sesión");
            modelo.PermitirTransmitir.Should().BeTrue();
            modelo.ConfirmarQueVaATransmitir = _ => true;
            await Asentar();

            reloj.Ahora = Mediodia.AddSeconds(7);
            Alternar(panel, "Tx habilitado");
            await Asentar();
            modelo.TxHabilitado.Should().BeTrue();
            Buscar<CheckBox>(panel, "Tx habilitado").IsChecked.Should().BeTrue();

            Pulsar(panel, "Detener");
            await Asentar();
            modelo.TxHabilitado.Should().BeFalse();
            Buscar<CheckBox>(panel, "Tx habilitado").IsChecked.Should().BeFalse("la casilla sigue al modelo");

            // Las radios de «siguiente» y el boton «Enviar» de cada mensaje.
            var radios = Todos<RadioButton>(panel).ToList();
            radios.Should().HaveCount(6);
            ((ISelectionItemProvider)new RadioButtonAutomationPeer(radios[2])).Select();
            await Asentar();
            modelo.TxSiguiente.Should().Be(3);
            ((ISelectionItemProvider)new RadioButtonAutomationPeer(radios[4])).Select();
            await Asentar();
            modelo.TxSiguiente.Should().Be(5);
            radios[4].IsChecked.Should().BeTrue();
            radios[2].IsChecked.Should().BeFalse("la marca es una sola");

            Todos<Button>(panel).Count(b => Equals(b.Content, "Enviar")).Should().Be(6);

            modelo.Corresponsal = "EA5XYZ";
            await Asentar();
            Pulsar(panel, "Olvidar");
            await Asentar();
            modelo.Corresponsal.Should().BeEmpty();

            Pulsar(panel, "Llamar CQ");
            await Asentar();
            modelo.Secuenciador.Activo.Should().BeTrue();
            modelo.TxHabilitado.Should().BeTrue();

            modem.EstaEmitiendo = true;
            Pulsar(panel, "Cortar y soltar PTT");
            await Asentar();
            modem.Abortos.Should().Be(1);
            modelo.Aviso.Should().Be("Emisión cortada y PTT soltado.");
            Pulsar(panel, "✕");
            await Asentar();
            modelo.Aviso.Should().BeEmpty("el aviso se cierra");

            Pulsar(panel, "Añadir");
            await Asentar();
            modelo.FrecuenciaElegida.Should().NotBeNull();
            Pulsar(panel, "Quitar");
            await Asentar();
            modelo.FrecuenciaElegida.Should().BeNull();

            Pulsar(panel, "Medir");
            await Asentar();
            reloj.Mediciones.Should().BeGreaterThan(0);

            Pulsar(panel, "Parar");
            await Asentar();
            modelo.Escuchando.Should().BeFalse();
        }
        finally
        {
            ventana.Close();
        }
    });

    [Fact]
    public Task ElDobleClicEnUnaDecodificacionPreparaElContacto() => HiloDeVentana.Ejecutar(async () =>
    {
        var modelo = Montar(out var modem, out var reloj, out _);
        var (ventana, panel) = Pintar(modelo, 1400, 700);
        try
        {
            reloj.Ahora = Mediodia.AddSeconds(16);
            // Se espera a la señal de la ventana procesada, no a un tiempo.
            var procesada = new TaskCompletionSource();
            modelo.VentanaProcesada += (_, _) => procesada.TrySetResult();
            modem.Soltar(Mediodia, Oido("CQ EA5XYZ IM98", -7, 800, Mediodia));
            await procesada.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Asentar();

            var fila = Todos<ListBoxItem>(panel).Single();
            fila.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(
                System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left)
            {
                RoutedEvent = Control.MouseDoubleClickEvent,
            });
            await Asentar();

            modelo.Corresponsal.Should().Be("EA5XYZ");
            modem.Emisiones.Should().BeEmpty("con el pestillo cerrado no sale nada");
        }
        finally
        {
            ventana.Close();
        }
    });

    [Theory]
    [InlineData(1880, 470)]   // lo que le queda al modem a 1366×768 con el frontal plegado
    [InlineData(1880, 560)]   // a 1920×1000 con el frontal a su tamaño recortado
    [InlineData(1300, 380)]   // mas bajo que el minimo: se desplaza, no se recorta
    public Task LaCascadaYLaListaNuncaQuedanEnUnaTira(double ancho, double alto) => HiloDeVentana.Ejecutar(async () =>
    {
        var modelo = Montar(out _, out _, out _);
        var (ventana, panel) = Pintar(modelo, ancho, alto);
        try
        {
            await Asentar();
            var cascada = Todos<Cascada>(panel).Single();
            var lista = Todos<ListBox>(panel).Single();

            cascada.ActualHeight.Should().BeGreaterThanOrEqualTo(PanelDelModem.AltoMinimoDeLaCascada - 1);
            lista.ActualHeight.Should().BeGreaterThanOrEqualTo(PanelDelModem.AltoMinimoDeLaLista - 40);

            // «Cortar y soltar PTT» siempre existe dentro del contenido (con barra, si hace falta).
            var cortar = Buscar<Button>(panel, "Cortar y soltar PTT");
            var abajo = cortar.TransformToAncestor(panel).Transform(new Point(0, cortar.ActualHeight)).Y;
            var desplazador = Todos<ScrollViewer>(panel).First();
            abajo.Should().BeLessThanOrEqualTo(Math.Max(panel.ActualHeight, desplazador.ExtentHeight) + 1);
        }
        finally
        {
            ventana.Close();
        }
    });

    [Theory]
    [InlineData(555, 45, 0)]     // 1366×768: no cabe, se pliega solo
    [InlineData(790, 45, 255)]   // 1920×1000 con la barra en una línea
    [InlineData(1100, 45, 300)]  // pantalla alta: su tamaño de siempre
    public void ElFrontalCedeElAlto(double pestana, double barra, double esperado) =>
        PestanaDigital.AltoDelFrontal(pestana, barra).Should().BeApproximately(esperado, 0.5);

    // ── Utilidades ────────────────────────────────────────────────────────

    private static (Window Ventana, PanelDelModem Panel) Pintar(VistaModeloModemPropio modelo, double ancho, double alto)
    {
        var panel = new PanelDelModem { DataContext = modelo };
        var ventana = new Window
        {
            Content = panel,
            Width = ancho,
            Height = alto,
            SizeToContent = SizeToContent.Manual,
            WindowStyle = WindowStyle.None,
            ShowActivated = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -8000,
            Top = 0,
        };
        ventana.Show();
        ventana.UpdateLayout();
        return (ventana, panel);
    }

    private static async Task Asentar()
    {
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
    }

    private static void Pulsar(DependencyObject raiz, string rotulo)
    {
        var boton = Buscar<Button>(raiz, rotulo);
        boton.IsEnabled.Should().BeTrue($"«{rotulo}» tiene que poder pulsarse aquí");
        ((IInvokeProvider)new ButtonAutomationPeer(boton)).Invoke();
    }

    private static void Alternar(DependencyObject raiz, string rotulo)
    {
        var casilla = Buscar<CheckBox>(raiz, rotulo);
        casilla.IsEnabled.Should().BeTrue($"«{rotulo}» tiene que poder marcarse aquí");
        // El camino de un clic de verdad (OnClick): el «peer» de accesibilidad solo cambia la
        // marca y no ejecuta la orden, así que no probaría lo que hace el ratón.
        typeof(ButtonBase).GetMethod("OnClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(casilla, null);
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

/// <summary>Las pruebas con ventana van solas, despues de las demas, en un solo hilo de ventana.</summary>
[CollectionDefinition(nameof(ColeccionDeLaVentana), DisableParallelization = true)]
public sealed class ColeccionDeLaVentana;

/// <summary>
/// Un unico hilo STA con su despachador y una <see cref="Application"/> con los estilos del
/// programa, vivo mientras dure la suite: los recursos estaticos del panel necesitan la
/// aplicacion, y solo puede haber una por proceso.
/// </summary>
internal static class HiloDeVentana
{
    private static readonly Lazy<Dispatcher> Despachador = new(Arrancar);

    public static Task Ejecutar(Func<Task> prueba) =>
        Despachador.Value.InvokeAsync(prueba).Task.Unwrap();

    private static Dispatcher Arrancar()
    {
        var listo = new TaskCompletionSource<Dispatcher>();
        var hilo = new Thread(() =>
        {
            try
            {
                var aplicacion = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                foreach (var diccionario in new[] { "Tema.Oscuro", "Iconos", "Estilos", "Estilos.Operacion", "Estilos.Cuaderno" })
                {
                    aplicacion.Resources.MergedDictionaries.Add((ResourceDictionary)Application.LoadComponent(
                        new Uri($"/Nodisla.Cuaderno.Ui;component/Recursos/{diccionario}.xaml", UriKind.Relative)));
                }

                listo.SetResult(Dispatcher.CurrentDispatcher);
            }
            catch (Exception ex)
            {
                listo.SetException(ex);
                return;
            }

            Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "Hilo de ventana de las pruebas",
        };
        hilo.SetApartmentState(ApartmentState.STA);
        hilo.Start();
        return listo.Task.GetAwaiter().GetResult();
    }
}
