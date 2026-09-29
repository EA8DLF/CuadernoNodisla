using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Nodisla.Cuaderno.Ui.VistaModelos;

namespace Nodisla.Cuaderno.Ui.Vistas;

/// <summary>
/// Ajustes del correo de las QSL. Lo unico que hace el codigo es pasar la contraseña tecleada
/// del <see cref="PasswordBox"/> al modelo (no se puede enlazar) y vaciar la casilla al guardarla.
/// </summary>
public partial class AjustesDeCorreo : UserControl
{
    private VistaModeloCorreoQsl? _modelo;

    /// <summary>Crea el apartado.</summary>
    public AjustesDeCorreo()
    {
        InitializeComponent();
        DataContextChanged += AlCambiarElModelo;
    }

    private void AlEscribirLaContrasena(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox campo && DataContext is VistaModeloCorreoQsl modelo) modelo.ContrasenaNueva = campo.Password;
    }

    private void AlCambiarElModelo(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_modelo is not null) _modelo.PropertyChanged -= AlCambiarAlgo;
        _modelo = e.NewValue as VistaModeloCorreoQsl;
        if (_modelo is not null) _modelo.PropertyChanged += AlCambiarAlgo;
    }

    private void AlCambiarAlgo(object? sender, PropertyChangedEventArgs e)
    {
        // Guardada la contraseña, el modelo la olvida y la casilla se vacia: no se queda a la vista.
        if (e.PropertyName == nameof(VistaModeloCorreoQsl.ContrasenaNueva)
            && _modelo is { ContrasenaNueva.Length: 0 }
            && CasillaDeLaContrasena.Password.Length > 0)
        {
            CasillaDeLaContrasena.Clear();
        }
    }
}
