using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    /// <summary>Texto que representa «no filtrar por este campo».</summary>
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
    }

    /// <summary>Se dispara cuando el operador pide modificar un contacto.</summary>
    public event EventHandler<Qso>? SolicitaEditar;

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
    private string _textoBuscado = string.Empty;

    [ObservableProperty]
    private string _bandaFiltro = Cualquiera;

    [ObservableProperty]
    private string _modoFiltro = CualquierModo;

    [ObservableProperty]
    private FilaDeQso? _filaSeleccionada;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextoDePagina))]
    [NotifyPropertyChangedFor(nameof(TotalDePaginas))]
    private int _tamanoDePagina = 200;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextoDePagina))]
    private int _pagina = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextoDePagina))]
    [NotifyPropertyChangedFor(nameof(TotalDePaginas))]
    private int _totalFiltrado;

    [ObservableProperty]
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

    [ObservableProperty]
    private bool _verLocalizador = true;

    [ObservableProperty]
    private bool _verDistancia;

    [ObservableProperty]
    private bool _verComentario;

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
        ? "Sin contactos"
        : string.Format(
            CultureInfo.CurrentCulture,
            "Página {0} de {1} · {2:N0} contactos",
            Pagina,
            TotalDePaginas,
            TotalFiltrado);

    /// <summary>Vuelve a pedir la pagina actual al cuaderno.</summary>
    [RelayCommand]
    public async Task RefrescarAsync()
    {
        _busquedaEnCurso?.Cancel();
        _busquedaEnCurso?.Dispose();
        var cts = new CancellationTokenSource();
        _busquedaEnCurso = cts;

        try
        {
            Cargando = true;
            var criterio = ConstruirCriterio();
            var desplazamiento = (Math.Max(1, Pagina) - 1) * TamanoDePagina;

            var pagina = await _buscar
                .EjecutarAsync(criterio, desplazamiento, TamanoDePagina, cts.Token)
                .ConfigureAwait(true);

            if (cts.Token.IsCancellationRequested) return;

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
    [RelayCommand]
    private async Task PaginaSiguienteAsync()
    {
        if (Pagina >= TotalDePaginas) return;
        Pagina++;
        await RefrescarAsync().ConfigureAwait(true);
    }

    /// <summary>Va a la pagina anterior.</summary>
    [RelayCommand]
    private async Task PaginaAnteriorAsync()
    {
        if (Pagina <= 1) return;
        Pagina--;
        await RefrescarAsync().ConfigureAwait(true);
    }

    /// <summary>Va a la primera pagina.</summary>
    [RelayCommand]
    private async Task PrimeraPaginaAsync()
    {
        if (Pagina == 1) return;
        Pagina = 1;
        await RefrescarAsync().ConfigureAwait(true);
    }

    /// <summary>Va a la ultima pagina.</summary>
    [RelayCommand]
    private async Task UltimaPaginaAsync()
    {
        if (Pagina == TotalDePaginas) return;
        Pagina = TotalDePaginas;
        await RefrescarAsync().ConfigureAwait(true);
    }

    /// <summary>Pasa el contacto seleccionado al formulario de entrada para modificarlo.</summary>
    [RelayCommand]
    public void EditarSeleccionado()
    {
        if (FilaSeleccionada is not { } fila) return;
        SolicitaEditar?.Invoke(this, fila.Qso);
    }

    /// <summary>Borra el contacto seleccionado, siempre previa confirmacion del operador.</summary>
    [RelayCommand]
    public async Task EliminarSeleccionadoAsync()
    {
        if (FilaSeleccionada is not { } fila) return;
        if (ConfirmarBorrado is not null && !ConfirmarBorrado(fila)) return;

        await _eliminar.EjecutarAsync(fila.Id).ConfigureAwait(true);
        FilaSeleccionada = null;
        await RefrescarAsync().ConfigureAwait(true);
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
