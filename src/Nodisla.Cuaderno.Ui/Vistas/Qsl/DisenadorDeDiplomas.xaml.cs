using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Nodisla.Cuaderno.Ui.VistaModelos;
using Serilog;

namespace Nodisla.Cuaderno.Ui.Vistas.Qsl;

/// <summary>El diseñador de diplomas: va como subpestaña «Diplomas» dentro de la pestaña QSL.</summary>
public partial class DisenadorDeDiplomas : UserControl
{
    private bool _cargado;

    /// <summary>Crea la vista.</summary>
    public DisenadorDeDiplomas() => InitializeComponent();

    private VistaModeloDisenadorDeDiplomas? Modelo => DataContext as VistaModeloDisenadorDeDiplomas;

    private async void AlCargar(object sender, RoutedEventArgs e)
    {
        if (_cargado || Modelo is not { } modelo) return;
        _cargado = true;
        try
        {
            await modelo.RefrescarAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se ha podido cargar el diseñador de diplomas.");
        }
    }

    private void AlEmpezarAArrastrar(object sender, DragStartedEventArgs e)
    {
        if (Modelo is not { } modelo || sender is not FrameworkElement elemento) return;
        if (elemento.DataContext is CampoEditable or ImagenEditable) modelo.ElementoElegido = elemento.DataContext;
    }

    private void AlArrastrar(object sender, DragDeltaEventArgs e)
    {
        // Las asas estan dentro de la rejilla medida en milimetros: el desplazamiento ya viene en mm.
        if (Modelo is not { } modelo || sender is not FrameworkElement elemento) return;
        switch (elemento.DataContext)
        {
            case CampoEditable campo:
                campo.Mover(e.HorizontalChange, e.VerticalChange, modelo.AnchoMm, modelo.AltoMm);
                break;
            case ImagenEditable imagen:
                imagen.Mover(e.HorizontalChange, e.VerticalChange, modelo.AnchoMm, modelo.AltoMm);
                break;
        }
    }

    private void AlArrastrarLaTabla(object sender, DragDeltaEventArgs e) =>
        Modelo?.MoverTabla(e.HorizontalChange, e.VerticalChange);
}
