using System.ComponentModel;
using System.Windows.Controls;
using System.Windows;
using System.Windows.Input;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Idiomas;
using Nodisla.Cuaderno.Ui.VistaModelos;
using Serilog;

namespace Nodisla.Cuaderno.Ui.Vistas;

/// <summary>
/// La rejilla del cuaderno, con sus filtros y su paginacion.
/// </summary>
/// <remarks>
/// El codigo de aqui es el que estaba en la ventana principal, mudado sin cambios: ordenar al
/// pulsar una cabecera, modificar al pulsar dos veces y borrar con la tecla Suprimir.
/// </remarks>
public partial class PanelDelCuaderno : UserControl
{
    /// <summary>Monta el panel.</summary>
    public PanelDelCuaderno() => InitializeComponent();

    /// <summary>Salta cuando algo del panel falla y hay que ensenarlo.</summary>
    public event EventHandler<Exception>? Fallo;

    /// <summary>Trae el foco al campo de busqueda.</summary>
    /// <remarks>
    /// Si la pestaña aun no se ha dibujado —la primera vez que se pulsa F3 desde otra—, el
    /// campo todavia no es visible y <c>Focus</c> no hace nada: el foco se quedaba en la
    /// pestaña. Entonces se espera a que el campo se vea.
    /// </remarks>
    public void EnfocarBusqueda()
    {
        if (CampoBusqueda.IsVisible)
        {
            CampoBusqueda.Focus();
            CampoBusqueda.SelectAll();
            return;
        }

        void AlVerse(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (!CampoBusqueda.IsVisible) return;
            CampoBusqueda.IsVisibleChanged -= AlVerse;
            Dispatcher.BeginInvoke(() =>
            {
                CampoBusqueda.Focus();
                CampoBusqueda.SelectAll();
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        }

        CampoBusqueda.IsVisibleChanged += AlVerse;
    }

    /// <summary>Pone la flecha de orden en la columna por la que se esta ordenando.</summary>
    public void MarcarOrdenEnLasCabeceras()
    {
        if (DataContext is not VistaModeloPrincipal modelo) return;

        var campo = modelo.Cuaderno.OrdenarPor.ToString();
        var sentido = modelo.Cuaderno.Descendente
            ? ListSortDirection.Descending
            : ListSortDirection.Ascending;

        // Fecha y hora ordenan por el mismo campo: la flecha va solo en la primera, no en
        // las dos, que parecia un orden doble.
        var marcada = false;
        foreach (var columna in RejillaDelCuaderno.Columns)
        {
            var esta = !marcada && string.Equals(columna.SortMemberPath, campo, StringComparison.Ordinal);
            columna.SortDirection = esta ? sentido : null;
            marcada |= esta;
        }
    }

    /// <summary>
    /// La rejilla solo tiene una pagina cargada, asi que ordenarla por su cuenta daria un
    /// resultado falso: se cancela y se vuelve a pedir el cuaderno ya ordenado.
    /// </summary>
    private async void AlOrdenar(object sender, DataGridSortingEventArgs e)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(e);
            e.Handled = true;

            if (DataContext is not VistaModeloPrincipal modelo) return;

            // La cabecera lleva en SortMemberPath el nombre del campo de CampoDeOrden; una
            // columna sin nombre valido —o que no se puede ordenar— simplemente no ordena.
            if (!Enum.TryParse<CampoDeOrden>(e.Column.SortMemberPath, out var campo)) return;

            await modelo.Cuaderno.OrdenarPorCampoAsync(campo).ConfigureAwait(true);
            MarcarOrdenEnLasCabeceras();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Fallo al ordenar el cuaderno.");
            Fallo?.Invoke(this, ex);
        }
    }

    /// <summary>
    /// «Ver/enviar QSL…»: abre la ventana de la tarjeta con las filas elegidas (o la activa).
    /// </summary>
    private void AlVerEnviarQsl(object sender, RoutedEventArgs e)
    {
        try
        {
            if (DataContext is not VistaModeloPrincipal modelo) return;
            if (modelo.Impresion.Qsl is not { } qsl)
            {
                MessageBox.Show(Window.GetWindow(this), Textos.T("Libro.QslNoDisponible"), Textos.T("Comun.NombreDelPrograma"));
                return;
            }

            var ids = RejillaDelCuaderno.SelectedItems.OfType<FilaDeQso>().Select(f => f.Id).ToList();
            if (ids.Count == 0 && modelo.Cuaderno.FilaSeleccionada is { } fila) ids.Add(fila.Id);
            if (ids.Count == 0)
            {
                MessageBox.Show(Window.GetWindow(this), Textos.T("Libro.ElijaContactos"), Textos.T("Comun.NombreDelPrograma"));
                return;
            }

            VentanaDeEnvioDeQsl.Mostrar(Window.GetWindow(this), qsl, ids);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Fallo al abrir la ventana de QSL desde el cuaderno.");
            Fallo?.Invoke(this, ex);
        }
    }

    private void AlModificarDesdeElMenu(object sender, RoutedEventArgs e)
    {
        if (DataContext is VistaModeloPrincipal modelo) modelo.Cuaderno.EditarSeleccionado();
    }

    private void AlPulsarDosVeces(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is VistaModeloPrincipal modelo) modelo.Cuaderno.EditarSeleccionado();
    }

    private async void AlTeclearEnLaRejilla(object sender, KeyEventArgs e)
    {
        try
        {
            if (DataContext is not VistaModeloPrincipal modelo) return;

            if (e.Key == Key.Delete)
            {
                e.Handled = true;
                await modelo.Cuaderno.EliminarSeleccionadoAsync().ConfigureAwait(true);
            }
            else if (e.Key == Key.Enter)
            {
                e.Handled = true;
                modelo.Cuaderno.EditarSeleccionado();
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Fallo operando sobre la rejilla del cuaderno.");
            Fallo?.Invoke(this, ex);
        }
    }
}
