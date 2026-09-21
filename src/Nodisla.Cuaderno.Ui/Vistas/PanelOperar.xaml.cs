using System.Windows;
using System.Windows.Controls;
using Nodisla.Cuaderno.Ui.VistaModelos;

namespace Nodisla.Cuaderno.Ui.Vistas;

/// <summary>
/// La cabina de operacion: el equipo arriba, la entrada y el cluster debajo, y los
/// decodificados de los modos digitales en la franja inferior.
/// </summary>
/// <remarks>
/// Lo unico que hace el codigo de esta vista es decidir cuando el frontal dibujado deja de
/// caber. Un dibujo vectorial no se repliega: o entra entero o hay que sustituirlo. Por eso,
/// cuando el ancho o la escala de letra lo dejan ilegible, el frontal cede el sitio a la lista
/// de mandos, que si se repliega. El operador no puede quedarse sin poder tocar la radio por
/// haber subido el tamano de la letra.
/// </remarks>
public partial class PanelOperar : UserControl
{
    /// <summary>
    /// Ancho minimo de la cabina para que el frontal dibujado siga sirviendo, en letras.
    /// </summary>
    /// <remarks>
    /// El dibujo mide mil puntos de ancho y su bloque se lleva alrededor de la mitad de la
    /// cabina. Por debajo de unas noventa letras el escalado lo deja tan pequeno que los
    /// rotulos de los mandos no se leen, y un mando que no se lee no se puede usar.
    /// </remarks>
    private const double LetrasQueNecesitaElFrontal = 90.0;

    /// <summary>Monta la cabina.</summary>
    public PanelOperar() => InitializeComponent();

    /// <summary>Trae el foco al indicativo, que es donde se empieza a teclear.</summary>
    public void EnfocarIndicativo() => Entrada.EnfocarIndicativo();

    private void AlCambiarDeTamano(object sender, SizeChangedEventArgs e)
    {
        if (DataContext is not VistaModeloPrincipal modelo) return;

        var letra = modelo.TamanoDeLetra > 0 ? modelo.TamanoDeLetra : 14.0;
        modelo.FrontalDibujadoSinSitio = ActualWidth < letra * LetrasQueNecesitaElFrontal;
    }
}
