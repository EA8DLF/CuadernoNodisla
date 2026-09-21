using System.Collections.Specialized;
using System.Windows.Controls;
using System.Windows.Input;
using Nodisla.Cuaderno.Ui.VistaModelos;

namespace Nodisla.Cuaderno.Ui.Vistas;

/// <summary>
/// Panel del cluster de DX.
/// </summary>
/// <remarks>
/// Lo poco que hay aqui es lo que es propio de la vista: el doble clic, la tecla Intro de la
/// consola y mantener la consola pegada al final segun van llegando lineas. Lo demas esta en
/// <see cref="VistaModeloCluster"/>.
/// </remarks>
public partial class PanelDeCluster : UserControl
{
    /// <summary>Monta el panel.</summary>
    public PanelDeCluster()
    {
        InitializeComponent();
        DataContextChanged += AlCambiarElModelo;
    }

    private void AlCambiarElModelo(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is VistaModeloCluster anterior)
        {
            ((INotifyCollectionChanged)anterior.Consola).CollectionChanged -= AlCrecerLaConsola;
        }

        if (e.NewValue is VistaModeloCluster nuevo)
        {
            ((INotifyCollectionChanged)nuevo.Consola).CollectionChanged += AlCrecerLaConsola;
        }
    }

    /// <summary>
    /// Deja la consola pegada al final.
    /// </summary>
    /// <remarks>
    /// Una consola que no se desplaza sola es una consola que no se lee: las lineas del nodo
    /// llegan solas y el operador no esta mirando la barra de desplazamiento.
    /// </remarks>
    private void AlCrecerLaConsola(object? origen, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add) DesplazamientoDeLaConsola.ScrollToEnd();
    }

    private void AlPulsarDosVeces(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is VistaModeloCluster modelo) modelo.IrAlSpot(modelo.SpotSeleccionado);
    }

    private void AlTeclearLaOrden(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not VistaModeloCluster modelo) return;

        e.Handled = true;
        if (modelo.EnviarOrdenCommand.CanExecute(null)) modelo.EnviarOrdenCommand.Execute(null);
    }
}
