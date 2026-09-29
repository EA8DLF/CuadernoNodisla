using System.Windows.Controls;

namespace Nodisla.Cuaderno.Ui.Vistas;

/// <summary>
/// La pantalla de diplomas: elegir los propios, ver como van y mirar el detalle.
/// </summary>
/// <remarks>
/// No hay codigo de vista: todo sale de <see cref="VistaModelos.VistaModeloDiplomas"/>. El
/// aviso de que una cifra es una cota inferior se dibuja <b>junto a la cifra</b>, no en una
/// ayuda emergente: un diploma mal entendido lleva a pedir uno que no se tiene.
/// </remarks>
public partial class PanelDeDiplomas : UserControl
{
    /// <summary>Monta la pantalla.</summary>
    public PanelDeDiplomas() => InitializeComponent();
}
