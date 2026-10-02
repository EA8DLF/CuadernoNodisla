using System.Windows;
using System.Windows.Controls;
using Nodisla.Cuaderno.Idiomas;
using Nodisla.Cuaderno.Ui.VistaModelos;

namespace Nodisla.Cuaderno.Ui.Vistas;

/// <summary>Las salvaguardas de transmision: plan de banda, ROE y potencia por banda.</summary>
/// <remarks>Liberar una banda del plan pregunta antes, con el dialogo del programa.</remarks>
public partial class SeguridadTx : UserControl
{
    /// <summary>Monta la vista.</summary>
    public SeguridadTx() => InitializeComponent();

    private void AlCargar(object sender, RoutedEventArgs e)
    {
        if (DataContext is VistaModeloSeguridadTx modelo) modelo.ConfirmarLiberarBanda ??= Preguntar;
    }

    private bool Preguntar(string detalle)
    {
        var dialogo = new VentanaDeConfirmacion
        {
            Owner = Window.GetWindow(this),
            Titulo = Textos.T("Ajustes.SeguridadTx.ConfirmarLiberar.Titulo"),
            Detalle = detalle,
            TextoDeAceptar = Textos.T("Ajustes.SeguridadTx.ConfirmarLiberar.Aceptar"),
        };

        return dialogo.ShowDialog() == true;
    }
}
