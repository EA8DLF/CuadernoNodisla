using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Satelites.Catalogo;
using Nodisla.Cuaderno.Satelites.Doppler;
using Nodisla.Cuaderno.Satelites.Fuentes;
using Nodisla.Cuaderno.Satelites.Orbital;
using Nodisla.Cuaderno.Satelites.Prediccion;
using Nodisla.Cuaderno.Satelites.Seguimiento;
using Nodisla.Cuaderno.Ui.Ajustes;
using Nodisla.Cuaderno.Ui.Conversores;
using Nodisla.Cuaderno.Ui.Mapa;
using Serilog;

namespace Nodisla.Cuaderno.Ui.VistaModelos;

/// <summary>Un satelite del catalogo con su proximo paso, listo para la lista.</summary>
public sealed partial class FilaDeSatelite : ObservableObject
{
    /// <summary>Monta la fila sobre el satelite del catalogo.</summary>
    /// <param name="satelite">Satelite con sus transpondedores.</param>
    public FilaDeSatelite(SateliteDeAficionado satelite)
    {
        Satelite = satelite ?? throw new ArgumentNullException(nameof(satelite));
        EsGeoestacionario = Geoestacionario.LongitudDe(satelite.Abreviatura) is not null;
    }

    /// <summary>El satelite del catalogo.</summary>
    public SateliteDeAficionado Satelite { get; }

    /// <summary>Abreviatura, la de <c>SAT_NAME</c>.</summary>
    public string Abreviatura => Satelite.Abreviatura;

    /// <summary>Nombre completo.</summary>
    public string Nombre => Satelite.Nombre;

    /// <summary>Sus transpondedores.</summary>
    public IReadOnlyList<Transpondedor> Transpondedores => Satelite.Transpondedores;

    /// <summary>Situacion del satelite, escrita.</summary>
    public string EstadoTexto => Satelite.Estado switch
    {
        EstadoDelSatelite.Activo => "Activo",
        EstadoDelSatelite.Intermitente => "Intermitente",
        _ => "Inactivo",
    };

    /// <summary>Es un geoestacionario (QO-100): no tiene pasos, esta siempre arriba.</summary>
    public bool EsGeoestacionario { get; }

    /// <summary>Resumen del proximo paso, o de por que no hay uno que ensenar.</summary>
    [ObservableProperty]
    private string _proximoPasoTexto = "Calculando…";

    /// <summary>Hay elementos orbitales cargados para este satelite (o es geoestacionario).</summary>
    [ObservableProperty]
    private bool _cargado;

    /// <summary>El proximo paso es rasante: sube poco y se pierde con facilidad.</summary>
    [ObservableProperty]
    private bool _esRasante;

    /// <summary>Los elementos con los que se calcula el proximo paso ya son viejos.</summary>
    [ObservableProperty]
    private bool _elementosViejos;

    /// <summary>El proximo paso encontrado, o nulo si no hay o el satelite es geoestacionario.</summary>
    public PasoDeSatelite? ProximoPaso { get; private set; }

    /// <summary>Vuelve a calcular el proximo paso.</summary>
    /// <param name="seguidor">Seguidor con los elementos cargados.</param>
    /// <param name="ahoraUtc">Momento actual.</param>
    /// <param name="ventana">Cuanto se mira hacia delante buscando el proximo paso.</param>
    /// <param name="frescura">Plazo dentro del cual los elementos se dan por buenos.</param>
    public void Refrescar(SeguidorDeSatelites seguidor, DateTimeOffset ahoraUtc, TimeSpan ventana, TimeSpan frescura)
    {
        ArgumentNullException.ThrowIfNull(seguidor);

        if (EsGeoestacionario)
        {
            Cargado = true;
            EsRasante = false;
            ElementosViejos = false;
            ProximoPaso = null;
            ProximoPasoTexto =
                "Geoestacionario: siempre por encima del horizonte, con línea de visión despejada hacia el satélite.";
            return;
        }

        var elementos = seguidor.ElementosDe(Abreviatura);
        if (elementos is null)
        {
            Cargado = false;
            EsRasante = false;
            ElementosViejos = false;
            ProximoPaso = null;
            ProximoPasoTexto = "Sin elementos orbitales cargados.";
            return;
        }

        Cargado = true;
        ElementosViejos = ahoraUtc - elementos.Epoca > frescura;

        var pasos = seguidor.Pasos(Abreviatura, ahoraUtc, ventana);
        var siguiente = pasos.FirstOrDefault(p => p.Puesta.Instante > ahoraUtc);
        if (siguiente is null)
        {
            EsRasante = false;
            ProximoPaso = null;
            ProximoPasoTexto = $"Sin pasos en las próximas {ventana.TotalHours:N0} horas.";
            return;
        }

        ProximoPaso = siguiente;
        EsRasante = siguiente.EsRasante;
        ProximoPasoTexto = siguiente.Describir(ahoraUtc, frescura);
    }
}

