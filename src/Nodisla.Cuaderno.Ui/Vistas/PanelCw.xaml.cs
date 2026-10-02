using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using Nodisla.Cuaderno.Ui.VistaModelos;

namespace Nodisla.Cuaderno.Ui.Vistas;

/// <summary>El decodificador de telegrafía a lo grande, en la página CW.</summary>
public partial class PanelCw : UserControl
{
    private VistaModeloCw? _modelo;

    /// <summary>Monta el panel.</summary>
    public PanelCw()
    {
        InitializeComponent();
        DataContextChanged += AlCambiarElModelo;
    }

    private void AlCambiarElModelo(object? origen, DependencyPropertyChangedEventArgs e)
    {
        if (_modelo is not null) _modelo.Principal.Palabras.CollectionChanged -= AlLlegarTexto;
        _modelo = DataContext as VistaModeloCw;
        if (_modelo is not null) _modelo.Principal.Palabras.CollectionChanged += AlLlegarTexto;
    }

    /// <summary>El texto nuevo se ve siempre: se baja al final, salvo que el operador esté leyendo más arriba.</summary>
    private void AlLlegarTexto(object? origen, NotifyCollectionChangedEventArgs e)
    {
        var abajo = Desplazamiento.VerticalOffset >= Desplazamiento.ScrollableHeight - 4;
        if (abajo) Dispatcher.BeginInvoke(Desplazamiento.ScrollToEnd, System.Windows.Threading.DispatcherPriority.Background);
    }

    /// <summary>La rueda sobre la casilla del tono: ±10 Hz por paso.</summary>
    private void AlGirarLaRueda(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        if (_modelo is null) return;
        if (e.Delta > 0) _modelo.SubirTono();
        else if (e.Delta < 0) _modelo.BajarTono();
        e.Handled = true;
    }
}
