using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nodisla.Cuaderno.Ui.VistaModelos;
using Nodisla.Cuaderno.Ui.Vistas;

namespace Nodisla.Cuaderno.Ui.Pruebas;

/// <summary>
/// La fonía en columna, junto al frontal (Operar): con la ventana baja de alto, el frontal
/// encoge hasta <see cref="PanelOperar.AltoMinimoDelFrontal"/> antes de plegarse solo, y esa
/// columna de fonía vive dentro de un <c>Viewbox</c> que solo encoge con el mismo alto
/// disponible. Sin un suelo propio, a ese alto mínimo el PTT y los deslizadores quedaban a menos
/// de la mitad de su tamaño (encogido con el frontal), prácticamente impulsables.
/// </summary>
/// <remarks>Fallo reportado por Jose: en Operar, con poco alto, la fonía salía diminuta.</remarks>
[Collection(nameof(ColeccionDeLaVentana))]
public sealed class FoniaEnColumnaPruebas
{
    private static readonly string[] Variables = ["CUADERNO_SIMULADO", "CUADERNO_CARPETA", "CUADERNO_MODELO"];

    /// <summary>
    /// Ventana baja: el frontal queda cerca de su mínimo (<see cref="PanelOperar.AltoMinimoDelFrontal"/>,
    /// 150) pero sin plegarse. Antes de darle a la columna de fonía su propio suelo
    /// (<see cref="PanelOperar.AltoMinimoDeFoniaEnColumna"/>), el Viewbox la escalaba al mismo
    /// ~150 que el frontal (natural ~330): el botón del PTT rondaba los 20 puntos de alto.
    /// </summary>
    [Fact]
    public Task Con_el_frontal_cerca_de_su_minimo_la_fonia_sigue_siendo_pulsable() => HiloDeVentana.Ejecutar(async () =>
    {
        var antes = Variables.ToDictionary(v => v, Environment.GetEnvironmentVariable);
        var carpeta = Path.Combine(Path.GetTempPath(), "cuaderno-fonia-columna-" + Guid.NewGuid().ToString("N"));
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
        await using var proveedor = servicios.BuildServiceProvider();

        var ventana = proveedor.GetRequiredService<VentanaPrincipal>();
        var modelo = (VistaModeloPrincipal)ventana.DataContext;
        ventana.WindowStartupLocation = WindowStartupLocation.Manual;
        ventana.ShowActivated = false;
        ventana.ShowInTaskbar = false;
        ventana.Left = -10000;
        ventana.Top = -10000;
        ventana.Width = 1460;

        // Alto elegido para que el frontal quede cerca de su minimo (AltoMinimoDelFrontal, 150)
        // sin llegar a plegarse: el caso mas exigente para la columna de fonia a su lado.
        ventana.Height = 910;

        try
        {
            ventana.Show();
            await Asentar();
            if (!modelo.Equipo.Conectado) await modelo.Equipo.ConectarAsync();
            await Asentar();

            modelo.Fonia.Should().NotBeNull("sin fonia no hay columna que proteger");

            var panelOperar = Todos<PanelOperar>(ventana).Should().ContainSingle().Subject;
            var hueco = (FrameworkElement)panelOperar.FindName("HuecoDelFrontal")!;
            var caja = (FrontalConBotonera)panelOperar.FindName("CajaDelFrontal")!;

            hueco.Visibility.Should().Be(Visibility.Visible, "a este alto el frontal todavia no se pliega solo");
            caja.AltoMaximoDelFrontal.Should().BeLessThan(PanelOperar.AltoMinimoDeFoniaEnColumna,
                "el dibujo del frontal esta cerca de su minimo: es justo el caso que antes dejaba la fonia diminuta");
            caja.MaxHeight.Should().BeGreaterThanOrEqualTo(PanelOperar.AltoMinimoDeFoniaEnColumna,
                "el hueco lateral (donde vive la columna de fonia) tiene que tener suelo propio, mas alto que el dibujo");

            // La columna de fonia, dentro de su Viewbox (CajaDelFrontal tiene otros dos, el de
            // la botonera HAM y el de los canales CB: se busca el que envuelve a PanelDeFonia).
            // BotonPtt es el control que el operador tiene que poder pulsar; su tamano en
            // pantalla es el de la capa que lo escala (el Viewbox), no su ActualHeight sin
            // escalar.
            var fonia = Todos<PanelDeFonia>(caja).Should().ContainSingle("solo hay una columna de fonia junto al frontal").Subject;
            var viewbox = Todos<Viewbox>(caja).Should().Contain(v => Todos<PanelDeFonia>(v).Contains(fonia)).Which;
            var boton = Todos<Button>(fonia).Should().Contain(b => b.Name == "BotonPtt").Which;

            var escala = viewbox.ActualHeight / Math.Max(1, fonia.ActualHeight);
            var pttAltoReal = boton.ActualHeight * escala;

            pttAltoReal.Should().BeGreaterThanOrEqualTo(40,
                $"el PTT tiene que seguir siendo pulsable (viewbox={viewbox.ActualWidth:0}x{viewbox.ActualHeight:0}, " +
                $"fonia.ActualHeight={fonia.ActualHeight:0}, boton.ActualHeight={boton.ActualHeight:0}, escala={escala:0.000})");
        }
        finally
        {
            ventana.Close();
            foreach (var (nombre, valor) in antes) Environment.SetEnvironmentVariable(nombre, valor);
            try { Directory.Delete(carpeta, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    });

    private static async Task Asentar()
    {
        for (var i = 0; i < 4; i++)
        {
            await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        }
    }

    private static List<T> Todos<T>(DependencyObject raiz)
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
}
