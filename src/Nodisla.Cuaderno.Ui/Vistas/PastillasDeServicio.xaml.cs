using System.Windows.Controls;

namespace Nodisla.Cuaderno.Ui.Vistas;

/// <summary>
/// Los testigos de la barra de estado: una pastilla por servicio.
/// </summary>
/// <remarks>
/// El color nunca va solo. Cada pastilla lleva el nombre del servicio escrito y la ayuda
/// emergente dice el estado con palabras, porque un testigo que solo es color deja fuera a
/// quien no distingue el verde del gris.
/// </remarks>
public partial class PastillasDeServicio : UserControl
{
    /// <summary>Monta los testigos.</summary>
    public PastillasDeServicio() => InitializeComponent();
}
