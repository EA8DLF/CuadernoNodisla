using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nodisla.Cuaderno.Idiomas;
using Nodisla.Cuaderno.Aplicacion.CasosDeUso;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Entidades;
using DominioBanda = Nodisla.Cuaderno.Dominio.Valores.Banda;

namespace Nodisla.Cuaderno.Ui.VistaModelos;

/// <summary>
/// La rejilla del cuaderno: filtro, orden, busqueda por texto y paginacion. Nunca se trae el
/// cuaderno entero a memoria; se pide una pagina cada vez, asi da igual que haya cien mil filas.
/// </summary>
public sealed partial class VistaModeloCuaderno : ObservableObject
{
    /// <summary>
    /// Valor que representa «no filtrar por este campo». Es un centinela y no se traduce: la
    /// lista lo escribe en el idioma en uso con su propia plantilla.
    /// </summary>
    public const string Cualquiera = "(todas)";

    /// <summary>Texto que representa «cualquier modo».</summary>
    public const string CualquierModo = "(todos)";

    private readonly BuscarEnCuaderno _buscar;
    private readonly EliminarQso _eliminar;

    private CancellationTokenSource? _busquedaEnCurso;

    /// <summary>Crea la rejilla con los casos de uso que necesita.</summary>
    public VistaModeloCuaderno(BuscarEnCuaderno buscar, EliminarQso eliminar)
    {
        _buscar = buscar;
        _eliminar = eliminar;

        Bandas = [Cualquiera, .. DominioBanda.Todas.Select(b => b.Nombre)];
        Modos =
        [
            CualquierModo, "SSB", "CW", "FT8", "FT4", "RTTY", "PSK31", "FM", "AM", "JS8", "SSTV",
            "MFSK", "DIGITALVOICE",
        ];
        TamanosDePagina = [50, 100, 200, 500, 1000];

        // Al cambiar de idioma: los textos calculados aqui y las filas ya formateadas (fechas,
        // «Sí», resumen de QSL) se vuelven a escribir en el idioma nuevo.
        Textos.AlCambiar(this, static vm => vm.AlCambiarDeIdioma());
    }

    private void AlCambiarDeIdioma()
    {
        OnPropertyChanged(nameof(TextoDePagina));
        OnPropertyChanged(nameof(PorQueNoHayFilas));
        var elegida = FilaSeleccionada?.Id;
        for (var i = 0; i < Filas.Count; i++) Filas[i] = new FilaDeQso(Filas[i].Qso);
        if (elegida is { } id) FilaSeleccionada = Filas.FirstOrDefault(f => f.Id == id);
    }

    /// <summary>Se dispara cuando el operador pide modificar un contacto.</summary>
    public event EventHandler<Qso>? SolicitaEditar;

    /// <summary>
    /// Se ha borrado un contacto desde la rejilla. La ventana lo usa para poner al dia lo que
    /// depende del cuaderno entero: el contador de la barra de estado, el mapa y los diplomas.
    /// </summary>
    public event EventHandler? CuadernoCambiado;

    /// <summary>La ventana pone aqui la pregunta de confirmacion antes de borrar.</summary>
    public Func<FilaDeQso, bool>? ConfirmarBorrado { get; set; }

    /// <summary>Filas de la pagina que se esta viendo.</summary>
    public ObservableCollection<FilaDeQso> Filas { get; } = [];

    /// <summary>Bandas que se ofrecen en el filtro.</summary>
    public IReadOnlyList<string> Bandas { get; }

    /// <summary>Modos que se ofrecen en el filtro.</summary>
    public IReadOnlyList<string> Modos { get; }

