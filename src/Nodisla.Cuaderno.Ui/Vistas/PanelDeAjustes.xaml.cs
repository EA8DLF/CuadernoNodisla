using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Nodisla.Cuaderno.Ui.VistaModelos;

namespace Nodisla.Cuaderno.Ui.Vistas;

/// <summary>
/// La pantalla de ajustes: credenciales, LoTW, cuaderno, equipo y cluster.
/// </summary>
/// <remarks>
/// <para>
/// El codigo de vista es el minimo imprescindible: los dialogos de fichero, que son cosa de la
/// ventana, y el paso del secreto tecleado desde el <see cref="PasswordBox"/> al modelo. Un
/// <c>PasswordBox</c> no se puede enlazar —su contenido no es una propiedad de dependencia, y
/// eso es a proposito: enlazarlo dejaria la contrasena en memoria del enlace—, asi que se
/// copia a mano y el campo se vacia en cuanto se guarda.
/// </para>
/// <para>
/// Los botones de importar y exportar van por orden del modelo; aqui solo se le dice al
/// modelo COMO preguntar el fichero, que es un dialogo de Windows.
/// </para>
/// </remarks>
public partial class PanelDeAjustes : UserControl
{
    /// <summary>Monta la pantalla.</summary>
    public PanelDeAjustes()
    {
        InitializeComponent();
        DataContextChanged += AlCambiarElModelo;
    }

    private void AlCambiarElModelo(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not VistaModeloAjustes modelo) return;

        modelo.ElegirFicheroParaImportar ??= PreguntarQueImportar;
        modelo.ElegirFicheroParaExportar ??= PreguntarDondeExportar;
    }

    private void AlEscribirElSecreto(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox campo && campo.DataContext is SecretoDeServicio secreto)
        {
            secreto.Nuevo = campo.Password;
        }
    }

    /// <summary>
    /// Engancha la casilla a su secreto para vaciarla cuando el modelo lo vacia.
    /// </summary>
    /// <remarks>
    /// Antes, al pulsar Guardar se guardaba, pero los puntos seguian en la casilla: parecia que
    /// no habia pasado nada y se volvia a pulsar, o se tecleaba encima y se guardaba mal.
    /// </remarks>
    private void AlMontarLaCasillaDelSecreto(object sender, RoutedEventArgs e)
    {
        if (sender is not PasswordBox campo || campo.DataContext is not SecretoDeServicio secreto) return;

        PropertyChangedEventHandler vaciar = (_, cambio) =>
        {
            if (cambio.PropertyName == nameof(SecretoDeServicio.Nuevo)
                && secreto.Nuevo.Length == 0
                && campo.Password.Length > 0)
            {
                campo.Clear();
            }
        };

        secreto.PropertyChanged += vaciar;
        campo.Tag = vaciar;
    }

    private void AlDesmontarLaCasillaDelSecreto(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox { DataContext: SecretoDeServicio secreto, Tag: PropertyChangedEventHandler vaciar } campo)
        {
            secreto.PropertyChanged -= vaciar;
            campo.Tag = null;
        }
    }

    private string? PreguntarQueImportar()
    {
        var dialogo = new OpenFileDialog
        {
            Title = "Elija el fichero ADIF que quiere importar",
            Filter = "Ficheros ADIF (*.adi;*.adif;*.adx)|*.adi;*.adif;*.adx|Todos los ficheros (*.*)|*.*",
            CheckFileExists = true,
        };

        return dialogo.ShowDialog(Window.GetWindow(this)) == true ? dialogo.FileName : null;
    }

    private string? PreguntarDondeExportar()
    {
        var dialogo = new SaveFileDialog
        {
            Title = "Elija dónde guardar el cuaderno en ADIF",
            Filter = "Ficheros ADIF (*.adi)|*.adi",
            FileName = $"cuaderno-{DateTime.Now:yyyy-MM-dd}.adi",
            OverwritePrompt = true,
        };

        return dialogo.ShowDialog(Window.GetWindow(this)) == true ? dialogo.FileName : null;
    }
}
