using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using FluentAssertions;
using Nodisla.Cuaderno.Ui.Vistas;

namespace Nodisla.Cuaderno.Ui.Pruebas;

/// <summary>
/// Las teclas tactiles del visor se iluminan (azul) con la opcion puesta: 3DSS y EXPAND.
/// </summary>
/// <remarks>
/// Hasta el 29-09-2026 no se iluminaban nunca: el disparador era un DataTrigger con
/// RelativeSource TemplatedParent, que desde los disparadores de la plantilla no encuentra la
/// tecla, y comparaba el Tag (object) con el texto "True".
/// </remarks>
public sealed class TeclasTactilesDelVisorPruebas
{
    /// <summary>Lo minimo que el visor enlaza para las teclas.</summary>
    public sealed class EquipoDeMentira
    {
        public bool AnalizadorEnTresD { get; init; }

        public bool AnalizadorAmpliado { get; init; }

        public string RotuloDeCenter => "CENTER";
    }

    /// <summary>Lo que el visor toma de la ventana para MULTI.</summary>
    public sealed class VentanaDeMentira
    {
        public VistaModelos.VistaModeloAnalizador Analizador { get; } = new(null);
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, false, false)]
    public void Tresdss_expand_y_multi_se_iluminan_cuando_estan_puestos(bool tresD, bool ampliado, bool multiple)
    {
        var colores = new Dictionary<string, Color?>();
        EnHiloDeInterfaz(() =>
        {
            var visor = new VisorDeVfos
            {
                DataContext = new EquipoDeMentira { AnalizadorEnTresD = tresD, AnalizadorAmpliado = ampliado },
                Width = 430,
                Height = 244,
            };
            var deLaVentana = new VentanaDeMentira();
            if (multiple) deLaVentana.Analizador.AlternarMultipleCommand.Execute(null);
            var ventana = new Window
            {
                DataContext = deLaVentana,
                Content = visor,
                Left = -6000,
                ShowActivated = false,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
                SizeToContent = SizeToContent.WidthAndHeight,
            };
            ventana.Show();
            visor.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

            foreach (var tecla in Buscar<Button>(visor).Where(b => b.CommandParameter is string))
            {
                var fondo = (Border)tecla.Template.FindName("fondo", tecla);
                colores[(string)tecla.CommandParameter] = (fondo.Background as SolidColorBrush)?.Color;
            }

            ventana.Close();
        });

        var azul = Color.FromRgb(0x1F, 0x5F, 0xAF);
        (colores["3DSS"] == azul).Should().Be(tresD);
        (colores["EXPAND"] == azul).Should().Be(ampliado);
        (colores["MULTI"] == azul).Should().Be(multiple);
        colores["CENTER"].Should().NotBe(azul);
    }

    private static IEnumerable<T> Buscar<T>(DependencyObject raiz)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(raiz); i++)
        {
            var hijo = VisualTreeHelper.GetChild(raiz, i);
            if (hijo is T t) yield return t;
            foreach (var x in Buscar<T>(hijo)) yield return x;
        }
    }

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
