using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Idiomas;
using Nodisla.Cuaderno.Impresion.Diplomas;
using Nodisla.Cuaderno.Impresion.Qsl;
using Nodisla.Cuaderno.Servicios.Correo;
using Nodisla.Cuaderno.Ui.Qsl;
using Serilog;

namespace Nodisla.Cuaderno.Ui.VistaModelos;

/// <summary>Logo, firma o adorno del diploma tal como lo toca el editor.</summary>
public sealed partial class ImagenEditable : ObservableObject
{
    private readonly Action _alCambiar;

    /// <summary>Envuelve una imagen de la plantilla.</summary>
    /// <param name="imagen">La imagen; se modifica en sitio.</param>
    /// <param name="alCambiar">A quien avisar.</param>
    public ImagenEditable(ImagenDeDiploma imagen, Action alCambiar)
    {
        Imagen = imagen ?? throw new ArgumentNullException(nameof(imagen));
        _alCambiar = alCambiar ?? throw new ArgumentNullException(nameof(alCambiar));
    }

    /// <summary>La imagen de la plantilla.</summary>
    public ImagenDeDiploma Imagen { get; }

    /// <summary>Nombre en la lista.</summary>
    public string Nombre { get => Imagen.Nombre; set => Poner(Imagen.Nombre, value, v => Imagen.Nombre = v); }

    /// <summary>Uso.</summary>
    public UsoDeImagen Uso { get => Imagen.Uso; set => Poner(Imagen.Uso, value, v => Imagen.Uso = v); }

    /// <summary>Izquierda, mm.</summary>
    public double XMm { get => Imagen.XMm; set => Poner(Imagen.XMm, Math.Round(value, 1), v => Imagen.XMm = v); }

    /// <summary>Arriba, mm.</summary>
    public double YMm { get => Imagen.YMm; set => Poner(Imagen.YMm, Math.Round(value, 1), v => Imagen.YMm = v); }

    /// <summary>Ancho, mm.</summary>
    public double AnchoMm { get => Imagen.AnchoMm; set => Poner(Imagen.AnchoMm, Math.Clamp(Math.Round(value, 1), 2, 400), v => Imagen.AnchoMm = v); }

    /// <summary>Alto, mm.</summary>
    public double AltoMm { get => Imagen.AltoMm; set => Poner(Imagen.AltoMm, Math.Clamp(Math.Round(value, 1), 2, 400), v => Imagen.AltoMm = v); }

    /// <summary>Opacidad.</summary>
    public double Opacidad { get => Imagen.Opacidad; set => Poner(Imagen.Opacidad, Math.Clamp(value, 0, 1), v => Imagen.Opacidad = v); }

    /// <summary>Linea de firma debajo.</summary>
    public bool LineaDeFirma { get => Imagen.LineaDeFirma; set => Poner(Imagen.LineaDeFirma, value, v => Imagen.LineaDeFirma = v); }

    /// <summary>Color de la linea de firma.</summary>
    public string ColorDeLinea { get => Imagen.ColorDeLinea; set => Poner(Imagen.ColorDeLinea, value ?? "#555555", v => Imagen.ColorDeLinea = v); }

    /// <summary>Se pinta.</summary>
    public bool Visible { get => Imagen.Visible; set => Poner(Imagen.Visible, value, v => Imagen.Visible = v); }

    /// <summary>Que imagen lleva.</summary>
    public string TextoDelFichero => string.IsNullOrWhiteSpace(Imagen.Fichero)
        ? Textos.T("Qsl.Disenador.SinImagen")
        : Textos.F("Qsl.Disenador.ImagenFichero", Imagen.Fichero);

    /// <summary>Es la elegida.</summary>
    [ObservableProperty]
    private bool _elegido;

    /// <summary>Avisa de que ha cambiado el fichero.</summary>
    public void FicheroCambiado()
    {
        OnPropertyChanged(nameof(TextoDelFichero));
        _alCambiar();
    }

    /// <summary>Mueve la imagen arrastrandola.</summary>
    /// <param name="dx">Desplazamiento horizontal, mm.</param>
    /// <param name="dy">Desplazamiento vertical, mm.</param>
    /// <param name="anchoMm">Ancho del papel.</param>
    /// <param name="altoMm">Alto del papel.</param>
    public void Mover(double dx, double dy, double anchoMm, double altoMm)
    {
        XMm = Math.Clamp(XMm + dx, -AnchoMm / 2, anchoMm - (AnchoMm / 2));
        YMm = Math.Clamp(YMm + dy, -AltoMm / 2, altoMm - (AltoMm / 2));
    }

    private void Poner<T>(T actual, T nuevo, Action<T> escribir, [System.Runtime.CompilerServices.CallerMemberName] string? propiedad = null)
    {
        if (EqualityComparer<T>.Default.Equals(actual, nuevo)) return;
        escribir(nuevo);
        OnPropertyChanged(propiedad);
        _alCambiar();
    }
}

/// <summary>Una columna de la tabla tal como la toca el editor.</summary>
public sealed class ColumnaEditable : ObservableObject
{
    private readonly Action _alCambiar;

    /// <summary>Envuelve una columna.</summary>
    /// <param name="columna">La columna; se modifica en sitio.</param>
    /// <param name="alCambiar">A quien avisar.</param>
    public ColumnaEditable(ColumnaDeTabla columna, Action alCambiar)
    {
        Columna = columna ?? throw new ArgumentNullException(nameof(columna));
        _alCambiar = alCambiar ?? throw new ArgumentNullException(nameof(alCambiar));
    }

    /// <summary>La columna.</summary>
    public ColumnaDeTabla Columna { get; }

    /// <summary>Cabecera.</summary>
    public string Titulo { get => Columna.Titulo; set => Poner(Columna.Titulo, value ?? string.Empty, v => Columna.Titulo = v); }

    /// <summary>Contenido con variables de la fila.</summary>
    public string Texto { get => Columna.Texto; set => Poner(Columna.Texto, value ?? string.Empty, v => Columna.Texto = v); }

    /// <summary>Peso del ancho.</summary>
    public double Ancho { get => Columna.Ancho; set => Poner(Columna.Ancho, Math.Clamp(value, 0.1, 10), v => Columna.Ancho = v); }

    private void Poner<T>(T actual, T nuevo, Action<T> escribir, [System.Runtime.CompilerServices.CallerMemberName] string? propiedad = null)
    {
        if (EqualityComparer<T>.Default.Equals(actual, nuevo)) return;
        escribir(nuevo);
        OnPropertyChanged(propiedad);
        _alCambiar();
    }
}

/// <summary>Un contacto del cuaderno que puede justificar el diploma.</summary>
public sealed partial class QsoElegible : ObservableObject
{
    private readonly Action _alCambiar;

    /// <summary>Crea la fila.</summary>
    /// <param name="qso">El contacto.</param>
    /// <param name="alCambiar">A quien avisar al marcarlo o desmarcarlo.</param>
    public QsoElegible(Qso qso, Action alCambiar)
    {
        Qso = qso ?? throw new ArgumentNullException(nameof(qso));
        _alCambiar = alCambiar ?? throw new ArgumentNullException(nameof(alCambiar));
    }

    /// <summary>El contacto.</summary>
    public Qso Qso { get; }

    /// <summary>Como se ve en la lista.</summary>
    public string Texto => string.Create(CultureInfo.InvariantCulture, $"{Qso.InicioUtc.UtcDateTime:yyyy-MM-dd HH:mm}  {Qso.Band.Nombre} {Qso.Mode.NombreUsual}");

    /// <summary>Entra en el diploma.</summary>
    [ObservableProperty]
    private bool _elegido = true;

    partial void OnElegidoChanged(bool value) => _alCambiar();
}

