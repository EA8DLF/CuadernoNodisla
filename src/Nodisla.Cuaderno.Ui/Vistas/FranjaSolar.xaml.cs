using System.Windows.Controls;

namespace Nodisla.Cuaderno.Ui.Vistas;

/// <summary>
/// La franja solar: indices del Sol y del campo magnetico, y las horas de orto y ocaso.
/// </summary>
/// <remarks>
/// Todo lo que hace esta aqui es ensenar lo que trae <see cref="VistaModelos.VistaModeloSolar"/>.
/// Los colores son fijos a proposito: es un visor, no un panel, y tiene que leerse igual con
/// el tema claro y con el oscuro.
/// </remarks>
public partial class FranjaSolar : UserControl
{
    /// <summary>Monta la franja.</summary>
    public FranjaSolar() => InitializeComponent();
}
