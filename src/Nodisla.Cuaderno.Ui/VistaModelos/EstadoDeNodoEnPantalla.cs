using CommunityToolkit.Mvvm.ComponentModel;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Idiomas;

namespace Nodisla.Cuaderno.Ui.VistaModelos;

/// <summary>
/// Un nodo del cluster tal y como se ensena en el panel de operar: nombre, estado y ritmo.
/// </summary>
/// <remarks>
/// Se actualiza en su sitio en vez de rehacerse: el desplegable de estado y el selector de la
/// consola estan enlazados a estas filas, y rehacerlas con cada anuncio los cerraria.
/// </remarks>
public sealed partial class EstadoDeNodoEnPantalla : ObservableObject
{
    /// <summary>Monta la fila con lo que dice la fuente.</summary>
    /// <param name="estado">Estado del nodo.</param>
    public EstadoDeNodoEnPantalla(EstadoDeNodo estado)
    {
        ArgumentNullException.ThrowIfNull(estado);
        Id = estado.Id;
        Actualizar(estado);
    }

    /// <summary>Identificador del nodo.</summary>
    public string Id { get; }

    /// <summary>Nombre del nodo.</summary>
    [ObservableProperty]
    private string _nombre = string.Empty;

    /// <summary>Maquina y puerto.</summary>
    [ObservableProperty]
    private string _direccion = string.Empty;

    /// <summary>Estado de la conexion.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EstadoTexto))]
    private EstadoDeConexion _estado;

    /// <summary>El nodo esta activo.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EstadoTexto))]
    private bool _activo;

    /// <summary>Red de escucha automatica.</summary>
    [ObservableProperty]
    private bool _esSkimmer;

    /// <summary>Por que no conecta, si se sabe.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayMotivo))]
    private string _motivo = string.Empty;

    /// <summary>Anuncios del ultimo minuto.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Ritmo))]
    private int _spotsPorMinuto;

    /// <summary>Hay motivo que ensenar.</summary>
    public bool HayMotivo => Motivo.Length > 0;

    /// <summary>Estado escrito para el operador.</summary>
    public string EstadoTexto => NodoDeClusterEnAjustes.TextoDeEstado(Estado, Activo);

    /// <summary>Anuncios por minuto, como se lee.</summary>
    public string Ritmo => Textos.F("Principal.Cluster.SpotsPorMinuto", SpotsPorMinuto);

    /// <summary>Pone lo ultimo que dice la fuente.</summary>
    /// <param name="estado">Estado del nodo.</param>
    public void Actualizar(EstadoDeNodo estado)
    {
        ArgumentNullException.ThrowIfNull(estado);

        Nombre = estado.Nombre;
        Direccion = $"{estado.Servidor}:{estado.Puerto}";
        Estado = estado.Estado;
        Activo = estado.Activo;
        EsSkimmer = estado.EsSkimmer;
        Motivo = estado.Motivo ?? string.Empty;
        SpotsPorMinuto = estado.SpotsPorMinuto;
        OnPropertyChanged(nameof(EstadoTexto));
        OnPropertyChanged(nameof(Ritmo));
    }

    /// <inheritdoc />
    public override string ToString() => Nombre;
}
