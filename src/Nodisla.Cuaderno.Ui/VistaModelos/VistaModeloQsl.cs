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
using Nodisla.Cuaderno.Impresion.Qsl;
using Nodisla.Cuaderno.Ui.Qsl;
using Serilog;

namespace Nodisla.Cuaderno.Ui.VistaModelos;

/// <summary>Un campo de la tarjeta tal como lo toca el editor.</summary>
public sealed partial class CampoEditable : ObservableObject
{
    private readonly Action _alCambiar;

    /// <summary>Envuelve un campo.</summary>
    /// <param name="campo">El campo de la plantilla; se modifica en sitio.</param>
    /// <param name="alCambiar">A quien avisar cuando cambia algo.</param>
    public CampoEditable(CampoDeQsl campo, Action alCambiar)
    {
        Campo = campo ?? throw new ArgumentNullException(nameof(campo));
        _alCambiar = alCambiar ?? throw new ArgumentNullException(nameof(alCambiar));
    }

    /// <summary>El campo de la plantilla.</summary>
    public CampoDeQsl Campo { get; }

    /// <summary>Nombre en la lista.</summary>
    public string Nombre { get => Campo.Nombre; set => Poner(Campo.Nombre, value, v => Campo.Nombre = v); }

    /// <summary>Texto con variables.</summary>
    public string Texto { get => Campo.Texto; set => Poner(Campo.Texto, value, v => Campo.Texto = v); }

    /// <summary>Posicion horizontal del ancla, mm.</summary>
    public double XMm { get => Campo.XMm; set => Poner(Campo.XMm, Math.Round(value, 1), v => Campo.XMm = v); }

    /// <summary>Posicion vertical, mm.</summary>
    public double YMm { get => Campo.YMm; set => Poner(Campo.YMm, Math.Round(value, 1), v => Campo.YMm = v); }

    /// <summary>Tipografia.</summary>
    public string Fuente { get => Campo.Fuente; set => Poner(Campo.Fuente, value, v => Campo.Fuente = v); }

    /// <summary>Tamaño en puntos.</summary>
    public double TamanoPt { get => Campo.TamanoPt; set => Poner(Campo.TamanoPt, Math.Clamp(value, 4, 200), v => Campo.TamanoPt = v); }

    /// <summary>Negrita.</summary>
    public bool Negrita { get => Campo.Negrita; set => Poner(Campo.Negrita, value, v => Campo.Negrita = v); }

    /// <summary>Cursiva.</summary>
    public bool Cursiva { get => Campo.Cursiva; set => Poner(Campo.Cursiva, value, v => Campo.Cursiva = v); }

    /// <summary>Color del texto.</summary>
    public string Color { get => Campo.Color; set => Poner(Campo.Color, value, v => Campo.Color = v); }

    /// <summary>Color del recuadro, vacio sin recuadro.</summary>
    public string ColorDeRecuadro
    {
        get => Campo.ColorDeRecuadro ?? string.Empty;
        set => Poner(Campo.ColorDeRecuadro ?? string.Empty, value ?? string.Empty, v => Campo.ColorDeRecuadro = v.Length == 0 ? null : v);
    }

    /// <summary>Alineacion respecto al ancla.</summary>
    public AlineacionDeCampo Alineacion { get => Campo.Alineacion; set => Poner(Campo.Alineacion, value, v => Campo.Alineacion = v); }

    /// <summary>Ancho maximo en mm (0: sin limite); lo que no quepa pasa a otra linea.</summary>
    public double AnchoMaximoMm { get => Campo.AnchoMaximoMm; set => Poner(Campo.AnchoMaximoMm, Math.Max(0, Math.Round(value, 1)), v => Campo.AnchoMaximoMm = v); }

    /// <summary>Se pinta.</summary>
    public bool Visible { get => Campo.Visible; set => Poner(Campo.Visible, value, v => Campo.Visible = v); }

    /// <summary>Es un dato del contacto (se quita en la imagen para eQSL).</summary>
    public bool EsDelContacto { get => Campo.EsDelContacto; set => Poner(Campo.EsDelContacto, value, v => Campo.EsDelContacto = v); }

    /// <summary>Izquierda del asa, en mm.</summary>
    [ObservableProperty]
    private double _cajaX;

    /// <summary>Arriba del asa, en mm.</summary>
    [ObservableProperty]
    private double _cajaY;

