using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nodisla.Cuaderno.Aplicacion.CasosDeUso;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Servicios.Subidas;
using Nodisla.Cuaderno.Ui.Ajustes;
using Serilog;

namespace Nodisla.Cuaderno.Ui.VistaModelos;

/// <summary>Una pastilla de la barra de estado: un servicio en linea.</summary>
public sealed partial class PastillaDeSubida : ObservableObject
{
    /// <summary>Crea la pastilla.</summary>
    /// <param name="nombre">Nombre del servicio.</param>
    public PastillaDeSubida(string nombre) => Nombre = nombre;

    /// <summary>Nombre del servicio.</summary>
    public string Nombre { get; }

    /// <summary>Color.</summary>
    [ObservableProperty]
    private SemaforoDeSubida _semaforo;

    /// <summary>Lo que se lee en la pastilla: el nombre y, si hay, los pendientes.</summary>
    [ObservableProperty]
    private string _etiqueta = string.Empty;

    /// <summary>El estado dicho con palabras, para la ayuda emergente.</summary>
    [ObservableProperty]
    private string _texto = string.Empty;
}

/// <summary>Un servicio en el apartado de subidas de Ajustes, con su casilla.</summary>
public sealed partial class FilaDeServicioDeSubida : ObservableObject
{
    private readonly Action<MedioDeConfirmacion, bool> _alCambiar;
    private bool _cargando;

    /// <summary>Crea la fila.</summary>
    /// <param name="medio">Servicio.</param>
    /// <param name="alCambiar">Que hacer cuando el operador toca la casilla.</param>
    public FilaDeServicioDeSubida(MedioDeConfirmacion medio, Action<MedioDeConfirmacion, bool> alCambiar)
    {
        Medio = medio;
        Nombre = ColaDeSubidas.NombreCorto(medio);
        _alCambiar = alCambiar;
        Nota = ColaDeSubidas.AdmiteModificar(medio)
            ? "Si modifica un contacto ya subido (F2), se vuelve a enviar corregido."
            : "No admite modificar: un contacto ya subido queda como se subió.";
    }

    /// <summary>Servicio.</summary>
    public MedioDeConfirmacion Medio { get; }

    /// <summary>Nombre del servicio.</summary>
    public string Nombre { get; }

    /// <summary>Lo que pasa si se modifica un contacto ya subido.</summary>
    public string Nota { get; }

    /// <summary>Subir automaticamente a este servicio.</summary>
    [ObservableProperty]
    private bool _activado;

    /// <summary>Estado con palabras.</summary>
    [ObservableProperty]
    private string _estado = string.Empty;

    /// <summary>Color del estado.</summary>
    [ObservableProperty]
    private SemaforoDeSubida _semaforo;

    /// <summary>Pone los valores sin que cuente como un cambio del operador.</summary>
    internal void Cargar(EstadoDeSubida estado)
    {
        _cargando = true;
        Activado = estado.Activado;
        Estado = estado.Texto;
        Semaforo = estado.Semaforo;
        _cargando = false;
    }

    partial void OnActivadoChanged(bool value)
    {
        if (!_cargando) _alCambiar(Medio, value);
    }
}

/// <summary>Un contacto en la cola, tal y como se lee en Ajustes.</summary>
/// <param name="Servicio">Servicio al que va.</param>
/// <param name="Indicativo">Corresponsal.</param>
/// <param name="Cuando">Fecha y hora del contacto.</param>
/// <param name="Estado">Que le pasa.</param>
public sealed record FilaDeCola(string Servicio, string Indicativo, string Cuando, string Estado);

/// <summary>
/// La subida automatica y el completado con QRZ en pantalla: las pastillas de la barra de
/// estado y el apartado de Ajustes con las cuentas, las casillas y la cola.
/// </summary>
public sealed partial class VistaModeloSubidas : ObservableObject
{
    private readonly ColaDeSubidas _cola;
    private readonly CompletadorDeQso _completador;
    private readonly AjustesDelPrograma _ajustes;
    private readonly string _carpeta;
    private readonly CuentasDeServicios? _cuentas;
    private readonly SynchronizationContext? _contexto;
    private readonly PastillaDeSubida _pastillaDeFicha = new("Ficha");

