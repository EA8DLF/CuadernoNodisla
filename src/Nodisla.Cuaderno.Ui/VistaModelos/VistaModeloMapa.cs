using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nodisla.Cuaderno.Aplicacion.CasosDeUso;
using Nodisla.Cuaderno.Dominio.Dxcc;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Ui.Mapa;
using Serilog;

namespace Nodisla.Cuaderno.Ui.VistaModelos;

/// <summary>
/// El mapa del cuaderno: los contactos, la estacion propia, los trayectos y el paso gris.
/// </summary>
/// <remarks>
/// Este modelo de vista no sabe como se dibuja un mapa. Prepara listas de marcas y trayectos
/// con los tipos propios de <c>Ui.Mapa</c> y se las da al control; el motor de mapas se queda
/// al otro lado de esa frontera. Si algun dia hay que cambiarlo, esta clase no se entera.
/// </remarks>
public sealed partial class VistaModeloMapa : ObservableObject
{
    private readonly PuntosDelCuaderno _puntos;
    private readonly IResolutorDxcc _dxcc;

    private IReadOnlyList<MarcaDelMapa> _contactos = [];
    private IReadOnlyList<MarcaDelMapa> _spots = [];
    private MarcaDelMapa? _satelite;

    /// <summary>Monta el mapa sobre el cuaderno.</summary>
    /// <param name="puntos">Caso de uso que saca del cuaderno los contactos situables.</param>
    /// <param name="dxcc">Resolutor de entidades, para situar los spots.</param>
    public VistaModeloMapa(PuntosDelCuaderno puntos, IResolutorDxcc dxcc)
    {
        ArgumentNullException.ThrowIfNull(puntos);
        ArgumentNullException.ThrowIfNull(dxcc);

        _puntos = puntos;
        _dxcc = dxcc;
    }

    /// <summary>Salta cuando el operador toca una marca del mapa.</summary>
    public event EventHandler<MarcaDelMapa>? MarcaElegida;