    /// <summary>Ancho del asa, en mm.</summary>
    [ObservableProperty]
    private double _cajaAncho;

    /// <summary>Alto del asa, en mm.</summary>
    [ObservableProperty]
    private double _cajaAlto;

    /// <summary>Es el campo elegido en el editor.</summary>
    [ObservableProperty]
    private bool _elegido;

    /// <summary>Mueve el campo arrastrandolo, en mm.</summary>
    /// <param name="dx">Desplazamiento horizontal.</param>
    /// <param name="dy">Desplazamiento vertical.</param>
    /// <param name="anchoMm">Ancho de la tarjeta, para no sacarlo.</param>
    /// <param name="altoMm">Alto de la tarjeta.</param>
    public void Mover(double dx, double dy, double anchoMm, double altoMm)
    {
        var x = XMm;
        var y = YMm;
        XMm = Math.Clamp(XMm + dx, 0, anchoMm);
        YMm = Math.Clamp(YMm + dy, -5, altoMm);

        // El asa se mueve ya, sin esperar al redibujo: el siguiente paso del arrastre se mide
        // desde donde esta el asa, y si no se hubiera movido el campo se pasaria de largo.
        CajaX += XMm - x;
        CajaY += YMm - y;
    }

    private void Poner<T>(T actual, T nuevo, Action<T> escribir, [System.Runtime.CompilerServices.CallerMemberName] string? propiedad = null)
    {
        if (EqualityComparer<T>.Default.Equals(actual, nuevo)) return;
        escribir(nuevo);
        OnPropertyChanged(propiedad);
        _alCambiar();
    }
}

/// <summary>Un contacto del cuaderno para elegir en la vista previa.</summary>
/// <param name="Qso">El contacto.</param>
public sealed record ContactoDeMuestra(Qso? Qso)
{
    /// <summary>Como se ve en la lista.</summary>
    public string Texto => Qso is null
        ? Textos.T("Qsl.Tarjeta.SinContacto")
        : string.Create(CultureInfo.InvariantCulture, $"{Qso.Call.Valor}  {Qso.InicioUtc.UtcDateTime:yyyy-MM-dd HH:mm}  {Qso.Band.Nombre} {Qso.Mode.NombreUsual}");

    /// <inheritdoc />
    public override string ToString() => Texto;
}

/// <summary>
/// El editor de la tarjeta QSL: plantillas, fondo, campos que se arrastran y vista previa con un
/// contacto de verdad.
/// </summary>
public sealed partial class VistaModeloQsl : ObservableObject, IColoresDelDiseno
{
    private DatosDeMiEstacion _yo = DatosDeMiEstacion.Vacios;
    private bool _redibujoPendiente;
    private bool _cargando;

    /// <summary>Crea el editor.</summary>
    /// <param name="servicio">Las QSL.</param>
    public VistaModeloQsl(ServicioDeQsl servicio)
    {
        Servicio = servicio ?? throw new ArgumentNullException(nameof(servicio));
        RecargarPlantillas(Servicio.Disenos.IdPorOmision());
        Servicio.QslEnviadas += (_, _) => Aviso = Textos.T("Qsl.Tarjeta.Enviadas");
        Textos.AlCambiar(this, static vm => vm.AlCambiarDeIdioma());
    }

    /// <summary>Piden abrir la ventana de envio con estos contactos.</summary>
    public event EventHandler<IReadOnlyList<long>>? SolicitaEnviar;

    /// <summary>Las QSL.</summary>
    public ServicioDeQsl Servicio { get; }

    /// <summary>Abre un fichero con el programa del sistema.</summary>
    public Action<string> AbrirFichero { get; set; } =
        ruta => Process.Start(new ProcessStartInfo(ruta) { UseShellExecute = true });

    /// <summary>Pide la imagen de fondo. Devuelve nulo si se cancela.</summary>
    public Func<string?> ElegirImagen { get; set; } = () =>
    {
        var dialogo = new Microsoft.Win32.OpenFileDialog
        {
            Title = Textos.T("Qsl.Tarjeta.DialogoFondo"),
            Filter = Textos.T("Qsl.Editor.FiltroImagenes"),
        };
        return dialogo.ShowDialog() == true ? dialogo.FileName : null;
    };

