using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Serilog;

namespace Nodisla.Cuaderno.Ui.VistaModelos;

/// <summary>Un diploma del catalogo, con su variante, listo para elegir y para enseñar.</summary>
public sealed partial class FilaDeDiploma : ObservableObject
{
    /// <summary>Monta la fila.</summary>
    /// <param name="diploma">Diploma del catalogo.</param>
    /// <param name="variante">Variante concreta.</param>
    public FilaDeDiploma(Diploma diploma, VarianteDeDiploma variante)
    {
        Diploma = diploma ?? throw new ArgumentNullException(nameof(diploma));
        Variante = variante ?? throw new ArgumentNullException(nameof(variante));
    }

    /// <summary>Diploma al que pertenece.</summary>
    public Diploma Diploma { get; }

    /// <summary>Variante concreta.</summary>
    public VarianteDeDiploma Variante { get; }

    /// <summary>Clave con la que se recuerda la eleccion: <c>CODIGO/VARIANTE</c>.</summary>
    public string Clave => $"{Diploma.Codigo}/{Variante.Variante}";

    /// <summary>
    /// Nombre corto para la lista: el codigo y la variante.
    /// </summary>
    /// <remarks>
    /// Corto a proposito. La lista de elegir es estrecha, y «DXCC · Mixto» se lee entero
    /// mientras que «DXCC — Entidades del mundo · Mixto» se corta justo por la variante, que
    /// es la mitad que distingue una fila de la siguiente.
    /// </remarks>
    public string Nombre => $"{Diploma.Codigo} · {Variante.Variante}";

    /// <summary>Nombre largo, para la ayuda emergente y el bloque de progreso.</summary>
    public string NombreLargo => $"{Diploma.Nombre} · {Variante.Variante}";

    /// <summary>Quien lo otorga.</summary>
    public string Gestor => Diploma.Gestor ?? "sin gestor conocido";

    /// <summary>Es uno de los diplomas que el operador sigue.</summary>
    [ObservableProperty]
    private bool _elegido;

    /// <summary>Progreso, cuando ya se ha calculado.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Cifra))]
    [NotifyPropertyChangedFor(nameof(Faltan))]
    [NotifyPropertyChangedFor(nameof(Fraccion))]
    [NotifyPropertyChangedFor(nameof(EsFirme))]
    [NotifyPropertyChangedFor(nameof(PorQueNoEsFirme))]
    private ProgresoDeDiploma? _progreso;

    /// <summary>Confirmadas sobre el objetivo, escrito.</summary>
    public string Cifra => Progreso is null
        ? "—"
        : Progreso.Objetivo is > 0
            ? $"{Progreso.Confirmadas.ToString("N0", CultureInfo.CurrentCulture)} de {Progreso.Objetivo!.Value.ToString("N0", CultureInfo.CurrentCulture)}"
            : Progreso.Confirmadas.ToString("N0", CultureInfo.CurrentCulture);

    /// <summary>Cuantas faltan, escrito para el operador.</summary>
    public string Faltan => Progreso?.Faltan switch
    {
        null => string.Empty,
        0 => "Ya lo tiene",
        var n => $"Le faltan {n.Value.ToString("N0", CultureInfo.CurrentCulture)}",
    };

    /// <summary>Parte cubierta, de cero a uno, para la barra.</summary>
    public double Fraccion => Progreso?.Fraccion ?? 0;

    /// <summary>La cifra es firme y sirve para pedir el diploma.</summary>
    public bool EsFirme => Progreso?.EsFirme ?? true;

    /// <summary>Por que la cifra no es firme. Vacio cuando lo es.</summary>
    public string PorQueNoEsFirme => Progreso?.PorQueNoEsFirme ?? string.Empty;

    /// <summary>Lo trabajado, para la ayuda emergente.</summary>
    public string Detalle => Progreso is null
        ? "Sin calcular"
        : $"Trabajadas {Progreso.Trabajadas.ToString("N0", CultureInfo.CurrentCulture)}, "
          + $"confirmadas {Progreso.Confirmadas.ToString("N0", CultureInfo.CurrentCulture)}. "
          + $"Calculado a las {Progreso.CalculadoUtc.UtcDateTime.ToString("HH:mm", CultureInfo.InvariantCulture)} UTC.";
}

