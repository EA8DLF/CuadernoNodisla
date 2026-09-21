using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nodisla.Cuaderno.Aplicacion.CasosDeUso;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Ui.Conversores;
using Serilog;
using DominioBanda = Nodisla.Cuaderno.Dominio.Valores.Banda;

namespace Nodisla.Cuaderno.Ui.VistaModelos;

/// <summary>
/// Panel del cluster de DX: los anuncios que llegan, los filtros y la consola cruda.
/// </summary>
/// <remarks>
/// Un cluster manda mas de lo que cabe en pantalla y casi todo sobra. Lo que hace util este
/// panel es lo que se resalta: la entidad que no esta en el cuaderno y el hueco de banda y
/// modo que falta. Eso no lo sabe el cluster, lo sabe el cuaderno, y lo pone
/// <see cref="SeguirElCluster"/> antes de que el anuncio llegue aqui.
///
/// Debajo va la consola con todo lo que no son anuncios. No es decoracion: es donde se ven los
/// avisos del nodo y las respuestas a las ordenes, y sin ella el operador no sabe por que el
/// cluster ha dejado de mandar cosas.
/// </remarks>
public sealed partial class VistaModeloCluster : ObservableObject
{
    /// <summary>Anuncios que se guardan; los mas viejos se van cayendo.</summary>
    public const int SpotsQueSeGuardan = 500;

    /// <summary>Lineas que se guardan en la consola cruda.</summary>
    public const int LineasDeConsola = 300;

    /// <summary>Texto del desplegable que quiere decir «no filtres por esto».</summary>
    public const string Cualquiera = "(todas)";

    private readonly SeguirElCluster _cluster;
    private readonly List<FilaDeSpot> _todos = [];

    /// <summary>Monta el panel sobre el seguimiento del cluster.</summary>
    /// <param name="cluster">Seguimiento que marca los spots con lo que sabe el cuaderno.</param>
    public VistaModeloCluster(SeguirElCluster cluster)
    {
        ArgumentNullException.ThrowIfNull(cluster);
        _cluster = cluster;

        _cluster.AnuncioRecibido += AlLlegarUnAnuncio;
        _cluster.LineaRecibida += AlLlegarUnaLinea;
        _cluster.EstadoCambiado += AlCambiarElEstado;

        Bandas = [Cualquiera, .. DominioBanda.Todas.Select(b => b.Nombre)];
        Modos = [Cualquiera, "CW", "SSB", "FT8", "FT4", "RTTY", "PSK31", "FM", "JS8", "SSTV"];
        Continentes = [Cualquiera, "EU", "NA", "SA", "AS", "AF", "OC", "AN"];
    }

    /// <summary>Salta cuando el operador elige un spot para ir a el.</summary>
    public event EventHandler<FilaDeSpot>? SpotElegido;

    /// <summary>Salta cuando cambia la lista de spots que se estan viendo.</summary>
    public event EventHandler? SpotsCambiaron;

    /// <summary>Spots que pasan el filtro, del mas reciente al mas antiguo.</summary>
    public ObservableCollection<FilaDeSpot> Spots { get; } = [];

    /// <summary>Todo lo que manda el cluster y no es un anuncio.</summary>
    public ObservableCollection<string> Consola { get; } = [];

    /// <summary>Bandas que se ofrecen en el filtro.</summary>
    public IReadOnlyList<string> Bandas { get; }

    /// <summary>Modos que se ofrecen en el filtro.</summary>
    public IReadOnlyList<string> Modos { get; }

    /// <summary>Continentes que se ofrecen en el filtro.</summary>
    public IReadOnlyList<string> Continentes { get; }