    /// <summary>Pide donde guardar un fichero. Recibe nombre sugerido y filtro.</summary>
    public Func<string, string, string?> ElegirDondeGuardar { get; set; } = (nombre, filtro) =>
    {
        var dialogo = new Microsoft.Win32.SaveFileDialog { FileName = nombre, Filter = filtro };
        return dialogo.ShowDialog() == true ? dialogo.FileName : null;
    };

    /// <summary>Las plantillas.</summary>
    public ObservableCollection<DisenoDeQsl> Disenos { get; } = [];

    /// <summary>Los campos de la plantilla elegida.</summary>
    public ObservableCollection<CampoEditable> Campos { get; } = [];

    /// <summary>Contactos para la vista previa.</summary>
    public ObservableCollection<ContactoDeMuestra> Contactos { get; } = [new(null)];

    /// <summary>Tipografias instaladas.</summary>
    public IReadOnlyList<string> Fuentes => CatalogosDelEditor.Fuentes;

    /// <summary>Alineaciones.</summary>
    public IReadOnlyList<AlineacionDeCampo> Alineaciones => CatalogosDelEditor.Alineaciones;

    /// <summary>Ajustes del fondo.</summary>
    public IReadOnlyList<AjusteDeFondo> AjustesDeFondo => CatalogosDelEditor.AjustesDeFondo;

    /// <summary>Variables para escribir en los campos, con su explicacion.</summary>
    public string AyudaDeVariables => string.Join("\n", VariablesDeQsl.Conocidas.Select(v => $"{{{v.Nombre}}} — {v.Descripcion}"));

    /// <summary>La plantilla elegida.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AnchoMm), nameof(AltoMm), nameof(NombreDelDiseno), nameof(Vertical), nameof(ColorDeFondo), nameof(Ajuste), nameof(TieneFondo), nameof(EsLaDeOmision))]
    private DisenoDeQsl? _diseno;

    /// <summary>El campo elegido.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayCampoElegido))]
    [NotifyCanExecuteChangedFor(nameof(QuitarCampoCommand), nameof(DuplicarCampoCommand))]
    private CampoEditable? _campoElegido;

    /// <summary>Contacto de la vista previa.</summary>
    [ObservableProperty]
    private ContactoDeMuestra? _contacto;

    /// <summary>La tarjeta dibujada.</summary>
    [ObservableProperty]
    private ImageSource? _vistaPrevia;

    /// <summary>Hay cambios sin guardar.</summary>
    [ObservableProperty]
    private bool _hayCambios;

    /// <summary>Lo ultimo que ha pasado.</summary>
    [ObservableProperty]
    private string _aviso = string.Empty;

    /// <summary>Ancho de la tarjeta, mm.</summary>
    public double AnchoMm => Diseno?.AnchoMm ?? DisenoDeQsl.AnchoInternacionalMm;

    /// <summary>Alto de la tarjeta, mm.</summary>
    public double AltoMm => Diseno?.AltoMm ?? DisenoDeQsl.AltoInternacionalMm;

    /// <summary>Hay un campo elegido.</summary>
    public bool HayCampoElegido => CampoElegido is not null;

    /// <summary>La plantilla tiene imagen de fondo.</summary>
    public bool TieneFondo => Diseno is not null && Servicio.Disenos.RutaDelFondo(Diseno) is not null;

    /// <summary>La plantilla elegida es la de omision.</summary>
    public bool EsLaDeOmision => Diseno is not null && Diseno.Id == Servicio.Disenos.IdPorOmision();

    /// <summary>Nombre de la plantilla.</summary>
    public string NombreDelDiseno
    {
        get => Diseno?.Nombre ?? string.Empty;
        set
        {
            if (Diseno is null || Diseno.Nombre == value) return;
            Diseno.Nombre = value;
            OnPropertyChanged();
            Cambiado();
        }
    }

    /// <summary>Tarjeta vertical.</summary>
    public bool Vertical
    {
        get => Diseno?.Vertical ?? false;
        set
        {
            if (Diseno is null || Diseno.Vertical == value) return;
            Diseno.Vertical = value;

            // Al girar la tarjeta se giran tambien las posiciones, en proporcion, para que
            // ningun campo se quede fuera.
            foreach (var c in Campos)
            {
                c.XMm = c.XMm * Diseno.AnchoMm / Diseno.AltoMm;
                c.YMm = c.YMm * Diseno.AltoMm / Diseno.AnchoMm;
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(AnchoMm));
            OnPropertyChanged(nameof(AltoMm));
            Cambiado();
        }
    }

