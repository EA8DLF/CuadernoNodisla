using System.Windows.Controls;

namespace Nodisla.Cuaderno.Ui.Vistas;

/// <summary>
/// El apartado de audio y modos digitales de la pantalla de ajustes.
/// </summary>
/// <remarks>
/// Sin codigo detras. Que la entrada se abra solo al probar y se cierre al parar lo decide el
/// modelo de vista, no esta pantalla: es una regla de funcionamiento, no de dibujo.
/// </remarks>
public partial class AjustesDeAudio : UserControl
{
    /// <summary>Monta la vista.</summary>
    public AjustesDeAudio() => InitializeComponent();
}