    /// <summary>Tamanos de pagina que se ofrecen.</summary>
    public IReadOnlyList<int> TamanosDePagina { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PorQueNoHayFilas))]
    private string _textoBuscado = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PorQueNoHayFilas))]
    private string _bandaFiltro = Cualquiera;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PorQueNoHayFilas))]
    private string _modoFiltro = CualquierModo;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EditarSeleccionadoCommand))]
    [NotifyCanExecuteChangedFor(nameof(EliminarSeleccionadoCommand))]
    private FilaDeQso? _filaSeleccionada;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextoDePagina))]
    [NotifyPropertyChangedFor(nameof(TotalDePaginas))]
    private int _tamanoDePagina = 200;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextoDePagina))]
    [NotifyCanExecuteChangedFor(nameof(PrimeraPaginaCommand))]
    [NotifyCanExecuteChangedFor(nameof(PaginaAnteriorCommand))]
    [NotifyCanExecuteChangedFor(nameof(PaginaSiguienteCommand))]
    [NotifyCanExecuteChangedFor(nameof(UltimaPaginaCommand))]
    private int _pagina = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextoDePagina))]
    [NotifyPropertyChangedFor(nameof(TotalDePaginas))]
    [NotifyPropertyChangedFor(nameof(PorQueNoHayFilas))]
    [NotifyCanExecuteChangedFor(nameof(PaginaSiguienteCommand))]
    [NotifyCanExecuteChangedFor(nameof(UltimaPaginaCommand))]
    private int _totalFiltrado;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PorQueNoHayFilas))]
    private bool _cargando;

    [ObservableProperty]
    private CampoDeOrden _ordenarPor = CampoDeOrden.Fecha;

    [ObservableProperty]
    private bool _descendente = true;

    [ObservableProperty]
    private bool _verFrecuencia = true;

    [ObservableProperty]
    private bool _verInformes = true;

    [ObservableProperty]
    private bool _verNombre = true;

    [ObservableProperty]
    private bool _verQth = true;

    // El localizador no se mira en el cuaderno: se mira en el mapa y en la ficha del
    // contacto. Ocho columnas de «IM88PO» seguidas no dicen nada y se llevan el sitio del
    // comentario, que si se lee. Sigue estando a un clic en «Columnas del cuaderno».
    [ObservableProperty]
    private bool _verLocalizador;

    [ObservableProperty]
    private bool _verDistancia;

    // El comentario si: es lo unico de la fila que cuenta que paso en el contacto, y es la
    // columna que se lleva el ancho que sobra en vez de dejar una franja muerta a la derecha.
    [ObservableProperty]
    private bool _verComentario = true;

    [ObservableProperty]
    private bool _verEstacion;

    [ObservableProperty]
    private bool _verConfirmado = true;

    // ── Columnas menos frecuentes: existen, pero no estorban hasta que se piden ──

    [ObservableProperty]
    private bool _verAntena;

    [ObservableProperty]
    private bool _verPropagacion;

    [ObservableProperty]
    private bool _verSwl;

    [ObservableProperty]
    private bool _verCompletado;

    [ObservableProperty]
    private bool _verCasual;

    [ObservableProperty]
    private bool _verMensajeQsl;

    [ObservableProperty]
    private bool _verMiNombre;

    [ObservableProperty]
    private bool _verIota;

    /// <summary>Numero de paginas que hay con el filtro actual.</summary>
    public int TotalDePaginas => TotalFiltrado == 0
        ? 1
        : (int)Math.Ceiling(TotalFiltrado / (double)Math.Max(1, TamanoDePagina));

    /// <summary>Texto del indicador de pagina de la barra de paginacion.</summary>
    public string TextoDePagina => TotalFiltrado == 0
        ? Textos.T("Libro.Pagina.SinContactos")
        : Textos.F(
            "Libro.Pagina.Texto",
            Pagina,
            TotalDePaginas,
            TotalFiltrado);

    /// <summary>Vuelve a pedir la pagina actual al cuaderno.</summary>
    /// <summary>
    /// Por que la rejilla sale vacia, dicho en claro; vacio si hay filas o se esta cargando.
    /// </summary>
    /// <remarks>
    /// La vista ya lo enlazaba y la propiedad no existia: el aviso no salia nunca y una rejilla
    /// en blanco no distinguia «cuaderno vacio» de «el filtro no deja pasar nada».
    /// </remarks>
    public string PorQueNoHayFilas =>
        Cargando || TotalFiltrado > 0 ? string.Empty
        : HayFiltro ? Textos.T("Libro.Vacia.Filtro")
        : Textos.F("Libro.Vacia.Cuaderno", Textos.T("Principal.Nav.Operar"));

    /// <summary>Hay algun filtro puesto.</summary>
    public bool HayFiltro =>
        !string.IsNullOrWhiteSpace(TextoBuscado)
        || !string.Equals(BandaFiltro, Cualquiera, StringComparison.Ordinal)
        || !string.Equals(ModoFiltro, CualquierModo, StringComparison.Ordinal);

    /// <summary>Las casillas «Ver…» de columnas, por nombre, para guardarlas y recuperarlas.</summary>
    private static readonly System.Reflection.PropertyInfo[] CasillasDeColumna =
        typeof(VistaModeloCuaderno).GetProperties()
            .Where(p => p.PropertyType == typeof(bool) && p.Name.StartsWith("Ver", StringComparison.Ordinal) && p.CanWrite)
            .ToArray();

    /// <summary>Nombres de las columnas a la vista. Antes no se guardaban y se perdian al cerrar.</summary>
    public List<string> ColumnasVisibles() =>
        CasillasDeColumna.Where(p => (bool)p.GetValue(this)!).Select(p => p.Name).ToList();

    /// <summary>Deja a la vista exactamente esas columnas.</summary>
    /// <param name="nombres">Nombres de las casillas «Ver…» que van marcadas.</param>
    public void PonerColumnasVisibles(IEnumerable<string> nombres)
    {
        var marcadas = new HashSet<string>(nombres, StringComparer.Ordinal);
        foreach (var p in CasillasDeColumna) p.SetValue(this, marcadas.Contains(p.Name));
    }

    private bool HayAnterior() => Pagina > 1;

    private bool HaySiguiente() => Pagina < TotalDePaginas;

    private bool HayElegida() => FilaSeleccionada is not null;

    [RelayCommand]
    public async Task RefrescarAsync()
    {
        _busquedaEnCurso?.Cancel();
        _busquedaEnCurso?.Dispose();
        var cts = new CancellationTokenSource();
        _busquedaEnCurso = cts;

        // El testigo se saca ANTES de esperar: si mientras tanto llega otro refresco, este cts
        // se anula y se libera, y pedirle el testigo despues reventaba la interfaz.
        var testigo = cts.Token;

        try
        {
            Cargando = true;
            var criterio = ConstruirCriterio();
            var desplazamiento = (Math.Max(1, Pagina) - 1) * TamanoDePagina;

            var pagina = await _buscar
                .EjecutarAsync(criterio, desplazamiento, TamanoDePagina, testigo)
                .ConfigureAwait(true);

            if (testigo.IsCancellationRequested) return;

            // Si el filtro ha dejado la pagina actual fuera de rango, se vuelve a la ultima.
            if (pagina.Elementos.Count == 0 && pagina.TotalFiltrado > 0 && Pagina > 1)
            {
                TotalFiltrado = pagina.TotalFiltrado;
                Pagina = TotalDePaginas;
                await RefrescarAsync().ConfigureAwait(true);
                return;
            }

            var seleccionado = FilaSeleccionada?.Id;
            Filas.Clear();
            foreach (var qso in pagina.Elementos) Filas.Add(new FilaDeQso(qso));
            TotalFiltrado = pagina.TotalFiltrado;
            FilaSeleccionada = Filas.FirstOrDefault(f => f.Id == seleccionado);
        }
        catch (OperationCanceledException)
        {
            // Ha llegado una busqueda mas nueva; esta ya no importa.
        }
        finally
        {
            Cargando = false;
        }
    }

    /// <summary>Aplica el filtro desde el principio del cuaderno.</summary>
    [RelayCommand]
    public async Task BuscarAsync()
    {
        Pagina = 1;
        await RefrescarAsync().ConfigureAwait(true);
    }

    /// <summary>Quita todos los filtros.</summary>
    [RelayCommand]
    private async Task QuitarFiltroAsync()
    {
        TextoBuscado = string.Empty;
        BandaFiltro = Cualquiera;
        ModoFiltro = CualquierModo;
        await BuscarAsync().ConfigureAwait(true);
    }

    /// <summary>Va a la pagina siguiente.</summary>
    [RelayCommand(CanExecute = nameof(HaySiguiente))]
    private async Task PaginaSiguienteAsync()
    {
        if (Pagina >= TotalDePaginas) return;
        Pagina++;
        await RefrescarAsync().ConfigureAwait(true);
    }

    /// <summary>Va a la pagina anterior.</summary>
    [RelayCommand(CanExecute = nameof(HayAnterior))]
    private async Task PaginaAnteriorAsync()
    {
        if (Pagina <= 1) return;
        Pagina--;
        await RefrescarAsync().ConfigureAwait(true);
    }

    /// <summary>Va a la primera pagina.</summary>
    [RelayCommand(CanExecute = nameof(HayAnterior))]
    private async Task PrimeraPaginaAsync()
    {
        if (Pagina == 1) return;
        Pagina = 1;
        await RefrescarAsync().ConfigureAwait(true);
    }

    /// <summary>Va a la ultima pagina.</summary>
    [RelayCommand(CanExecute = nameof(HaySiguiente))]
    private async Task UltimaPaginaAsync()
    {
        if (Pagina == TotalDePaginas) return;
        Pagina = TotalDePaginas;
        await RefrescarAsync().ConfigureAwait(true);
    }

    /// <summary>Pasa el contacto seleccionado al formulario de entrada para modificarlo.</summary>
    [RelayCommand(CanExecute = nameof(HayElegida))]
    public void EditarSeleccionado()
    {
        if (FilaSeleccionada is not { } fila) return;
        SolicitaEditar?.Invoke(this, fila.Qso);
    }

    /// <summary>Borra el contacto seleccionado, siempre previa confirmacion del operador.</summary>
    [RelayCommand(CanExecute = nameof(HayElegida))]
    public async Task EliminarSeleccionadoAsync()
    {
        if (FilaSeleccionada is not { } fila) return;
        if (ConfirmarBorrado is not null && !ConfirmarBorrado(fila)) return;

        await _eliminar.EjecutarAsync(fila.Id).ConfigureAwait(true);
        FilaSeleccionada = null;
        await RefrescarAsync().ConfigureAwait(true);
        CuadernoCambiado?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Ordena por el campo indicado; si ya se ordenaba por el, invierte el sentido.</summary>
    public async Task OrdenarPorCampoAsync(CampoDeOrden campo)
    {
        if (OrdenarPor == campo)
        {
            Descendente = !Descendente;
        }
        else
        {
            OrdenarPor = campo;
            // La fecha se lee del contacto mas reciente al mas antiguo; lo demas, al reves.
            Descendente = campo == CampoDeOrden.Fecha;
        }

        await BuscarAsync().ConfigureAwait(true);
    }

    partial void OnTextoBuscadoChanged(string value) => BuscarConRetardo();

    partial void OnBandaFiltroChanged(string value) => BuscarConRetardo();

    partial void OnModoFiltroChanged(string value) => BuscarConRetardo();

    partial void OnTamanoDePaginaChanged(int value) => BuscarConRetardo();

    private void BuscarConRetardo() => _ = BuscarConRetardoAsync();

    private async Task BuscarConRetardoAsync()
    {
        var espera = new CancellationTokenSource();
        _busquedaEnCurso?.Cancel();
        _busquedaEnCurso?.Dispose();
        _busquedaEnCurso = espera;

        try
        {
            // Se espera a que el operador termine de teclear antes de tocar el cuaderno.
            await Task.Delay(320, espera.Token).ConfigureAwait(true);
            if (espera.Token.IsCancellationRequested) return;
            await BuscarAsync().ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Ha llegado otro cambio de filtro.
        }
    }

    private CriterioQso ConstruirCriterio()
    {
        DominioBanda? banda = null;
        if (!string.Equals(BandaFiltro, Cualquiera, StringComparison.Ordinal)
            && DominioBanda.TryParse(BandaFiltro, out var b))
        {
            banda = b;
        }

        var modo = string.Equals(ModoFiltro, CualquierModo, StringComparison.Ordinal) ? null : ModoFiltro;

        return new CriterioQso
        {
            Texto = string.IsNullOrWhiteSpace(TextoBuscado) ? null : TextoBuscado.Trim(),
            Band = banda,
            Mode = modo,
            OrdenarPor = OrdenarPor,
            Descendente = Descendente,
        };
    }
}