    /// <inheritdoc />
    public IEnumerable<string?> ColoresEnUso()
    {
        if (Diseno is not { } d) yield break;
        yield return d.ColorDeFondo;
        foreach (var campo in d.Campos.Where(c => c.Visible))
        {
            yield return campo.Color;
            yield return campo.ColorDeRecuadro;
        }
    }

    /// <summary>Color liso del fondo.</summary>
    public string ColorDeFondo
    {
        get => Diseno?.ColorDeFondo ?? "#FFFFFF";
        set
        {
            if (Diseno is null || Diseno.ColorDeFondo == value) return;
            Diseno.ColorDeFondo = value;
            OnPropertyChanged();
            Cambiado();
        }
    }

    /// <summary>Como se coloca la foto.</summary>
    public AjusteDeFondo Ajuste
    {
        get => Diseno?.Ajuste ?? AjusteDeFondo.Rellenar;
        set
        {
            if (Diseno is null || Diseno.Ajuste == value) return;
            Diseno.Ajuste = value;
            OnPropertyChanged();
            Cambiado();
        }
    }

    /// <summary>Carga los contactos de muestra y los datos de mi estacion.</summary>
    /// <returns>Tarea.</returns>
    [RelayCommand]
    public async Task RefrescarAsync()
    {
        try
        {
            _yo = await Servicio.MiEstacionAsync().ConfigureAwait(true);
            var ultimos = await Servicio.UltimosContactosAsync(40).ConfigureAwait(true);
            var elegido = Contacto?.Qso?.Id;
            Contactos.Clear();
            Contactos.Add(new ContactoDeMuestra(null));
            foreach (var q in ultimos) Contactos.Add(new ContactoDeMuestra(q));
            Contacto = Contactos.FirstOrDefault(c => c.Qso?.Id == elegido && elegido is not null)
                ?? (Contactos.Count > 1 ? Contactos[1] : Contactos[0]);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se han podido cargar los contactos de muestra de la QSL.");
            Aviso = Textos.F("Qsl.Tarjeta.NoContactos", ex.Message);
        }

        Redibujar();
    }

    partial void OnContactoChanged(ContactoDeMuestra? value) => Redibujar();

    partial void OnDisenoChanged(DisenoDeQsl? value)
    {
        _cargando = true;
        try
        {
            Campos.Clear();
            if (value is not null)
            {
                foreach (var c in value.Campos) Campos.Add(new CampoEditable(c, Cambiado));
            }

            CampoElegido = null;
            HayCambios = false;
        }
        finally
        {
            _cargando = false;
        }

        GuardarCommand.NotifyCanExecuteChanged();
        Redibujar();
    }

    partial void OnCampoElegidoChanged(CampoEditable? oldValue, CampoEditable? newValue)
    {
        if (oldValue is not null) oldValue.Elegido = false;
        if (newValue is not null) newValue.Elegido = true;
    }

    /// <summary>Nueva plantilla a partir de la del programa.</summary>
    [RelayCommand]
    public void NuevaPlantilla()
    {
        var nueva = DisenoDeQsl.PorOmision();
        nueva.Id = Guid.NewGuid().ToString("N")[..12];
        nueva.Nombre = Textos.T("Qsl.Tarjeta.TarjetaNueva");
        Disenos.Add(nueva);
        Diseno = nueva;
        HayCambios = true;
    }

