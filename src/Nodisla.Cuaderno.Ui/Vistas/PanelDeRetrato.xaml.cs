using System.Windows.Controls;

namespace Nodisla.Cuaderno.Ui.Vistas;

/// <summary>
/// El retrato del indicativo: las dos rejillas que dicen si merece la pena llamar.
/// </summary>
/// <remarks>
/// No hay codigo de vista: todo sale de <see cref="VistaModelos.VistaModeloRetrato"/>. El
/// color de las casillas <b>nunca va solo</b>: cada una lleva su texto y su nombre accesible,
/// porque uno de cada doce hombres no distingue el rojo del verde.
/// </remarks>
public partial class PanelDeRetrato : UserControl
{
    /// <summary>Monta el panel.</summary>
    public PanelDeRetrato() => InitializeComponent();
}
