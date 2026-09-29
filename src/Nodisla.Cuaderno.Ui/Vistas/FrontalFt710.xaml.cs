using System.Windows.Controls;
using System.Windows.Input;
using Nodisla.Cuaderno.Ui.VistaModelos;

namespace Nodisla.Cuaderno.Ui.Vistas;

/// <summary>
/// El frontal del equipo, dibujado en vector, con cada mando donde esta en la radio.
/// </summary>
/// <remarks>
/// <para>
/// La disposicion sale del manual oficial del FT-710 («Front Panel Controls &amp; Switches») y
/// las proporciones y el aspecto, de las fotos de la radio del operador
/// (docs/capturas/referencia-ft710-*.jpg), medidas sobre ellas. Comparativa en
/// docs/capturas/frontal-vs-real.png.
/// </para>
/// <para>
/// Los botones accionan comandos de <see cref="VistaModeloEquipo"/> y los mandos giratorios los
/// mismos <see cref="VistaModeloMando"/> que la lista de «Todos los mandos». Aqui solo se pasa la
/// rueda del raton a muescas: el dial, el anillo STEP/MCH y la rueda sobre CLAR
/// (docs/15-botones-ft710-validados.md).
/// </para>
/// </remarks>
public partial class FrontalFt710 : UserControl
{
    /// <summary>Monta el frontal.</summary>
    public FrontalFt710() => InitializeComponent();

    private VistaModeloEquipo? Equipo => DataContext as VistaModeloEquipo;

    private async void AlGirarElDial(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        if (Equipo is { } equipo) await equipo.GirarDialAsync(e.Delta > 0 ? 1 : -1).ConfigureAwait(true);
    }

    private async void AlTeclearElDial(object sender, KeyEventArgs e)
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

    private async void AlGirarLosPasos(object? sender, int muescas)
    {
        if (Equipo is { } equipo) await equipo.GirarPasosAsync(muescas).ConfigureAwait(true);
    }

    // ── ⏻LOCK: corta = bloqueo, larga = encender/apagar ─────────────────
    private System.Windows.Threading.DispatcherTimer? _pulsacionLarga;
    private bool _lockLargoHecho;

    private void AlPulsarLock(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _lockLargoHecho = false;
        TeclaLock.Opacity = 0.7;
        _pulsacionLarga?.Stop();
        _pulsacionLarga = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _pulsacionLarga.Tick += async (_, _) =>
        {
            _pulsacionLarga?.Stop();
            _lockLargoHecho = true;
            TeclaLock.Opacity = 1;
            if (Equipo is { } equipo) await equipo.EncenderOApagarAsync().ConfigureAwait(true);
        };
        _pulsacionLarga.Start();
    }

    private void AlSoltarLock(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        TeclaLock.Opacity = 1;
        var eraCorta = _pulsacionLarga?.IsEnabled == true && !_lockLargoHecho;
        _pulsacionLarga?.Stop();
        if (!eraCorta || Equipo is not { } equipo) return;

        var alternar = equipo.AlternarMandoCommand;
        if (alternar.CanExecute(Aplicacion.Puertos.MandoDeEquipo.Bloqueo))
            alternar.Execute(Aplicacion.Puertos.MandoDeEquipo.Bloqueo);
    }

    private void AlSalirDeLock(object sender, MouseEventArgs e)
    {
        // Salirse del botón con el ratón pulsado anula la pulsación, como en cualquier botón.
        _pulsacionLarga?.Stop();
        TeclaLock.Opacity = 1;
    }

    private void AlGirarSobreClar(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        Equipo?.MoverClarificador(e.Delta > 0 ? 1 : -1);
    }
}
