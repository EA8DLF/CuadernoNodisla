using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Ui.Ajustes;
using Nodisla.Cuaderno.Ui.VistaModelos;
using Nodisla.Cuaderno.Ui.Vistas;

namespace Nodisla.Cuaderno.Ui.Pruebas;

/// <summary>
/// La fonia montada en la ventana DE VERDAD —las dos copias del panel (columna y franja) y la
/// pagina de Configuracion › Fonia, todas enlazadas al mismo modelo—, con los puertos simulados
/// (ni radio ni tarjeta de sonido de verdad).
/// </summary>
/// <remarks>
/// Aqui se reproducen los fallos que Jose vio el 2026-11-15 con la 0.2.18: desplegables de
/// dispositivos en blanco, escucha que no se abria sola y PTT que no hacia nada. Los tres
/// tenian la misma raiz y solo salian arrancando en una pestana que no fuese Operar
/// (<c>paneles.json</c> guarda la ultima; la suya era la 5, Configuracion): por eso las pruebas
/// de antes, que arrancaban siempre en Operar, no lo vieron nunca.
/// </remarks>
[Collection(nameof(ColeccionDeLaVentana))]
public sealed class FoniaEnLaVentanaPruebas
{
    private const int PestanaDeConfiguracion = 5;

    private static readonly string[] Variables = ["CUADERNO_SIMULADO", "CUADERNO_CARPETA", "CUADERNO_MODELO"];

    /// <summary>
    /// La copia en columna (la que se ve junto al frontal) tiene que tener su modelo arranque
    /// el programa donde arranque. Con la 0.2.18, arrancando en otra pestana, se quedaba sin
    /// <c>DataContext</c> para siempre: listas vacias y nada elegido en los cuatro desplegables.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(PestanaDeConfiguracion)]
    public Task La_fonia_en_columna_tiene_modelo_y_dispositivos_arranque_donde_arranque(int pestanaInicial) =>
        HiloDeVentana.Ejecutar(() => ConLaVentana(pestanaInicial, escucharAlArrancar: false, async (ventana, modelo) =>
        {
            var fonia = modelo.Fonia!;
            modelo.IndiceDeLaPestana = 0;
            await Asentar();

            var panel = ElPanelALaVista(ventana);
            panel.Vertical.Should().BeTrue("con el frontal a la vista, la fonia que se ve es la de la columna");
            panel.DataContext.Should().BeSameAs(fonia, "sin modelo el panel entero esta muerto: ni dispositivos, ni PTT, ni escucha");

            ((ToggleButton)panel.FindName("Desplegar")).IsChecked = true;
            await Asentar();

            var popup = (Popup)panel.FindName("PopupDispositivos");
            popup.IsOpen.Should().BeTrue();
            var combos = EnLogico<ComboBox>(popup.Child).ToList();
            combos.Should().HaveCount(4);

            var elegidos = new[] { fonia.Ajustes.Microfono, fonia.Ajustes.Altavoces, fonia.Ajustes.EntradaDelEquipo, fonia.Ajustes.SalidaAlEquipo };
            elegidos.Should().NotContainNulls("el simulador trae dispositivos para los cuatro huecos");
            for (var i = 0; i < combos.Count; i++)
            {
                combos[i].Items.Count.Should().BeGreaterThan(0, $"el desplegable {i} tiene que traer la lista de dispositivos");
                combos[i].SelectedItem.Should().Be(elegidos[i], $"el desplegable {i} tiene que ensenar lo elegido, no quedarse en blanco");
                combos[i].SelectionBoxItem.Should().Be(elegidos[i], $"lo que se pinta en la caja del desplegable {i} es lo elegido");
            }
        }));