/// <summary>
/// El panel de satelites: catalogo con sus pasos, seguimiento en vivo y Doppler.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nada de esto sale a la red ni transmite por su cuenta.</b> Los elementos orbitales solo
/// se traen cuando el operador pulsa «Actualizar elementos orbitales»; al arrancar solo se lee
/// la copia que haya en disco de la ultima vez. El seguimiento Doppler solo escribe frecuencia
/// en los dos VFO —nunca PTT ni modo— y solo mientras el operador lo ha pedido con «Seguir».
/// </para>
/// <para>
/// El mapa no es nuevo: el subpunto del satelite es una marca mas dentro del mismo
/// <see cref="VistaModeloMapa"/> que usa la pestana Mapa, puesta con
/// <see cref="VistaModeloMapa.PonerSatelite"/>.
/// </para>
/// </remarks>
public sealed partial class VistaModeloSatelites : ObservableObject
{
    /// <summary>Cuanto se mira hacia delante buscando el proximo paso.</summary>
    private static readonly TimeSpan VentanaDePrediccion = TimeSpan.FromHours(48);

    /// <summary>Cada cuantos latidos de un segundo se recalcula la lista entera de pasos.</summary>
    private const int RefrescoDeListaCadaTics = 300;

    private readonly CatalogoDeSatelites _catalogo;
    private readonly SeguidorDeSatelites _seguidor;
    private readonly OpcionesDeSatelites _opciones;
    private readonly IControlEquipo _equipo;
    private readonly IHttpClientFactory _httpFactory;
    private readonly AjustesDelPrograma _ajustes;
    private readonly string _carpetaDeDatos;
    private readonly VistaModeloMapa _mapa;
    private readonly System.Windows.Threading.DispatcherTimer _reloj;
    private readonly TimeProvider _hora;

    private SeguimientoDoppler? _seguimiento;
    private int _tics;

