using System.Windows;
using System.Windows.Controls;

namespace Nodisla.Cuaderno.Ui.Vistas;

/// <summary>
/// El visor del equipo: los dos VFO a la vez, con quien transmite y quien recibe.
/// </summary>
/// <remarks>
/// <para>
/// Ocupa en el frontal dibujado el hueco de la pantalla tactil del equipo y se dibuja como
/// ella: medidor, VFO-A grande, VFO-B, la fila ATT/IPO/DNF/AGC, el analizador y las teclas.
/// </para>
/// <para>
/// EL ANALIZADOR ES UN HUECO CON NOMBRE, <c>AreaDelAnalizador</c>. Lo que se pinta dentro
/// llega por <see cref="ContenidoDelAnalizador"/>; mientras nadie lo ponga, va la cascada del
/// audio de recepcion. Sus rotulos —la linea de encima y la escala de debajo— se leen de
/// <see cref="RotuloDelAnalizador"/> y <see cref="AnchoDelAnalizadorHz"/>.
/// </para>
/// </remarks>
public partial class VisorDeVfos : UserControl
{
    /// <summary>Lo que se pinta en el analizador. Nulo: la cascada del audio.</summary>
    public static readonly DependencyProperty ContenidoDelAnalizadorProperty = DependencyProperty.Register(
        nameof(ContenidoDelAnalizador), typeof(object), typeof(VisorDeVfos), new PropertyMetadata(null));

    /// <summary>Linea de encima del analizador (p. ej. «CENTER FAST1 SPAN 200kHz»). Vacia: la del audio.</summary>
    public static readonly DependencyProperty RotuloDelAnalizadorProperty = DependencyProperty.Register(
        nameof(RotuloDelAnalizador), typeof(string), typeof(VisorDeVfos), new PropertyMetadata(string.Empty));

    /// <summary>Ancho del analizador en hercios, para la escala. Cero: la escala del audio.</summary>
    public static readonly DependencyProperty AnchoDelAnalizadorHzProperty = DependencyProperty.Register(
        nameof(AnchoDelAnalizadorHz), typeof(double), typeof(VisorDeVfos), new PropertyMetadata(0.0));

    /// <summary>En FIX, donde empieza la escala del analizador (hercios). Cero: no esta en FIX.</summary>
    public static readonly DependencyProperty InicioFijoDelAnalizadorHzProperty = DependencyProperty.Register(
        nameof(InicioFijoDelAnalizadorHz), typeof(double), typeof(VisorDeVfos), new PropertyMetadata(0.0));

    /// <summary>EXPAND: el analizador se come la fila de casillas y gana alto.</summary>
    public static readonly DependencyProperty AnalizadorAmpliadoProperty = DependencyProperty.Register(
        nameof(AnalizadorAmpliado), typeof(bool), typeof(VisorDeVfos), new PropertyMetadata(false));

    /// <summary>Monta el visor.</summary>
    public VisorDeVfos() => InitializeComponent();

    /// <summary>Lo que se pinta en el analizador. Nulo: la cascada del audio.</summary>
    public object? ContenidoDelAnalizador
    {
        get => GetValue(ContenidoDelAnalizadorProperty);
        set => SetValue(ContenidoDelAnalizadorProperty, value);
    }

    /// <summary>Linea de encima del analizador. Vacia: la del audio.</summary>
    public string RotuloDelAnalizador
    {
        get => (string)GetValue(RotuloDelAnalizadorProperty);
        set => SetValue(RotuloDelAnalizadorProperty, value);
    }

    /// <summary>Ancho del analizador en hercios, para la escala. Cero: la escala del audio.</summary>
    public double AnchoDelAnalizadorHz
    {
        get => (double)GetValue(AnchoDelAnalizadorHzProperty);
        set => SetValue(AnchoDelAnalizadorHzProperty, value);
    }

    /// <summary>En FIX, donde empieza la escala del analizador (hercios). Cero: no esta en FIX.</summary>
    public double InicioFijoDelAnalizadorHz
    {
        get => (double)GetValue(InicioFijoDelAnalizadorHzProperty);
        set => SetValue(InicioFijoDelAnalizadorHzProperty, value);
    }

    /// <summary>EXPAND: el analizador se come la fila de casillas y gana alto.</summary>
    public bool AnalizadorAmpliado
    {
        get => (bool)GetValue(AnalizadorAmpliadoProperty);
        set => SetValue(AnalizadorAmpliadoProperty, value);
    }
}
