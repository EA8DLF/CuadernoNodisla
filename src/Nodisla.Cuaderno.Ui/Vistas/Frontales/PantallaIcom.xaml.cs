using System.Windows;
using System.Windows.Controls;

namespace Nodisla.Cuaderno.Ui.Vistas.Frontales;

/// <summary>La pantalla táctil a color de los ICOM, en lo esencial (PantallaIcom.xaml).</summary>
public partial class PantallaIcom : UserControl
{
    /// <summary>MAIN y SUB a la par, como la doble recepción del IC-7610.</summary>
    public static readonly DependencyProperty DosReceptoresProperty = DependencyProperty.Register(
        nameof(DosReceptores), typeof(bool), typeof(PantallaIcom),
        new PropertyMetadata(false, (d, _) => ((PantallaIcom)d).Repartir()));

    /// <summary>El equipo tiene analizador de espectro.</summary>
    public static readonly DependencyProperty ConAnalizadorProperty = DependencyProperty.Register(
        nameof(ConAnalizador), typeof(bool), typeof(PantallaIcom),
        new PropertyMetadata(true, (d, _) => ((PantallaIcom)d).Repartir()));

    /// <summary>Monta la pantalla.</summary>
    public PantallaIcom()
    {
        InitializeComponent();
        Repartir();
    }

    /// <summary>MAIN y SUB a la par.</summary>
    public bool DosReceptores
    {
        get => (bool)GetValue(DosReceptoresProperty);
        set => SetValue(DosReceptoresProperty, value);
    }

    /// <summary>El equipo tiene analizador de espectro.</summary>
    public bool ConAnalizador
    {
        get => (bool)GetValue(ConAnalizadorProperty);
        set => SetValue(ConAnalizadorProperty, value);
    }

    private void Repartir()
    {
        if (UnVfo is null) return;
        UnVfo.Visibility = DosReceptores ? Visibility.Collapsed : Visibility.Visible;
        DosVfos.Visibility = DosReceptores ? Visibility.Visible : Visibility.Collapsed;
        OtroVfo.Visibility = DosReceptores ? Visibility.Hidden : Visibility.Visible;
        HuecoDelAnalizador.Visibility = ConAnalizador ? Visibility.Visible : Visibility.Collapsed;
        SinAnalizador.Visibility = ConAnalizador ? Visibility.Collapsed : Visibility.Visible;
    }
}
