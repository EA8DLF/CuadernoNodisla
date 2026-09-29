using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Nodisla.Cuaderno.Ui.VistaModelos;

namespace Nodisla.Cuaderno.Ui.Vistas.Frontales;

/// <summary>
/// Lo común a los frontales dibujados de otros modelos (docs/18-frontales.md): los gestos que
/// no son una orden enlazable (encender, la rueda sobre CLAR, el anillo de pasos) y lo que el
/// modelo declara de sí mismo (analizador, doble recepción) para el frontal genérico.
/// </summary>
/// <remarks>
/// El frontal no sabe qué protocolo hay detrás: acciona las mismas órdenes de
/// <see cref="VistaModeloEquipo"/> que el FT-710, y lo que el equipo no tiene sale apagado
/// porque esas órdenes no se pueden ejecutar.
/// </remarks>
public class FrontalDibujado : UserControl
{
    /// <summary>El modelo tiene analizador de espectro (o no se sabe el modelo).</summary>
    public static readonly DependencyProperty ConAnalizadorProperty = DependencyProperty.Register(
        nameof(ConAnalizador), typeof(bool), typeof(FrontalDibujado), new PropertyMetadata(true));

    /// <summary>El modelo tiene doble recepción real.</summary>
    public static readonly DependencyProperty DosReceptoresProperty = DependencyProperty.Register(
        nameof(DosReceptores), typeof(bool), typeof(FrontalDibujado), new PropertyMetadata(false));

    private VistaModeloEquipo? _escuchado;

    /// <summary>Monta el frontal.</summary>
    protected FrontalDibujado()
    {
        DataContextChanged += (_, _) => Escuchar();
        Unloaded += (_, _) => Soltar();
        Loaded += (_, _) => Escuchar();
    }

    /// <summary>El modelo tiene analizador de espectro (o no se sabe el modelo).</summary>
    public bool ConAnalizador
    {
        get => (bool)GetValue(ConAnalizadorProperty);
        set => SetValue(ConAnalizadorProperty, value);
    }

    /// <summary>El modelo tiene doble recepción real.</summary>
    public bool DosReceptores
    {
        get => (bool)GetValue(DosReceptoresProperty);
        set => SetValue(DosReceptoresProperty, value);
    }

    /// <summary>El equipo que hay detrás.</summary>
    protected VistaModeloEquipo? Equipo => DataContext as VistaModeloEquipo;

    /// <summary>La tecla de encendido: enciende o apaga (apagar pide confirmación).</summary>
    /// <param name="sender">La tecla.</param>
    /// <param name="e">El clic.</param>
    protected async void AlEncender(object sender, RoutedEventArgs e)
    {
        if (Equipo is { PuedeEncenderOApagar: true } equipo) await equipo.EncenderOApagarAsync().ConfigureAwait(true);
    }

    /// <summary>La rueda sobre la tecla del clarificador (RIT): ±1 paso por muesca.</summary>
    /// <param name="sender">La tecla.</param>
    /// <param name="e">La rueda.</param>
    protected void AlGirarSobreClar(object sender, MouseWheelEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        e.Handled = true;
        Equipo?.MoverClarificador(e.Delta > 0 ? 1 : -1);
    }

    /// <summary>Un mando de pasos o canales (STEP/MCH, M.CH): saltos de canal del dial.</summary>
    /// <param name="sender">El mando.</param>
    /// <param name="muescas">Muescas giradas.</param>
    protected async void AlGirarLosPasos(object? sender, int muescas)
    {
        if (Equipo is { } equipo) await equipo.GirarPasosAsync(muescas).ConfigureAwait(true);
    }

    /// <summary>La rueda sobre un anillo de pasos dibujado (MPVD del FTDX): saltos de canal.</summary>
    /// <param name="sender">El anillo.</param>
    /// <param name="e">La rueda.</param>
    protected async void AlGirarElAnillo(object sender, MouseWheelEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        e.Handled = true;
        if (Equipo is { } equipo) await equipo.GirarPasosAsync(e.Delta > 0 ? 1 : -1).ConfigureAwait(true);
    }

    private void Escuchar()
    {
        if (ReferenceEquals(_escuchado, Equipo))
        {
            Leer();
            return;
        }

        Soltar();
        _escuchado = Equipo;
        if (_escuchado is not null) _escuchado.PropertyChanged += AlCambiarElEquipo;
        Leer();
    }

    private void Soltar()
    {
        if (_escuchado is not null) _escuchado.PropertyChanged -= AlCambiarElEquipo;
        _escuchado = null;
    }

    private void AlCambiarElEquipo(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(VistaModeloEquipo.Modelo) or nameof(VistaModeloEquipo.NombreDelFrontal) or null or "")
        {
            Leer();
        }
    }

    private void Leer()
    {
        var capacidades = Equipo?.Modelo?.Capacidades;
        ConAnalizador = capacidades?.Analizador ?? true;
        DosReceptores = capacidades?.DosReceptores ?? false;
    }
}