/// <summary>
/// La pantalla de diplomas: elegir los propios, ver como van y mirar el detalle.
/// </summary>
/// <remarks>
/// <para>
/// <b>Sin eleccion no hay cifras, y se dice.</b> El motor devuelve lista vacia a proposito
/// cuando no hay diplomas marcados: recalcular los ochenta y siete «por si acaso» seria
/// trabajo tirado sobre medio millon de referencias. Asi que la pantalla empieza invitando a
/// elegir, y no finge estar calculando algo.
/// </para>
/// <para>
/// El detalle va <b>paginado</b> porque hay diplomas enormes —SOTA pasa de 182.000
/// referencias—. Traerlas todas para ensenar treinta dejaria la ventana colgada.
/// </para>
/// </remarks>
public sealed partial class VistaModeloDiplomas : ObservableObject
{
    /// <summary>Cuantas referencias se piden de una vez en el detalle.</summary>
    public const int ReferenciasPorPagina = 100;

    private readonly IDiplomas _diplomas;
    private readonly Ajustes.DiplomasElegidos _eleccion;

    /// <summary>Monta la pantalla.</summary>
    /// <param name="diplomas">Motor de diplomas.</param>
    /// <param name="eleccion">Los diplomas que el operador sigue, recordados entre sesiones.</param>
    public VistaModeloDiplomas(IDiplomas diplomas, Ajustes.DiplomasElegidos eleccion)
    {
        _diplomas = diplomas ?? throw new ArgumentNullException(nameof(diplomas));
        _eleccion = eleccion ?? throw new ArgumentNullException(nameof(eleccion));
    }

    /// <summary>Todo el catalogo, para elegir.</summary>
    public ObservableCollection<FilaDeDiploma> Catalogo { get; } = [];

    /// <summary>Los que el operador sigue, con su progreso.</summary>
    public ObservableCollection<FilaDeDiploma> Mios { get; } = [];

    /// <summary>Las referencias de la variante que se esta mirando.</summary>
    public ObservableCollection<EstadoDeReferencia> Referencias { get; } = [];

    /// <summary>Diploma cuyo detalle se esta mirando.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TituloDelDetalle))]
    private FilaDeDiploma? _elegidoParaElDetalle;

    /// <summary>Se esta trabajando.</summary>
    [ObservableProperty]
    private bool _ocupado;

    /// <summary>Lo que esta pasando, escrito.</summary>
    [ObservableProperty]
    private string _aviso = string.Empty;

    /// <summary>No hay ningun diploma elegido todavia.</summary>
    [ObservableProperty]
    private bool _sinEleccion = true;

    /// <summary>Cuantas referencias tiene la variante que se mira.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextoDeLaPagina))]
    private int _totalDeReferencias;

    /// <summary>Por que pagina del detalle se va.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextoDeLaPagina))]
    private int _desplazamiento;

    /// <summary>Titulo del bloque de detalle.</summary>
    public string TituloDelDetalle => ElegidoParaElDetalle is null
        ? "Detalle"
        : $"Detalle · {ElegidoParaElDetalle.Nombre}";

    /// <summary>Que trozo del detalle se esta viendo.</summary>
    public string TextoDeLaPagina => TotalDeReferencias == 0
        ? string.Empty
        : $"{(Desplazamiento + 1).ToString("N0", CultureInfo.CurrentCulture)} a "
          + $"{Math.Min(Desplazamiento + ReferenciasPorPagina, TotalDeReferencias).ToString("N0", CultureInfo.CurrentCulture)} "
          + $"de {TotalDeReferencias.ToString("N0", CultureInfo.CurrentCulture)}";