/// <summary>Una linea del historial de diplomas emitidos.</summary>
/// <param name="Emitido">El apunte.</param>
public sealed record FilaDelHistorialDeDiplomas(DiplomaEmitido Emitido)
{
    /// <summary>Numero con su serie.</summary>
    public string Numero => string.Create(CultureInfo.InvariantCulture, $"{Emitido.Serie} {Emitido.Numero}");

    /// <summary>Cuando.</summary>
    public string Fecha => Emitido.EmitidoUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    /// <summary>Que diploma.</summary>
    public string Diploma => string.IsNullOrWhiteSpace(Emitido.Categoria) ? Emitido.NombreDelDiploma : $"{Emitido.NombreDelDiploma} · {Emitido.Categoria}";

    /// <summary>Si se ha mandado y a donde.</summary>
    public string Envio => Emitido.EnviadoUtc is { } cuando
        ? $"{Emitido.Correo} ({cuando.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)})"
        : Textos.T(Emitido.Origen == OrigenDeDiplomaEmitido.Conseguido ? "Qsl.Disenador.CertificadoPropio" : "Qsl.Disenador.SinEnviar");
}

/// <summary>Para que se usa el diseñador ahora.</summary>
public enum UsoDelDisenador
{
    /// <summary>Emitir un diploma a otra estacion.</summary>
    EmitirAOtraEstacion = 0,

    /// <summary>Imprimir el certificado de un diploma conseguido.</summary>
    CertificadoDeUnDiplomaConseguido,
}

/// <summary>
/// El diseñador de diplomas: la misma experiencia que el editor de la tarjeta QSL (plantillas,
/// fondo, campos que se arrastran, vista previa de verdad) mas orla, logo, firma, tabla de
/// referencias, numeracion correlativa, correo e historial.
/// </summary>
public sealed partial class VistaModeloDisenadorDeDiplomas : ObservableObject, IColoresDelDiseno
{
    private DatosDeMiEstacion _yo = DatosDeMiEstacion.Vacios;
    private DatosDeDiploma _datos = new();
    private DiplomaEmitido? _emitido;
    private int? _numeroProvisional;
    private bool _redibujoPendiente;
    private bool _cargando;
    private bool _rellenando;