    /// <summary>Nombre de la fuente de anuncios.</summary>
    public string Nombre => _cluster.Nombre;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EstadoTexto))]
    [NotifyPropertyChangedFor(nameof(TextoDelBotonDeConexion))]
    [NotifyCanExecuteChangedFor(nameof(ConectarCommand))]
    [NotifyCanExecuteChangedFor(nameof(DesconectarCommand))]
    [NotifyCanExecuteChangedFor(nameof(EnviarOrdenCommand))]
    private EstadoDeConexion _estado = EstadoDeConexion.Desconectado;

    [ObservableProperty]
    private string _bandaFiltro = Cualquiera;

    [ObservableProperty]
    private string _modoFiltro = Cualquiera;

    [ObservableProperty]
    private string _continenteFiltro = Cualquiera;

    [ObservableProperty]
    private bool _soloNuevos;

    [ObservableProperty]
    private bool _sinEscuchaAutomatica;

    [ObservableProperty]
    private string _orden = string.Empty;

    [ObservableProperty]
    private FilaDeSpot? _spotSeleccionado;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Resumen))]
    private int _recibidos;

    /// <summary>Como esta la conexion, en una linea.</summary>
    public string EstadoTexto => Estado switch
    {
        EstadoDeConexion.Conectado => "Conectado",
        EstadoDeConexion.Conectando => "Conectando…",
        EstadoDeConexion.Reintentando => "Reintentando…",
        EstadoDeConexion.Fallido => "Conexión fallida",
        _ => "Sin conexión",
    };

    /// <summary>Texto del boton que conecta o desconecta.</summary>
    public string TextoDelBotonDeConexion =>
        Estado is EstadoDeConexion.Conectado or EstadoDeConexion.Conectando or EstadoDeConexion.Reintentando
            ? "Desconectar"
            : "Conectar";

    /// <summary>Cuantos anuncios se ven y cuantos han llegado.</summary>
    public string Resumen =>
        $"{Spots.Count.ToString("N0", CultureInfo.CurrentCulture)} estaciones · " +
        $"{Recibidos.ToString("N0", CultureInfo.CurrentCulture)} anuncios recibidos";

    /// <summary>Conecta con el cluster.</summary>
    [RelayCommand(CanExecute = nameof(SePuedeConectar))]
    public async Task ConectarAsync()
    {
        try
        {
            await _cluster.ConectarAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se ha podido conectar con el cluster.");
            AnadirALaConsola($"No se ha podido conectar: {ex.Message}");
        }
    }

    /// <summary>Cierra la conexion con el cluster.</summary>
    [RelayCommand(CanExecute = nameof(SePuedeDesconectar))]
    public async Task DesconectarAsync()
    {
        try
        {
            await _cluster.DesconectarAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Fallo al desconectar del cluster.");
            AnadirALaConsola($"Fallo al desconectar: {ex.Message}");
        }
    }

    /// <summary>Manda al cluster lo que el operador ha escrito en la consola.</summary>
    [RelayCommand(CanExecute = nameof(SePuedeEnviar))]
    public async Task EnviarOrdenAsync()
    {
        var orden = Orden.Trim();
        if (orden.Length == 0) return;

        try
        {
            Orden = string.Empty;
            await _cluster.EnviarAsync(orden).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se ha podido enviar la orden al cluster.");
            AnadirALaConsola($"No se ha podido enviar «{orden}»: {ex.Message}");
        }
    }

    /// <summary>Vacia la lista de anuncios y la consola.</summary>
    [RelayCommand]
    public void Limpiar()
    {
        _todos.Clear();
        Spots.Clear();
        Consola.Clear();
        _cluster.OlvidarRepetidos();
        Recibidos = 0;
        OnPropertyChanged(nameof(Resumen));
        SpotsCambiaron?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Quita todos los filtros.</summary>
    [RelayCommand]
    public void QuitarFiltro()
    {
        BandaFiltro = Cualquiera;
        ModoFiltro = Cualquiera;
        ContinenteFiltro = Cualquiera;
        SoloNuevos = false;
        SinEscuchaAutomatica = false;
    }

    /// <summary>Lleva el equipo al spot elegido y rellena el indicativo en el formulario.</summary>
    /// <param name="fila">Spot al que se va.</param>
    [RelayCommand]
    public void IrAlSpot(FilaDeSpot? fila)
    {
        var elegido = fila ?? SpotSeleccionado;
        if (elegido is null) return;

        SpotElegido?.Invoke(this, elegido);
    }

    /// <summary>Deja de escuchar al cluster. Lo llama la ventana al cerrarse.</summary>
    public void Detener()
    {
        _cluster.AnuncioRecibido -= AlLlegarUnAnuncio;
        _cluster.LineaRecibida -= AlLlegarUnaLinea;
        _cluster.EstadoCambiado -= AlCambiarElEstado;
    }

    /// <inheritdoc cref="ObservableObject" />
    partial void OnBandaFiltroChanged(string value) => RehacerLaLista();

    /// <inheritdoc cref="ObservableObject" />
    partial void OnModoFiltroChanged(string value) => RehacerLaLista();

    /// <inheritdoc cref="ObservableObject" />
    partial void OnContinenteFiltroChanged(string value) => RehacerLaLista();

    /// <inheritdoc cref="ObservableObject" />
    partial void OnSoloNuevosChanged(bool value) => RehacerLaLista();

    /// <inheritdoc cref="ObservableObject" />
    partial void OnSinEscuchaAutomaticaChanged(bool value) => RehacerLaLista();

    private bool SePuedeConectar() => Estado is EstadoDeConexion.Desconectado or EstadoDeConexion.Fallido;

    private bool SePuedeDesconectar() => !SePuedeConectar();

    private bool SePuedeEnviar() => Estado == EstadoDeConexion.Conectado;

    /// <summary>Filtro que resulta de lo que el operador tiene puesto.</summary>
    private FiltroDeSpots FiltroActual => new(
        Banda: BandaFiltro == Cualquiera ? null : BandaFiltro,
        Modo: ModoFiltro == Cualquiera ? null : ModoFiltro,
        Continente: ContinenteFiltro == Cualquiera ? null : ContinenteFiltro,
        SoloNuevos: SoloNuevos)
    {
        SinEscuchaAutomatica = SinEscuchaAutomatica,
    };

    /// <summary>
    /// Mete un anuncio en la lista, juntandolo con el que ya estuviera si es el mismo.
    /// </summary>
    /// <remarks>
    /// La repeticion no se tira: sube en la lista y ensena cuantas estaciones la estan
    /// oyendo, que es justo lo que dice que esta entrando bien.
    ///
    /// Con una excepcion: si el que repite es una estacion automatica de escucha y el anuncio
    /// lo habia puesto antes una persona, la fila se actualiza pero <b>no sube</b>. Las redes
    /// automaticas anuncian decenas de veces por minuto, y dejarlas subir hundiria en el fondo
    /// todo lo que ha anunciado una persona.
    /// </remarks>
    private void AlLlegarUnAnuncio(object? origen, AnuncioDelCluster anuncio) => Hilo.EnLaVentana(() =>
    {
        Recibidos++;

        var fila = new FilaDeSpot(anuncio);
        var anterior = _todos.FindIndex(f => f.Clave.Equals(fila.Clave));
        var sube = !anuncio.ElUltimoEsAutomatico || !anuncio.AnunciadoPorPersona;

        if (anterior >= 0)
        {
            _todos.RemoveAt(anterior);
            _todos.Insert(sube ? 0 : anterior, fila);
        }
        else
        {
            _todos.Insert(0, fila);
        }

        if (_todos.Count > SpotsQueSeGuardan) _todos.RemoveRange(SpotsQueSeGuardan, _todos.Count - SpotsQueSeGuardan);

        // La lista visible se rehace cuando el anuncio ya estaba: mover una fila a mano y
        // mantenerla cuadrada con el filtro cuesta mas de lo que ahorra.
        if (anterior >= 0)
        {
            RehacerLaLista();
            return;
        }

        if (!FiltroActual.Admite(anuncio.Spot)) return;

        Spots.Insert(0, fila);
        while (Spots.Count > SpotsQueSeGuardan) Spots.RemoveAt(Spots.Count - 1);

        OnPropertyChanged(nameof(Resumen));
        SpotsCambiaron?.Invoke(this, EventArgs.Empty);
    });

    private void AlLlegarUnaLinea(object? origen, string linea) =>
        Hilo.EnLaVentana(() => AnadirALaConsola(linea));

    private void AlCambiarElEstado(object? origen, EstadoDeConexion estado) =>
        Hilo.EnLaVentana(() => Estado = estado);

    private void AnadirALaConsola(string linea)
    {
        Consola.Add(linea);
        while (Consola.Count > LineasDeConsola) Consola.RemoveAt(0);
    }

    private void RehacerLaLista()
    {
        var filtro = FiltroActual;

        Spots.Clear();
        foreach (var fila in _todos)
        {
            if (filtro.Admite(fila.Spot)) Spots.Add(fila);
        }

        OnPropertyChanged(nameof(Resumen));
        SpotsCambiaron?.Invoke(this, EventArgs.Empty);
    }
}
