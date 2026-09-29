using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Nodisla.Cuaderno.Ui.VistaModelos;

namespace Nodisla.Cuaderno.Ui.Vistas;

/// <summary>
/// El apartado de conexion al cluster de la pantalla de ajustes.
/// </summary>
/// <remarks>
/// Lo unico que hay aqui es el paso de la contrasena tecleada al modelo. Un
/// <see cref="PasswordBox"/> no se puede enlazar —su contenido no es una propiedad de
/// dependencia, y es a proposito—, asi que se copia a mano y el campo se vacia al guardar.
/// </remarks>
public partial class ConexionDelCluster : UserControl
{
    private VistaModeloAjustesCluster? _modelo;

    /// <summary>Monta la vista.</summary>
    public ConexionDelCluster()
    {
        InitializeComponent();
        DataContextChanged += AlCambiarElModelo;
    }

    private void AlEscribirLaContrasena(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox campo && DataContext is VistaModeloAjustesCluster modelo)
        {
            modelo.ContrasenaNueva = campo.Password;
        }
    }

    private void AlCambiarElModelo(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_modelo is not null) _modelo.PropertyChanged -= AlCambiarAlgoDelModelo;
        _modelo = e.NewValue as VistaModeloAjustesCluster;
        if (_modelo is not null) _modelo.PropertyChanged += AlCambiarAlgoDelModelo;
    }

    /// <summary>
    /// Cuando el modelo vacia la contrasena —al guardarla o al borrarla— se vacian los puntos.
    /// </summary>
    /// <remarks>
    /// Antes se quedaban: se guardaba, pero la casilla seguia llena y parecia que no.
    /// </remarks>
    private void AlCambiarAlgoDelModelo(object? origen, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(VistaModeloAjustesCluster.ContrasenaNueva)
            && _modelo is { ContrasenaNueva.Length: 0 }
            && CasillaDeLaContrasena.Password.Length > 0)
        {
            CasillaDeLaContrasena.Clear();
        }
    }
}
