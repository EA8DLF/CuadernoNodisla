using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Nodisla.Cuaderno.Ui.Vistas;

/// <summary>
/// La cascada del modem propio, con sus dos cursores de sintonia: recepcion y transmision.
/// </summary>
/// <remarks>
/// <para>
/// La imagen se pinta fuera —en <see cref="Recursos.PintorDeCascada"/>— y llega hecha. Aqui
/// solo se estira, se ponen los cursores donde toca y se recoge la pulsacion que cambia los
/// tonos: clic mueve la recepcion (y la transmision, si no esta fijada con
/// <see cref="MantenerTx"/>); mayusculas y clic mueve solo la transmision. Es lo mismo que en
/// WSJT-X, para que quien venga de alli no tenga que aprender otro gesto.
/// </para>
/// <para>
/// Se hizo asi y no con un control que se pintara solo porque el modem entrega columnas a su
/// ritmo, varias por segundo, y no al ritmo al que el operador mueve la ventana: si la imagen
/// siguiera al tamaño del hueco, cada tiron del raton tiraria el historial entero.
/// </para>
/// </remarks>
public partial class Cascada : UserControl
{
    /// <summary>Frecuencia mas alta que se pinta, en hercios.</summary>
    public static readonly DependencyProperty TopeHzProperty = DependencyProperty.Register(
        nameof(TopeHz),
        typeof(double),
        typeof(Cascada),
        new PropertyMetadata(3000.0, (d, _) => ((Cascada)d).Redibujar()));

    /// <summary>La imagen de la cascada.</summary>
    public static readonly DependencyProperty ImagenProperty = DependencyProperty.Register(
        nameof(Imagen),
        typeof(ImageSource),
        typeof(Cascada),
        new PropertyMetadata(null, AlCambiarLaImagen));

    /// <summary>Tono de transmision, en hercios.</summary>
    public static readonly DependencyProperty TonoHzProperty = DependencyProperty.Register(
        nameof(TonoHz),
        typeof(int),
        typeof(Cascada),
        new FrameworkPropertyMetadata(
            1500,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            (d, _) => ((Cascada)d).ColocarLosCursores()));

    /// <summary>Tono de recepcion, en hercios.</summary>
    public static readonly DependencyProperty TonoRxHzProperty = DependencyProperty.Register(
        nameof(TonoRxHz),
        typeof(int),
        typeof(Cascada),
        new FrameworkPropertyMetadata(
            1500,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            (d, _) => ((Cascada)d).ColocarLosCursores()));

    /// <summary>La transmision no sigue al clic: solo se mueve con mayusculas.</summary>
    public static readonly DependencyProperty MantenerTxProperty = DependencyProperty.Register(
        nameof(MantenerTx),
        typeof(bool),
        typeof(Cascada),
        new PropertyMetadata(false));

    /// <summary>Monta el control.</summary>
    public Cascada()
    {
        InitializeComponent();
        SizeChanged += (_, _) => Redibujar();
        Loaded += (_, _) => Redibujar();
    }

    /// <summary>Frecuencia mas alta que se pinta, en hercios.</summary>
    public double TopeHz
    {
        get => (double)GetValue(TopeHzProperty);
        set => SetValue(TopeHzProperty, value);
    }

    /// <summary>La imagen de la cascada, o nula mientras no haya nada.</summary>
    public ImageSource? Imagen
    {
        get => (ImageSource?)GetValue(ImagenProperty);
        set => SetValue(ImagenProperty, value);
    }

    /// <summary>Tono de transmision, en hercios.</summary>
    public int TonoHz
    {
        get => (int)GetValue(TonoHzProperty);
        set => SetValue(TonoHzProperty, value);
    }

    /// <summary>Tono de recepcion, en hercios.</summary>
    public int TonoRxHz
    {
        get => (int)GetValue(TonoRxHzProperty);
        set => SetValue(TonoRxHzProperty, value);
    }

    /// <summary>La transmision no sigue al clic.</summary>
    public bool MantenerTx
    {
        get => (bool)GetValue(MantenerTxProperty);
        set => SetValue(MantenerTxProperty, value);
    }

    private static void AlCambiarLaImagen(DependencyObject objeto, DependencyPropertyChangedEventArgs args)
    {
        var cascada = (Cascada)objeto;
        cascada.Lienzo.Source = args.NewValue as ImageSource;
        cascada.Hueco.Visibility = args.NewValue is null ? Visibility.Visible : Visibility.Collapsed;
        cascada.CursorDeTono.Visibility = args.NewValue is null ? Visibility.Collapsed : Visibility.Visible;
        cascada.CursorDeRx.Visibility = args.NewValue is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Redibujar()
    {
        ColocarLosCursores();
        PintarLaRegla();
    }

    private void ColocarLosCursores()
    {
        var ancho = Marco.ActualWidth;
        if (ancho <= 0 || TopeHz <= 0) return;

        CursorDeTono.Margin = new Thickness((Math.Clamp(TonoHz / TopeHz, 0, 1) * ancho) - 1, 0, 0, 0);
        CursorDeRx.Margin = new Thickness((Math.Clamp(TonoRxHz / TopeHz, 0, 1) * ancho) - 1, 0, 0, 0);
    }

    /// <summary>La regla se recalcula con el ancho visible: una marca cada 500 Hz.</summary>
    private void PintarLaRegla()
    {
        Regla.Children.Clear();
        Regla.ColumnDefinitions.Clear();
        if (TopeHz <= 0) return;

        var marcas = (int)Math.Max(1, Math.Round(TopeHz / 500));
        for (var i = 0; i < marcas; i++)
        {
            Regla.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var hz = (i + 1) * TopeHz / marcas;
            var etiqueta = new TextBlock
            {
                Text = i == marcas - 1
                    ? string.Create(CultureInfo.CurrentCulture, $"{hz:0} Hz")
                    : hz.ToString("0", CultureInfo.CurrentCulture),
                FontSize = 10,
                HorizontalAlignment = HorizontalAlignment.Right,
                Foreground = (Brush)(TryFindResource("TextoTenue") ?? Brushes.Gray),
            };
            Grid.SetColumn(etiqueta, i);
            Regla.Children.Add(etiqueta);
        }
    }

    private void AlPulsarEnLaCascada(object sender, MouseButtonEventArgs e)
    {
        var ancho = Marco.ActualWidth;
        if (ancho <= 0) return;

        var x = e.GetPosition(Marco).X;

        // Se acota al ancho util de audio: por debajo de 200 Hz y por encima de 3.000 el
        // filtro del equipo se lo come, y transmitir ahi es transmitir para nadie.
        var hz = (int)Math.Clamp(Math.Round(x / ancho * TopeHz), 200, 3000);

        var soloTx = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
        if (soloTx)
        {
            TonoHz = hz;
        }
        else
        {
            TonoRxHz = hz;
            if (!MantenerTx) TonoHz = hz;
        }

        e.Handled = true;
    }
}
