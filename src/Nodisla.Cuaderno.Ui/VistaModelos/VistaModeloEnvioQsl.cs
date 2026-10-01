using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Impresion.Qsl;
using Nodisla.Cuaderno.Servicios.Correo;
using Nodisla.Cuaderno.Ui.Qsl;
using Serilog;

namespace Nodisla.Cuaderno.Ui.VistaModelos;

/// <summary>Una estacion a la que se le manda la tarjeta: sus contactos y su direccion.</summary>
public sealed partial class DestinatarioDeQsl : ObservableObject
{
    /// <summary>Crea el destinatario.</summary>
    /// <param name="contactos">Sus contactos (todos con el mismo indicativo).</param>
    public DestinatarioDeQsl(IReadOnlyList<Qso> contactos)
    {
        ArgumentNullException.ThrowIfNull(contactos);
        if (contactos.Count == 0) throw new ArgumentException("Un destinatario sin contactos.", nameof(contactos));
        Contactos = contactos;
        _enviar = !contactos.All(MarcaDeQslEnviada.YaEnviada);
    }

    /// <summary>Sus contactos.</summary>
    public IReadOnlyList<Qso> Contactos { get; }

    /// <summary>Indicativo.</summary>
    public string Indicativo => Contactos[0].Call.Valor ?? string.Empty;

    /// <summary>Resumen de los contactos.</summary>
    public string ContactosTexto => Contactos.Count == 1
        ? string.Create(CultureInfo.InvariantCulture, $"{Contactos[0].InicioUtc.UtcDateTime:yyyy-MM-dd HH:mm} {Contactos[0].Band.Nombre} {Contactos[0].Mode.NombreUsual}")
        : $"{Contactos.Count} contactos";

    /// <summary>Alguno ya consta como enviado.</summary>
    public bool AlgunoYaEnviado => Contactos.Any(MarcaDeQslEnviada.YaEnviada);

    /// <summary>La direccion.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CorreoValido))]
    private string _correo = string.Empty;

    /// <summary>De donde ha salido la direccion.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OrigenTexto))]
    private OrigenDelCorreo _origen;

    /// <summary>Marcado para mandar.</summary>
    [ObservableProperty]
    private bool _enviar;

    /// <summary>Como ha ido.</summary>
    [ObservableProperty]
    private string _estado = string.Empty;

    /// <summary>Ya se ha mandado en esta ventana.</summary>
    [ObservableProperty]
    private bool _enviado;

    /// <summary>La direccion tiene buena pinta.</summary>
    public bool CorreoValido => DireccionDeCorreo.EsValida(Correo);

    /// <summary>De donde sale la direccion, escrito.</summary>
    public string OrigenTexto => Origen switch
    {
        OrigenDelCorreo.DelContacto => "del contacto",
        OrigenDelCorreo.DeLaFicha => "de su ficha QRZ",
        OrigenDelCorreo.Escrita => "escrita a mano",
        _ => "sin correo",
    };

    partial void OnCorreoChanged(string value)
    {
        if (Origen != OrigenDelCorreo.Escrita && value.Length > 0 && _buscado) Origen = OrigenDelCorreo.Escrita;
    }

    private bool _buscado;

    /// <summary>Apunta la direccion encontrada.</summary>
    /// <param name="correo">La direccion.</param>
    /// <param name="origen">De donde sale.</param>
    public void PonerEncontrado(string? correo, OrigenDelCorreo origen)
    {
        _buscado = false;
        Correo = correo ?? string.Empty;
        Origen = origen;
        _buscado = true;
        if (origen == OrigenDelCorreo.Ninguno) Estado = "No publica correo: escríbalo o se saltará.";
    }
}

/// <summary>
/// La ventana de «Ver/enviar QSL»: la tarjeta de cada contacto elegido y el envio por correo.
/// </summary>
public sealed partial class VistaModeloEnvioQsl : ObservableObject
{
    private readonly IReadOnlyList<long> _ids;
    private readonly string? _idDelDiseno;
    private DatosDeMiEstacion _yo = DatosDeMiEstacion.Vacios;
    private CancellationTokenSource? _cancelacion;

    /// <summary>Crea el modelo.</summary>
    /// <param name="servicio">Las QSL.</param>
    /// <param name="ids">Los contactos elegidos.</param>
    /// <param name="idDelDiseno">Plantilla con la que abrir; nula, la de omision.</param>
    public VistaModeloEnvioQsl(ServicioDeQsl servicio, IReadOnlyList<long> ids, string? idDelDiseno = null)
    {
        Servicio = servicio ?? throw new ArgumentNullException(nameof(servicio));
        _ids = ids ?? throw new ArgumentNullException(nameof(ids));
        _idDelDiseno = idDelDiseno;
        _asunto = servicio.Ajustes.Asunto;
        _texto = servicio.Ajustes.Texto;
        foreach (var d in servicio.Disenos.Listar()) Disenos.Add(d);
        _diseno = Disenos.FirstOrDefault(d => d.Id == (idDelDiseno ?? servicio.Disenos.IdPorOmision())) ?? Disenos.FirstOrDefault();
    }

