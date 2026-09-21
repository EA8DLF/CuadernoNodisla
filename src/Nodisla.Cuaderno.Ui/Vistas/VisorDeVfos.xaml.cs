using System.Windows.Controls;

namespace Nodisla.Cuaderno.Ui.Vistas;

/// <summary>
/// El visor del equipo: los dos VFO a la vez, con quien transmite y quien recibe.
/// </summary>
/// <remarks>
/// Ocupa en el frontal dibujado el hueco de la pantalla tactil del equipo. No la imita: la
/// radio ya ensena lo suyo en su pantalla; aqui va lo que hace falta para operar desde el
/// ordenador, que es la frecuencia y el modo de los dos VFO, quien esta en antena, y quien
/// esta anunciado en esas mismas frecuencias.
/// </remarks>
public partial class VisorDeVfos : UserControl
{
    /// <summary>Monta el visor.</summary>
    public VisorDeVfos() => InitializeComponent();
}
