using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Nodisla.Cuaderno.Ui.VistaModelos;

namespace Nodisla.Cuaderno.Ui.Vistas;

/// <summary>
/// Un mando giratorio del frontal dibujado, doble y concentrico como los de la radio.
/// </summary>
/// <remarks>
/// <para>
/// El anillo acciona <see cref="Mando"/> y el centro <see cref="MandoInterior"/>: exactamente los
/// mismos <see cref="VistaModeloMando"/> que la lista de «Todos los mandos». Si el centro no tiene
/// mando propio, la rueda en cualquier sitio mueve el anillo.
/// </para>
/// <para>
/// Hay mandos que no mueven un valor sino el equipo (STEP/MCH salta de canal): para esos,
/// <see cref="GiroLibre"/> activo y el evento <see cref="Girado"/> dice cuantas muescas.
/// Pulsar el mando (clic) y mantenerlo (clic derecho) ejecutan <see cref="AlPulsar"/> y
/// <see cref="AlPulsarDerecho"/>, como el FUNC del equipo, que al pulsarlo cambia de funcion.
/// </para>
/// </remarks>
public partial class MandoGiratorio : UserControl
{
    /// <summary>Arco util de un potenciometro de verdad: de las siete a las cinco.</summary>
    private const double ArcoEnGrados = 270.0;

    /// <summary>Radio del mando de encima, sobre el radio total (29 de 50 en el dibujo).</summary>
    private const double RadioDelCentro = 0.58;

    /// <summary>Mando del anillo. Nulo si el equipo no lo ofrece por CAT.</summary>
    public static readonly DependencyProperty MandoProperty = DependencyProperty.Register(
        nameof(Mando), typeof(VistaModeloMando), typeof(MandoGiratorio), new PropertyMetadata(null, AlCambiarDeMando));

    /// <summary>Mando del centro. Nulo si el centro no tiene mando propio.</summary>
    public static readonly DependencyProperty MandoInteriorProperty = DependencyProperty.Register(
        nameof(MandoInterior), typeof(VistaModeloMando), typeof(MandoGiratorio), new PropertyMetadata(null, AlCambiarDeMando));

    /// <summary>Rotulo tal y como esta serigrafiado en el equipo.</summary>
    public static readonly DependencyProperty RotuloProperty = DependencyProperty.Register(
        nameof(Rotulo), typeof(string), typeof(MandoGiratorio), new PropertyMetadata(string.Empty, AlCambiarDeMando));

    /// <summary>Que hace el mando, en espanol, para la ayuda y el lector de pantalla.</summary>
    public static readonly DependencyProperty ExplicacionProperty = DependencyProperty.Register(
        nameof(Explicacion), typeof(string), typeof(MandoGiratorio), new PropertyMetadata(string.Empty, AlCambiarDeMando));

    /// <summary>El anillo mueve el equipo (evento <see cref="Girado"/>) cuando no hay <see cref="Mando"/>.</summary>
    public static readonly DependencyProperty GiroLibreProperty = DependencyProperty.Register(
        nameof(GiroLibre), typeof(bool), typeof(MandoGiratorio), new PropertyMetadata(false, AlCambiarDeMando));

    /// <summary>Orden al pulsar el mando (clic).</summary>
    public static readonly DependencyProperty AlPulsarProperty = DependencyProperty.Register(
        nameof(AlPulsar), typeof(ICommand), typeof(MandoGiratorio), new PropertyMetadata(null, AlCambiarDeMando));

    /// <summary>Parametro de <see cref="AlPulsar"/>.</summary>
    public static readonly DependencyProperty ParametroAlPulsarProperty = DependencyProperty.Register(
        nameof(ParametroAlPulsar), typeof(object), typeof(MandoGiratorio), new PropertyMetadata(null));

    /// <summary>Orden al mantener el mando (clic derecho).</summary>
    public static readonly DependencyProperty AlPulsarDerechoProperty = DependencyProperty.Register(
        nameof(AlPulsarDerecho), typeof(ICommand), typeof(MandoGiratorio), new PropertyMetadata(null));

    /// <summary>Parametro de <see cref="AlPulsarDerecho"/>.</summary>
    public static readonly DependencyProperty ParametroAlPulsarDerechoProperty = DependencyProperty.Register(
        nameof(ParametroAlPulsarDerecho), typeof(object), typeof(MandoGiratorio), new PropertyMetadata(null));

    /// <summary>Giro de la marca, en grados.</summary>
    public static readonly DependencyProperty GiroProperty = DependencyProperty.Register(
        nameof(Giro), typeof(double), typeof(MandoGiratorio), new PropertyMetadata(-135.0));

