using System.Windows.Controls;

namespace Nodisla.Cuaderno.Ui.Vistas;

/// <summary>
/// La cabina de operacion: el frontal del equipo a todo el ancho, y debajo el contacto nuevo
/// a la izquierda con el cluster y los modos digitales a la derecha.
/// </summary>
/// <remarks>
/// Esta vista ya no mide nada ni decide cuando esconder el frontal. Lo hacia antes, y el
/// resultado fue que el operador no llegaba a ver nunca la radio dibujada. El frontal es
/// vectorial y vive en un <c>Viewbox</c>: se encoge con la ventana, que es exactamente para
/// lo que sirve ser vectorial. Solo se pliega si el operador lo pliega.
/// </remarks>
public partial class PanelOperar : UserControl
{
    /// <summary>Monta la cabina.</summary>
    public PanelOperar() => InitializeComponent();

    /// <summary>Trae el foco al indicativo, que es donde se empieza a teclear.</summary>
    public void EnfocarIndicativo() => Entrada.EnfocarIndicativo();
}
