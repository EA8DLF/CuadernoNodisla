using System.Windows;
using System.Windows.Controls;
using Nodisla.Cuaderno.Idiomas;
using Nodisla.Cuaderno.Ui.VistaModelos;

namespace Nodisla.Cuaderno.Ui.Vistas;

/// <summary>Configuracion › «Servidor para otros programas» (rigctld y TCI).</summary>
/// <remarks>Abrir a la red local y dejar transmitir a los programas preguntan antes, con el dialogo del programa.</remarks>
public partial class ServidoresExternos : UserControl
{
    /// <summary>Monta la vista.</summary>
    public ServidoresExternos() => InitializeComponent();

    private void AlCargar(object sender, RoutedEventArgs e)
    {
        if (DataContext is VistaModeloServidores modelo) modelo.Confirmar ??= Preguntar;
    }

    private bool Preguntar(string titulo, string detalle)
    {
        var dialogo = new VentanaDeConfirmacion
        {
            Owner = Window.GetWindow(this),
            Titulo = titulo,
            Detalle = detalle,
            TextoDeAceptar = Textos.T("Ajustes.Servidor.Confirmar.Aceptar"),
        };

        return dialogo.ShowDialog() == true;
    }
}
