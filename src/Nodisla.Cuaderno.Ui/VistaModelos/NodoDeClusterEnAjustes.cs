using CommunityToolkit.Mvvm.ComponentModel;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Idiomas;
using Nodisla.Cuaderno.Ui.Ajustes;

namespace Nodisla.Cuaderno.Ui.VistaModelos;

/// <summary>
/// Un nodo de la lista del cluster en la pantalla de ajustes: lo que se edita y como esta.
/// </summary>
/// <remarks>
/// Lo editable se queda aqui hasta pulsar «Guardar y aplicar»; lo vivo —estado, motivo,
/// anuncios por minuto— llega de la fuente en cuanto cambia, este o no aplicado lo demas.
/// </remarks>
public sealed partial class NodoDeClusterEnAjustes : ObservableObject
{
    /// <summary>Monta la fila a partir del nodo guardado.</summary>
    /// <param name="guardado">Nodo tal y como esta en los ajustes.</param>
    public NodoDeClusterEnAjustes(AjustesDeNodoDeCluster guardado)
    {
        ArgumentNullException.ThrowIfNull(guardado);

        Id = guardado.Id;
        _nombre = guardado.Nombre;
        _servidor = guardado.Servidor;
        _puerto = guardado.Puerto;
        _activo = guardado.Activo;
        _esSkimmer = guardado.EsSkimmer;
        _indicativo = guardado.Indicativo ?? string.Empty;
        _sufijo = guardado.Sufijo ?? string.Empty;
        _guionDeArranque = string.Join(Environment.NewLine, guardado.GuionDeArranque);
        _reconectarSolo = guardado.ReconectarSolo;
    }

    /// <summary>Identificador del nodo. No cambia.</summary>
    public string Id { get; }

    /// <summary>Clave de su contrasena en el almacen cifrado.</summary>
    public string ClaveDeContrasena => ClavesDeCredencial.ContrasenaDeNodoDeCluster(Id);

    /// <summary>Nombre que se ve en pantalla.</summary>
    [ObservableProperty]
    private string _nombre;

    /// <summary>Maquina.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Direccion))]
    private string _servidor;

    /// <summary>Puerto de Telnet.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Direccion))]
    private int _puerto;

    /// <summary>Se conecta al conectar el cluster.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EstadoTexto))]
    private bool _activo;

    /// <summary>Red de escucha automatica: lo que trae se marca como «skimmer».</summary>
    [ObservableProperty]
    private bool _esSkimmer;

    /// <summary>Indicativo propio de este nodo; vacio, el comun.</summary>
    [ObservableProperty]
    private string _indicativo;

    /// <summary>Sufijo propio de este nodo; vacio, el comun.</summary>
    [ObservableProperty]
    private string _sufijo;

    /// <summary>Ordenes al conectar, una por linea.</summary>
    [ObservableProperty]
    private string _guionDeArranque;

    /// <summary>Reconectar solo, con espera creciente.</summary>
    [ObservableProperty]
    private bool _reconectarSolo;

    /// <summary>Hay una contrasena guardada para este nodo.</summary>
    [ObservableProperty]
    private bool _contrasenaGuardada;

    /// <summary>Estado de la conexion, tal y como lo cuenta la fuente.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EstadoTexto))]
    [NotifyPropertyChangedFor(nameof(EstaEnMarcha))]
    private EstadoDeConexion _estado = EstadoDeConexion.Desconectado;

    /// <summary>Por que no conecta o por que se cayo, si se sabe.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayMotivo))]
    private string _motivo = string.Empty;

    /// <summary>Anuncios del ultimo minuto.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Ritmo))]
    private int _spotsPorMinuto;

    /// <summary>El nodo ya esta en la fuente (aplicado), y se puede conectar.</summary>
    [ObservableProperty]
    private bool _aplicado;

    /// <summary>Lo que salio de la ultima prueba.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayPrueba))]
    private string _prueba = string.Empty;

    /// <summary>La ultima prueba salio bien.</summary>
    [ObservableProperty]
    private bool _pruebaBien;

    /// <summary>Maquina y puerto, como se escribe.</summary>
    public string Direccion => $"{Servidor}:{Puerto}";

    /// <summary>Hay un motivo que ensenar.</summary>
    public bool HayMotivo => Motivo.Length > 0;

    /// <summary>Hay resultado de prueba que ensenar.</summary>
    public bool HayPrueba => Prueba.Length > 0;

    /// <summary>Esta conectado o intentandolo: el boton dice «Desconectar».</summary>
    public bool EstaEnMarcha =>
        Estado is EstadoDeConexion.Conectado or EstadoDeConexion.Conectando or EstadoDeConexion.Reintentando;

    /// <summary>Anuncios por minuto, como se lee en la lista.</summary>
    public string Ritmo => Textos.F("Principal.Cluster.SpotsPorMinuto", SpotsPorMinuto);

    /// <summary>Estado escrito para el operador.</summary>
    public string EstadoTexto => TextoDeEstado(Estado, Activo);

    /// <summary>Pone lo que dice la fuente de este nodo.</summary>
    /// <param name="estado">Estado del nodo en la fuente, o nulo si aun no esta aplicado.</param>
    public void PonerEstado(EstadoDeNodo? estado)
    {
        Aplicado = estado is not null;
        Estado = estado?.Estado ?? EstadoDeConexion.Desconectado;
        Motivo = estado?.Motivo ?? string.Empty;
        SpotsPorMinuto = estado?.SpotsPorMinuto ?? 0;
    }

    /// <summary>Vuelve a escribir lo que depende del idioma.</summary>
    public void RefrescarTextos()
    {
        OnPropertyChanged(nameof(EstadoTexto));
        OnPropertyChanged(nameof(Ritmo));
    }

    /// <summary>Lo que se guarda en los ajustes.</summary>
    /// <returns>El nodo para el fichero.</returns>
    public AjustesDeNodoDeCluster AGuardar() => new()
    {
        Id = Id,
        Nombre = Nombre.Trim(),
        Servidor = Servidor.Trim(),
        Puerto = Puerto,
        Activo = Activo,
        EsSkimmer = EsSkimmer,
        Indicativo = string.IsNullOrWhiteSpace(Indicativo) ? null : Indicativo.Trim().ToUpperInvariant(),
        Sufijo = string.IsNullOrWhiteSpace(Sufijo) ? null : Sufijo.Trim().TrimStart('-'),
        GuionDeArranque = [.. GuionDeArranque
            .Split('\n')
            .Select(linea => linea.Trim())
            .Where(linea => linea.Length > 0)],
        ReconectarSolo = ReconectarSolo,
    };

    /// <summary>Estado de un nodo escrito para el operador.</summary>
    /// <param name="estado">Estado de la conexion.</param>
    /// <param name="activo">El nodo esta activo.</param>
    /// <returns>El texto.</returns>
    public static string TextoDeEstado(EstadoDeConexion estado, bool activo) => estado switch
    {
        EstadoDeConexion.Conectado => Textos.T("Comun.Conectado"),
        EstadoDeConexion.Conectando => Textos.T("Principal.Cluster.Estado.Conectando"),
        EstadoDeConexion.Reintentando => Textos.T("Principal.Cluster.Estado.Reintentando"),
        EstadoDeConexion.Fallido => Textos.T("Principal.Cluster.Estado.Fallida"),
        _ when !activo => Textos.T("Principal.Cluster.Estado.Desactivado"),
        _ => Textos.T("Principal.Cluster.Estado.SinConexion"),
    };
}
