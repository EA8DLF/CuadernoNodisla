using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Nodisla.Cuaderno.Idiomas;
using Nodisla.Cuaderno.Ui.VistaModelos;

namespace Nodisla.Cuaderno.Ui.Vistas;

/// <summary>El panel de RTTY: terminal de recepción y línea de transmisión (página Operar › RTTY).</summary>
public partial class PanelRtty : UserControl
{
    private VistaModeloRtty? _modelo;

    /// <summary>Monta el panel.</summary>
    public PanelRtty()
    {
        InitializeComponent();
        DataContextChanged += AlCambiarElModelo;
    }

    private void AlCambiarElModelo(object? origen, DependencyPropertyChangedEventArgs e)
    {
        if (_modelo is not null) _modelo.PropertyChanged -= AlCambiarAlgo;
        _modelo = DataContext as VistaModeloRtty;
        if (_modelo is not null)
        {
            _modelo.PropertyChanged += AlCambiarAlgo;
            _modelo.ConfirmarQueVaATransmitir ??= PreguntarSiTransmite;
        }
    }

    /// <summary>
    /// La misma pregunta (y el mismo «no volver a preguntar») que CW y la fonía: ver
    /// <c>MacrosCw.PreguntarSiTransmite</c> y <c>PanelDeFonia.PreguntarSiTransmite</c>.
    /// </summary>
    private bool PreguntarSiTransmite(string mensaje)
    {
        var dialogo = new VentanaDeConfirmacion
        {
            Owner = Window.GetWindow(this),
            Titulo = Textos.T("Cabina.Rtty.Confirmar.Titulo"),
            Detalle = Textos.F("Cabina.Rtty.Confirmar.Detalle", mensaje),
            TextoDeAceptar = Textos.T("Principal.Confirmar.TransmitirAceptar"),
            OfrecerNoVolverAPreguntar = true,
        };

        var si = dialogo.ShowDialog() == true;
        if (si && dialogo.NoVolverAPreguntar) _modelo?.NoVolverAPreguntarAlTransmitir();
        return si;
    }

    /// <summary>El texto nuevo se ve siempre: se baja al final, salvo que el operador esté leyendo más arriba.</summary>
    private void AlCambiarAlgo(object? origen, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(VistaModeloRtty.TextoRecibido)) return;
        var abajo = Terminal.VerticalOffset >= Terminal.ExtentHeight - Terminal.ViewportHeight - 4;
        if (abajo) Dispatcher.BeginInvoke(Terminal.ScrollToEnd, System.Windows.Threading.DispatcherPriority.Background);
    }

    /// <summary>La rueda sobre la casilla del tono: ±10 Hz por paso.</summary>
    private void AlGirarLaRueda(object sender, MouseWheelEventArgs e)
    {
        if (_modelo is null) return;
        if (e.Delta > 0) _modelo.SubirTono();
        else if (e.Delta < 0) _modelo.BajarTono();
        e.Handled = true;
    }

    /// <summary>Intro en la caja de texto a emitir: manda (o encola) lo escrito.</summary>
    private async void AlTeclearElTextoAEmitir(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || _modelo is null) return;
        e.Handled = true;
        await _modelo.EmitirCommand.ExecuteAsync(null);
    }
}
