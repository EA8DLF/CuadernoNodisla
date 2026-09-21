using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Nodisla.Cuaderno.Ui.VistaModelos;

namespace Nodisla.Cuaderno.Ui.Vistas;

/// <summary>
/// Un mando giratorio del frontal dibujado.
/// </summary>
/// <remarks>
/// <para>
/// Lleva el rotulo real del equipo y acciona exactamente el mismo <see cref="VistaModeloMando"/>
/// que la lista de «Todos los mandos»: el dibujo y la lista son dos maneras de ver lo mismo, no
/// dos caminos distintos. Girar el mando aqui mueve el deslizador de alla, y al reves.
/// </para>
/// <para>
/// Un mando que el equipo tiene pero no ofrece por CAT se dibuja igual, apagado: que este y no
/// se pueda tocar es informacion; que no estuviera seria un frontal incompleto.
/// </para>
/// </remarks>
public partial class MandoGiratorio : UserControl
{
    /// <summary>Arco util de un potenciometro de verdad: de las siete a las cinco.</summary>
    private const double ArcoEnGrados = 270.0;

    /// <summary>Mando que se acciona. Nulo si el equipo no lo ofrece por CAT.</summary>
    public static readonly DependencyProperty MandoProperty = DependencyProperty.Register(
        nameof(Mando),
        typeof(VistaModeloMando),
        typeof(MandoGiratorio),
        new PropertyMetadata(null, AlCambiarDeMando));

    /// <summary>Rotulo tal y como esta serigrafiado en el equipo.</summary>
    public static readonly DependencyProperty RotuloProperty = DependencyProperty.Register(
        nameof(Rotulo),
        typeof(string),
        typeof(MandoGiratorio),
        new PropertyMetadata(string.Empty, AlCambiarDeMando));

    /// <summary>Que hace el mando, en espanol, para la ayuda y el lector de pantalla.</summary>
    public static readonly DependencyProperty ExplicacionProperty = DependencyProperty.Register(
        nameof(Explicacion),
        typeof(string),
        typeof(MandoGiratorio),
        new PropertyMetadata(string.Empty, AlCambiarDeMando));

    /// <summary>Giro de la marca, en grados.</summary>
    public static readonly DependencyProperty GiroProperty = DependencyProperty.Register(
        nameof(Giro),
        typeof(double),
        typeof(MandoGiratorio),
        new PropertyMetadata(-135.0));

    /// <summary>Valor escrito bajo el mando.</summary>
    public static readonly DependencyProperty ValorTextoProperty = DependencyProperty.Register(
        nameof(ValorTexto),
        typeof(string),
        typeof(MandoGiratorio),
        new PropertyMetadata("—"));

    private VistaModeloMando? _escuchado;

    /// <summary>Monta el mando.</summary>
    public MandoGiratorio()
    {
        InitializeComponent();
        MouseWheel += AlGirarLaRueda;
        PreviewKeyDown += AlTeclear;
        Unloaded += (_, _) => DejarDeEscuchar();
    }

    /// <summary>Mando que se acciona. Nulo si el equipo no lo ofrece por CAT.</summary>
    public VistaModeloMando? Mando
    {
        get => (VistaModeloMando?)GetValue(MandoProperty);
        set => SetValue(MandoProperty, value);
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

    /// <summary>Giro de la marca, en grados.</summary>
    public double Giro
    {
        get => (double)GetValue(GiroProperty);
        set => SetValue(GiroProperty, value);
    }

    /// <summary>Valor escrito bajo el mando.</summary>
    public string ValorTexto
    {
        get => (string)GetValue(ValorTextoProperty);
        set => SetValue(ValorTextoProperty, value);
    }

    private static void AlCambiarDeMando(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (MandoGiratorio)d;
        control.DejarDeEscuchar();

        if (control.Mando is { } mando)
        {
            control._escuchado = mando;
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
        if (_escuchado is null) return;
        _escuchado.PropertyChanged -= AlCambiarElValor;
        _escuchado = null;
    }

    /// <summary>
    /// Pone el nombre accesible del mando.
    /// </summary>
    /// <remarks>
    /// Un dibujo sin nombres no lo lee un lector de pantalla. El nombre va en espanol aunque el
    /// rotulo dibujado sea el del equipo, que esta en ingles porque asi esta serigrafiado.
    /// </remarks>
    private void PonerElNombreAccesible()
    {
        var nombre = Explicacion.Length > 0 ? Explicacion : Rotulo;

        if (Mando is null)
        {
            AutomationProperties.SetName(this, $"{nombre} ({Rotulo})");
            AutomationProperties.SetHelpText(this, "Este equipo no ofrece este mando por CAT.");
            ToolTip = $"{Rotulo} — {nombre}. Este equipo no lo ofrece por CAT.";
            IsEnabled = false;
            return;
        }

        IsEnabled = true;
        AutomationProperties.SetName(this, $"{nombre} ({Rotulo})");
        AutomationProperties.SetHelpText(
            this,
            Mando.EsDePosiciones
                ? $"Posiciones: {string.Join(", ", Mando.Posiciones)}."
                : $"Entre {Mando.Minimo} y {Mando.Maximo}{(Mando.Unidad.Length > 0 ? " " + Mando.Unidad : string.Empty)}.");

        ToolTip = $"{Rotulo} — {nombre}. Gire con la rueda del ratón o con las flechas.";
    }

    /// <summary>Lleva la marca y el valor a lo que dice el mando.</summary>
    private void Recoger()
    {
        if (Mando is not { } mando)
        {
            Giro = -ArcoEnGrados / 2;
            ValorTexto = "—";
            return;
        }

        var recorrido = mando.Maximo - mando.Minimo;
        var parte = recorrido <= 0 ? 0 : (mando.Valor - mando.Minimo) / recorrido;

        Giro = (-ArcoEnGrados / 2) + (parte * ArcoEnGrados);
        ValorTexto = mando.ValorTexto;
    }

    private void AlGirarLaRueda(object sender, MouseWheelEventArgs e)
    {
        if (Mando is not { Disponible: true } mando) return;

        e.Handled = true;
        Mover(mando, e.Delta > 0 ? 1 : -1);
    }

    private void AlTeclear(object sender, KeyEventArgs e)
    {
        if (Mando is not { Disponible: true } mando) return;

        var pasos = e.Key switch
        {
            Key.Up or Key.Right => 1,
            Key.Down or Key.Left => -1,
            _ => 0,
        };

        if (pasos == 0) return;

        e.Handled = true;
        Mover(mando, pasos);
    }

    private static void Mover(VistaModeloMando mando, int pasos)
    {
        var salto = mando.Paso > 0 ? mando.Paso : 1;
        mando.Valor = Math.Clamp(mando.Valor + (salto * pasos), mando.Minimo, mando.Maximo);
    }
}
