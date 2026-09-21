using System.Windows;

namespace Nodisla.Cuaderno.Ui.Vistas;

/// <summary>
/// Ventana de confirmacion propia. No se usa el cuadro de dialogo de Windows porque sus botones
/// salen en el idioma del sistema, y aqui todo tiene que estar en espanol.
/// </summary>
public partial class VentanaDeConfirmacion : Window
{
    /// <summary>Crea la ventana vacia; el texto se pone con las propiedades.</summary>
    public VentanaDeConfirmacion() => InitializeComponent();

    /// <summary>Titulo grande de la ventana.</summary>
    public string Titulo
    {
        get => TextoTitulo.Text;
        set { TextoTitulo.Text = value; Title = value; }
    }

    /// <summary>Explicacion de lo que va a pasar.</summary>
    public string Detalle
    {
        get => TextoDetalle.Text;
        set => TextoDetalle.Text = value;
    }

    /// <summary>Texto del boton que confirma.</summary>
    public string TextoDeAceptar
    {
        get => (string)BotonAceptar.Content;
        set => BotonAceptar.Content = value;
    }

    /// <summary>Si es cierto, la ventana solo informa y no ofrece cancelar.</summary>
    public bool SoloAviso
    {
        get => BotonCancelar.Visibility != Visibility.Visible;
        set => BotonCancelar.Visibility = value ? Visibility.Collapsed : Visibility.Visible;
    }

    private void AlAceptar(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void AlCancelar(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