    /// <summary>Trae el catalogo y recalcula lo elegido. Lo llama la ventana al abrirse.</summary>
    [RelayCommand]
    public async Task CargarAsync()
    {
        if (Catalogo.Count > 0) return;

        Ocupado = true;
        Aviso = "Trayendo el catálogo de diplomas…";
        try
        {
            var diplomas = await _diplomas.CatalogoAsync().ConfigureAwait(true);

            foreach (var diploma in diplomas)
            {
                var variantes = await _diplomas.VariantesAsync(diploma.Codigo).ConfigureAwait(true);
                foreach (var variante in variantes)
                {
                    var fila = new FilaDeDiploma(diploma, variante)
                    {
                        Elegido = _eleccion.Contiene($"{diploma.Codigo}/{variante.Variante}"),
                    };

                    fila.PropertyChanged += AlCambiarLaEleccion;
                    Catalogo.Add(fila);
                }
            }

            Aviso = string.Empty;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se ha podido traer el catálogo de diplomas.");
            Aviso = $"No se ha podido traer el catálogo: {ex.Message}";
        }
        finally
        {
            Ocupado = false;
        }

        await RefrescarAsync().ConfigureAwait(true);
    }

    /// <summary>Vuelve a calcular el progreso de los diplomas elegidos.</summary>
    [RelayCommand]
    public async Task RefrescarAsync()
    {
        var elegidos = Catalogo.Where(f => f.Elegido).ToList();

        Mios.Clear();
        SinEleccion = elegidos.Count == 0;
        if (SinEleccion) return;

        Ocupado = true;
        Aviso = $"Calculando {elegidos.Count} diploma(s)…";
        try
        {
            foreach (var fila in elegidos)
            {
                fila.Progreso = await _diplomas
                    .ProgresoAsync(fila.Diploma.Codigo, fila.Variante.Variante)
                    .ConfigureAwait(true);

                Mios.Add(fila);
            }

            Aviso = string.Empty;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se ha podido calcular el progreso de los diplomas.");
            Aviso = $"No se ha podido calcular el progreso: {ex.Message}";
        }
        finally
        {
            Ocupado = false;
        }
    }

    /// <summary>Trae el detalle de la variante elegida, desde el principio.</summary>
    /// <param name="fila">Diploma y variante que se quiere mirar.</param>
    [RelayCommand]
    public async Task VerElDetalleAsync(FilaDeDiploma? fila)
    {
        if (fila is null) return;

        ElegidoParaElDetalle = fila;
        Desplazamiento = 0;
        await TraerLaPaginaAsync().ConfigureAwait(true);
    }

    /// <summary>Pasa a la pagina siguiente del detalle.</summary>
    [RelayCommand]
    public async Task PaginaSiguienteAsync()
    {
        if (Desplazamiento + ReferenciasPorPagina >= TotalDeReferencias) return;

        Desplazamiento += ReferenciasPorPagina;
        await TraerLaPaginaAsync().ConfigureAwait(true);
    }

    /// <summary>Vuelve a la pagina anterior del detalle.</summary>
    [RelayCommand]
    public async Task PaginaAnteriorAsync()
    {
        if (Desplazamiento == 0) return;

        Desplazamiento = Math.Max(0, Desplazamiento - ReferenciasPorPagina);
        await TraerLaPaginaAsync().ConfigureAwait(true);
    }

    /// <summary>Guarda la eleccion. Lo llama la ventana al cerrarse.</summary>
    /// <param name="carpeta">Carpeta de datos del programa.</param>
    public void GuardarLaEleccion(string carpeta)
    {
        _eleccion.Poner(Catalogo.Where(f => f.Elegido).Select(f => f.Clave));
        _eleccion.Escribir(carpeta);
    }

    private async Task TraerLaPaginaAsync()
    {
        if (ElegidoParaElDetalle is not { } fila) return;

        Ocupado = true;
        try
        {
            var pagina = await _diplomas.DetalleAsync(
                fila.Diploma.Codigo,
                fila.Variante.Variante,
                Desplazamiento,
                ReferenciasPorPagina).ConfigureAwait(true);

            Referencias.Clear();
            foreach (var referencia in pagina.Elementos) Referencias.Add(referencia);

            TotalDeReferencias = pagina.TotalFiltrado;

            Aviso = TotalDeReferencias == 0
                ? "Este diploma cuenta referencias que el cuaderno todavía no guarda."
                : string.Empty;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se ha podido traer el detalle del diploma.");
            Aviso = $"No se ha podido traer el detalle: {ex.Message}";
        }
        finally
        {
            Ocupado = false;
        }
    }

    private async void AlCambiarLaEleccion(object? origen, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(FilaDeDiploma.Elegido)) return;

        try
        {
            await RefrescarAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se ha podido refrescar tras cambiar la elección de diplomas.");
        }
    }
}
