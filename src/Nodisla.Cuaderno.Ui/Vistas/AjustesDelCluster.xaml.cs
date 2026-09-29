using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Nodisla.Cuaderno.Ui.VistaModelos;

namespace Nodisla.Cuaderno.Ui.Vistas;

/// <summary>
/// La configuracion del cluster de DX, con su consola cruda.
/// </summary>
/// <remarks>
/// Esto vivia en la pantalla de operar y de ahi se ha quitado: mientras se opera se leen los
/// anuncios, no las ordenes del nodo. En operar se queda el estado de la conexion, que eso si
/// se mira. Aqui queda lo que hace falta para configurar y para diagnosticar.
/// </remarks>
public partial class AjustesDelCluster : UserControl
{
    /// <summary>Monta la vista.</summary>
    public AjustesDelCluster()
    {
        InitializeComponent();
        DataContextChanged += AlCambiarElModelo;
    }

    private void AlCambiarElModelo(object sender, DependencyPropertyChangedEventArgs e)
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
    /// llegan solas y quien mira no esta pendiente de la barra de desplazamiento.
    /// </remarks>
    private void AlCrecerLaConsola(object? origen, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add) DesplazamientoDeLaConsola.ScrollToEnd();
    }

    private void AlTeclearLaOrden(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not VistaModeloCluster modelo) return;

        e.Handled = true;
        if (modelo.EnviarOrdenCommand.CanExecute(null)) modelo.EnviarOrdenCommand.Execute(null);
    }
}
