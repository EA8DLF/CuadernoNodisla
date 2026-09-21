using System.Windows.Controls;

namespace Nodisla.Cuaderno.Ui.Vistas;

/// <summary>
/// El frontal del equipo, dibujado en vector, con cada mando donde esta en la radio.
/// </summary>
/// <remarks>
/// <para>
/// La disposicion esta <b>verificada</b> con el manual de instrucciones oficial del FT-710,
/// seccion «Front Panel Controls &amp; Switches»: la lista numerada de sus veinticinco mandos y
/// las laminas con las llamadas. No hay nada colocado a ojo.
/// </para>
/// <para>
/// No lleva codigo propio: los botones accionan comandos de
/// <see cref="VistaModelos.VistaModeloEquipo"/> y los mandos giratorios accionan los mismos
/// <see cref="VistaModelos.VistaModeloMando"/> que la lista de «Todos los mandos». El dibujo y
/// la lista son dos vistas de lo mismo.
/// </para>
/// </remarks>
public partial class FrontalFt710 : UserControl
{
    /// <summary>Monta el frontal.</summary>
    public FrontalFt710() => InitializeComponent();
}