    /// <summary>
    /// Volver a buscar los dispositivos (Windows da objetos nuevos cada vez, iguales por valor)
    /// no puede dejar en blanco ningun desplegable de ninguna de las tres vistas enlazadas al
    /// mismo modelo, ni perder lo elegido.
    /// </summary>
    [Fact]
    public Task Volver_a_buscar_no_deja_en_blanco_ningun_desplegable() =>
        HiloDeVentana.Ejecutar(() => ConLaVentana(PestanaDeConfiguracion, escucharAlArrancar: false, async (ventana, modelo) =>
        {
            var fonia = modelo.Fonia!;

            // La pagina Configuracion › Fonia tambien enlaza los cuatro dispositivos.
            foreach (var pestanas in EnVisual<TabControl>(ventana))
            {
                var deFonia = pestanas.Items.OfType<TabItem>()
                    .FirstOrDefault(t => Equals(t.Header, Nodisla.Cuaderno.Idiomas.Textos.T("Ajustes.Apartado.Fonia")));
                if (deFonia is not null) pestanas.SelectedItem = deFonia;
            }

            await Asentar();
            var pagina = EnVisual<Vistas.AjustesDeFonia>(ventana).Should().ContainSingle().Subject;

            modelo.IndiceDeLaPestana = 0;
            await Asentar();
            var panel = ElPanelALaVista(ventana);
            ((ToggleButton)panel.FindName("Desplegar")).IsChecked = true;
            await Asentar();

            for (var vuelta = 0; vuelta < 3; vuelta++)
            {
                fonia.Ajustes.RefrescarDispositivos();
                await Asentar();
            }

            var a = fonia.Ajustes;
            a.Microfono.Should().NotBeNull();
            a.Altavoces.Should().NotBeNull();
            a.EntradaDelEquipo.Should().NotBeNull();
            a.SalidaAlEquipo.Should().NotBeNull();

            var deDispositivos = EnVisual<PanelDeFonia>(ventana)
                .SelectMany(p => EnLogico<ComboBox>(((Popup)p.FindName("PopupDispositivos")).Child))
                .Concat(EnLogico<ComboBox>(pagina).Where(c => c.ItemsSource is IEnumerable<DispositivoDeAudio>))
                .ToList();
            deDispositivos.Should().HaveCount(12, "cuatro en la columna, cuatro en la franja y cuatro en Configuracion");
            foreach (var combo in deDispositivos)
            {
                combo.SelectedItem.Should().NotBeNull("ningun desplegable se queda en blanco tras volver a buscar");
                combo.SelectedItem.Should().BeOneOf(a.Microfono, a.Altavoces, a.EntradaDelEquipo, a.SalidaAlEquipo);
                combo.Items.Contains(combo.SelectedItem).Should().BeTrue("lo elegido es un elemento de la lista nueva");
            }
        }, dispositivosNuevosCadaVez: true));