    /// <summary>Copia de la plantilla elegida.</summary>
    [RelayCommand]
    public void DuplicarPlantilla()
    {
        if (Diseno is null) return;
        var copia = Diseno.Copiar();
        copia.Id = Guid.NewGuid().ToString("N")[..12];
        copia.Nombre = Textos.F("Qsl.Editor.Copia", Diseno.Nombre);
        if (Servicio.Disenos.RutaDelFondo(Diseno) is { } fondo)
        {
            copia.ImagenDeFondo = null;
            Servicio.Disenos.PonerFondo(copia, fondo);
        }

        Disenos.Add(copia);
        Diseno = copia;
        HayCambios = true;
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
            Log.Error(ex, "No se ha podido guardar la plantilla de QSL.");
            Aviso = Textos.F("Qsl.Editor.NoSeHaPodidoGuardar", ex.Message);
        }
    }

    /// <summary>Deshace lo que no se ha guardado.</summary>
    [RelayCommand]
    public void Descartar() => RecargarPlantillas(Diseno?.Id);

    /// <summary>Borra la plantilla elegida.</summary>
    [RelayCommand]
    public void BorrarPlantilla()
    {
        if (Diseno is null) return;
        var nombre = Diseno.Nombre;
        Servicio.Disenos.Borrar(Diseno);
        RecargarPlantillas(null);
        Aviso = Textos.F("Qsl.Editor.PlantillaBorrada", nombre);
    }

    /// <summary>La elegida pasa a ser la de omision.</summary>
    [RelayCommand]
    public void PonerPorOmision()
    {
        if (Diseno is null) return;
        Guardar();
        Servicio.Disenos.PonerPorOmision(Diseno.Id);
        OnPropertyChanged(nameof(EsLaDeOmision));
        Aviso = Textos.F("Qsl.Tarjeta.AhoraPorOmision", Diseno.Nombre);
    }

    /// <summary>Elige la imagen de fondo.</summary>
    [RelayCommand]
    public void ElegirFondo()
    {
        if (Diseno is null || ElegirImagen() is not { } ruta) return;
        try
        {
            DibujanteDeQsl.OlvidarFondo(Servicio.Disenos.RutaDelFondo(Diseno));
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

    /// <summary>Añade un campo de texto libre en el centro.</summary>
    [RelayCommand]
    public void AnadirCampo()
    {
        if (Diseno is null) return;
        var campo = new CampoDeQsl { Nombre = Textos.T("Qsl.Editor.Texto"), Texto = Textos.T("Qsl.Editor.TextoLibre"), XMm = AnchoMm / 2, YMm = AltoMm / 2, Alineacion = AlineacionDeCampo.Centro, TamanoPt = 14 };
        Diseno.Campos.Add(campo);
        var editable = new CampoEditable(campo, Cambiado);
        Campos.Add(editable);
        CampoElegido = editable;
        Cambiado();
    }

    /// <summary>Copia del campo elegido, un poco desplazada.</summary>
    [RelayCommand(CanExecute = nameof(HayCampoElegido))]
    public void DuplicarCampo()
    {
        if (Diseno is null || CampoElegido is null) return;
        var campo = CampoElegido.Campo.Copiar();
        campo.Nombre = Textos.F("Qsl.Editor.Copia", campo.Nombre);
        campo.YMm = Math.Min(AltoMm - 5, campo.YMm + 5);
        Diseno.Campos.Add(campo);
        var editable = new CampoEditable(campo, Cambiado);
        Campos.Add(editable);
        CampoElegido = editable;
        Cambiado();
    }

    /// <summary>Quita el campo elegido.</summary>
    [RelayCommand(CanExecute = nameof(HayCampoElegido))]
    public void QuitarCampo()
    {
        if (Diseno is null || CampoElegido is null) return;
        Diseno.Campos.Remove(CampoElegido.Campo);
        Campos.Remove(CampoElegido);
        CampoElegido = null;
        Cambiado();
    }

    /// <summary>Guarda la tarjeta del contacto de muestra como imagen.</summary>
    [RelayCommand]
    public void ExportarImagen()
    {
        if (Diseno is null) return;
        var qso = Contacto?.Qso;
        var nombre = qso is null ? "QSL_" + Limpio(Diseno.Nombre) : ServicioDeQsl.NombreDeFichero(qso, _yo);
        if (ElegirDondeGuardar(nombre + ".jpg", Textos.T("Qsl.Tarjeta.FiltroImagen")) is not { } ruta) return;
        var formato = ruta.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? FormatoDeImagen.Png : FormatoDeImagen.Jpg;
        try
        {
            File.WriteAllBytes(ruta, DibujanteDeQsl.Codificar(Servicio.Dibujar(Diseno, qso, _yo, DibujanteDeQsl.PppDeImprenta), formato));
            Aviso = Textos.F("Qsl.Tarjeta.Guardada", ruta);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Aviso = Textos.F("Qsl.Editor.NoSeHaPodidoGuardar", ex.Message);
        }
    }

    /// <summary>Guarda la tarjeta del contacto de muestra en PDF y la abre.</summary>
    [RelayCommand]
    public void ExportarPdf()
    {
        if (Diseno is null || Contacto?.Qso is not { } qso)
        {
            Aviso = Textos.T("Qsl.Tarjeta.ElijaContactoPdf");
            return;
        }

        var impreso = Servicio.Pdf(Diseno, [qso], _yo, unaPorPagina: false);
        if (ElegirDondeGuardar(impreso.NombreSugerido, "PDF|*.pdf") is not { } ruta) return;
        try
        {
            File.WriteAllBytes(ruta, impreso.Bytes);
            AbrirFichero(ruta);
            Aviso = Textos.F("Qsl.Editor.PdfGuardado", ruta);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            Aviso = Textos.F("Qsl.Editor.NoPdf", ex.Message);
        }
    }

    /// <summary>
    /// Guarda el diseño sin los datos del contacto, para subirlo a eQSL a mano.
    /// </summary>
    /// <remarks>
    /// eQSL no tiene API para subir el diseño de la tarjeta: solo su web, con la sesion
    /// iniciada. Lo que se puede hacer es darle la imagen lista y abrir su pagina.
    /// </remarks>
    [RelayCommand]
    public void ExportarParaEqsl()
    {
        if (Diseno is null) return;
        if (ElegirDondeGuardar("QSL_" + Limpio(Diseno.Nombre) + "_eqsl.jpg", "JPG|*.jpg") is not { } ruta) return;
        try
        {
            var imagen = Servicio.Dibujar(Diseno, null, _yo, DibujanteDeQsl.PppDeCorreo, soloMisDatos: true);
            File.WriteAllBytes(ruta, DibujanteDeQsl.Codificar(imagen, FormatoDeImagen.Jpg));
            Aviso = Textos.F("Qsl.Tarjeta.EqslGuardado", ruta);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Aviso = Textos.F("Qsl.Editor.NoSeHaPodidoGuardar", ex.Message);
        }
    }

    /// <summary>Abre la pagina de eQSL donde se sube el diseño.</summary>
    [RelayCommand]
    public void AbrirEqsl()
    {
        try
        {
            AbrirFichero("https://www.eqsl.cc/qslcard/Index.cfm");
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            Aviso = Textos.F("Qsl.Tarjeta.NoNavegador", ex.Message);
        }
    }

    /// <summary>Manda la tarjeta del contacto de muestra.</summary>
    [RelayCommand]
    public void EnviarMuestra()
    {
        if (Contacto?.Qso is not { } qso)
        {
            Aviso = Textos.T("Qsl.Tarjeta.ElijaContactoEnviar");
            return;
        }

        if (HayCambios) Guardar();
        SolicitaEnviar?.Invoke(this, [qso.Id]);
    }

    /// <summary>Crea el modelo de la ventana de envio para unos contactos.</summary>
    /// <param name="ids">Los contactos.</param>
    /// <returns>El modelo.</returns>
    public VistaModeloEnvioQsl CrearEnvio(IReadOnlyList<long> ids)
    {
        if (HayCambios) Guardar();
        return new VistaModeloEnvioQsl(Servicio, ids, Diseno?.Id);
    }

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
            var variables = VariablesDeQsl.Para(Contacto?.Qso, _yo);
            VistaPrevia = DibujanteDeQsl.Dibujar(Diseno, variables, Servicio.Disenos.RutaDelFondo(Diseno), DibujanteDeQsl.PppDePantalla);
            foreach (var c in Campos)
            {
                var caja = DibujanteDeQsl.Caja(c.Campo, variables);
                c.CajaX = caja.X;
                c.CajaY = caja.Y;
                c.CajaAncho = Math.Max(3, caja.Width);
                c.CajaAlto = Math.Max(3, caja.Height);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se ha podido dibujar la vista previa de la QSL.");
            Aviso = Textos.F("Qsl.Tarjeta.NoDibujar", ex.Message);
        }
    }

    private void Cambiado()
    {
        if (_cargando) return;
        HayCambios = true;
        Redibujar();
    }

    /// <summary>
    /// Al cambiar de idioma: la ayuda de variables y el texto del contacto «sin contacto» se
    /// vuelven a leer (la lista se rehace con los mismos contactos).
    /// </summary>
    private void AlCambiarDeIdioma()
    {
        OnPropertyChanged(nameof(AyudaDeVariables));
        var elegido = Contacto;
        var lista = Contactos.ToList();
        Contactos.Clear();
        foreach (var c in lista) Contactos.Add(c with { });
        Contacto = elegido is null ? null : Contactos.FirstOrDefault(c => c.Qso?.Id == elegido.Qso?.Id);
    }

    private static string Limpio(string s) => new(s.Select(c => char.IsAsciiLetterOrDigit(c) ? c : '_').ToArray());
}