    /// <summary>Monta el modelo.</summary>
    /// <param name="cola">Cola de subidas.</param>
    /// <param name="completador">Completado con QRZ.com.</param>
    /// <param name="ajustes">Ajustes, donde van las cuentas y las casillas.</param>
    /// <param name="carpeta">Carpeta de datos, para guardar los ajustes.</param>
    /// <param name="cuentas">
    /// Las cuentas de verdad. Nulo con los puertos simulados: ahi no hay cuentas que escribir.
    /// </param>
    public VistaModeloSubidas(
        ColaDeSubidas cola,
        CompletadorDeQso completador,
        AjustesDelPrograma ajustes,
        string carpeta,
        CuentasDeServicios? cuentas = null,
        AvisosDeQsos? avisos = null)
    {
        _cola = cola ?? throw new ArgumentNullException(nameof(cola));
        _completador = completador ?? throw new ArgumentNullException(nameof(completador));
        _ajustes = ajustes ?? throw new ArgumentNullException(nameof(ajustes));
        _carpeta = carpeta;
        _cuentas = cuentas;
        // Solo el hilo de la ventana: fuera de WPF (pruebas) se refresca en el acto, sin mandar
        // el refresco a otro hilo a pisarse con el que ya esta en marcha.
        _contexto = SynchronizationContext.Current as System.Windows.Threading.DispatcherSynchronizationContext;

        foreach (var medio in ColaDeSubidas.Medios)
        {
            Pastillas.Add(new PastillaDeSubida(ColaDeSubidas.NombreCorto(medio)));
            Servicios.Add(new FilaDeServicioDeSubida(medio, CambiarCasilla));
        }
        Pastillas.Add(_pastillaDeFicha);

        var s = ajustes.Servicios;
        _completarConQrz = s.CompletarConQrz;
        _usuarioQrz = s.UsuarioQrz ?? string.Empty;
        _usuarioHamQth = s.UsuarioHamQth ?? string.Empty;
        _usuarioLotw = s.UsuarioLotw ?? string.Empty;
        _ubicacionTqsl = s.UbicacionTqsl ?? string.Empty;
        _rutaTqsl = s.RutaTqsl ?? string.Empty;
        _usuarioEqsl = s.UsuarioEqsl ?? string.Empty;
        _apodoEqsl = s.ApodoEqsl ?? string.Empty;
        _correoClubLog = s.CorreoClubLog ?? string.Empty;
        _indicativoClubLog = s.IndicativoClubLog ?? string.Empty;

        cola.EstadoCambiado += (_, _) => EnLaInterfaz(Refrescar);
        completador.EstadoCambiado += (_, _) => EnLaInterfaz(Refrescar);
        cola.CuadernoCambiado += (_, _) => EnLaInterfaz(AvisarDelCuaderno);
        if (avisos is not null)
        {
            // Un contacto completado con QRZ despues de guardarlo: la rejilla tiene que
            // enseñar el nombre y el QTH que acaban de llegar.
            avisos.QsoGuardado += (_, e) =>
            {
                if (e.Tipo == TipoDeGuardado.Completado) EnLaInterfaz(AvisarDelCuaderno);
            };
        }
        Refrescar();
    }

    /// <summary>
    /// El cuaderno ha cambiado por detras (estado de envio, datos de QRZ) y la rejilla debe
    /// refrescarse.
    /// </summary>
    public event EventHandler? CuadernoCambiado;

    private void AvisarDelCuaderno() => CuadernoCambiado?.Invoke(this, EventArgs.Empty);

    /// <summary>Las pastillas de la barra: LoTW, eQSL, Club Log, QRZ y la ficha.</summary>
    public ObservableCollection<PastillaDeSubida> Pastillas { get; } = [];

    /// <summary>Los servicios con su casilla, para Ajustes.</summary>
    public ObservableCollection<FilaDeServicioDeSubida> Servicios { get; } = [];

    /// <summary>Lo que hay en la cola.</summary>
    public ObservableCollection<FilaDeCola> Cola { get; } = [];

    /// <summary>La cola esta vacia.</summary>
    public bool ColaVacia => Cola.Count == 0;

    /// <summary>Hay cuentas que escribir (no con los puertos simulados).</summary>
    public bool HayCuentas => _cuentas is not null;

    /// <summary>Lo que vale un usuario vacio, para el texto de ayuda.</summary>
    public string UsuarioPorOmision => _cuentas?.IndicativoDelPerfil is { Length: > 0 } p
        ? $"Vacío: se usa el indicativo del perfil ({p})."
        : "Vacío: se usa el indicativo del perfil de estación.";

    /// <summary>Lo que ha pasado con la ultima accion.</summary>
    [ObservableProperty]
    private string _parte = string.Empty;

    /// <summary>Se esta subiendo por el boton.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SubirAhoraCommand))]
    private bool _subiendo;

    [ObservableProperty]
    private bool _completarConQrz;

    [ObservableProperty]
    private string _usuarioQrz;

    [ObservableProperty]
    private string _usuarioHamQth;

    [ObservableProperty]
    private string _usuarioLotw;

    [ObservableProperty]
    private string _ubicacionTqsl;

    [ObservableProperty]
    private string _rutaTqsl;

    [ObservableProperty]
    private string _usuarioEqsl;

    [ObservableProperty]
    private string _apodoEqsl;

    [ObservableProperty]
    private string _correoClubLog;

    [ObservableProperty]
    private string _indicativoClubLog;

    /// <summary>Guarda las cuentas escritas.</summary>
    [RelayCommand]
    public void GuardarCuentas()
    {
        var s = _ajustes.Servicios;
        s.UsuarioQrz = Limpio(UsuarioQrz);
        s.UsuarioHamQth = Limpio(UsuarioHamQth);
        s.UsuarioLotw = Limpio(UsuarioLotw);
        s.UbicacionTqsl = Limpio(UbicacionTqsl);
        s.RutaTqsl = Limpio(RutaTqsl);
        s.UsuarioEqsl = Limpio(UsuarioEqsl);
        s.ApodoEqsl = Limpio(ApodoEqsl);
        s.CorreoClubLog = Limpio(CorreoClubLog);
        s.IndicativoClubLog = Limpio(IndicativoClubLog);
        _ajustes.Guardar(_carpeta);
        Parte = "Cuentas guardadas. Valen ya, sin reiniciar.";
        Refrescar();
        _cola.Despertar();
    }

