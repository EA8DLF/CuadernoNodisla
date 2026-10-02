using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace Nodisla.Cuaderno.Ui.Vistas;

/// <summary>
/// La página CW: la barra y el frontal del equipo, el decodificador a lo grande, el glosario y el
/// hueco de la transmisión (<see cref="HuecoDeTransmision"/>).
/// </summary>
/// <remarks>
/// El alto se reparte como en Digital: el frontal se queda con lo que sobre tras dar al
/// decodificador su mínimo, hasta 300 puntos, y se pliega solo si no llega a legible.
/// </remarks>
public partial class PestanaCw : UserControl
{
    /// <summary>Lo mínimo que se le da al decodificador y al hueco de transmisión.</summary>
    public const double AltoMinimoDeAbajo = 360;

    /// <summary>Monta la página.</summary>
    public PestanaCw()
    {
        InitializeComponent();

        // Mientras nadie ponga la vista de transmisión, el hueco dice qué irá ahí.
        DependencyPropertyDescriptor.FromProperty(ContentControl.ContentProperty, typeof(ContentControl))
            .AddValueChanged(HuecoDeTransmision, (_, _) => MirarElHueco());
        MirarElHueco();
    }

    /// <summary>Alto que le toca al frontal con el alto de la página y el de la barra.</summary>
    public static double AltoDelFrontal(double altoDeLaPagina, double altoDeLaBarra)
    {
        var sobra = altoDeLaPagina - altoDeLaBarra - 60 - AltoMinimoDeAbajo;
        return sobra < PestanaDigital.AltoMinimoDelFrontal ? 0 : Math.Min(sobra, PestanaDigital.AltoMaximoDelFrontal);
    }

    private void MirarElHueco() =>
        RotuloDelHueco.Visibility = HuecoDeTransmision.Content is null ? Visibility.Visible : Visibility.Collapsed;

    private void AlCambiarDeTamano(object sender, SizeChangedEventArgs e)
    {
        var alto = AltoDelFrontal(Raiz.ActualHeight, Barra.ActualHeight);
        HuecoDelFrontal.Visibility = alto > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (alto > 0)
        {
            Frontal.MaxHeight = alto;
            Frontal.Repartir();
        }
    }
}
