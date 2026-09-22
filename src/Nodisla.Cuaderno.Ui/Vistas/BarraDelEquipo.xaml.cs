using System.Windows.Controls;

namespace Nodisla.Cuaderno.Ui.Vistas;

/// <summary>
/// La barra del equipo: nombre, estado, conexion, plegado y el boton de soltar el PTT.
/// </summary>
/// <remarks>
/// Sale en Operar y en Digital, y es la misma en las dos porque es el mismo control sobre el
/// mismo modelo de vista. Asi no puede pasar que el boton de panico este en una pantalla y no
/// en la otra.
/// </remarks>
public partial class BarraDelEquipo : UserControl
{
    /// <summary>Monta la barra.</summary>
    public BarraDelEquipo() => InitializeComponent();
}