    /// <summary>Valor del anillo, escrito en la tapa.</summary>
    public static readonly DependencyProperty ValorTextoProperty = DependencyProperty.Register(
        nameof(ValorTexto), typeof(string), typeof(MandoGiratorio), new PropertyMetadata("—"));

    /// <summary>Valor del centro, escrito pequeno debajo.</summary>
    public static readonly DependencyProperty ValorInteriorTextoProperty = DependencyProperty.Register(
        nameof(ValorInteriorTexto), typeof(string), typeof(MandoGiratorio), new PropertyMetadata(string.Empty));

    private readonly List<VistaModeloMando> _escuchados = [];

    /// <summary>Monta el mando.</summary>
    public MandoGiratorio()
    {
        InitializeComponent();
        MouseWheel += AlGirarLaRueda;
        PreviewKeyDown += AlTeclear;
        MouseLeftButtonUp += (_, e) => Pulsar(AlPulsar, ParametroAlPulsar, e);
        MouseRightButtonUp += (_, e) => Pulsar(AlPulsarDerecho, ParametroAlPulsarDerecho, e);
        Unloaded += (_, _) => DejarDeEscuchar();
    }

    /// <summary>El anillo ha girado sin mando detras (<see cref="GiroLibre"/>): muescas, con signo.</summary>
    public event EventHandler<int>? Girado;

    /// <summary>Mando del anillo.</summary>
    public VistaModeloMando? Mando
    {
        get => (VistaModeloMando?)GetValue(MandoProperty);
        set => SetValue(MandoProperty, value);
    }

    /// <summary>Mando del centro.</summary>
    public VistaModeloMando? MandoInterior
    {
        get => (VistaModeloMando?)GetValue(MandoInteriorProperty);
        set => SetValue(MandoInteriorProperty, value);
    }

    /// <summary>Rotulo tal y como esta serigrafiado en el equipo.</summary>
    public string Rotulo
    {
        get => (string)GetValue(RotuloProperty);
        set => SetValue(RotuloProperty, value);
    }

    /// <summary>Que hace el mando, en espanol.</summary>
    public string Explicacion
    {
        get => (string)GetValue(ExplicacionProperty);
        set => SetValue(ExplicacionProperty, value);
    }

    /// <summary>El anillo mueve el equipo en vez de un valor.</summary>
    public bool GiroLibre
    {
        get => (bool)GetValue(GiroLibreProperty);
        set => SetValue(GiroLibreProperty, value);
    }

    /// <summary>Orden al pulsar.</summary>
    public ICommand? AlPulsar
    {
        get => (ICommand?)GetValue(AlPulsarProperty);
        set => SetValue(AlPulsarProperty, value);
    }

    /// <summary>Parametro de la orden al pulsar.</summary>
    public object? ParametroAlPulsar
    {
        get => GetValue(ParametroAlPulsarProperty);
        set => SetValue(ParametroAlPulsarProperty, value);
    }

    /// <summary>Orden al mantener (clic derecho).</summary>
    public ICommand? AlPulsarDerecho
    {
        get => (ICommand?)GetValue(AlPulsarDerechoProperty);
        set => SetValue(AlPulsarDerechoProperty, value);
    }

    /// <summary>Parametro de la orden al mantener.</summary>
    public object? ParametroAlPulsarDerecho
    {
        get => GetValue(ParametroAlPulsarDerechoProperty);
        set => SetValue(ParametroAlPulsarDerechoProperty, value);
    }

    /// <summary>Giro de la marca, en grados.</summary>
    public double Giro
    {
        get => (double)GetValue(GiroProperty);
        set => SetValue(GiroProperty, value);
    }

    /// <summary>Valor del anillo.</summary>
    public string ValorTexto
    {
        get => (string)GetValue(ValorTextoProperty);
        set => SetValue(ValorTextoProperty, value);
    }

    /// <summary>Valor del centro.</summary>
    public string ValorInteriorTexto
    {
        get => (string)GetValue(ValorInteriorTextoProperty);
        set => SetValue(ValorInteriorTextoProperty, value);
    }

    private static void AlCambiarDeMando(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (MandoGiratorio)d;
        control.DejarDeEscuchar();

        foreach (var mando in new[] { control.Mando, control.MandoInterior })
        {
            if (mando is null) continue;
            control._escuchados.Add(mando);
            mando.PropertyChanged += control.AlCambiarElValor;
        }

        control.PonerElNombreAccesible();
        control.Recoger();
    }

