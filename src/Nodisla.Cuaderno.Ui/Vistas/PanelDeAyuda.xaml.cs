using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Threading;
using Nodisla.Cuaderno.Ui.Soporte;
using Nodisla.Cuaderno.Ui.VistaModelos;
using Serilog;

namespace Nodisla.Cuaderno.Ui.Vistas;

/// <summary>
/// La pagina de la ayuda. DataContext: <see cref="VistaModeloAyuda"/>.
/// </summary>
/// <remarks>
/// El codigo de vista solo hace lo que es de la vista: convertir el capitulo elegido en un
/// <see cref="FlowDocument"/> (que es un objeto de WPF y no puede vivir en el modelo) y bajar al
/// apartado pedido cuando se sigue un enlace «capitulo.md#apartado».
/// </remarks>
public partial class PanelDeAyuda : UserControl
{
    private VistaModeloAyuda? _modelo;
    private Dictionary<string, Block> _anclas = new(StringComparer.Ordinal);
    private CapituloDeAyuda? _pintado;
    private string? _busquedaPintada;

    /// <summary>Monta la pagina.</summary>
    public PanelDeAyuda()
    {
        InitializeComponent();
        DataContextChanged += AlCambiarElModelo;
        Loaded += (_, _) => Pintar();
    }

    private void AlCambiarElModelo(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_modelo is not null) _modelo.PropertyChanged -= AlCambiarAlgo;
        _modelo = e.NewValue as VistaModeloAyuda;
        if (_modelo is not null) _modelo.PropertyChanged += AlCambiarAlgo;
        _pintado = null;
        Pintar();
    }

    private void AlCambiarAlgo(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(VistaModeloAyuda.CapituloElegido):
            case nameof(VistaModeloAyuda.Busqueda):
                Pintar();
                break;
            case nameof(VistaModeloAyuda.AnclaPedida):
                BajarAlApartado();
                break;
        }
    }

    private void Pintar()
    {
        if (_modelo?.CapituloElegido is not { } capitulo) return;
        if (ReferenceEquals(capitulo, _pintado) && _busquedaPintada == _modelo.Busqueda) return;
        if (!IsLoaded && _pintado is not null) return;

        try
        {
            var bloques = DocumentoMarkdown.Leer(capitulo.Texto);
            var documento = ConstructorDeLaAyuda.Construir(
                bloques, _modelo.Libro, _modelo.SeguirEnlaceCommand, _modelo.Busqueda, FontSize, out _anclas);
            Lector.Document = documento;
            _pintado = capitulo;
            _busquedaPintada = _modelo.Busqueda;
            BajarAlApartado();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se ha podido pintar el capítulo {Capitulo} de la ayuda.", capitulo.Clave);
        }
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        // Con Ctrl+ / Ctrl- cambia la letra de toda la ventana: el capitulo se rehace a esa escala.
        if (e.Property == FontSizeProperty && IsLoaded)
        {
            _pintado = null;
            Pintar();
        }
    }

    private void BajarAlApartado()
    {
        if (_modelo?.AnclaPedida is not { Length: > 0 } ancla || !_anclas.TryGetValue(ancla, out var bloque)) return;

        // Despues de maquetar: antes de eso el bloque no tiene sitio al que bajar.
        Dispatcher.BeginInvoke(bloque.BringIntoView, DispatcherPriority.Loaded);
    }

    /// <summary>Pone el foco en el buscador.</summary>
    public void EnfocarBusqueda()
    {
        CampoDeBusqueda.Focus();
        CampoDeBusqueda.SelectAll();
    }
}