    /// <summary>Monta el panel de satelites.</summary>
    /// <param name="catalogo">Catalogo de satelites de aficionado.</param>
    /// <param name="seguidor">Seguidor con los elementos orbitales que se vayan cargando.</param>
    /// <param name="opciones">Ajustes compartidos del seguidor, para fijar la estacion.</param>
    /// <param name="equipo">Control del equipo, para el seguimiento Doppler si tiene dos VFO.</param>
    /// <param name="httpFactory">Fabrica de clientes HTTP, solo para la descarga explicita.</param>
    /// <param name="ajustes">Ajustes del programa, para recordar el reparto de VFO.</param>
    /// <param name="carpetaDeDatos">Carpeta de datos, donde se guarda la cache de elementos.</param>
    /// <param name="mapa">El mapa compartido, para dibujar el subpunto del satelite.</param>
    /// <param name="hora">De donde se saca la hora; nulo para la del sistema. Las pruebas ponen una fija.</param>
    public VistaModeloSatelites(
        CatalogoDeSatelites catalogo,
        SeguidorDeSatelites seguidor,
        OpcionesDeSatelites opciones,
        IControlEquipo equipo,
        IHttpClientFactory httpFactory,
        AjustesDelPrograma ajustes,
        string carpetaDeDatos,
        VistaModeloMapa mapa,
        TimeProvider? hora = null)
    {
        _hora = hora ?? TimeProvider.System;
        _catalogo = catalogo ?? throw new ArgumentNullException(nameof(catalogo));
        _seguidor = seguidor ?? throw new ArgumentNullException(nameof(seguidor));
        _opciones = opciones ?? throw new ArgumentNullException(nameof(opciones));
        _equipo = equipo ?? throw new ArgumentNullException(nameof(equipo));
        _httpFactory = httpFactory ?? throw new ArgumentNullException(nameof(httpFactory));
        _ajustes = ajustes ?? throw new ArgumentNullException(nameof(ajustes));
        ArgumentException.ThrowIfNullOrWhiteSpace(carpetaDeDatos);
        _carpetaDeDatos = carpetaDeDatos;
        _mapa = mapa ?? throw new ArgumentNullException(nameof(mapa));

        // Sin propiedad de por medio: el valor de fabrica no tiene que dejar rastro en el
        // fichero de ajustes ni disparar un guardado antes de que el operador toque nada.
        _vfoDeSubida = ajustes.Satelites.VfoDeSubida;
        _vfoDeBajada = ajustes.Satelites.VfoDeBajada;

        if (_equipo is IControlEquipoConmutable conmutable)
        {
            conmutable.ControlCambiado += AlCambiarElControl;
        }

        foreach (var satelite in _catalogo.EnServicio.OrderBy(s => s.Abreviatura, StringComparer.OrdinalIgnoreCase))
        {
            Satelites.Add(new FilaDeSatelite(satelite));
        }

        RefrescarPasos();

        // Un segundo de cadencia: sobra para orbita baja y no carga nada, porque mientras no
        // hay satelite elegido el reloj esta parado.
        _reloj = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1),
        };
        _reloj.Tick += async (_, _) => await LatirAsync().ConfigureAwait(true);

        // Con un satelite ya elegido, para poder capturar el panel entero sin tener que
        // darle clics a la ventana del operador. Es el mismo recurso que CUADERNO_INDICATIVO:
        // en uso normal la variable no esta puesta y esto no existe.
        if (Environment.GetEnvironmentVariable("CUADERNO_SATELITE") is { Length: > 0 } inicial)
        {
            SateliteSeleccionado = Satelites.FirstOrDefault(
                f => string.Equals(f.Abreviatura, inicial, StringComparison.OrdinalIgnoreCase));

            // Con CUADERNO_DOPPLER puesta ademas se pulsa «Seguir», para poder comprobar que
            // el seguimiento escribe frecuencia en un equipo de mentira sin tener que tocar la
            // ventana. Nunca toca PTT ni modo: SeguimientoDoppler no sabe hacerlo.
            if (Environment.GetEnvironmentVariable("CUADERNO_DOPPLER") is { Length: > 0 }
                && SeguirDopplerCommand.CanExecute(null))
            {
                SeguirDopplerCommand.Execute(null);
            }
        }
    }

    /// <summary>Satelites del catalogo, con su proximo paso.</summary>
    public ObservableCollection<FilaDeSatelite> Satelites { get; } = [];

    /// <summary>Transpondedores del satelite elegido.</summary>
    public ObservableCollection<Transpondedor> Transpondedores { get; } = [];

    /// <summary>Los dos VFO entre los que se reparte el Doppler.</summary>
    public IReadOnlyList<NombreDeVfo> VfosDisponibles { get; } = [NombreDeVfo.A, NombreDeVfo.B];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SeguirDopplerCommand))]
    [NotifyPropertyChangedFor(nameof(HaySateliteSeleccionado))]
    [NotifyPropertyChangedFor(nameof(NombreDelSateliteElegido))]
    private FilaDeSatelite? _sateliteSeleccionado;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SeguirDopplerCommand))]
    private Transpondedor? _transpondedorSeleccionado;

    [ObservableProperty]
    private NombreDeVfo _vfoDeSubida;

    [ObservableProperty]
    private NombreDeVfo _vfoDeBajada;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SeguirDopplerCommand))]
    [NotifyCanExecuteChangedFor(nameof(SoltarDopplerCommand))]
    private bool _siguiendoDoppler;

    [ObservableProperty]
    private string _azEnVivoTexto = "—";

    [ObservableProperty]
    private string _elEnVivoTexto = "—";

    [ObservableProperty]
    private string _distanciaTexto = "—";

    [ObservableProperty]
    private bool _sobreElHorizonte;

    /// <summary>
    /// Por que no se ve el satelite ahora mismo, o vacio si se ve.
    /// </summary>
    /// <remarks>
    /// Antes decia «Por debajo del horizonte» tambien cuando no habia elementos orbitales, y
    /// entonces no se sabe donde esta: no es lo mismo y hay que decir que hacer.
    /// </remarks>
    [ObservableProperty]
    private string _avisoDePosicion = string.Empty;

    [ObservableProperty]
    private string _desplazamientoSubidaTexto = "—";

    [ObservableProperty]
    private string _desplazamientoBajadaTexto = "—";

    [ObservableProperty]
    private string _avisoDoppler = string.Empty;

    [ObservableProperty]
    private string _avisoElementos = "Sin elementos orbitales cargados. Pulse «Actualizar elementos orbitales».";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ActualizarElementosCommand))]
    private bool _actualizandoElementos;

    /// <summary>El equipo conectado informa de sus dos VFO, asi que se puede seguir el Doppler.</summary>
    public bool HayEquipoConDosVfos => EquipoConDosVfos is not null;

    /// <summary>
    /// Nombre del satelite elegido, o vacio. Propiedad propia y no «SateliteSeleccionado.Nombre»:
    /// con nada elegido ese camino queda roto y cuenta como enlace muerto.
    /// </summary>
    public string NombreDelSateliteElegido => SateliteSeleccionado?.Nombre ?? string.Empty;

    /// <summary>Hay un satelite elegido en la lista.</summary>
    public bool HaySateliteSeleccionado => SateliteSeleccionado is not null;

    /// <summary>Que VFO lleva cada sentido, en una linea.</summary>
    public string RepartoDeVfosTexto => $"Subida por VFO {VfoDeSubida}, bajada por VFO {VfoDeBajada}.";

    private IEquipoConDosVfos? EquipoConDosVfos =>
        (_equipo is IControlEquipoConmutable conmutable ? conmutable.Actual : _equipo) as IEquipoConDosVfos;

    /// <summary>
    /// Lee del disco los elementos que se hubieran descargado la ultima vez.
    /// </summary>
    /// <remarks>
    /// Es lectura de disco, no de red: se puede llamar al abrir la pestana sin romper la regla
    /// de que nada sale a internet por su cuenta.
    /// </remarks>
    public async Task CargarCacheAsync(CancellationToken ct = default)
    {
        var descripciones = new List<string>();

        foreach (var fuente in FuentesDeElementos.Todas)
        {
            var ruta = RutaDeCache(fuente);
            if (!File.Exists(ruta))
            {
                continue;
            }

            try
            {
                var lectura = await DescargaDeElementos.LeerDeDiscoAsync(ruta, fuente.Titulo, ct).ConfigureAwait(true);
                _seguidor.CargarFichero(lectura.TextoOriginal);
                descripciones.Add(lectura.Describir(_hora.GetUtcNow(), _opciones.FrescuraDeLosElementos));
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "No se ha podido leer la caché de elementos de {Fuente}.", fuente.Titulo);
            }
        }

        if (descripciones.Count > 0)
        {
            AvisoElementos = string.Join("  ·  ", descripciones);
        }

        RefrescarPasos();
    }

    /// <summary>
    /// Descarga los elementos orbitales de las fuentes conocidas y los deja listos para usar.
    /// </summary>
    /// <remarks>
    /// Es la unica forma de que el modulo salga a la red: el operador lo pide con este boton, y
    /// solo entonces.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(SePuedeActualizarElementos))]
    public async Task ActualizarElementosAsync()
    {
        if (ActualizandoElementos)
        {
            return;
        }

        ActualizandoElementos = true;
        AvisoElementos = "Descargando elementos orbitales…";

        try
        {
            var cliente = _httpFactory.CreateClient();
            var descarga = new DescargaDeElementos(cliente);
            var resumen = new List<string>();

            foreach (var fuente in FuentesDeElementos.Todas)
            {
                try
                {
                    var lectura = await descarga.TraerAsync(fuente).ConfigureAwait(true);
                    var cargados = _seguidor.CargarFichero(lectura.TextoOriginal);
                    resumen.Add($"{fuente.Titulo}: {cargados} satélites.");

                    await DescargaDeElementos.GuardarAsync(lectura, RutaDeCache(fuente)).ConfigureAwait(true);
                }
                catch (Exception ex)
                {
                    resumen.Add($"{fuente.Titulo}: no se ha podido descargar ({ex.Message}).");
                    Log.Warning(ex, "No se han podido descargar los elementos de {Fuente}.", fuente.Titulo);
                }
            }

            AvisoElementos =
                $"Actualizado a las {_hora.GetUtcNow().UtcDateTime.ToString("HH:mm", CultureInfo.InvariantCulture)} UTC.  ·  "
                + string.Join("  ·  ", resumen);

            RefrescarPasos();
        }
        finally
        {
            ActualizandoElementos = false;
        }
    }

    /// <summary>Vuelve a calcular el proximo paso de todos los satelites.</summary>
    [RelayCommand]
    public void RefrescarPasos()
    {
        var ahora = _hora.GetUtcNow();
        foreach (var fila in Satelites)
        {
            fila.Refrescar(_seguidor, ahora, VentanaDePrediccion, _opciones.FrescuraDeLosElementos);
        }
    }

    /// <summary>Empieza a seguir el Doppler del transpondedor elegido.</summary>
    [RelayCommand(CanExecute = nameof(SePuedeSeguirDoppler))]
    public void SeguirDoppler()
    {
        if (SateliteSeleccionado is not { } fila || TranspondedorSeleccionado is not { } transpondedor)
        {
            return;
        }

        if (EquipoConDosVfos is not { } equipoConDosVfos)
        {
            AvisoDoppler = "El equipo conectado no informa de sus dos VFO: no se puede seguir el Doppler.";
            return;
        }

        // Subida y bajada por el mismo VFO: se escribiria la bajada y encima la subida, y el
        // receptor se quedaria sintonizado en la frecuencia de transmision.
        if (VfoDeSubida == VfoDeBajada)
        {
            AvisoDoppler = $"La subida y la bajada van las dos por el VFO {VfoDeSubida}: elija un VFO distinto para cada una.";
            return;
        }

        // Sin elementos orbitales no hay posicion, y sin posicion no hay Doppler que corregir:
        // antes se decia «Siguiendo» y no se movia nada.
        if (_seguidor.Donde(fila.Abreviatura, _hora.GetUtcNow()) is null)
        {
            AvisoDoppler = fila.EsGeoestacionario
                ? $"{fila.Abreviatura} es geoestacionario: su Doppler es despreciable y no hace falta seguirlo."
                : $"No hay elementos orbitales de {fila.Abreviatura}: pulse «Actualizar elementos orbitales» antes de seguir su Doppler.";
            return;
        }

        var opciones = new OpcionesDeDoppler { VfoDeSubida = VfoDeSubida, VfoDeBajada = VfoDeBajada };
        _seguimiento = new SeguimientoDoppler(_seguidor, equipoConDosVfos, opciones);
        _seguimiento.Seguir(EnlaceDeSatelite.Centrado(transpondedor));

        SiguiendoDoppler = true;
        AvisoDoppler = $"Siguiendo el Doppler de {fila.Abreviatura} · {transpondedor.NombreDelTranspondedor}.";
    }

    /// <summary>Deja de seguir el Doppler. No devuelve el equipo a ninguna frecuencia.</summary>
    [RelayCommand(CanExecute = nameof(SePuedeSoltarDoppler))]
    public void SoltarDoppler()
    {
        _seguimiento?.Soltar();
        _seguimiento = null;
        SiguiendoDoppler = false;
        DesplazamientoSubidaTexto = "—";
        DesplazamientoBajadaTexto = "—";
        AvisoDoppler = string.Empty;
    }

    /// <summary>
    /// Pone la estacion desde la que se observa, a partir de su localizador.
    /// </summary>
    /// <remarks>
    /// Lo llama la ventana cuando cambia el perfil de estacion activo, igual que hace con el
    /// mapa y con la franja solar. Sin esto, todos los azimuts y elevaciones saldrian medidos
    /// desde el origen de coordenadas.
    /// </remarks>
    /// <param name="localizador">Localizador de la estacion activa.</param>
    public void FijarEstacion(Locator localizador)
    {
        if (localizador.EsVacio)
        {
            return;
        }

        _opciones.Observador = Observador.DesdeLocator(localizador);
    }

    /// <summary>Deja de mirar el reloj y suelta el Doppler. Lo llama la ventana al cerrarse.</summary>
    public void Detener()
    {
        _reloj.Stop();
        SoltarDoppler();

        if (_equipo is IControlEquipoConmutable conmutable)
        {
            conmutable.ControlCambiado -= AlCambiarElControl;
        }
    }

    partial void OnSateliteSeleccionadoChanged(FilaDeSatelite? value)
    {
        Transpondedores.Clear();
        if (value is not null)
        {
            foreach (var transpondedor in value.Transpondedores)
            {
                Transpondedores.Add(transpondedor);
            }
        }

        TranspondedorSeleccionado = Transpondedores.FirstOrDefault();

        // Cambiar de satelite sin soltar antes seguiria corrigiendo el Doppler del satelite
        // equivocado.
        if (SiguiendoDoppler)
        {
            SoltarDoppler();
        }

        AzEnVivoTexto = "—";
        ElEnVivoTexto = "—";
        DistanciaTexto = "—";
        SobreElHorizonte = false;
        AvisoDePosicion = string.Empty;

        if (value is null)
        {
            _reloj.Stop();
            _mapa.PonerSatelite(null);
            return;
        }

        _reloj.Start();
        _ = LatirAsync();
    }

    partial void OnVfoDeSubidaChanged(NombreDeVfo value)
    {
        OnPropertyChanged(nameof(RepartoDeVfosTexto));
        SeguirDopplerCommand.NotifyCanExecuteChanged();
        _ajustes.Satelites.VfoDeSubida = value;
        _ajustes.Guardar(_carpetaDeDatos);
    }

    partial void OnVfoDeBajadaChanged(NombreDeVfo value)
    {
        OnPropertyChanged(nameof(RepartoDeVfosTexto));
        SeguirDopplerCommand.NotifyCanExecuteChanged();
        _ajustes.Satelites.VfoDeBajada = value;
        _ajustes.Guardar(_carpetaDeDatos);
    }

    private bool SePuedeSeguirDoppler() =>
        !SiguiendoDoppler
        && SateliteSeleccionado is not null
        && TranspondedorSeleccionado is not null
        && HayEquipoConDosVfos;

    private bool SePuedeActualizarElementos() => !ActualizandoElementos;

    private bool SePuedeSoltarDoppler() => SiguiendoDoppler;

    /// <summary>
    /// Un latido: posicion en vivo, subpunto en el mapa y, si se sigue, un ajuste del Doppler.
    /// </summary>
    /// <remarks>Lo llama el reloj cada segundo; es publico para poder probarlo sin esperar.</remarks>
    /// <returns>La tarea del latido.</returns>
    public async Task LatirAsync()
    {
        if (SateliteSeleccionado is not { } fila)
        {
            return;
        }

        var ahora = _hora.GetUtcNow();
        var estado = _seguidor.Donde(fila.Abreviatura, ahora);
        if (estado is null)
        {
            AzEnVivoTexto = "—";
            ElEnVivoTexto = "—";
            DistanciaTexto = "—";
            SobreElHorizonte = false;
            AvisoDePosicion = fila.EsGeoestacionario
                ? "Geoestacionario: siempre a la vista, en la misma posición."
                : "Sin elementos orbitales: no se sabe dónde está. Pulse «Actualizar elementos orbitales».";
            _mapa.PonerSatelite(null);
            return;
        }

        var ci = CultureInfo.CurrentCulture;
        AzEnVivoTexto = $"{estado.Vista.AzimutGrados.ToString("F1", ci)}°";
        ElEnVivoTexto = $"{estado.Vista.ElevacionGrados.ToString("F1", ci)}°";
        DistanciaTexto = $"{estado.Vista.DistanciaKm.ToString("N0", ci)} km";
        SobreElHorizonte = estado.Vista.SobreElHorizonte;
        AvisoDePosicion = SobreElHorizonte ? string.Empty : "Por debajo del horizonte.";

        _mapa.PonerSatelite(new MarcaDelMapa(estado.Subpunto, fila.Abreviatura, ClaseDeMarca.Satelite)
        {
            Detalle = $"{fila.Nombre} · subpunto a las {ahora.UtcDateTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture)} UTC",
        });

        if (SiguiendoDoppler && _seguimiento is not null)
        {
            try
            {
                var resultado = await _seguimiento.AjustarAsync(ahora).ConfigureAwait(true);
                DesplazamientoSubidaTexto = FormatoDeDesplazamiento(resultado.Sintonia.DesplazamientoSubidaHz);
                DesplazamientoBajadaTexto = FormatoDeDesplazamiento(resultado.Sintonia.DesplazamientoBajadaHz);
                AvisoDoppler = resultado.Motivo ?? string.Empty;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "No se ha podido ajustar el Doppler.");
                AvisoDoppler = $"No se ha podido ajustar el Doppler: {ex.Message}";
            }
        }

        _tics++;
        if (_tics % RefrescoDeListaCadaTics == 0)
        {
            RefrescarPasos();
        }
    }

    private void AlCambiarElControl(object? origen, IControlEquipo nuevo) => Hilo.EnLaVentana(() =>
    {
        if (SiguiendoDoppler)
        {
            SoltarDoppler();
        }

        OnPropertyChanged(nameof(HayEquipoConDosVfos));
        SeguirDopplerCommand.NotifyCanExecuteChanged();
    });

    private static string FormatoDeDesplazamiento(double hz) =>
        $"{hz.ToString("+0;-0;0", CultureInfo.CurrentCulture)} Hz";

    private string RutaDeCache(FuenteDeElementos fuente) =>
        Path.Combine(_carpetaDeDatos, "satelites", $"{fuente.Clave}.txt");
}
