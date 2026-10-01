using System.Windows;
using System.Windows.Controls;
using Nodisla.Cuaderno.Ui.VistaModelos;
using Serilog;

namespace Nodisla.Cuaderno.Ui.Vistas;

/// <summary>
/// Formulario de entrada de contactos.
/// </summary>
/// <remarks>
/// Estaba dentro de la ventana principal y se ha mudado aqui tal cual: la pantalla principal
/// pasa a ser la cabina de operacion, y este formulario es una de sus piezas, no la ventana
/// entera. El contenido no ha cambiado.
/// </remarks>
public partial class PanelDeEntrada : UserControl
{
    /// <summary>Monta el formulario.</summary>
    public PanelDeEntrada() => InitializeComponent();

    /// <summary>Trae el foco al campo del indicativo, que es donde se empieza a teclear.</summary>
    public void EnfocarIndicativo()
    {
        CampoIndicativo.Focus();
        CampoIndicativo.SelectAll();
    }

    /// <summary>La QSL del contacto que se esta modificando.</summary>
    private void AlVerEnviarQsl(object sender, RoutedEventArgs e)
    {
        try
        {
            if (DataContext is not VistaModeloPrincipal { Impresion.Qsl: { } qsl } modelo) return;
            if (modelo.Entrada.IdDelContactoEnEdicion is not { } id) return;
            VentanaDeEnvioDeQsl.Mostrar(Window.GetWindow(this), qsl, [id]);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Fallo al abrir la QSL desde la ficha del contacto.");
        }
    }
}
