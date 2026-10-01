using System.Windows;
using System.Windows.Controls;

namespace Nodisla.Cuaderno.Ui.Vistas;

/// <summary>Panel de la botonera junto al frontal (ver el XAML).</summary>
public partial class BotoneraHam : UserControl
{
    /// <summary>Monta el panel.</summary>
    public BotoneraHam() => InitializeComponent();

    /// <summary>El operador quiere ver el otro panel en este lado (cuando no caben los dos).</summary>
    public event EventHandler? CambioPedido;

    /// <summary>Ensena la tecla que cambia al otro panel: solo cuando no caben los dos.</summary>
    public bool MostrarCambio
    {
        get => BotonCambiar.Visibility == Visibility.Visible;
        set => BotonCambiar.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AlPedirCambio(object sender, RoutedEventArgs e) => CambioPedido?.Invoke(this, EventArgs.Empty);
}
