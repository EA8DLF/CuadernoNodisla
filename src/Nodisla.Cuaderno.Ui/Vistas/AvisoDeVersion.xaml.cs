using System.Windows.Controls;
using Nodisla.Cuaderno.Ui.VistaModelos;

namespace Nodisla.Cuaderno.Ui.Vistas;

/// <summary>
/// Barra del aviso de version nueva. Su DataContext es <see cref="VistaModeloActualizaciones"/>.
/// </summary>
/// <remarks>
/// Al cargarse (o al recibir el modelo, si llega despues) lanza la comprobacion del arranque. El
/// modelo se encarga de que sea una sola vez por sesion y como mucho una vez al dia; asi basta
/// con poner la barra en la ventana para que el aviso funcione, sin tocar el arranque.
/// </remarks>
public partial class AvisoDeVersion : UserControl
{
    /// <summary>Monta la barra.</summary>
    public AvisoDeVersion()
    {
        InitializeComponent();
        Loaded += (_, _) => Comprobar();
        DataContextChanged += (_, _) => { if (IsLoaded) Comprobar(); };
    }

    private void Comprobar()
    {
        if (DataContext is VistaModeloActualizaciones modelo) _ = modelo.ComprobarAlArrancarAsync();
    }
}