    /// <summary>
    /// El boton del PTT del panel que se ve tiene que llegar al modelo. Sin modelo (el fallo de
    /// arriba) el boton se veia habilitado y pulsarlo no hacia nada ni dejaba nada en el
    /// registro: «ahora tampoco puedo hacer PTT». Todo simulado: no hay radio.
    /// </summary>
    [Fact]
    public Task El_boton_del_ptt_de_la_columna_transmite_arrancando_en_otra_pestana() =>
        HiloDeVentana.Ejecutar(() => ConLaVentana(PestanaDeConfiguracion, escucharAlArrancar: false, async (ventana, modelo) =>
        {
            var fonia = modelo.Fonia!;
            if (!modelo.Equipo.Conectado) await modelo.Equipo.ConectarAsync();
            modelo.IndiceDeLaPestana = 0;
            await Asentar();

            VistaModeloFonia.EsModoDeVoz(modelo.Equipo.Modo).Should().BeTrue($"el equipo simulado arranca en un modo de voz (esta en «{modelo.Equipo.Modo}»)");
            fonia.PuedeTransmitir.Should().BeTrue(fonia.MotivoDeNoPoder);

            var panel = ElPanelALaVista(ventana);
            var boton = (Button)panel.FindName("BotonPtt");
            boton.IsEnabled.Should().BeTrue();

            boton.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent });
            await Asentar();
            fonia.Transmitiendo.Should().BeTrue("pulsar el PTT de la columna tiene que salir al aire (simulado)");

            boton.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent });
            await Asentar();
            fonia.Transmitiendo.Should().BeFalse("soltar el boton corta la pasada");
        }));

    // ── Montaje ──────────────────────────────────────────────────────────

    private static PanelDeFonia ElPanelALaVista(Window ventana) =>
        EnVisual<PanelDeFonia>(ventana).Where(p => p.IsVisible).Should().ContainSingle("de las dos copias del panel solo se ve una").Subject;

    private static async Task ConLaVentana(
        int pestana,
        bool escucharAlArrancar,
        Func<VentanaPrincipal, VistaModeloPrincipal, Task> prueba,
        bool dispositivosNuevosCadaVez = false)
    {
        var antes = Variables.ToDictionary(v => v, Environment.GetEnvironmentVariable);
        var carpeta = Path.Combine(Path.GetTempPath(), "cuaderno-fonia-ventana-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(carpeta);
        File.WriteAllText(
            Path.Combine(carpeta, EstadoDeLosPaneles.NombreDelFichero),
            $"{{ \"Pestana\": {pestana}, \"EquipoDesplegado\": true, \"TemaOscuro\": true }}");
        Environment.SetEnvironmentVariable("CUADERNO_SIMULADO", "1");
        Environment.SetEnvironmentVariable("CUADERNO_CARPETA", carpeta);
        Environment.SetEnvironmentVariable("CUADERNO_MODELO", "yaesu-ft710");

        var recursos = Application.Current.Resources;
        if (recursos["MarcaDelCuaderno"] is null)
        {
            recursos.MergedDictionaries.Add((ResourceDictionary)Application.LoadComponent(
                new Uri("/Nodisla.Cuaderno.Ui;component/Recursos/Marca.xaml", UriKind.Relative)));
        }

        var servicios = new ServiceCollection();
        servicios.AddLogging();
        servicios.AnadirCuaderno();

        // Los ajustes de fonia de la prueba: «escuchar al arrancar» a voluntad y, si se pide,
        // dispositivos que son objetos NUEVOS en cada busqueda (como los da Windows de verdad;
        // el simulador devuelve siempre los mismos y asi no se veria un fallo de igualdad).
        servicios.AddSingleton(proveedor =>
        {
            var ajustes = proveedor.GetRequiredService<AjustesDelPrograma>();
            ajustes.Fonia ??= new Ajustes.AjustesDeFonia();
            ajustes.Fonia.EscucharAlArrancar = escucharAlArrancar;
            var entradas = proveedor.GetRequiredService<IEntradaDeAudio>();
            var salidas = proveedor.GetRequiredService<ISalidaDeAudio>();
            return new VistaModeloAjustesFonia(
                ajustes,
                carpeta: null,
                () => dispositivosNuevosCadaVez ? [.. entradas.Dispositivos.Select(d => d with { })] : entradas.Dispositivos,
                () => dispositivosNuevosCadaVez ? [.. salidas.Dispositivos.Select(d => d with { })] : salidas.Dispositivos);
        });

        await using var proveedorDeServicios = servicios.BuildServiceProvider();

        var ventana = proveedorDeServicios.GetRequiredService<VentanaPrincipal>();
        var modelo = (VistaModeloPrincipal)ventana.DataContext;
        ventana.WindowStartupLocation = WindowStartupLocation.Manual;
        ventana.ShowActivated = false;
        ventana.ShowInTaskbar = false;
        ventana.Left = -10000;
        ventana.Top = -10000;
        ventana.Width = 1460;
        ventana.Height = 1000;

        try
        {
            ventana.Show();
            await Asentar();
            modelo.IndiceDeLaPestana.Should().Be(pestana, "la prueba arranca en la pestana guardada");
            modelo.Fonia.Should().NotBeNull();
            await prueba(ventana, modelo);
        }
        finally
        {
            ventana.Close();
            foreach (var (nombre, valor) in antes) Environment.SetEnvironmentVariable(nombre, valor);
            try { Directory.Delete(carpeta, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static async Task Asentar()
    {
        for (var i = 0; i < 4; i++)
        {
            await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        }
    }

    private static List<T> EnVisual<T>(DependencyObject raiz)
        where T : DependencyObject
    {
        var hallados = new List<T>();
        var pendientes = new Stack<DependencyObject>();
        pendientes.Push(raiz);
        while (pendientes.Count > 0)
        {
            var actual = pendientes.Pop();
            if (actual is T t) hallados.Add(t);
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(actual); i++) pendientes.Push(VisualTreeHelper.GetChild(actual, i));
        }

        return hallados;
    }

    private static IEnumerable<T> EnLogico<T>(DependencyObject raiz)
        where T : DependencyObject
    {
        if (raiz is T yo) yield return yo;
        foreach (var hijo in LogicalTreeHelper.GetChildren(raiz).OfType<DependencyObject>())
        {
            foreach (var nieto in EnLogico<T>(hijo)) yield return nieto;
        }
    }
}
