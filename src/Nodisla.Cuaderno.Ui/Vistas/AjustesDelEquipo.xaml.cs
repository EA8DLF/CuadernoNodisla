using System.Windows.Controls;

namespace Nodisla.Cuaderno.Ui.Vistas;

/// <summary>
/// El apartado CAT de la pantalla de ajustes.
/// </summary>
/// <remarks>
/// Sin codigo detras a proposito: todo lo que hace esta pantalla —listar puertos, probar,
/// aplicar— vive en <see cref="VistaModelos.VistaModeloAjustesCat"/>, que se puede probar sin
/// abrir una ventana.
/// </remarks>
public partial class AjustesDelEquipo : UserControl
{
    /// <summary>Monta la vista.</summary>
    public AjustesDelEquipo() => InitializeComponent();
}
