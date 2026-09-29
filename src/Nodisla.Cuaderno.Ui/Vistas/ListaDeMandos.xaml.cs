using System.Windows.Controls;

namespace Nodisla.Cuaderno.Ui.Vistas;

/// <summary>
/// Todos los mandos del equipo, construidos a partir de lo que el equipo declara.
/// </summary>
/// <remarks>
/// Es la vista que toma el relevo del frontal dibujado cuando aquel no cabe —con la letra muy
/// grande o la ventana estrecha—, y la unica que sirve para un equipo que no sea el FT-710 o
/// para los mandos que no estan en su frontal. Las dos accionan los mismos objetos.
/// </remarks>
public partial class ListaDeMandos : UserControl
{
    /// <summary>Monta la lista.</summary>
    public ListaDeMandos() => InitializeComponent();
}
