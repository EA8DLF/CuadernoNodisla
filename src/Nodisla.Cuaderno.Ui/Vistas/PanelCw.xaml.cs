using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using Nodisla.Cuaderno.Ui.VistaModelos;

namespace Nodisla.Cuaderno.Ui.Vistas;

/// <summary>El panel de telegrafía de la cabina.</summary>
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
}