    /// <summary>Sube ya todo lo pendiente, incluido lo que fallo.</summary>
    [RelayCommand(CanExecute = nameof(PuedeSubirAhora))]
    public async Task SubirAhoraAsync()
    {
        Subiendo = true;
        Parte = "Subiendo…";
        try
        {
            var subidos = await _cola.ProcesarAsync(forzar: true).ConfigureAwait(true);
            Parte = subidos == 0
                ? (_cola.Elementos.Count == 0 ? "No había nada pendiente." : "No se ha podido subir nada: mire el estado de cada servicio.")
                : string.Create(CultureInfo.CurrentCulture, $"{subidos:N0} contacto(s) subido(s).");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Falló «Subir ahora».");
            Parte = $"No se ha podido subir: {ex.Message}";
        }
        finally
        {
            Subiendo = false;
            Refrescar();
        }
    }

    /// <summary>Vuelve a leer el estado de la cola y de la consulta.</summary>
    public void Refrescar()
    {
        var estados = _cola.Estados();
        for (var i = 0; i < estados.Count && i < Servicios.Count; i++)
        {
            var e = estados[i];
            Servicios[i].Cargar(e);
            var pastilla = Pastillas[i];
            pastilla.Semaforo = e.Semaforo;
            pastilla.Texto = e.Texto;
            var enCola = e.Pendientes + e.Fallidos;
            pastilla.Etiqueta = enCola > 0
                ? string.Create(CultureInfo.CurrentCulture, $"{e.Nombre} {enCola}")
                : e.Nombre;
        }

        RefrescarFicha();

        Cola.Clear();
        foreach (var e in _cola.Elementos.OrderBy(e => e.InicioUtc).Take(200))
        {
            var estado = e.Fallido ? $"Falló: {e.UltimoError}"
                : e.UltimoError is { } error ? $"Reintento {e.Intentos}: {error}"
                : e.Modificado ? "Pendiente (corregido)"
                : "Pendiente";
            Cola.Add(new FilaDeCola(
                ColaDeSubidas.NombreCorto(e.Medio),
                e.Indicativo,
                e.InicioUtc.UtcDateTime.ToString("dd-MM-yyyy HH:mm", CultureInfo.InvariantCulture),
                estado));
        }
        OnPropertyChanged(nameof(ColaVacia));
        OnPropertyChanged(nameof(UsuarioPorOmision));
    }

    private void RefrescarFicha()
    {
        _pastillaDeFicha.Etiqueta = "Ficha";
        if (!_ajustes.Servicios.CompletarConQrz)
        {
            _pastillaDeFicha.Semaforo = SemaforoDeSubida.Apagado;
            _pastillaDeFicha.Texto = "Completar con QRZ.com: desactivado en Configuración › Subidas y QRZ.";
        }
        else if (!_completador.EstaDisponible)
        {
            _pastillaDeFicha.Semaforo = SemaforoDeSubida.Apagado;
            _pastillaDeFicha.Texto = "Completar con QRZ.com: falta la contraseña de QRZ.com (o de HamQTH) en Configuración › Cuentas y servicios. "
                + "Los contactos se guardan igual, sin nombre ni QTH de la ficha.";
        }
        else if (_completador.Problema is { } problema)
        {
            _pastillaDeFicha.Semaforo = SemaforoDeSubida.Ambar;
            _pastillaDeFicha.Texto = $"{problema} Los contactos se guardan igual, sin los datos de la ficha.";
        }
        else
        {
            _pastillaDeFicha.Semaforo = SemaforoDeSubida.Verde;
            _pastillaDeFicha.Texto = $"Completar con {_completador.Servicio}: listo."
                + (_completador.Aviso is { } aviso ? $" Aviso del servicio: {aviso}" : string.Empty);
        }
    }

    partial void OnCompletarConQrzChanged(bool value)
    {
        if (_ajustes.Servicios.CompletarConQrz == value) return;
        _ajustes.Servicios.CompletarConQrz = value;
        _ajustes.Guardar(_carpeta);
        RefrescarFicha();
    }

    private void CambiarCasilla(MedioDeConfirmacion medio, bool valor)
    {
        CuentasDeServicios.Fijar(_ajustes.Servicios, medio, valor);
        _ajustes.Guardar(_carpeta);
        EnLaInterfaz(Refrescar);
        if (valor) _cola.Despertar();
    }

    private bool PuedeSubirAhora() => !Subiendo;

    private void EnLaInterfaz(Action accion)
    {
        if (_contexto is null || SynchronizationContext.Current == _contexto)
        {
            accion();
            return;
        }
        _contexto.Post(_ => accion(), null);
    }

    private static string? Limpio(string? texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();
}
