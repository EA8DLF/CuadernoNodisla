using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nodisla.Cuaderno.Ui.Soporte;

namespace Nodisla.Cuaderno.Ui.VistaModelos;

/// <summary>Lo que se ve a la derecha de la ayuda.</summary>
public enum VistaDeLaAyuda
{
    /// <summary>Un capitulo.</summary>
    Capitulo,

    /// <summary>El formulario de «Reportar un fallo».</summary>
    ReportarFallo,

    /// <summary>«Acerca de»: version, licencia, repositorio y correo.</summary>
    AcercaDe,
}

/// <summary>
/// La ayuda dentro del programa: el indice de capitulos, la busqueda, el capitulo abierto y,
/// abajo del indice, «Reportar un fallo», «Buscar actualizaciones» y «Acerca de».
/// </summary>
/// <remarks>
/// <para>
/// Los capitulos son los mismos <c>docs/ayuda/*.md</c> del repositorio, incrustados al compilar:
/// la ayuda del programa y la de la web no pueden contar cosas distintas.
/// </para>
/// <para>
/// «Reportar un fallo» se monta NUEVO cada vez que se abre (es transitorio): el entorno que
/// adjunta tiene que ser el de ese momento, no el del arranque.
/// </para>
/// </remarks>
public sealed partial class VistaModeloAyuda : ObservableObject
{
    /// <summary>Repositorio publico del programa.</summary>
    public const string DireccionDelRepositorio = "https://github.com/EA8DLF/CuadernoNodisla";

    /// <summary>Correo publico de NODISLA. Nunca uno personal.</summary>
    public const string CorreoDeContacto = "nodisla@nodisla.org";

    /// <summary>Licencia del programa.</summary>
    public const string Licencia = "GPL-3.0 (Licencia Pública General de GNU, versión 3)";

    private readonly Func<VistaModeloReportarFallo>? _nuevoReporte;
    private readonly AccionesDelSistema _acciones;

    /// <summary>Monta la ayuda.</summary>
    /// <param name="libro">Los capitulos.</param>
    /// <param name="actualizaciones">El aviso de versiones (el mismo de la barra), o nulo.</param>
    /// <param name="nuevoReporte">Fabrica de «Reportar un fallo», o nula.</param>
    /// <param name="acciones">Navegador y correo.</param>
    /// <param name="version">Version instalada, ya escrita.</param>
    public VistaModeloAyuda(
        LibroDeAyuda libro,
        VistaModeloActualizaciones? actualizaciones = null,
        Func<VistaModeloReportarFallo>? nuevoReporte = null,
        AccionesDelSistema? acciones = null,
        string? version = null)
    {
        Libro = libro ?? throw new ArgumentNullException(nameof(libro));
        Actualizaciones = actualizaciones;
        _nuevoReporte = nuevoReporte;
        _acciones = acciones ?? new AccionesDelSistema();
        Version = version ?? VersionInstalada.Actual.ToString();
        _capitulosVisibles = libro.Capitulos;
        _capituloElegido = libro.Buscar("01-primer-uso") ?? libro.Capitulos.FirstOrDefault();

        // Para las capturas de la ayuda: con que capitulo (o apartado) abre.
        if (Environment.GetEnvironmentVariable("CUADERNO_AYUDA") is { Length: > 0 } pedido)
        {
            switch (pedido.ToLowerInvariant())
            {
                case "fallo": ReportarUnFallo(); break;
                case "acerca": VerAcercaDe(); break;
                default: AbrirCapitulo(pedido); break;
            }
        }

        if (Environment.GetEnvironmentVariable("CUADERNO_AYUDA_BUSCAR") is { Length: > 0 } busqueda)
        {
            Busqueda = busqueda;
        }
    }

    /// <summary>Los capitulos y sus capturas.</summary>
    public LibroDeAyuda Libro { get; }

    /// <summary>El aviso de versiones, el MISMO objeto que la barra de arriba. Nulo en pruebas.</summary>
    public VistaModeloActualizaciones? Actualizaciones { get; }

    /// <summary>Hay aviso de versiones montado.</summary>
    public bool HayActualizaciones => Actualizaciones is not null;

    /// <summary>Se puede reportar un fallo desde aqui.</summary>
    public bool SePuedeReportar => _nuevoReporte is not null;

    /// <summary>Version instalada.</summary>
    public string Version { get; }

    /// <summary>Repositorio, para el «Acerca de».</summary>
    public string Repositorio => DireccionDelRepositorio;

    /// <summary>Correo de contacto, para el «Acerca de».</summary>
    public string Correo => CorreoDeContacto;

    /// <summary>Licencia, para el «Acerca de».</summary>
    public string TextoDeLicencia => Licencia;

    /// <summary>Lo tecleado en el buscador.</summary>
    [ObservableProperty]
    private string _busqueda = string.Empty;

    /// <summary>Capitulos que casan con la busqueda (todos si no hay busqueda).</summary>
    [ObservableProperty]
    private IReadOnlyList<CapituloDeAyuda> _capitulosVisibles;

    /// <summary>El capitulo abierto.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TituloDeLaVista))]
    private CapituloDeAyuda? _capituloElegido;

