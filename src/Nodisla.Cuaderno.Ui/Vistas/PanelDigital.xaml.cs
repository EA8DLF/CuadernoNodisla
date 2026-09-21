using System.Windows.Controls;

namespace Nodisla.Cuaderno.Ui.Vistas;

/// <summary>
/// Panel de modos digitales: instancias conectadas, decodificaciones en vivo y los contactos
/// que llegan ya cerrados.
/// </summary>
/// <remarks>
/// Sin codigo detras: lo que se puede y no se puede hacer con cada instancia lo decide
/// <see cref="VistaModelos.VistaModeloDigital"/> preguntandole al puente, y el XAML solo
/// desactiva los botones que no proceden.
/// </remarks>
public partial class PanelDigital : UserControl
{
    /// <summary>Monta el panel.</summary>
    public PanelDigital() => InitializeComponent();
}
