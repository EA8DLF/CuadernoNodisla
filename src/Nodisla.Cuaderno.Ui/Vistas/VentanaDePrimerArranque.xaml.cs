using System.Windows;
using Nodisla.Cuaderno.Ui.VistaModelos;

namespace Nodisla.Cuaderno.Ui.Vistas;

/// <summary>
/// Ventana del primer arranque. Se cierra sola en cuanto el perfil esta creado; si el operador
/// prefiere salir, el programa se cierra, porque sin perfil no hay nada que hacer.
/// </summary>
public partial class VentanaDePrimerArranque : Window
{
    /// <summary>Monta la ventana con su modelo de vista.</summary>
    public VentanaDePrimerArranque(VistaModeloPrimerArranque vistaModelo)
    {
        ArgumentNullException.ThrowIfNull(vistaModelo);

        InitializeComponent();
        DataContext = vistaModelo;

        vistaModelo.PerfilCreado += (_, _) =>
        {
            DialogResult = true;
            Close();
        };
    }

    private void AlCargar(object sender, RoutedEventArgs e) => CampoIndicativo.Focus();

    private void AlSalir(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
