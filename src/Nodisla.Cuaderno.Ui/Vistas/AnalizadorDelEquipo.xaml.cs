using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Nodisla.Cuaderno.Ui.VistaModelos;

namespace Nodisla.Cuaderno.Ui.Vistas;

/// <summary>El analizador de espectro de la propia radio, dentro del visor del frontal.</summary>
/// <remarks>
/// Arranca la lectura al mostrarse y la para al ocultarse. Aqui solo se coloca la marca del
/// VFO; las imagenes las pinta <see cref="VistaModeloAnalizador"/>.
/// </remarks>
public partial class AnalizadorDelEquipo : UserControl
{
    private VistaModeloAnalizador? _modelo;

    /// <summary>Monta el control.</summary>
    public AnalizadorDelEquipo()
    {
        InitializeComponent();
        // Se engancha cuando esta A LA VISTA y tiene modelo, sea cual sea el orden en que
        // lleguen las dos cosas. Antes se miraba en Loaded, y en la pestaña Digital el modelo
        // llegaba (DataContextChanged) antes de Loaded y se descartaba: al cambiar de Operar a
        // Digital el analizador se paraba y no volvia (28-09-2026). Con IsVisible, ademas, se
        // deja de leer con el frontal plegado y se vuelve a leer al desplegarlo.
        IsVisibleChanged += (_, _) => Reenganchar();
        DataContextChanged += (_, _) => Reenganchar();
        Loaded += (_, _) => Reenganchar();
        Unloaded += (_, _) => Enganchar(null);
        Lienzo.SizeChanged += (_, _) => ColocarLaMarca();
        VistaDeCascada.SizeChanged += (_, _) => ColocarLaMarca();
    }

    private void Reenganchar() =>
        Enganchar(IsVisible ? DataContext as VistaModeloAnalizador : null);

    private void Enganchar(VistaModeloAnalizador? modelo)
    {
        if (ReferenceEquals(modelo, _modelo)) return;
        if (_modelo is not null)
        {
            _modelo.PropertyChanged -= AlCambiarElModelo;
            _modelo.Parar();
        }

        _modelo = modelo;
        if (_modelo is not null)
        {
            _modelo.PropertyChanged += AlCambiarElModelo;
            _modelo.Arrancar();
        }

        MostrarLaVista();
        MostrarElAviso();
    }

    private void AlCambiarElModelo(object? remitente, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(VistaModeloAnalizador.PosicionDelVfo)) ColocarLaMarca();
        if (e.PropertyName == nameof(VistaModeloAnalizador.Recibiendo)) MostrarElAviso();
        if (e.PropertyName is nameof(VistaModeloAnalizador.EnTresD) or nameof(VistaModeloAnalizador.Multiple)
            or nameof(VistaModeloAnalizador.HayAudio))
        {
            MostrarLaVista();
        }
    }

    /// <summary>3DSS: la vista en perspectiva en todo el hueco; si no, traza y cascada.</summary>
    private void MostrarLaVista()
    {
        var tresD = _modelo is { EnTresD: true };
        VistaTresD.Visibility = tresD ? Visibility.Visible : Visibility.Collapsed;
        VistaDeCascada.Visibility = tresD ? Visibility.Collapsed : Visibility.Visible;

        // MULTI: el analizador se queda con la mitad de arriba y abajo van los dos paneles.
        var multiple = _modelo is { Multiple: true };
        FilaMultiple.Height = multiple ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        VistaMultiple.Visibility = multiple ? Visibility.Visible : Visibility.Collapsed;
        AvisoDeAudio.Visibility = _modelo is { HayAudio: true } ? Visibility.Collapsed : Visibility.Visible;
        ColocarLaMarca();
    }

    private void MostrarElAviso() =>
        Aviso.Visibility = _modelo is { Recibiendo: true } ? Visibility.Collapsed : Visibility.Visible;

    private void ColocarLaMarca()
    {
        var ancho = Lienzo.ActualWidth;
        var alto = VistaDeCascada.ActualHeight > 0 ? VistaDeCascada.ActualHeight : VistaTresD.ActualHeight;
        var x = (_modelo?.PosicionDelVfo ?? 0.5) * ancho;
        Canvas.SetLeft(TrianguloDelVfo, x - 3.5);
        Canvas.SetTop(TrianguloDelVfo, 0);
        Canvas.SetLeft(LineaDelVfo, Math.Round(x) - 0.5);
        Canvas.SetTop(LineaDelVfo, 5);
        // En 3DSS la linea baja hasta la pasada de delante; en cascada, solo por la traza.
        LineaDelVfo.Height = Math.Max(0, (_modelo is { EnTresD: true } ? alto : alto / 2) - 5);
    }
}
