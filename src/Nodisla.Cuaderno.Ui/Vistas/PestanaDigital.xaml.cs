using System.Windows.Controls;

namespace Nodisla.Cuaderno.Ui.Vistas;

/// <summary>
/// La pestana de los modos digitales.
/// </summary>
/// <remarks>
/// Esta dispuesta alrededor de <b>nuestro</b> modem de FT8 y FT4, que llega en la Fase 4: la
/// cascada manda en la pantalla y el puente con WSJT-X y JTDX ocupa una columna, como una
/// fuente mas. Lo que todavia no existe esta dibujado, apagado y dicho con todas las letras,
/// para que el sitio quede guardado y no haya que deshacer la pestana cuando llegue.
/// </remarks>
public partial class PestanaDigital : UserControl
{
    /// <summary>Monta la pestana.</summary>
    public PestanaDigital() => InitializeComponent();
}