    /// <summary>Las QSL.</summary>
    public ServicioDeQsl Servicio { get; }

    /// <summary>Pide donde guardar. Recibe nombre sugerido y filtro.</summary>
    public Func<string, string, string?> ElegirDondeGuardar { get; set; } = (nombre, filtro) =>
    {
        var dialogo = new Microsoft.Win32.SaveFileDialog { FileName = nombre, Filter = filtro };
        return dialogo.ShowDialog() == true ? dialogo.FileName : null;
    };

    /// <summary>Pide una carpeta. Devuelve nula si se cancela.</summary>
    public Func<string?> ElegirCarpeta { get; set; } = () =>
    {
        var dialogo = new Microsoft.Win32.OpenFolderDialog { Title = "Carpeta donde guardar las tarjetas" };
        return dialogo.ShowDialog() == true ? dialogo.FolderName : null;
    };

    /// <summary>Abre un fichero con el programa del sistema.</summary>
    public Action<string> AbrirFichero { get; set; } =
        ruta => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ruta) { UseShellExecute = true });

    /// <summary>Las estaciones a las que se manda.</summary>
    public ObservableCollection<DestinatarioDeQsl> Destinatarios { get; } = [];

    /// <summary>Las plantillas.</summary>
    public ObservableCollection<DisenoDeQsl> Disenos { get; } = [];

    /// <summary>Se ha mandado alguna tarjeta: el cuaderno tiene que refrescarse.</summary>
    public bool HaEnviadoAlguna { get; private set; }

    /// <summary>Los contactos ya marcados como enviados en esta ventana.</summary>
    public List<long> Apuntados { get; } = [];

    /// <summary>La plantilla.</summary>
    [ObservableProperty]
    private DisenoDeQsl? _diseno;

    /// <summary>El destinatario que se ve.</summary>
    [ObservableProperty]
    private DestinatarioDeQsl? _elegido;

    /// <summary>La tarjeta del elegido.</summary>
    [ObservableProperty]
    private ImageSource? _vistaPrevia;

    /// <summary>Asunto, con variables.</summary>
    [ObservableProperty]
    private string _asunto;

    /// <summary>Texto, con variables.</summary>
    [ObservableProperty]
    private string _texto;

    /// <summary>Se esta buscando o mandando.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EnviarCommand), nameof(GuardarImagenesCommand), nameof(GuardarPdfCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelarCommand))]
    private bool _ocupado;

    /// <summary>Lo ultimo que ha pasado.</summary>
    [ObservableProperty]
    private string _aviso = string.Empty;

    /// <summary>Resumen del correo saliente, para que se sepa desde donde sale.</summary>
    public string DesdeTexto => Servicio.Ajustes.Smtp.EstaCompleta
        ? $"Sale de {Servicio.Ajustes.Smtp.Remitente} por {Servicio.Ajustes.Smtp.Servidor}:{Servicio.Ajustes.Smtp.Puerto}."
        : "El correo saliente no está configurado: Configuración › Correo de las QSL.";

    /// <summary>Carga los contactos, agrupa por estacion y busca las direcciones.</summary>
    /// <returns>Tarea.</returns>
    public async Task CargarAsync()
    {
        Ocupado = true;
        try
        {
            _yo = await Servicio.MiEstacionAsync().ConfigureAwait(true);
            var qsos = await Servicio.TraerAsync(_ids).ConfigureAwait(true);
            Destinatarios.Clear();
            foreach (var grupo in qsos.GroupBy(q => (q.Call.Valor ?? string.Empty).ToUpperInvariant()))
            {
                Destinatarios.Add(new DestinatarioDeQsl(grupo.OrderBy(q => q.InicioUtc).ToList()));
            }

            Elegido = Destinatarios.FirstOrDefault();
            if (Destinatarios.Count == 0)
            {
                Aviso = "Esos contactos ya no están en el cuaderno.";
                return;
            }

            Aviso = "Buscando las direcciones de correo…";
            foreach (var d in Destinatarios)
            {
                var (correo, origen) = await Servicio.BuscarCorreoAsync(d.Contactos[0]).ConfigureAwait(true);
                d.PonerEncontrado(correo, origen);
                if (origen == OrigenDelCorreo.Ninguno) d.Enviar = false;
            }

            var sin = Destinatarios.Count(d => !d.CorreoValido);
            Aviso = sin == 0
                ? "Listo para mandar."
                : $"{sin} estación(es) no publican correo: escriba la dirección o se saltarán.";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se ha podido preparar el envío de QSL.");
            Aviso = $"No se ha podido preparar: {ex.Message}";
        }
        finally
        {
            Ocupado = false;
        }
    }

    partial void OnElegidoChanged(DestinatarioDeQsl? value) => Redibujar();

    partial void OnDisenoChanged(DisenoDeQsl? value) => Redibujar();

    /// <summary>Manda la tarjeta a todos los marcados que tengan direccion.</summary>
    /// <returns>Tarea.</returns>
    [RelayCommand(CanExecute = nameof(Libre))]
    public async Task EnviarAsync()
    {
        if (Diseno is null) return;
        var lista = Destinatarios.Where(d => d.Enviar && !d.Enviado).ToList();
        if (lista.Count == 0)
        {
            Aviso = "No hay ninguna estación marcada para mandar.";
            return;
        }

        Ocupado = true;
        _cancelacion = new CancellationTokenSource();
        var bien = 0;
        var saltados = 0;
        var fallos = 0;
        try
        {
            foreach (var d in lista)
            {
                if (_cancelacion.IsCancellationRequested) break;
                if (!d.CorreoValido)
                {
                    d.Estado = "Saltado: sin dirección de correo.";
                    saltados++;
                    continue;
                }

                d.Estado = "Mandando…";
                try
                {
                    await Servicio.EnviarAsync(d.Correo, d.Contactos, Diseno, _yo, Asunto, Texto, _cancelacion.Token).ConfigureAwait(true);
                    d.Enviado = true;
                    d.Enviar = false;
                    d.Estado = string.Create(CultureInfo.CurrentCulture, $"Enviada {DateTime.Now:HH:mm}.");
                    Apuntados.AddRange(d.Contactos.Select(q => q.Id));
                    HaEnviadoAlguna = true;
                    bien++;
                }
                catch (ErrorDeCorreo ex)
                {
                    d.Estado = ex.Message;
                    fallos++;

                    // Si el fallo es del servidor (contraseña, red, configuracion), va a fallar
                    // igual con todos: se para en vez de repetir el mismo error cincuenta veces.
                    if (!ex.EsDelDestinatario)
                    {
                        Aviso = $"Parado: {ex.Message}";
                        return;
                    }
                }
                catch (OperationCanceledException)
                {
                    d.Estado = "Cancelado.";
                    break;
                }
            }

            Aviso = $"Enviadas {bien}; saltadas {saltados}; con fallo {fallos}.";
        }
        finally
        {
            Ocupado = false;
            _cancelacion.Dispose();
            _cancelacion = null;
        }
    }

    /// <summary>Para el envio en curso tras el correo que se este mandando.</summary>
    [RelayCommand(CanExecute = nameof(Ocupado))]
    public void Cancelar() => _cancelacion?.Cancel();

    /// <summary>Guarda las tarjetas de todos los contactos como imagen en una carpeta.</summary>
    [RelayCommand(CanExecute = nameof(Libre))]
    public void GuardarImagenes()
    {
        if (Diseno is null || ElegirCarpeta() is not { } carpeta) return;
        try
        {
            var n = 0;
            foreach (var q in Destinatarios.SelectMany(d => d.Contactos))
            {
                var adjunto = Servicio.Adjunto(Diseno, q, _yo, Servicio.Ajustes.Formato);
                File.WriteAllBytes(Path.Combine(carpeta, adjunto.Nombre), adjunto.Bytes);
                n++;
            }

            Aviso = $"{n} tarjeta(s) guardada(s) en {carpeta}.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Aviso = $"No se han podido guardar: {ex.Message}";
        }
    }

    /// <summary>Todas las tarjetas en un PDF (A4 con marcas de corte) y lo abre.</summary>
    [RelayCommand(CanExecute = nameof(Libre))]
    public void GuardarPdf()
    {
        if (Diseno is null) return;
        var qsos = Destinatarios.SelectMany(d => d.Contactos).ToList();
        if (qsos.Count == 0) return;
        var impreso = Servicio.Pdf(Diseno, qsos, _yo, unaPorPagina: false);
        if (ElegirDondeGuardar(impreso.NombreSugerido, "PDF|*.pdf") is not { } ruta) return;
        try
        {
            File.WriteAllBytes(ruta, impreso.Bytes);
            AbrirFichero(ruta);
            Aviso = $"PDF guardado en {ruta}.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            Aviso = $"No se ha podido guardar o abrir el PDF: {ex.Message}";
        }
    }

    private bool Libre() => !Ocupado;

    private void Redibujar()
    {
        if (Diseno is null || Elegido is null)
        {
            VistaPrevia = null;
            return;
        }

        try
        {
            VistaPrevia = Servicio.Dibujar(Diseno, Elegido.Contactos[0], _yo, DibujanteDeQsl.PppDePantalla);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se ha podido dibujar la QSL.");
            Aviso = $"No se ha podido dibujar la tarjeta: {ex.Message}";
        }
    }
}
