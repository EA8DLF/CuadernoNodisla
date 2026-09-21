using System.Windows;
using System.Windows.Controls;
using Nodisla.Cuaderno.Ui.Mapa;
using Nodisla.Cuaderno.Ui.VistaModelos;

namespace Nodisla.Cuaderno.Ui.Vistas;

/// <summary>
/// Panel del mapa. Envuelve el control del mapa y le pasa lo que dice el modelo de vista.
/// </summary>
/// <remarks>
/// Es la unica clase de la interfaz que toca el control del mapa, y solo lo hace para dos
/// cosas que son propias de la vista: encuadrar el mundo y avisar de que se ha tocado una
/// marca. El motor de mapas no sale de su proyecto.
/// </remarks>
public partial class PanelDeMapa : UserControl
{
    /// <summary>Monta el panel.</summary>
    public PanelDeMapa()
    {
        InitializeComponent();
        Unloaded += (_, _) => Mapa.Dispose();
    }

    private void AlElegirUnaMarca(object? origen, MarcaDelMapa marca)
    {
        if (DataContext is VistaModeloMapa modelo) modelo.ElegirMarca(marca);
    }

    private void AlPedirElMundo(object sender, RoutedEventArgs e) => Mapa.VerElMundo();
}
