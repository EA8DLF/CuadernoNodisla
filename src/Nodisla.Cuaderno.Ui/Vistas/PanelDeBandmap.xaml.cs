using System.Windows.Controls;

namespace Nodisla.Cuaderno.Ui.Vistas;

/// <summary>
/// El bandmap: los anuncios del cluster colocados por frecuencia en vez de por lista.
/// </summary>
/// <remarks>
/// No hay codigo de vista: las alturas las calcula
/// <see cref="VistaModelos.VistaModeloBandmap"/>, que es donde se sabe que banda se dibuja y
/// donde cae cada anuncio. La vista solo coloca en un lienzo lo que el modelo ya ha medido.
/// </remarks>
public partial class PanelDeBandmap : UserControl
{
    /// <summary>Monta el bandmap.</summary>
    public PanelDeBandmap() => InitializeComponent();

    /// <summary>
    /// Le dice al modelo cuanto sitio hay, que es lo unico que la vista sabe y el modelo no.
    /// </summary>
    /// <remarks>
    /// Se quitan doce puntos por los margenes de arriba y abajo, y no se baja del minimo: con
    /// menos no cabe ni la regla, y estirar los anuncios en dos centimetros no ayudaria a
    /// nadie; para eso esta el desplazamiento.
    /// </remarks>
    private void AlCambiarElAlto(object sender, System.Windows.SizeChangedEventArgs e)
    {
        if (DataContext is not VistaModelos.VistaModeloBandmap modelo) return;

        modelo.AltoDeLaEscala = System.Math.Max(
            VistaModelos.VistaModeloBandmap.AltoMinimo,
            e.NewSize.Height - 12);
    }
}
