using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Nodisla.Cuaderno.Ui.VistaModelos;
using Serilog;

namespace Nodisla.Cuaderno.Ui.Vistas;

/// <summary>El editor de la tarjeta QSL.</summary>
public partial class PanelDeQsl : UserControl
{
    private bool _cargado;

    /// <summary>Crea el panel.</summary>
    public PanelDeQsl()
    {
        InitializeComponent();
        DataContextChanged += AlCambiarElModelo;
    }

    private async void AlCargar(object sender, RoutedEventArgs e)
    {
        if (_cargado || DataContext is not VistaModeloQsl modelo) return;
        _cargado = true;
        try
        {
            await modelo.RefrescarAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se ha podido cargar el editor de QSL.");
        }
    }

    private void AlCambiarElModelo(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is VistaModeloQsl viejo) viejo.SolicitaEnviar -= AlPedirEnviar;
        if (e.NewValue is VistaModeloQsl nuevo) nuevo.SolicitaEnviar += AlPedirEnviar;
    }

    private void AlPedirEnviar(object? sender, IReadOnlyList<long> ids)
    {
        if (sender is not VistaModeloQsl modelo) return;
        try
        {
            VentanaDeEnvioDeQsl.Mostrar(Window.GetWindow(this), modelo, ids);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se ha podido abrir la ventana de envío de QSL.");
            modelo.Aviso = $"No se ha podido abrir el envío: {ex.Message}";
        }
    }

    private void AlEmpezarAArrastrar(object sender, DragStartedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: CampoEditable campo } && DataContext is VistaModeloQsl modelo)
        {
            modelo.CampoElegido = campo;
        }
    }

    private void AlArrastrar(object sender, DragDeltaEventArgs e)
    {
        // El asa esta dentro de la rejilla medida en milimetros: el desplazamiento ya viene en mm.
        if (sender is FrameworkElement { DataContext: CampoEditable campo } && DataContext is VistaModeloQsl modelo)
        {
            campo.Mover(e.HorizontalChange, e.VerticalChange, modelo.AnchoMm, modelo.AltoMm);
        }
    }
}
