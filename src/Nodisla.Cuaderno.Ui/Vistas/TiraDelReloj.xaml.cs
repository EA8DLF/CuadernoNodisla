using System.Windows.Controls;

namespace Nodisla.Cuaderno.Ui.Vistas;

/// <summary>
/// La tira del reloj del modem: desvio, semaforo y boton de poner en hora.
/// </summary>
/// <remarks>
/// Sin codigo detras. El veredicto, el consejo y el escalon los entrega el reloj ya escritos;
/// aqui solo se pintan. La regla de cuantos milisegundos son demasiados es del reloj, no de
/// la pantalla.
/// </remarks>
public partial class TiraDelReloj : UserControl
{
    /// <summary>Monta la tira.</summary>
    public TiraDelReloj() => InitializeComponent();
}