    /// <summary>Lo que se ve a la derecha.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VerCapitulo), nameof(VerReporte), nameof(VerAcerca), nameof(TituloDeLaVista))]
    private VistaDeLaAyuda _vista = VistaDeLaAyuda.Capitulo;

    /// <summary>El formulario de «Reportar un fallo» abierto, o nulo.</summary>
    [ObservableProperty]
    private VistaModeloReportarFallo? _reporteDeFallo;

    /// <summary>Apartado del capitulo al que hay que bajar al abrirlo, o nulo.</summary>
    [ObservableProperty]
    private string? _anclaPedida;

    /// <summary>Se ve un capitulo.</summary>
    public bool VerCapitulo => Vista == VistaDeLaAyuda.Capitulo;

    /// <summary>Se ve «Reportar un fallo».</summary>
    public bool VerReporte => Vista == VistaDeLaAyuda.ReportarFallo;

    /// <summary>Se ve «Acerca de».</summary>
    public bool VerAcerca => Vista == VistaDeLaAyuda.AcercaDe;

    /// <summary>Texto de cuantos capitulos casan con la busqueda.</summary>
    public string ResultadoDeLaBusqueda => LibroDeAyuda.Palabras(Busqueda).Length == 0
        ? string.Empty
        : CapitulosVisibles.Count switch
        {
            0 => "Ningún capítulo lo menciona.",
            1 => "Lo menciona 1 capítulo.",
            var n => $"Lo mencionan {n} capítulos.",
        };

    /// <summary>Titulo de lo que se ve a la derecha.</summary>
    public string TituloDeLaVista => Vista switch
    {
        VistaDeLaAyuda.ReportarFallo => "Reportar un fallo",
        VistaDeLaAyuda.AcercaDe => "Acerca de Cuaderno NODISLA",
        _ => CapituloElegido?.Titulo ?? "Ayuda",
    };

    partial void OnBusquedaChanged(string value)
    {
        CapitulosVisibles = Libro.Filtrar(value);
        OnPropertyChanged(nameof(ResultadoDeLaBusqueda));

        // Si el capitulo abierto ya no casa, se abre el primero que si.
        if (CapitulosVisibles.Count > 0 && (CapituloElegido is null || !CapitulosVisibles.Contains(CapituloElegido)))
        {
            CapituloElegido = CapitulosVisibles[0];
            Vista = VistaDeLaAyuda.Capitulo;
        }
    }

    partial void OnCapituloElegidoChanged(CapituloDeAyuda? value)
    {
        if (value is not null) Vista = VistaDeLaAyuda.Capitulo;
    }

    /// <summary>Abre un capitulo por su clave o por un enlace «capitulo.md#apartado».</summary>
    /// <param name="destino">Clave, fichero o enlace.</param>
    /// <returns>Cierto si el capitulo existe.</returns>
    public bool AbrirCapitulo(string? destino)
    {
        if (string.IsNullOrWhiteSpace(destino)) return false;

        var almohadilla = destino.IndexOf('#', StringComparison.Ordinal);
        var fichero = almohadilla >= 0 ? destino[..almohadilla] : destino;
        var ancla = almohadilla >= 0 ? destino[(almohadilla + 1)..] : null;

        var capitulo = fichero.Length == 0 ? CapituloElegido : Libro.Buscar(fichero);
        if (capitulo is null) return false;

        // Si la busqueda lo esconderia, se quita: lo pedido manda.
        if (!CapitulosVisibles.Contains(capitulo)) Busqueda = string.Empty;

        AnclaPedida = null;
        CapituloElegido = capitulo;
        Vista = VistaDeLaAyuda.Capitulo;
        AnclaPedida = ancla;
        return true;
    }

    /// <summary>Atiende un enlace pulsado dentro de un capitulo.</summary>
    /// <param name="destino">El destino tal cual viene en el Markdown.</param>
    [RelayCommand]
    public void SeguirEnlace(string? destino)
    {
        if (string.IsNullOrWhiteSpace(destino)) return;

        if (destino.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
            || destino.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || destino.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            if (Uri.TryCreate(destino, UriKind.Absolute, out var uri)) _acciones.AbrirEnNavegador(uri);
            return;
        }

        AbrirCapitulo(destino);
    }

    /// <summary>Abre «Reportar un fallo» con un formulario nuevo, en blanco.</summary>
    [RelayCommand]
    public void ReportarUnFallo()
    {
        if (_nuevoReporte is null) return;
        ReporteDeFallo = _nuevoReporte();
        Vista = VistaDeLaAyuda.ReportarFallo;
    }

    /// <summary>Abre «Acerca de».</summary>
    [RelayCommand]
    public void VerAcercaDe() => Vista = VistaDeLaAyuda.AcercaDe;

    /// <summary>Vuelve al capitulo que estuviera abierto.</summary>
    [RelayCommand]
    public void VolverAlCapitulo() => Vista = VistaDeLaAyuda.Capitulo;

    /// <summary>Abre el repositorio en el navegador.</summary>
    [RelayCommand]
    public void AbrirElRepositorio() => _acciones.AbrirEnNavegador(new Uri(DireccionDelRepositorio));

    /// <summary>Abre el programa de correo con la direccion publica de NODISLA.</summary>
    [RelayCommand]
    public void EscribirUnCorreo() => _acciones.AbrirEnNavegador(new Uri("mailto:" + CorreoDeContacto));
}
