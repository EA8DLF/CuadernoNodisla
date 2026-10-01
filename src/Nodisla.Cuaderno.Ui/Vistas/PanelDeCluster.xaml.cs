using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Nodisla.Cuaderno.Ui.Conversores;
using Nodisla.Cuaderno.Ui.VistaModelos;

namespace Nodisla.Cuaderno.Ui.Vistas;

/// <summary>
/// Panel del cluster de DX.
/// </summary>
/// <remarks>
/// Lo poco que hay aqui es lo que es propio de la vista: el doble clic sobre un anuncio y el
/// puente que lleva la letra del panel a los anchos de columna. Lo demas esta en
/// <see cref="VistaModeloCluster"/>. La consola cruda y su linea de ordenes se mudaron a
/// <see cref="AjustesDelCluster"/>: operando se leen los anuncios, no los «set/».
/// </remarks>
public partial class PanelDeCluster : UserControl
{
    /// <summary>Monta el panel.</summary>
    public PanelDeCluster()
    {
        InitializeComponent();
        ((Puente)Resources["PuenteDelPanel"]).Datos = this;
    }

    /// <summary>
    /// Doble clic: al spot. Solo si el clic cae sobre una fila; en la cabecera o en la barra de
    /// desplazamiento no se va a ningun sitio.
    /// </summary>
    private void AlPulsarDosVeces(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not VistaModeloCluster modelo) return;

        for (var elemento = e.OriginalSource as DependencyObject; elemento is not null;
             elemento = elemento is Visual or System.Windows.Media.Media3D.Visual3D
                 ? VisualTreeHelper.GetParent(elemento)
                 : LogicalTreeHelper.GetParent(elemento))
        {
            if (elemento is DataGridRow { Item: FilaDeSpot fila })
            {
                modelo.IrAlSpot(fila);
                return;
            }
        }
    }
}
