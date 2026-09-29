using System.Windows.Controls;
using System.Windows.Input;
using Nodisla.Cuaderno.Ui.VistaModelos;

namespace Nodisla.Cuaderno.Ui.Vistas.Frontales;

/// <summary>El dial principal de los frontales dibujados: rueda y flechas mueven el dial del equipo.</summary>
public partial class DialDelFrontal : UserControl
{
    /// <summary>Monta el dial.</summary>
    public DialDelFrontal() => InitializeComponent();

    private VistaModeloEquipo? Equipo => DataContext as VistaModeloEquipo;

    private async void AlGirar(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        if (Equipo is { } equipo) await equipo.GirarDialAsync(e.Delta > 0 ? 1 : -1).ConfigureAwait(true);
    }

    private async void AlTeclear(object sender, KeyEventArgs e)
    {
        var muescas = e.Key switch
        {
            Key.Up or Key.Right => 1,
            Key.Down or Key.Left => -1,
            _ => 0,
        };

        if (muescas == 0 || Equipo is not { } equipo) return;
        e.Handled = true;
        await equipo.GirarDialAsync(muescas).ConfigureAwait(true);
    }
}