    [ObservableProperty]
    private IReadOnlyList<MarcaDelMapa> _marcas = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(QuitarTrayectoCommand))]
    private IReadOnlyList<TrayectoDelMapa> _trayectos = [];

    [ObservableProperty]
    private Coordenada? _estacionPropia;

    [ObservableProperty]
    private string _indicativoPropio = string.Empty;

    [ObservableProperty]
    private DateTimeOffset _instanteDelPasoGris = DateTimeOffset.UtcNow;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextoDelPasoGris))]
    private bool _mostrarPasoGris = true;

    [ObservableProperty]
    private bool _mostrarFondo = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Resumen))]
    private bool _mostrarContactos = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Resumen))]
    private bool _mostrarSpots = true;

    [ObservableProperty]
    private bool _cargando;

    [ObservableProperty]
    private string _aviso = string.Empty;

    /// <summary>Que se esta viendo en el mapa, en una linea.</summary>
    public string Resumen
    {
        get
        {
            var partes = new List<string>(2);
            if (MostrarContactos)
            {
                partes.Add($"{_contactos.Count.ToString("N0", CultureInfo.CurrentCulture)} contactos");
            }

            if (MostrarSpots && _spots.Count > 0)
            {
                partes.Add($"{_spots.Count.ToString("N0", CultureInfo.CurrentCulture)} spots");
            }

            return partes.Count == 0 ? "Mapa vacío" : string.Join(" · ", partes);
        }
    }

    /// <summary>Etiqueta del interruptor del paso gris, con la hora que se esta dibujando.</summary>
    public string TextoDelPasoGris =>
        $"Paso gris ({InstanteDelPasoGris.UtcDateTime.ToString("HH:mm", CultureInfo.InvariantCulture)} UTC)";

    /// <summary>Carga del cuaderno los contactos que se pueden situar.</summary>
    /// <param name="ct">Testigo de cancelacion.</param>
    public async Task CargarAsync(CancellationToken ct = default)
    {
        try
        {
            Cargando = true;
            Aviso = string.Empty;

            var situados = await _puntos.EjecutarAsync(ct: ct).ConfigureAwait(true);

            var marcas = new List<MarcaDelMapa>(situados.Count);
            foreach (var contacto in situados)
            {
                marcas.Add(new MarcaDelMapa(contacto.Donde, contacto.Indicativo.Valor, ClaseDeMarca.Contacto)
                {
                    Detalle = DetalleDe(contacto),
                    Etiquetado = contacto,
                });
            }

            _contactos = marcas;
            Rehacer();
        }
        catch (OperationCanceledException)
        {
            // La ventana se esta cerrando.
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se han podido cargar los contactos del mapa.");
            Aviso = $"No se han podido cargar los contactos del mapa: {ex.Message}";
        }
        finally
        {
            Cargando = false;
        }
    }

    /// <summary>Pone en el mapa la estacion propia a partir de su localizador.</summary>
    /// <param name="localizador">Localizador de la estacion.</param>
    /// <param name="indicativo">Indicativo propio.</param>
    public void FijarEstacion(Locator localizador, string indicativo)
    {
        IndicativoPropio = indicativo;
        EstacionPropia = localizador.EsVacio ? null : Coordenada.Desde(localizador);
    }

    /// <summary>Lleva al mapa la lista de spots que se estan viendo en el cluster.</summary>
    /// <param name="filas">Spots que pasan el filtro del panel de cluster.</param>
    public void PonerSpots(IEnumerable<FilaDeSpot> filas)
    {
        ArgumentNullException.ThrowIfNull(filas);

        var marcas = new List<MarcaDelMapa>();
        foreach (var fila in filas)
        {
            if (fila.EnElMapa(_dxcc) is not { } donde) continue;

            marcas.Add(new MarcaDelMapa(donde, fila.Indicativo, ClaseDeMarca.Spot)
            {
                Detalle = fila.Detalle,
                Destacada = fila.EsInteresante,
                Etiquetado = fila,
            });
        }

        _spots = marcas;
        Rehacer();
    }

    /// <summary>Dibuja el trayecto de la estacion propia a un punto.</summary>
    /// <param name="destino">Punto al que se traza.</param>
    /// <param name="etiqueta">Texto que acompana al trayecto.</param>
    public void TrazarHasta(Coordenada destino, string? etiqueta = null)
    {
        if (EstacionPropia is not { } origen)
        {
            Trayectos = [];
            return;
        }

        Trayectos = [new TrayectoDelMapa(origen, destino) { Etiqueta = etiqueta }];
    }

    /// <summary>
    /// Dibuja el trayecto de la estacion propia a la estacion anunciada en un spot.
    /// </summary>
    /// <param name="fila">Spot elegido.</param>
    public void TrazarHastaElSpot(FilaDeSpot fila)
    {
        ArgumentNullException.ThrowIfNull(fila);

        if (fila.EnElMapa(_dxcc) is not { } donde)
        {
            // No se ha podido situar la estacion: no se traza nada, pero todo lo demas sigue.
            Trayectos = [];
            return;
        }

        TrazarHasta(donde, fila.Indicativo);
    }

    /// <summary>Quita el trayecto dibujado.</summary>
    [RelayCommand(CanExecute = nameof(HayTrayecto))]
    public void QuitarTrayecto() => Trayectos = [];

    private bool HayTrayecto() => Trayectos.Count > 0;

    /// <summary>
    /// Pone o quita el subpunto de un satelite sobre el mapa.
    /// </summary>
    /// <remarks>
    /// Lo llama el panel de satelites mientras hay uno elegido, una vez por segundo. No hay
    /// pestana nueva que dibuje su propio mapa: este es el mismo mapa de siempre, y el subpunto
    /// es una marca mas dentro de <see cref="Marcas"/>.
    /// </remarks>
    /// <param name="marca">El subpunto, o <c>null</c> para quitarlo.</param>
    public void PonerSatelite(MarcaDelMapa? marca)
    {
        _satelite = marca;
        Rehacer();
    }

    /// <summary>Pone el paso gris en la hora actual.</summary>
    /// <param name="ahora">Hora UTC.</param>
    public void ActualizarReloj(DateTimeOffset ahora)
    {
        // El paso gris se redibuja una vez por minuto, no una vez por segundo: la linea se
        // mueve un cuarto de grado por minuto y redibujarla mas a menudo no se nota.
        if (InstanteDelPasoGris.UtcDateTime.Minute == ahora.UtcDateTime.Minute
            && InstanteDelPasoGris.UtcDateTime.Hour == ahora.UtcDateTime.Hour)
        {
            return;
        }

        InstanteDelPasoGris = ahora;
    }

    /// <summary>Avisa de que el operador ha tocado una marca.</summary>
    /// <param name="marca">Marca elegida.</param>
    public void ElegirMarca(MarcaDelMapa marca) => MarcaElegida?.Invoke(this, marca);

    /// <inheritdoc cref="ObservableObject" />
    partial void OnMostrarContactosChanged(bool value) => Rehacer();

    /// <inheritdoc cref="ObservableObject" />
    partial void OnMostrarSpotsChanged(bool value) => Rehacer();

    /// <inheritdoc cref="ObservableObject" />
    partial void OnInstanteDelPasoGrisChanged(DateTimeOffset value) =>
        OnPropertyChanged(nameof(TextoDelPasoGris));

    private void Rehacer()
    {
        var total = new List<MarcaDelMapa>(
            (MostrarContactos ? _contactos.Count : 0) + (MostrarSpots ? _spots.Count : 0) + 1);

        if (MostrarContactos) total.AddRange(_contactos);

        // Los spots van despues para que, cuando caigan sobre el mismo sitio que un contacto,
        // sea el spot el que se vea: es lo que esta pasando ahora mismo en la banda.
        if (MostrarSpots) total.AddRange(_spots);

        // El satelite va el ultimo y con el peso mas alto de todos (ver Peso en CapasDelMapa):
        // es lo unico que se mueve de verdad mientras se mira el mapa.
        if (_satelite is { } satelite) total.Add(satelite);

        Marcas = total;
        OnPropertyChanged(nameof(Resumen));
    }

    private static string DetalleDe(ContactoEnElMapa contacto)
    {
        var fecha = contacto.CuandoUtc.UtcDateTime.ToString("dd-MM-yyyy HH:mm", CultureInfo.InvariantCulture);
        var sitio = contacto.Origen == OrigenDeLaPosicion.Localizador
            ? "por localizador"
            : "situado en el centro del país";

        return $"{contacto.Indicativo.Valor} · {contacto.Banda} {contacto.Modo} · {fecha} UTC" +
               (contacto.Pais is { Length: > 0 } pais ? $" · {pais}" : string.Empty) +
               $" · {sitio}";
    }
}
