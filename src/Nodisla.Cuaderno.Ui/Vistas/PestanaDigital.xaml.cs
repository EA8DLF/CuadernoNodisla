using System.Windows;
using System.Windows.Controls;

namespace Nodisla.Cuaderno.Ui.Vistas;

/// <summary>
/// La pestana de los modos digitales: la barra del equipo, el frontal si cabe y el modem.
/// </summary>
/// <remarks>
/// El reparto del alto manda el modem: primero se le da lo minimo para operar
/// (<see cref="PanelDelModem.AltoMinimo"/>) y el frontal se queda con lo que sobre, hasta 300
/// puntos. Por debajo de <see cref="AltoMinimoDelFrontal"/> no se lee, asi que se pliega y no
/// ocupa nada: el frontal sigue entero en Operar.
/// </remarks>
public partial class PestanaDigital : UserControl
{
    /// <summary>Alto por debajo del cual el frontal no se lee y no merece la pena pintarlo.</summary>
    public const double AltoMinimoDelFrontal = 150;

    /// <summary>Alto con el que se pinta el frontal cuando sobra sitio.</summary>
    public const double AltoMaximoDelFrontal = 300;

    /// <summary>Margenes y rellenos de las tarjetas que rodean al frontal y al modem.</summary>
    private const double Rellenos = 60;

    /// <summary>Monta la pestana.</summary>
    public PestanaDigital() => InitializeComponent();

    /// <summary>Alto que le toca al frontal con el alto de la pestana y el de la barra.</summary>
    public static double AltoDelFrontal(double altoDeLaPestana, double altoDeLaBarra)
    {
        var sobra = altoDeLaPestana - altoDeLaBarra - Rellenos - PanelDelModem.AltoMinimo;
        return sobra < AltoMinimoDelFrontal ? 0 : Math.Min(sobra, AltoMaximoDelFrontal);
    }

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
