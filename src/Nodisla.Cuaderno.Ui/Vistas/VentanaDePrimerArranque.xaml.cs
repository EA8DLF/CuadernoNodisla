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

        // Para las capturas de la ayuda: el perfil ficticio ya escrito, sin teclear en la ventana.
        // CUADERNO_PERFIL_DE_PRUEBA=indicativo;localizador;operador;localidad
        if (Environment.GetEnvironmentVariable("CUADERNO_PERFIL_DE_PRUEBA") is { Length: > 0 } perfil)
        {
            var partes = perfil.Split(';');
            vistaModelo.Indicativo = partes[0];
            if (partes.Length > 1) vistaModelo.Localizador = partes[1];
            if (partes.Length > 2) vistaModelo.NombreOperador = partes[2];
            if (partes.Length > 3) vistaModelo.Localidad = partes[3];
        }

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
