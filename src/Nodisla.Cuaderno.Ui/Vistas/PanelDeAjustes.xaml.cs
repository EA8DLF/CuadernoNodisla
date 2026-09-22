using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Nodisla.Cuaderno.Ui.VistaModelos;

namespace Nodisla.Cuaderno.Ui.Vistas;

/// <summary>
/// La pantalla de ajustes: credenciales, LoTW, cuaderno, equipo y cluster.
/// </summary>
/// <remarks>
/// El codigo de vista es el minimo imprescindible: los dialogos de fichero, que son cosa de la
/// ventana, y el paso del secreto tecleado desde el <see cref="PasswordBox"/> al modelo. Un
/// <c>PasswordBox</c> no se puede enlazar —su contenido no es una propiedad de dependencia, y
/// eso es a proposito: enlazarlo dejaria la contrasena en memoria del enlace—, asi que se
/// copia a mano y el campo se vacia en cuanto se guarda.
/// </remarks>
public partial class PanelDeAjustes : UserControl
{
    /// <summary>Monta la pantalla.</summary>
    public PanelDeAjustes() => InitializeComponent();

    private void AlEscribirElSecreto(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox campo && campo.DataContext is SecretoDeServicio secreto)
        {
            secreto.Nuevo = campo.Password;
        }
    }

    private async void AlPedirImportar(object sender, RoutedEventArgs e)
    {
        if (DataContext is not VistaModeloAjustes modelo) return;

        var dialogo = new OpenFileDialog
        {
            Title = "Elija el fichero ADIF que quiere importar",
            Filter = "Ficheros ADIF (*.adi;*.adif;*.adx)|*.adi;*.adif;*.adx|Todos los ficheros (*.*)|*.*",
            CheckFileExists = true,
        };

        if (dialogo.ShowDialog(Window.GetWindow(this)) != true) return;

        await modelo.ImportarAsync(dialogo.FileName).ConfigureAwait(true);
    }

    private void AlPedirExportar(object sender, RoutedEventArgs e)
    {
        if (DataContext is not VistaModeloAjustes modelo) return;

        var dialogo = new SaveFileDialog
        {
            Title = "Elija dónde guardar el cuaderno en ADIF",
            Filter = "Ficheros ADIF (*.adi)|*.adi",
            FileName = "cuaderno.adi",
        };

        if (dialogo.ShowDialog(Window.GetWindow(this)) != true) return;

        // La exportacion la trae el escritor de ADIF de verdad; mientras no este, se dice.
        modelo.ParteDeLaImportacion =
            "La exportación a ADIF llega con el escritor de ADIF, que todavía no está conectado "
            + "a la interfaz. El fichero no se ha escrito.";
    }
}
