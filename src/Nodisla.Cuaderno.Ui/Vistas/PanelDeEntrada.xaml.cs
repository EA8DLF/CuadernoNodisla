using System.Windows.Controls;

namespace Nodisla.Cuaderno.Ui.Vistas;

/// <summary>
/// Formulario de entrada de contactos.
/// </summary>
/// <remarks>
/// Estaba dentro de la ventana principal y se ha mudado aqui tal cual: la pantalla principal
/// pasa a ser la cabina de operacion, y este formulario es una de sus piezas, no la ventana
/// entera. El contenido no ha cambiado.
/// </remarks>
public partial class PanelDeEntrada : UserControl
{
    /// <summary>Monta el formulario.</summary>
    public PanelDeEntrada() => InitializeComponent();

    /// <summary>Trae el foco al campo del indicativo, que es donde se empieza a teclear.</summary>
    public void EnfocarIndicativo()
    {
        CampoIndicativo.Focus();
        CampoIndicativo.SelectAll();
    }
}