    /// <summary>Crea el diseñador.</summary>
    /// <param name="servicio">Los diplomas.</param>
    public VistaModeloDisenadorDeDiplomas(ServicioDeDiplomas servicio)
    {
        Servicio = servicio ?? throw new ArgumentNullException(nameof(servicio));
        Contactos.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HayContactos));
        Historial.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HistorialVacio));
        RecargarPlantillas(Servicio.Disenos.IdPorOmision());
        Textos.AlCambiar(this, static vm => vm.AlCambiarDeIdioma());
    }

    /// <summary>Hay contactos que elegir.</summary>
    public bool HayContactos => Contactos.Count > 0;

    /// <summary>No se ha emitido ninguno.</summary>
    public bool HistorialVacio => Historial.Count == 0;

    /// <summary>Los diplomas.</summary>
    public ServicioDeDiplomas Servicio { get; }

    /// <summary>Abre un fichero con el programa del sistema.</summary>
    public Action<string> AbrirFichero { get; set; } =
        ruta => Process.Start(new ProcessStartInfo(ruta) { UseShellExecute = true });

    /// <summary>Pide una imagen. Devuelve nulo si se cancela.</summary>
    public Func<string?> ElegirImagen { get; set; } = () =>
    {
        var dialogo = new Microsoft.Win32.OpenFileDialog
        {
            Title = Textos.T("Qsl.Disenador.DialogoImagen"),
            Filter = Textos.T("Qsl.Editor.FiltroImagenes"),
        };
        return dialogo.ShowDialog() == true ? dialogo.FileName : null;
    };

    /// <summary>Pide un fichero para abrir (importar). Recibe el filtro.</summary>
    public Func<string, string?> ElegirFichero { get; set; } = filtro =>
    {
        var dialogo = new Microsoft.Win32.OpenFileDialog { Filter = filtro };
        return dialogo.ShowDialog() == true ? dialogo.FileName : null;
    };

    /// <summary>Pide donde guardar. Recibe nombre sugerido y filtro.</summary>
    public Func<string, string, string?> ElegirDondeGuardar { get; set; } = (nombre, filtro) =>
    {
        var dialogo = new Microsoft.Win32.SaveFileDialog { FileName = nombre, Filter = filtro };
        return dialogo.ShowDialog() == true ? dialogo.FileName : null;
    };

    /// <summary>Las plantillas.</summary>
    public ObservableCollection<DisenoDeDiploma> Disenos { get; } = [];

    /// <summary>Los textos de la plantilla elegida.</summary>
    public ObservableCollection<CampoEditable> Campos { get; } = [];

    /// <summary>Logos, firma y adornos.</summary>
    public ObservableCollection<ImagenEditable> Imagenes { get; } = [];

    /// <summary>Columnas de la tabla.</summary>
    public ObservableCollection<ColumnaEditable> Columnas { get; } = [];

    /// <summary>Contactos con la estacion que recibe el diploma.</summary>
    public ObservableCollection<QsoElegible> Contactos { get; } = [];

    /// <summary>Diplomas conseguidos del modulo de diplomas.</summary>
    public ObservableCollection<DiplomaConseguido> Conseguidos { get; } = [];

    /// <summary>El historial de emitidos.</summary>
    public ObservableCollection<FilaDelHistorialDeDiplomas> Historial { get; } = [];

    /// <summary>Usos del diseñador.</summary>
    public IReadOnlyList<UsoDelDisenador> Usos { get; } = Enum.GetValues<UsoDelDisenador>();

    /// <summary>Variables de los textos, con su explicacion.</summary>
    public string AyudaDeVariables =>
        string.Join("\n", VariablesDeDiploma.Conocidas.Select(v => $"{{{v.Nombre}}} — {v.Descripcion}"))
        + "\n\n" + Textos.T("Qsl.Disenador.VariablesDeLaTabla") + "\n"
        + string.Join("\n", VariablesDeDiploma.DeLaFila.Select(v => $"{{{v.Nombre}}} — {v.Descripcion}"));

    /// <summary>La plantilla elegida.</summary>
    [ObservableProperty]
    private DisenoDeDiploma? _diseno;

    /// <summary>El texto o la imagen elegidos (el panel derecho los edita por su tipo).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayElementoElegido))]
    [NotifyCanExecuteChangedFor(nameof(QuitarElementoCommand), nameof(DuplicarElementoCommand))]
    private object? _elementoElegido;

    /// <summary>Para que se usa ahora.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EsParaOtraEstacion), nameof(EsCertificadoPropio))]
    private UsoDelDisenador _uso;

    /// <summary>Indicativo que recibe el diploma.</summary>
    [ObservableProperty]
    private string _indicativo = string.Empty;

    /// <summary>Su nombre.</summary>
    [ObservableProperty]
    private string _nombre = string.Empty;

    /// <summary>Nombre del diploma.</summary>
    [ObservableProperty]
    private string _nombreDelDiploma = string.Empty;

    /// <summary>Categoria o nivel.</summary>
    [ObservableProperty]
    private string _categoria = string.Empty;

    /// <summary>Correo del destinatario.</summary>
    [ObservableProperty]
    private string _correo = string.Empty;

    /// <summary>Fecha de emision.</summary>
    [ObservableProperty]
    private DateTime _fechaDeEmision = DateTime.Today;

    /// <summary>Diploma conseguido elegido.</summary>
    [ObservableProperty]
    private DiplomaConseguido? _conseguido;

    /// <summary>Linea del historial elegida.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ReimprimirCommand), nameof(ReenviarCommand))]
    private FilaDelHistorialDeDiplomas? _historialElegido;

    /// <summary>El diploma dibujado.</summary>
    [ObservableProperty]
    private ImageSource? _vistaPrevia;

    /// <summary>Hay cambios sin guardar en la plantilla.</summary>
    [ObservableProperty]
    private bool _hayCambios;

    /// <summary>Lo ultimo que ha pasado.</summary>
    [ObservableProperty]
    private string _aviso = string.Empty;

    /// <summary>Numero que se esta viendo: el emitido o el provisional.</summary>
    [ObservableProperty]
    private string _textoDelNumero = string.Empty;

    /// <summary>Cuantas hojas sale, con anexos.</summary>
    [ObservableProperty]
    private string _textoDeHojas = string.Empty;

    /// <summary>Izquierda del asa de la tabla, mm.</summary>
    [ObservableProperty]
    private double _tablaX;

    /// <summary>Arriba del asa de la tabla, mm.</summary>
    [ObservableProperty]
    private double _tablaY;

    /// <summary>Ancho del asa de la tabla, mm.</summary>
    [ObservableProperty]
    private double _tablaAncho;

    /// <summary>Alto del asa de la tabla, mm.</summary>
    [ObservableProperty]
    private double _tablaAlto;

    /// <summary>Es para emitir a otra estacion (para los botones de opcion).</summary>
    public bool EsParaOtraEstacion
    {
        get => Uso == UsoDelDisenador.EmitirAOtraEstacion;
        set
        {
            if (value) Uso = UsoDelDisenador.EmitirAOtraEstacion;
        }
    }

    /// <summary>Es el certificado de un diploma propio (para los botones de opcion).</summary>
    public bool EsCertificadoPropio
    {
        get => Uso == UsoDelDisenador.CertificadoDeUnDiplomaConseguido;
        set
        {
            if (value) Uso = UsoDelDisenador.CertificadoDeUnDiplomaConseguido;
        }
    }

    /// <summary>Hay un elemento elegido.</summary>
    public bool HayElementoElegido => ElementoElegido is not null;

    /// <summary>Ancho del papel, mm.</summary>
    public double AnchoMm => Diseno?.AnchoMm ?? 297;

    /// <summary>Alto del papel, mm.</summary>
    public double AltoMm => Diseno?.AltoMm ?? 210;

    /// <summary>La plantilla tiene imagen de fondo.</summary>
    public bool TieneFondo => Diseno is not null && Servicio.Disenos.RutaDeImagen(Diseno.ImagenDeFondo) is not null;

    /// <summary>La plantilla elegida es la de omision.</summary>
    public bool EsLaDeOmision => Diseno is not null && Diseno.Id == Servicio.Disenos.IdPorOmision();

    /// <summary>Los datos que se estan viendo.</summary>
    public DatosDeDiploma Datos => _datos;

    /// <summary>El ultimo diploma emitido desde la pantalla, si los datos no han cambiado despues.</summary>
    public DiplomaEmitido? Emitido => _emitido;

    // ── Propiedades de la plantilla ────────────────────────────────────

    /// <summary>Nombre de la plantilla.</summary>
    public string NombreDelDiseno { get => Diseno?.Nombre ?? string.Empty; set => EnPlantilla(d => d.Nombre, (d, v) => d.Nombre = v, value); }

    /// <summary>Papel.</summary>
    public PapelDeDiploma Papel
    {
        get => Diseno?.Papel ?? PapelDeDiploma.A4;
        set
        {
            if (Diseno is null || Diseno.Papel == value) return;
            Diseno.CambiarFormato(value, Diseno.Vertical);
            RecargarElementos();
            Formato();
        }
    }

    /// <summary>Vertical.</summary>
    public bool Vertical
    {
        get => Diseno?.Vertical ?? false;
        set
        {
            if (Diseno is null || Diseno.Vertical == value) return;
            Diseno.CambiarFormato(Diseno.Papel, value);
            RecargarElementos();
            Formato();
        }
    }

    /// <summary>Color del papel.</summary>
    public string ColorDeFondo { get => Diseno?.ColorDeFondo ?? "#FFFFFF"; set => EnPlantilla(d => d.ColorDeFondo, (d, v) => d.ColorDeFondo = v, value); }

    /// <summary>Ajuste de la imagen de fondo.</summary>
    public AjusteDeFondo Ajuste { get => Diseno?.Ajuste ?? AjusteDeFondo.Rellenar; set => EnPlantilla(d => d.Ajuste, (d, v) => d.Ajuste = v, value); }

    /// <summary>Estilo de la orla.</summary>
    public EstiloDeMarco EstiloDeMarco { get => Diseno?.Marco.Estilo ?? EstiloDeMarco.Ninguno; set => EnPlantilla(d => d.Marco.Estilo, (d, v) => d.Marco.Estilo = v, value); }

    /// <summary>Color de la orla.</summary>
    public string ColorDelMarco { get => Diseno?.Marco.Color ?? string.Empty; set => EnPlantilla(d => d.Marco.Color, (d, v) => d.Marco.Color = v, value); }

    /// <summary>Color secundario de la orla.</summary>
    public string ColorSecundarioDelMarco { get => Diseno?.Marco.ColorSecundario ?? string.Empty; set => EnPlantilla(d => d.Marco.ColorSecundario, (d, v) => d.Marco.ColorSecundario = v, value); }

    /// <summary>Margen de la orla, mm.</summary>
    public double MargenDelMarco { get => Diseno?.Marco.MargenMm ?? 10; set => EnPlantilla(d => d.Marco.MargenMm, (d, v) => d.Marco.MargenMm = v, Math.Clamp(value, 0, 60)); }

    /// <summary>Grosor de la orla, mm.</summary>
    public double GrosorDelMarco { get => Diseno?.Marco.GrosorMm ?? 1; set => EnPlantilla(d => d.Marco.GrosorMm, (d, v) => d.Marco.GrosorMm = v, Math.Clamp(value, 0.1, 10)); }

    /// <summary>Serie de numeracion.</summary>
    public string Serie
    {
        get => Diseno?.Serie ?? string.Empty;
        set
        {
            EnPlantilla(d => d.Serie, (d, v) => d.Serie = v, string.IsNullOrWhiteSpace(value) ? "NODISLA" : value.Trim());
            _ = ActualizarNumeroAsync();
        }
    }

    /// <summary>Prefijo del numero.</summary>
    public string PrefijoDeNumero { get => Diseno?.PrefijoDeNumero ?? string.Empty; set => EnPlantilla(d => d.PrefijoDeNumero, (d, v) => d.PrefijoDeNumero = v, value ?? string.Empty); }

    /// <summary>Cifras del numero.</summary>
    public int CifrasDelNumero { get => Diseno?.CifrasDelNumero ?? 4; set => EnPlantilla(d => d.CifrasDelNumero, (d, v) => d.CifrasDelNumero = v, Math.Clamp(value, 1, 9)); }

    /// <summary>Gestor que firma.</summary>
    public string NombreDelGestor { get => Diseno?.NombreDelGestor ?? string.Empty; set => EnPlantilla(d => d.NombreDelGestor, (d, v) => d.NombreDelGestor = v, value ?? string.Empty); }

    /// <summary>Asunto del correo.</summary>
    public string Asunto { get => Diseno?.Asunto ?? string.Empty; set => EnPlantilla(d => d.Asunto, (d, v) => d.Asunto = v, value ?? string.Empty); }

    /// <summary>Texto del correo.</summary>
    public string TextoDelCorreo { get => Diseno?.TextoDelCorreo ?? string.Empty; set => EnPlantilla(d => d.TextoDelCorreo, (d, v) => d.TextoDelCorreo = v, value ?? string.Empty); }

    /// <summary>Se pinta la tabla.</summary>
    public bool TablaVisible { get => Diseno?.Tabla.Visible ?? false; set => EnPlantilla(d => d.Tabla.Visible, (d, v) => d.Tabla.Visible = v, value); }

    /// <summary>Titulo de la tabla.</summary>
    public string TituloDeLaTabla { get => Diseno?.Tabla.Titulo ?? string.Empty; set => EnPlantilla(d => d.Tabla.Titulo, (d, v) => d.Tabla.Titulo = v, value ?? string.Empty); }

    /// <summary>Izquierda de la tabla, mm.</summary>
    public double XDeLaTabla { get => Diseno?.Tabla.XMm ?? 0; set => EnPlantilla(d => d.Tabla.XMm, (d, v) => d.Tabla.XMm = v, Math.Round(value, 1)); }

    /// <summary>Arriba de la tabla, mm.</summary>
    public double YDeLaTabla { get => Diseno?.Tabla.YMm ?? 0; set => EnPlantilla(d => d.Tabla.YMm, (d, v) => d.Tabla.YMm = v, Math.Round(value, 1)); }

    /// <summary>Ancho de la tabla, mm.</summary>
    public double AnchoDeLaTabla { get => Diseno?.Tabla.AnchoMm ?? 0; set => EnPlantilla(d => d.Tabla.AnchoMm, (d, v) => d.Tabla.AnchoMm = v, Math.Clamp(Math.Round(value, 1), 20, 400)); }

    /// <summary>Alto de la tabla, mm.</summary>
    public double AltoDeLaTabla { get => Diseno?.Tabla.AltoMm ?? 0; set => EnPlantilla(d => d.Tabla.AltoMm, (d, v) => d.Tabla.AltoMm = v, Math.Clamp(Math.Round(value, 1), 10, 400)); }

    /// <summary>Bloques de columnas.</summary>
    public int BloquesDeLaTabla { get => Diseno?.Tabla.Bloques ?? 1; set => EnPlantilla(d => d.Tabla.Bloques, (d, v) => d.Tabla.Bloques = v, Math.Clamp(value, 1, 4)); }

    /// <summary>Letra de la tabla.</summary>
    public string FuenteDeLaTabla { get => Diseno?.Tabla.Fuente ?? "Segoe UI"; set => EnPlantilla(d => d.Tabla.Fuente, (d, v) => d.Tabla.Fuente = v, value ?? "Segoe UI"); }

    /// <summary>Tamaño de letra de la tabla.</summary>
    public double TamanoDeLaTabla { get => Diseno?.Tabla.TamanoPt ?? 8; set => EnPlantilla(d => d.Tabla.TamanoPt, (d, v) => d.Tabla.TamanoPt = v, Math.Clamp(value, 4, 30)); }

    /// <summary>Anexar lo que no quepa.</summary>
    public bool AnexoDeLaTabla { get => Diseno?.Tabla.Anexo ?? true; set => EnPlantilla(d => d.Tabla.Anexo, (d, v) => d.Tabla.Anexo = v, value); }

    /// <summary>Color del texto de la tabla.</summary>
    public string ColorDelTextoDeLaTabla { get => Diseno?.Tabla.ColorDeTexto ?? "#222222"; set => EnPlantilla(d => d.Tabla.ColorDeTexto, (d, v) => d.Tabla.ColorDeTexto = v, value ?? "#222222"); }

    /// <summary>Color de la cabecera y del título de la tabla.</summary>
    public string ColorDeCabeceraDeLaTabla { get => Diseno?.Tabla.ColorDeCabecera ?? "#1B3A5C"; set => EnPlantilla(d => d.Tabla.ColorDeCabecera, (d, v) => d.Tabla.ColorDeCabecera = v, value ?? "#1B3A5C"); }

    /// <summary>Color de las líneas de la tabla.</summary>
    public string ColorDeLineasDeLaTabla { get => Diseno?.Tabla.ColorDeLineas ?? "#B8B8B8"; set => EnPlantilla(d => d.Tabla.ColorDeLineas, (d, v) => d.Tabla.ColorDeLineas = v, value ?? "#B8B8B8"); }

    /// <inheritdoc />
    public IEnumerable<string?> ColoresEnUso()
    {
        if (Diseno is not { } d) yield break;
        yield return d.ColorDeFondo;
        if (d.Marco.Estilo != EstiloDeMarco.Ninguno)
        {
            yield return d.Marco.Color;
            yield return d.Marco.ColorSecundario;
        }

        foreach (var campo in d.Campos.Where(c => c.Visible))
        {
            yield return campo.Color;
            yield return campo.ColorDeRecuadro;
        }

        if (d.Tabla.Visible)
        {
            yield return d.Tabla.ColorDeCabecera;
            yield return d.Tabla.ColorDeTexto;
            yield return d.Tabla.ColorDeLineas;
        }

        foreach (var imagen in d.ImagenesColocadas.Where(i => i.Visible && i.LineaDeFirma)) yield return imagen.ColorDeLinea;
    }

    // ── Carga ──────────────────────────────────────────────────────────

    /// <summary>Lee mi estacion, el historial y los diplomas conseguidos.</summary>
    /// <returns>Tarea.</returns>
    [RelayCommand]
    public async Task RefrescarAsync()
    {
        try
        {
            _yo = await Servicio.Qsl.MiEstacionAsync().ConfigureAwait(true);
            await CargarHistorialAsync().ConfigureAwait(true);
            var elegido = Conseguido;
            Conseguidos.Clear();
            foreach (var c in await Servicio.ConseguidosAsync().ConfigureAwait(true)) Conseguidos.Add(c);
            Conseguido = Conseguidos.FirstOrDefault(c => c.Diploma.Codigo == elegido?.Diploma.Codigo && c.Progreso.Variante == elegido?.Progreso.Variante);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se han podido cargar los datos del diseñador de diplomas.");
            Aviso = Textos.F("Qsl.Disenador.NoDatos", ex.Message);
        }

        if (string.IsNullOrWhiteSpace(Indicativo) && Uso == UsoDelDisenador.EmitirAOtraEstacion) RellenarDeMuestra();
        await ActualizarNumeroAsync().ConfigureAwait(true);
        Redibujar();
    }

    /// <summary>Busca en el cuaderno los contactos con el indicativo y rellena nombre, correo y tabla.</summary>
    /// <returns>Tarea.</returns>
    [RelayCommand]
    public async Task BuscarContactosAsync()
    {
        if (string.IsNullOrWhiteSpace(Indicativo))
        {
            Aviso = Textos.T("Qsl.Disenador.EscribaIndicativo");
            return;
        }

        try
        {
            var qsos = await Servicio.ContactosConAsync(Indicativo).ConfigureAwait(true);
            _rellenando = true;
            Contactos.Clear();
            foreach (var q in qsos) Contactos.Add(new QsoElegible(q, DatosCambiados));
            var datos = ServicioDeDiplomas.DesdeContactos(Indicativo, qsos, Diseno ?? PlantillasDeDiplomaDeFabrica.Clasico());
            if (!string.IsNullOrWhiteSpace(datos.Nombre)) Nombre = datos.Nombre;
            if (!string.IsNullOrWhiteSpace(datos.Correo)) Correo = datos.Correo;
            Aviso = qsos.Count == 0
                ? Textos.F("Qsl.Disenador.SinContactos", Indicativo.ToUpperInvariant())
                : Textos.F("Qsl.Disenador.ContactosCon", qsos.Count, Indicativo.ToUpperInvariant());
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se han podido buscar los contactos para el diploma.");
            Aviso = Textos.F("Qsl.Disenador.NoBuscar", ex.Message);
        }
        finally
        {
            _rellenando = false;
        }

        DatosCambiados();
    }

    /// <summary>Carga los datos del diploma conseguido elegido.</summary>
    /// <returns>Tarea.</returns>
    [RelayCommand]
    public async Task CargarConseguidoAsync()
    {
        if (Conseguido is not { } c)
        {
            Aviso = Textos.T("Qsl.Disenador.ElijaConseguido");
            return;
        }

        try
        {
            _yo = await Servicio.Qsl.MiEstacionAsync().ConfigureAwait(true);
            var datos = await Servicio.DesdeDiplomaAsync(c, _yo).ConfigureAwait(true);
            _rellenando = true;
            Contactos.Clear();
            Indicativo = datos.Indicativo;
            Nombre = datos.Nombre ?? string.Empty;
            NombreDelDiploma = datos.NombreDelDiploma;
            Categoria = datos.Categoria ?? string.Empty;
            Correo = string.Empty;
            _rellenando = false;
            _datos = datos;
            _datos.Fecha = new DateTimeOffset(FechaDeEmision);
            _emitido = null;
            Aviso = Textos.F("Qsl.Disenador.ReferenciasConfirmadas", c.Diploma.Nombre, datos.Filas.Count);
        }
        catch (Exception ex)
        {
            _rellenando = false;
            Log.Error(ex, "No se ha podido cargar el diploma conseguido.");
            Aviso = Textos.F("Qsl.Disenador.NoLeerDiploma", ex.Message);
        }

        await ActualizarNumeroAsync().ConfigureAwait(true);
        Redibujar();
    }

    partial void OnIndicativoChanged(string value) => DatosCambiados();

    partial void OnNombreChanged(string value) => DatosCambiados();

    partial void OnNombreDelDiplomaChanged(string value) => DatosCambiados();

    partial void OnCategoriaChanged(string value) => DatosCambiados();

    partial void OnFechaDeEmisionChanged(DateTime value) => DatosCambiados();

    partial void OnUsoChanged(UsoDelDisenador value)
    {
        _rellenando = true;
        _datos = new DatosDeDiploma();
        _emitido = null;
        Contactos.Clear();
        if (value == UsoDelDisenador.CertificadoDeUnDiplomaConseguido)
        {
            Indicativo = _yo.Indicativo;
            Nombre = _yo.Nombre ?? string.Empty;
            Correo = string.Empty;
        }
        else
        {
            Indicativo = string.Empty;
            Nombre = string.Empty;
            NombreDelDiploma = Diseno?.DiplomaPorOmision ?? string.Empty;
            Categoria = Diseno?.CategoriaPorOmision ?? string.Empty;
        }

        _rellenando = false;
        DatosCambiados();
        if (value == UsoDelDisenador.CertificadoDeUnDiplomaConseguido && Conseguido is not null) _ = CargarConseguidoAsync();
    }

    partial void OnConseguidoChanged(DiplomaConseguido? value)
    {
        if (value is not null && EsCertificadoPropio) _ = CargarConseguidoAsync();
    }

    partial void OnDisenoChanged(DisenoDeDiploma? value)
    {
        _cargando = true;
        try
        {
            RecargarElementos();
            HayCambios = false;
            if (value is not null && EsParaOtraEstacion && Contactos.Count == 0 && _datos.Filas.Count == 0 && string.IsNullOrWhiteSpace(NombreDelDiploma))
            {
                NombreDelDiploma = value.DiplomaPorOmision;
                Categoria = value.CategoriaPorOmision;
            }
        }
        finally
        {
            _cargando = false;
        }

        foreach (var p in new[]
        {
            nameof(AnchoMm), nameof(AltoMm), nameof(NombreDelDiseno), nameof(Papel), nameof(Vertical), nameof(ColorDeFondo), nameof(Ajuste),
            nameof(TieneFondo), nameof(EsLaDeOmision), nameof(EstiloDeMarco), nameof(ColorDelMarco), nameof(ColorSecundarioDelMarco),
            nameof(MargenDelMarco), nameof(GrosorDelMarco), nameof(Serie), nameof(PrefijoDeNumero), nameof(CifrasDelNumero),
            nameof(NombreDelGestor), nameof(Asunto), nameof(TextoDelCorreo), nameof(TablaVisible), nameof(TituloDeLaTabla),
            nameof(XDeLaTabla), nameof(YDeLaTabla), nameof(AnchoDeLaTabla), nameof(AltoDeLaTabla), nameof(BloquesDeLaTabla),
            nameof(FuenteDeLaTabla), nameof(TamanoDeLaTabla), nameof(AnexoDeLaTabla),
            nameof(ColorDelTextoDeLaTabla), nameof(ColorDeCabeceraDeLaTabla), nameof(ColorDeLineasDeLaTabla),
        })
        {
            OnPropertyChanged(p);
        }

        _emitido = null;
        _ = ActualizarNumeroAsync();
        Redibujar();
    }

    partial void OnElementoElegidoChanged(object? oldValue, object? newValue)
    {
        if (oldValue is CampoEditable c0) c0.Elegido = false;
        if (oldValue is ImagenEditable i0) i0.Elegido = false;
        if (newValue is CampoEditable c1) c1.Elegido = true;
        if (newValue is ImagenEditable i1) i1.Elegido = true;
    }

    // ── Plantillas ─────────────────────────────────────────────────────

    /// <summary>Nueva plantilla a partir de la clasica.</summary>
    [RelayCommand]
    public void NuevaPlantilla()
    {
        var nueva = PlantillasDeDiplomaDeFabrica.Clasico();
        nueva.Id = Servicio.Disenos.IdNuevo();
        nueva.Nombre = Textos.T("Qsl.Disenador.DiplomaNuevo");
        Disenos.Add(nueva);
        Diseno = nueva;
        HayCambios = true;
    }

    /// <summary>Copia de la plantilla elegida, con sus imagenes.</summary>
    [RelayCommand]
    public void DuplicarPlantilla()
    {
        if (Diseno is null) return;
        try
        {
            var copia = Servicio.Disenos.Duplicar(Diseno, Textos.F("Qsl.Editor.Copia", Diseno.Nombre));
            Disenos.Add(copia);
            Diseno = copia;
            HayCambios = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Aviso = Textos.F("Qsl.Disenador.NoDuplicar", ex.Message);
        }
    }

    /// <summary>Guarda la plantilla elegida.</summary>
    [RelayCommand]
    public void Guardar()
    {
        if (Diseno is null) return;
        try
        {
            Servicio.Disenos.Guardar(Diseno);
            HayCambios = false;
            Aviso = Textos.F("Qsl.Editor.PlantillaGuardada", Diseno.Nombre);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Log.Error(ex, "No se ha podido guardar la plantilla de diploma.");
            Aviso = Textos.F("Qsl.Editor.NoSeHaPodidoGuardar", ex.Message);
        }
    }

    /// <summary>Deshace lo que no se ha guardado.</summary>
    [RelayCommand]
    public void Descartar() => RecargarPlantillas(Diseno?.Id);

    /// <summary>Borra la plantilla elegida (las de fabrica vuelven a su estado original).</summary>
    [RelayCommand]
    public void BorrarPlantilla()
    {
        if (Diseno is null) return;
        var nombre = Diseno.Nombre;
        var deFabrica = Servicio.Disenos.EsDeFabrica(Diseno.Id);
        Servicio.Disenos.Borrar(Diseno);
        RecargarPlantillas(deFabrica ? Diseno.Id : null);
        Aviso = Textos.F(deFabrica ? "Qsl.Disenador.VueltaDeFabrica" : "Qsl.Editor.PlantillaBorrada", nombre);
    }

    /// <summary>La elegida pasa a ser la de omision.</summary>
    [RelayCommand]
    public void PonerPorOmision()
    {
        if (Diseno is null) return;
        Guardar();
        Servicio.Disenos.PonerPorOmision(Diseno.Id);
        OnPropertyChanged(nameof(EsLaDeOmision));
        Aviso = Textos.F("Qsl.Disenador.AhoraPorOmision", Diseno.Nombre);
    }

    /// <summary>Exporta la plantilla con sus imagenes a un fichero.</summary>
    [RelayCommand]
    public void ExportarPlantilla()
    {
        if (Diseno is null) return;
        var filtro = Textos.F("Qsl.Disenador.FiltroPlantilla", AlmacenDeDisenosDeDiploma.ExtensionExportada);
        if (ElegirDondeGuardar(Limpio(Diseno.Nombre) + AlmacenDeDisenosDeDiploma.ExtensionExportada, filtro) is not { } ruta) return;
        try
        {
            Servicio.Disenos.Exportar(Diseno, ruta);
            Aviso = Textos.F("Qsl.Disenador.Exportada", ruta);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Aviso = Textos.F("Qsl.Disenador.NoExportar", ex.Message);
        }
    }

    /// <summary>Importa una plantilla exportada.</summary>
    [RelayCommand]
    public void ImportarPlantilla()
    {
        if (ElegirFichero(Textos.F("Qsl.Disenador.FiltroImportar", AlmacenDeDisenosDeDiploma.ExtensionExportada)) is not { } ruta) return;
        try
        {
            var importada = Servicio.Disenos.Importar(ruta);
            RecargarPlantillas(importada.Id);
            Aviso = Textos.F("Qsl.Disenador.Importada", importada.Nombre);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Aviso = Textos.F("Qsl.Disenador.NoImportar", ex.Message);
        }
    }

    /// <summary>Elige la imagen de fondo u orla.</summary>
    [RelayCommand]
    public void ElegirFondo()
    {
        if (Diseno is null || ElegirImagen() is not { } ruta) return;
        try
        {
            Servicio.Disenos.PonerFondo(Diseno, ruta);
            OnPropertyChanged(nameof(TieneFondo));
            Cambiado();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Aviso = Textos.F("Qsl.Editor.NoImagen", ex.Message);
        }
    }

    /// <summary>Quita la imagen de fondo.</summary>
    [RelayCommand]
    public void QuitarFondo()
    {
        if (Diseno is null) return;
        Diseno.ImagenDeFondo = null;
        OnPropertyChanged(nameof(TieneFondo));
        Cambiado();
    }

    // ── Elementos ──────────────────────────────────────────────────────

    /// <summary>Añade un texto libre en el centro.</summary>
    [RelayCommand]
    public void AnadirTexto()
    {
        if (Diseno is null) return;
        var campo = new CampoDeQsl { Nombre = Textos.T("Qsl.Editor.Texto"), Texto = Textos.T("Qsl.Editor.TextoLibre"), XMm = AnchoMm / 2, YMm = AltoMm / 2, Alineacion = AlineacionDeCampo.Centro, TamanoPt = 14, Fuente = "Georgia", Color = "#333333" };
        Diseno.Campos.Add(campo);
        var editable = new CampoEditable(campo, Cambiado);
        Campos.Add(editable);
        ElementoElegido = editable;
        Cambiado();
    }

    /// <summary>Añade un hueco de imagen (logo, firma o adorno) y pide la imagen.</summary>
    [RelayCommand]
    public void AnadirImagen()
    {
        if (Diseno is null) return;
        var imagen = new ImagenDeDiploma { Nombre = Textos.T("Qsl.Disenador.Imagen"), Uso = UsoDeImagen.Adorno, XMm = (AnchoMm / 2) - 15, YMm = (AltoMm / 2) - 15 };
        Diseno.ImagenesColocadas.Add(imagen);
        var editable = new ImagenEditable(imagen, Cambiado);
        Imagenes.Add(editable);
        ElementoElegido = editable;
        ElegirImagenDelElemento();
        Cambiado();
    }

    /// <summary>Pone imagen al hueco elegido.</summary>
    [RelayCommand]
    public void ElegirImagenDelElemento()
    {
        if (Diseno is null || ElementoElegido is not ImagenEditable editable)
        {
            Aviso = Textos.T("Qsl.Disenador.ElijaHueco");
            return;
        }

        if (ElegirImagen() is not { } ruta) return;
        try
        {
            Servicio.Disenos.PonerImagen(Diseno, editable.Imagen, ruta);
            editable.FicheroCambiado();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Aviso = Textos.F("Qsl.Editor.NoImagen", ex.Message);
        }
    }

    /// <summary>Copia del elemento elegido, un poco desplazada.</summary>
    [RelayCommand(CanExecute = nameof(HayElementoElegido))]
    public void DuplicarElemento()
    {
        if (Diseno is null) return;
        if (ElementoElegido is CampoEditable c)
        {
            var campo = c.Campo.Copiar();
            campo.Nombre = Textos.F("Qsl.Editor.Copia", campo.Nombre);
            campo.YMm = Math.Min(AltoMm - 5, campo.YMm + 8);
            Diseno.Campos.Add(campo);
            var editable = new CampoEditable(campo, Cambiado);
            Campos.Add(editable);
            ElementoElegido = editable;
        }
        else if (ElementoElegido is ImagenEditable i)
        {
            var imagen = i.Imagen.Copiar();
            imagen.Nombre = Textos.F("Qsl.Editor.Copia", imagen.Nombre);
            imagen.XMm = Math.Min(AnchoMm - 5, imagen.XMm + 8);
            Diseno.ImagenesColocadas.Add(imagen);
            var editable = new ImagenEditable(imagen, Cambiado);
            Imagenes.Add(editable);
            ElementoElegido = editable;
        }

        Cambiado();
    }

    /// <summary>Quita el elemento elegido.</summary>
    [RelayCommand(CanExecute = nameof(HayElementoElegido))]
    public void QuitarElemento()
    {
        if (Diseno is null) return;
        if (ElementoElegido is CampoEditable c)
        {
            Diseno.Campos.Remove(c.Campo);
            Campos.Remove(c);
        }
        else if (ElementoElegido is ImagenEditable i)
        {
            Diseno.ImagenesColocadas.Remove(i.Imagen);
            Imagenes.Remove(i);
        }

        ElementoElegido = null;
        Cambiado();
    }

    /// <summary>Añade una columna a la tabla.</summary>
    [RelayCommand]
    public void AnadirColumna()
    {
        if (Diseno is null) return;
        var columna = new ColumnaDeTabla { Titulo = Textos.T("Qsl.Disenador.Columna"), Texto = "{referencia}" };
        Diseno.Tabla.Columnas.Add(columna);
        Columnas.Add(new ColumnaEditable(columna, Cambiado));
        Cambiado();
    }

    /// <summary>Quita una columna de la tabla.</summary>
    /// <param name="columna">La columna.</param>
    [RelayCommand]
    public void QuitarColumna(ColumnaEditable? columna)
    {
        if (Diseno is null || columna is null) return;
        Diseno.Tabla.Columnas.Remove(columna.Columna);
        Columnas.Remove(columna);
        Cambiado();
    }

    // ── Salida ─────────────────────────────────────────────────────────

    /// <summary>Guarda el diploma tal como se ve en PNG (sin emitir: el numero es provisional).</summary>
    [RelayCommand]
    public void GuardarPng()
    {
        if (Diseno is null) return;
        var datos = DatosParaImprimir();
        if (ElegirDondeGuardar(ServicioDeDiplomas.NombreDeFichero(Diseno, Numerado(datos)) + ".png", "PNG|*.png") is not { } ruta) return;
        try
        {
            File.WriteAllBytes(ruta, Servicio.Png(Diseno, datos, _yo));
            Aviso = Textos.F("Qsl.Disenador.ImagenGuardada", ruta);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Aviso = Textos.F("Qsl.Editor.NoSeHaPodidoGuardar", ex.Message);
        }
    }

    /// <summary>PDF de prueba, sin emitir, para revisar antes de dar numero.</summary>
    [RelayCommand]
    public void PdfDePrueba()
    {
        if (Diseno is null) return;
        var datos = DatosParaImprimir();
        GuardarPdf(Servicio.Pdf(Diseno, datos, _yo), ServicioDeDiplomas.NombreDeFichero(Diseno, Numerado(datos)) + ".pdf");
    }

    /// <summary>Emite el diploma (numero correlativo e historial) y guarda el PDF.</summary>
    /// <returns>Tarea.</returns>
    [RelayCommand]
    public async Task EmitirYGuardarPdfAsync()
    {
        if (await EmitirAsync().ConfigureAwait(true) is not { } emitido || Diseno is null) return;
        var impreso = Servicio.Pdf(Diseno, _datos, _yo);
        GuardarPdf(impreso, impreso.NombreSugerido);
        Aviso = Textos.F("Qsl.Disenador.Emitido", Diseno.FormatearNumero(emitido.Numero), emitido.Indicativo) + " " + Aviso;
    }

    /// <summary>Emite el diploma y lo manda por correo en PDF.</summary>
    /// <returns>Tarea.</returns>
    [RelayCommand]
    public async Task EmitirYEnviarAsync()
    {
        if (!DireccionDeCorreo.EsValida(Correo))
        {
            Aviso = Textos.T("Qsl.Disenador.EscribaCorreo");
            return;
        }

        if (await EmitirAsync().ConfigureAwait(true) is not { } emitido || Diseno is null) return;
        await EnviarAsync(Diseno, _datos, emitido, Correo).ConfigureAwait(true);
    }

    /// <summary>Vuelve a sacar en PDF un diploma del historial, con su numero y sus datos de entonces.</summary>
    [RelayCommand(CanExecute = nameof(HayHistorialElegido))]
    public void Reimprimir()
    {
        if (HistorialElegido is null || ReconstruirDelHistorial(HistorialElegido.Emitido) is not { } r) return;
        var impreso = Servicio.Pdf(r.Diseno, r.Datos, _yo);
        GuardarPdf(impreso, impreso.NombreSugerido);
    }

    /// <summary>Vuelve a mandar por correo un diploma del historial.</summary>
    /// <returns>Tarea.</returns>
    [RelayCommand(CanExecute = nameof(HayHistorialElegido))]
    public async Task ReenviarAsync()
    {
        if (HistorialElegido is null || ReconstruirDelHistorial(HistorialElegido.Emitido) is not { } r) return;
        var para = !string.IsNullOrWhiteSpace(HistorialElegido.Emitido.Correo) ? HistorialElegido.Emitido.Correo! : r.Datos.Correo ?? Correo;
        if (!DireccionDeCorreo.EsValida(para))
        {
            Aviso = Textos.T("Qsl.Disenador.SinDireccion");
            if (DireccionDeCorreo.EsValida(Correo)) para = Correo;
            else return;
        }

        await EnviarAsync(r.Diseno, r.Datos, HistorialElegido.Emitido, para).ConfigureAwait(true);
    }

    /// <summary>Hay una linea del historial elegida.</summary>
    public bool HayHistorialElegido => HistorialElegido is not null;

    /// <summary>Vuelve a leer las plantillas del disco.</summary>
    /// <param name="elegir">Cual dejar elegida; nula, la de omision.</param>
    public void RecargarPlantillas(string? elegir)
    {
        Disenos.Clear();
        foreach (var d in Servicio.Disenos.Listar()) Disenos.Add(d);
        var id = elegir ?? Servicio.Disenos.IdPorOmision();
        Diseno = Disenos.FirstOrDefault(d => d.Id == id) ?? Disenos.FirstOrDefault();
        HayCambios = false;
    }

    /// <summary>Redibuja la vista previa (agrupando cambios seguidos, como al arrastrar).</summary>
    public void Redibujar()
    {
        if (_redibujoPendiente) return;
        var despachador = Application.Current?.Dispatcher;
        if (despachador is null || despachador.HasShutdownStarted)
        {
            RedibujarYa();
            return;
        }

        _redibujoPendiente = true;
        despachador.BeginInvoke(DispatcherPriority.Render, () =>
        {
            _redibujoPendiente = false;
            RedibujarYa();
        });
    }

    /// <summary>Redibuja la vista previa ahora mismo.</summary>
    public void RedibujarYa()
    {
        if (Diseno is null)
        {
            VistaPrevia = null;
            return;
        }

        try
        {
            var datos = DatosParaImprimir();
            var variables = VariablesDeDiploma.Para(datos, Diseno, _yo);
            VistaPrevia = DibujanteDeDiplomas.Dibujar(Diseno, variables, datos.Filas, Servicio.Disenos.RutaDeImagen, DibujanteDeDiplomas.PppDePantalla);
            foreach (var c in Campos)
            {
                var caja = DibujanteDeQsl.Caja(c.Campo, variables);
                c.CajaX = caja.X;
                c.CajaY = caja.Y;
                c.CajaAncho = Math.Max(3, caja.Width);
                c.CajaAlto = Math.Max(3, caja.Height);
            }

            var t = Diseno.Tabla;
            TablaX = t.XMm;
            TablaY = t.YMm;
            TablaAncho = t.Visible ? t.AnchoMm : 0;
            TablaAlto = t.Visible ? t.AltoMm : 0;

            var caben = t.Visible ? DibujanteDeDiplomas.Capacidad(t, t.AltoMm, t.Bloques) : 0;
            TextoDeHojas = !t.Visible || datos.Filas.Count == 0
                ? string.Empty
                : datos.Filas.Count <= caben
                    ? Textos.F("Qsl.Disenador.Hojas.Caben", datos.Filas.Count, caben)
                    : t.Anexo
                        ? Textos.F("Qsl.Disenador.Hojas.Anexo", datos.Filas.Count, Math.Max(0, caben - 1))
                        : Textos.F("Qsl.Disenador.Hojas.SoloCaben", datos.Filas.Count, Math.Max(0, caben - 1));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se ha podido dibujar la vista previa del diploma.");
            Aviso = Textos.F("Qsl.Disenador.NoDibujar", ex.Message);
        }
    }

    /// <summary>Mueve la tabla arrastrandola.</summary>
    /// <param name="dx">Desplazamiento horizontal, mm.</param>
    /// <param name="dy">Desplazamiento vertical, mm.</param>
    public void MoverTabla(double dx, double dy)
    {
        if (Diseno is null) return;
        XDeLaTabla = Math.Clamp(XDeLaTabla + dx, 0, AnchoMm - 10);
        YDeLaTabla = Math.Clamp(YDeLaTabla + dy, 0, AltoMm - 10);
        TablaX = XDeLaTabla;
        TablaY = YDeLaTabla;
    }

    // ── Interno ────────────────────────────────────────────────────────

    private void RellenarDeMuestra()
    {
        // Para que la vista previa no salga vacia antes de elegir a nadie.
        _rellenando = true;
        if (string.IsNullOrWhiteSpace(NombreDelDiploma)) NombreDelDiploma = Diseno?.DiplomaPorOmision ?? string.Empty;
        if (string.IsNullOrWhiteSpace(Categoria)) Categoria = Diseno?.CategoriaPorOmision ?? string.Empty;
        _rellenando = false;
    }

    private DatosDeDiploma DatosParaImprimir()
    {
        if (EsParaOtraEstacion)
        {
            var elegidos = Contactos.Where(c => c.Elegido).Select(c => c.Qso).ToList();
            var desdeContactos = ServicioDeDiplomas.DesdeContactos(Indicativo, elegidos, Diseno ?? PlantillasDeDiplomaDeFabrica.Clasico());
            _datos.Filas = desdeContactos.Filas;
            _datos.Referencias = desdeContactos.Referencias;
            _datos.Qsos = desdeContactos.Qsos;
        }

        _datos.Indicativo = Indicativo;
        _datos.Nombre = Nombre;
        _datos.NombreDelDiploma = NombreDelDiploma;
        _datos.Categoria = Categoria;
        _datos.Correo = string.IsNullOrWhiteSpace(Correo) ? null : Correo.Trim();
        _datos.Fecha = new DateTimeOffset(FechaDeEmision.Date);
        if (_emitido is null) _datos.Numero = null;
        return Numerado(_datos);
    }

    private DatosDeDiploma Numerado(DatosDeDiploma datos)
    {
        // La vista previa enseña el numero que le tocaria; el fichero sin emitir dice «borrador».
        if (datos.Numero is null && _numeroProvisional is { } n && _emitido is null)
        {
            return new DatosDeDiploma
            {
                Indicativo = datos.Indicativo,
                Nombre = datos.Nombre,
                NombreDelDiploma = datos.NombreDelDiploma,
                Categoria = datos.Categoria,
                Fecha = datos.Fecha,
                Referencias = datos.Referencias,
                Qsos = datos.Qsos,
                Entidad = datos.Entidad,
                Correo = datos.Correo,
                Filas = datos.Filas,
                Numero = n,
            };
        }

        return datos;
    }

    private async Task ActualizarNumeroAsync()
    {
        if (Diseno is null) return;
        try
        {
            _numeroProvisional = await Servicio.SiguienteNumeroAsync(Diseno.Serie).ConfigureAwait(true);
            TextoDelNumero = _emitido is { } e
                ? Textos.F("Qsl.Disenador.EmitidoConNumero", Diseno.FormatearNumero(e.Numero), e.Serie)
                : Textos.F("Qsl.Disenador.LlevaraNumero", Diseno.FormatearNumero(_numeroProvisional), Diseno.Serie);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "No se ha podido leer la numeración de los diplomas.");
            TextoDelNumero = Textos.T("Qsl.Disenador.NoNumeracion");
        }

        Redibujar();
    }

    private async Task<DiplomaEmitido?> EmitirAsync()
    {
        if (Diseno is null) return null;
        if (HayCambios) Guardar();
        try
        {
            var datos = DatosParaImprimir();
            var copia = new DatosDeDiploma
            {
                Indicativo = datos.Indicativo,
                Nombre = datos.Nombre,
                NombreDelDiploma = datos.NombreDelDiploma,
                Categoria = datos.Categoria,
                Fecha = datos.Fecha,
                Referencias = datos.Referencias,
                Qsos = datos.Qsos,
                Entidad = datos.Entidad,
                Correo = datos.Correo,
                Filas = datos.Filas,
            };
            var origen = EsCertificadoPropio ? OrigenDeDiplomaEmitido.Conseguido : OrigenDeDiplomaEmitido.Emitido;
            var emitido = await Servicio.EmitirAsync(Diseno, copia, origen).ConfigureAwait(true);
            _datos = copia;
            _emitido = emitido;
            await CargarHistorialAsync().ConfigureAwait(true);
            await ActualizarNumeroAsync().ConfigureAwait(true);
            return emitido;
        }
        catch (ArgumentException ex)
        {
            Aviso = ex.Message.Split(" (Parameter", 2)[0];
            return null;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se ha podido emitir el diploma.");
            Aviso = Textos.F("Qsl.Disenador.NoEmitir", ex.Message);
            return null;
        }
    }

    private async Task EnviarAsync(DisenoDeDiploma diseno, DatosDeDiploma datos, DiplomaEmitido emitido, string para)
    {
        try
        {
            await Servicio.EnviarAsync(diseno, datos, emitido, para).ConfigureAwait(true);
            Aviso = Textos.F("Qsl.Disenador.Enviado", diseno.FormatearNumero(emitido.Numero), para);
        }
        catch (ErrorDeCorreo ex)
        {
            Aviso = Textos.F("Qsl.Disenador.EmitidoSinMandar", diseno.FormatearNumero(emitido.Numero), ex.Message);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se ha podido mandar el diploma.");
            Aviso = Textos.F("Qsl.Disenador.NoMandar", ex.Message);
        }

        await CargarHistorialAsync().ConfigureAwait(true);
    }

    private (DisenoDeDiploma Diseno, DatosDeDiploma Datos)? ReconstruirDelHistorial(DiplomaEmitido emitido)
    {
        var datos = ServicioDeDiplomas.DatosDe(emitido);
        if (datos is null)
        {
            Aviso = Textos.T("Qsl.Disenador.SinDatos");
            return null;
        }

        var diseno = (emitido.PlantillaId is { } id ? Servicio.Disenos.Obtener(id) : null) ?? Diseno;
        if (diseno is null) return null;
        if (!string.Equals(diseno.Serie, emitido.Serie, StringComparison.OrdinalIgnoreCase))
        {
            diseno = diseno.Copiar();
            diseno.Serie = emitido.Serie;
        }

        return (diseno, datos);
    }

    private async Task CargarHistorialAsync()
    {
        var elegido = HistorialElegido?.Emitido.Id;
        Historial.Clear();
        foreach (var d in await Servicio.HistorialAsync().ConfigureAwait(true)) Historial.Add(new FilaDelHistorialDeDiplomas(d));
        HistorialElegido = Historial.FirstOrDefault(h => h.Emitido.Id == elegido);
    }

    private void GuardarPdf(Nodisla.Cuaderno.Impresion.Impreso impreso, string nombre)
    {
        if (ElegirDondeGuardar(nombre, "PDF|*.pdf") is not { } ruta) return;
        try
        {
            File.WriteAllBytes(ruta, impreso.Bytes);
            Aviso = Textos.F("Qsl.Disenador.PdfGuardado", ruta, impreso.Paginas);
            AbrirFichero(ruta);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            Aviso = Textos.F("Qsl.Editor.NoPdf", ex.Message);
        }
    }

    private void RecargarElementos()
    {
        var antes = _cargando;
        _cargando = true;
        try
        {
            Campos.Clear();
            Imagenes.Clear();
            Columnas.Clear();
            if (Diseno is not null)
            {
                foreach (var c in Diseno.Campos) Campos.Add(new CampoEditable(c, Cambiado));
                foreach (var i in Diseno.ImagenesColocadas) Imagenes.Add(new ImagenEditable(i, Cambiado));
                foreach (var c in Diseno.Tabla.Columnas) Columnas.Add(new ColumnaEditable(c, Cambiado));
            }

            ElementoElegido = null;
        }
        finally
        {
            _cargando = antes;
        }
    }

    private void Formato()
    {
        foreach (var p in new[] { nameof(Papel), nameof(Vertical), nameof(AnchoMm), nameof(AltoMm), nameof(XDeLaTabla), nameof(YDeLaTabla), nameof(AnchoDeLaTabla), nameof(AltoDeLaTabla) })
        {
            OnPropertyChanged(p);
        }

        Cambiado();
    }

    private void EnPlantilla<T>(Func<DisenoDeDiploma, T> leer, Action<DisenoDeDiploma, T> escribir, T valor, [System.Runtime.CompilerServices.CallerMemberName] string? propiedad = null)
    {
        if (Diseno is null || EqualityComparer<T>.Default.Equals(leer(Diseno), valor)) return;
        escribir(Diseno, valor);
        OnPropertyChanged(propiedad);
        Cambiado();
    }

    private void DatosCambiados()
    {
        if (_rellenando) return;
        if (_emitido is not null)
        {
            // Cambiar los datos despues de emitir es empezar otro diploma: vuelve el numero provisional.
            _emitido = null;
            _ = ActualizarNumeroAsync();
        }

        Redibujar();
    }

    private void Cambiado()
    {
        if (_cargando) return;
        HayCambios = true;
        Redibujar();
    }

    /// <summary>Al cambiar de idioma: la ayuda, el número y las hojas se vuelven a escribir.</summary>
    private void AlCambiarDeIdioma()
    {
        OnPropertyChanged(nameof(AyudaDeVariables));
        _ = ActualizarNumeroAsync();
    }

    private static string Limpio(string s) => new(s.Select(c => char.IsAsciiLetterOrDigit(c) ? c : '_').ToArray());
}
