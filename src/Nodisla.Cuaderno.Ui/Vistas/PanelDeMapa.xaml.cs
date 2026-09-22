using System.Windows;
using System.Windows.Controls;
using Nodisla.Cuaderno.Ui.Mapa;
using Nodisla.Cuaderno.Ui.VistaModelos;

namespace Nodisla.Cuaderno.Ui.Vistas;

/// <summary>
/// Panel del mapa. Envuelve el control del mapa y le pasa lo que dice el modelo de vista.
/// </summary>
/// <remarks>
/// <para>
/// Es la unica clase de la interfaz que toca el control del mapa, y solo lo hace para tres
/// cosas que son propias de la vista: encuadrar el mundo, avisar de que se ha tocado una marca
/// y soltarlo cuando se cierra la ventana. El motor de mapas no sale de su proyecto.
/// </para>
/// <para>
/// <b>El mapa NO se suelta al descargarse el panel.</b> Aqui hubo un fallo que dejaba el mapa
/// en negro: el panel vive dentro de una pestana, y en WPF el contenido de una pestana se
/// DESCARGA cada vez que se cambia a otra. Soltar el control ahi lo mataba a la primera
/// pasada por otra pestana —se desmontaba el motor de mapas, se paraban sus relojes y se
/// soltaban sus enganches—, y al volver quedaba un recuadro negro con el contador de spots
/// debajo, como si el mapa no tuviera datos. Los tenia todos; lo que ya no habia era quien
/// los pintara. Se suelta cuando se cierra la ventana, que es cuando de verdad sobra.
/// </para>
/// </remarks>
public partial class PanelDeMapa : UserControl
{
    private Window? _ventana;

    /// <summary>Monta el panel.</summary>
    public PanelDeMapa()
    {
        InitializeComponent();
        Loaded += AlCargar;
    }

    /// <summary>
    /// Se engancha al cierre de la ventana para soltar el mapa entonces, y no antes.
    /// </summary>
    /// <remarks>
    /// Va en <c>Loaded</c> y no en el constructor porque hasta que el panel no esta montado no
    /// hay ventana a la que preguntar. Se engancha una sola vez, aunque la pestana entre y
    /// salga muchas veces.
    /// </remarks>
    private void AlCargar(object sender, RoutedEventArgs e)
    {
        if (_ventana is not null) return;

        _ventana = Window.GetWindow(this);
        if (_ventana is null) return;

        _ventana.Closed += AlCerrarLaVentana;
    }

    private void AlCerrarLaVentana(object? origen, EventArgs e)
    {
        if (_ventana is not null) _ventana.Closed -= AlCerrarLaVentana;
        Mapa.Dispose();
    }

    private void AlElegirUnaMarca(object? origen, MarcaDelMapa marca)
    {
        if (DataContext is VistaModeloMapa modelo) modelo.ElegirMarca(marca);
    }

    private void AlPedirElMundo(object sender, RoutedEventArgs e) => Mapa.VerElMundo();
}
