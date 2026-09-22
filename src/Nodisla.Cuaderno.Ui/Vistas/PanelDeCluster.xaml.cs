using System.Windows.Controls;
using System.Windows.Input;
using Nodisla.Cuaderno.Ui.VistaModelos;

namespace Nodisla.Cuaderno.Ui.Vistas;

/// <summary>
/// Panel del cluster de DX.
/// </summary>
/// <remarks>
/// Lo poco que hay aqui es lo que es propio de la vista: el doble clic sobre un anuncio. Lo
/// demas esta en <see cref="VistaModeloCluster"/>. La consola cruda y su linea de ordenes se
/// mudaron a <see cref="AjustesDelCluster"/>: operando se leen los anuncios, no los «set/».
/// </remarks>
public partial class PanelDeCluster : UserControl
{
    /// <summary>Monta el panel.</summary>
    public PanelDeCluster() => InitializeComponent();

    private void AlPulsarDosVeces(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is VistaModeloCluster modelo) modelo.IrAlSpot(modelo.SpotSeleccionado);
    }
}