    private void AlCambiarElValor(object? origen, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(VistaModeloMando.Valor)
            or nameof(VistaModeloMando.ValorTexto)
            or nameof(VistaModeloMando.Disponible))
        {
            Recoger();
        }
    }

    private void DejarDeEscuchar()
    {
        foreach (var mando in _escuchados) mando.PropertyChanged -= AlCambiarElValor;
        _escuchados.Clear();
    }

    /// <summary>Pone el nombre accesible y el rotulo de ayuda.</summary>
    private void PonerElNombreAccesible()
    {
        var nombre = Explicacion.Length > 0 ? Explicacion : Rotulo;
        AutomationProperties.SetName(this, $"{nombre} ({Rotulo})");

        if (Mando is null && MandoInterior is null && !GiroLibre && AlPulsar is null)
        {
            AutomationProperties.SetHelpText(this, "Este equipo no ofrece este mando por CAT.");
            ToolTip = $"{Rotulo} — {nombre}. Este equipo no lo ofrece por CAT.";
            IsEnabled = false;
            return;
        }

        IsEnabled = true;
        var partes = new List<string>();
        if (Mando is { } anillo) partes.Add($"Anillo: {anillo.Nombre} ({Rango(anillo)}).");
        else if (GiroLibre) partes.Add("Anillo: mueve el equipo.");
        if (MandoInterior is { } centro) partes.Add($"Centro: {centro.Nombre} ({Rango(centro)}).");
        if (AlPulsar is not null) partes.Add("Clic: pulsar el mando.");
        if (AlPulsarDerecho is not null) partes.Add("Clic derecho: mantenerlo pulsado.");
        var ayuda = string.Join(" ", partes);

        AutomationProperties.SetHelpText(this, ayuda);
        ToolTip = $"{Rotulo} — {nombre}. {ayuda} Gire con la rueda del ratón o con las flechas.";
    }

    private static string Rango(VistaModeloMando mando) =>
        mando.EsDePosiciones
            ? string.Join(", ", mando.Posiciones)
            : $"{mando.Minimo} a {mando.Maximo}{(mando.Unidad.Length > 0 ? " " + mando.Unidad : string.Empty)}";

    /// <summary>Lleva la marca y los valores a lo que dicen los mandos.</summary>
    private void Recoger()
    {
        ValorInteriorTexto = MandoInterior is { Disponible: true } centro ? centro.ValorTexto : string.Empty;

        if (Mando is not { } mando)
        {
            Giro = -ArcoEnGrados / 2;
            ValorTexto = GiroLibre ? string.Empty : "—";
            return;
        }

        var recorrido = mando.Maximo - mando.Minimo;
        var parte = recorrido <= 0 ? 0 : (mando.Valor - mando.Minimo) / recorrido;

        Giro = (-ArcoEnGrados / 2) + (parte * ArcoEnGrados);
        ValorTexto = mando.ValorTexto;
    }

    private void AlGirarLaRueda(object sender, MouseWheelEventArgs e)
    {
        var sentido = e.Delta > 0 ? 1 : -1;
        var p = e.GetPosition(this);
        var radio = Math.Min(ActualWidth, ActualHeight) / 2;
        var enElCentro = radio > 0
                         && Math.Sqrt(Math.Pow(p.X - (ActualWidth / 2), 2) + Math.Pow(p.Y - (ActualHeight / 2), 2)) < radio * RadioDelCentro;

        e.Handled = Mover(sentido, enElCentro);
    }

    private void AlTeclear(object sender, KeyEventArgs e)
    {
        var pasos = e.Key switch
        {
            Key.Up or Key.Right => 1,
            Key.Down or Key.Left => -1,
            _ => 0,
        };

        if (pasos == 0) return;

        // Con mayusculas se mueve el centro, como quien gira el mando de encima.
        e.Handled = Mover(pasos, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
    }

    private bool Mover(int pasos, bool elCentro)
    {
        if (elCentro && MandoInterior is { Disponible: true } centro)
        {
            Mover(centro, pasos);
            return true;
        }

        if (Mando is { Disponible: true } anillo)
        {
            Mover(anillo, pasos);
            return true;
        }

        if (GiroLibre)
        {
            Girado?.Invoke(this, pasos);
            return true;
        }

        return false;
    }

    private static void Mover(VistaModeloMando mando, int pasos)
    {
        var salto = mando.Paso > 0 ? mando.Paso : 1;
        mando.Valor = Math.Clamp(mando.Valor + (salto * pasos), mando.Minimo, mando.Maximo);
    }

    private static void Pulsar(ICommand? orden, object? parametro, MouseButtonEventArgs e)
    {
        if (orden is null || !orden.CanExecute(parametro)) return;
        orden.Execute(parametro);
        e.Handled = true;
    }
}
