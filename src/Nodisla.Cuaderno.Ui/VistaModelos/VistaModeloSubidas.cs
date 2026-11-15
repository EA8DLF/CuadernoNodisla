using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nodisla.Cuaderno.Aplicacion.CasosDeUso;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Idiomas;
using Nodisla.Cuaderno.Servicios.Qrz;
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

/// <summary>
/// Un servicio en el apartado de subidas de Ajustes: su casilla, su estado, cuantos contactos
/// tiene subidos y cuantos no, y la subida de los que ya estaban en el cuaderno.
/// </summary>
public sealed partial class FilaDeServicioDeSubida : ObservableObject
{
    private readonly Action<MedioDeConfirmacion, bool> _alCambiar;
    private readonly VistaModeloSubidas? _subidas;
    private bool _cargando;
    private int _totalDeLaTanda;
    private bool _tandaCancelada;

    /// <summary>Crea la fila.</summary>
    /// <param name="medio">Servicio.</param>
    /// <param name="alCambiar">Que hacer cuando el operador toca la casilla.</param>
    /// <param name="subidas">Quien sabe contar y encolar; nulo si la fila solo lleva la casilla.</param>
    public FilaDeServicioDeSubida(
        MedioDeConfirmacion medio, Action<MedioDeConfirmacion, bool> alCambiar, VistaModeloSubidas? subidas = null)
    {
        Medio = medio;
        Nombre = ColaDeSubidas.NombreCorto(medio);
        _alCambiar = alCambiar;
        _subidas = subidas;
        Textos.AlCambiar(this, static f => f.OnPropertyChanged(string.Empty));
    }

    /// <summary>Servicio.</summary>
    public MedioDeConfirmacion Medio { get; }

    /// <summary>Nombre del servicio.</summary>
    public string Nombre { get; }

    /// <summary>Lo que pasa si se modifica un contacto ya subido.</summary>
    public string Nota => Textos.T(ColaDeSubidas.AdmiteModificar(Medio)
        ? "Ajustes.Subidas.NotaModificable"
        : "Ajustes.Subidas.NotaNoModificable");

    /// <summary>Subir automaticamente a este servicio.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayAnterioresQueOfrecer))]
    private bool _activado;

    /// <summary>Estado con palabras.</summary>
    [ObservableProperty]
    private string _estado = string.Empty;

    /// <summary>Color del estado.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayAnterioresQueOfrecer))]
    private SemaforoDeSubida _semaforo;

    // ── El recuento ──────────────────────────────────────────────────────────

    /// <summary>Lo ultimo que se ha contado, o nulo si aun no se ha contado.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RecuentoTexto))]
    [NotifyPropertyChangedFor(nameof(SinSubir))]
    [NotifyPropertyChangedFor(nameof(HayAnterioresQueOfrecer))]
    [NotifyPropertyChangedFor(nameof(TextoDeAnteriores))]
    private RecuentoDeSubida? _recuento;

    /// <summary>Contactos del cuaderno que ni estan subidos ni esperan en la cola.</summary>
    public int SinSubir => Recuento?.SinSubir ?? 0;

    /// <summary>«1.234 subidos · 3 pendientes · 1 con error · 560 sin subir».</summary>
    public string RecuentoTexto
    {
        get
        {
            if (Recuento is not { } r) return Textos.T("Ajustes.Subidas.Recuento.Contando");
            if (r.Total == 0) return Textos.T("Ajustes.Subidas.Recuento.CuadernoVacio");
            var partes = new List<string> { Textos.F("Ajustes.Subidas.Recuento.Subidos", r.Subidos) };
            if (r.Pendientes > 0) partes.Add(Textos.F("Ajustes.Subidas.Recuento.Pendientes", r.Pendientes));
            if (r.ConError > 0) partes.Add(Textos.F("Ajustes.Subidas.Recuento.ConError", r.ConError));
            if (r.SinSubir > 0) partes.Add(Textos.F("Ajustes.Subidas.Recuento.SinSubir", r.SinSubir));
            return string.Join("  ·  ", partes);
        }
    }

    // ── Los contactos que ya estaban en el cuaderno ──────────────────────────

    /// <summary>
    /// Hay contactos sin subir y tiene sentido ofrecer subirlos: el servicio esta activado, no
    /// se esta ya en ello y no esta abierta la confirmacion. Con el servicio en rojo (faltan
    /// credenciales, clave rechazada) no se ofrece: primero hay que arreglar eso.
    /// </summary>
    public bool HayAnterioresQueOfrecer =>
        _subidas is not null && Activado && Semaforo != SemaforoDeSubida.Rojo
        && SinSubir > 0 && !PidiendoAnteriores && !AnterioresEnMarcha;

    /// <summary>«Hay 1.812 contactos en el cuaderno que no estan subidos a QRZ…».</summary>
    public string TextoDeAnteriores => Textos.F("Ajustes.Subidas.Anteriores.Explicacion", SinSubir, Nombre);

    /// <summary>Esta abierta la confirmacion de subir los anteriores.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayAnterioresQueOfrecer))]
    private bool _pidiendoAnteriores;

    /// <summary>Subir solo los contactos desde ese dia (UTC). Nulo: todos.</summary>
    [ObservableProperty]
    private DateTime? _desde;

    /// <summary>
    /// La fecha «desde» tal y como la escribe el operador: 2026-01-31 o 31/01/2026. Vacio:
    /// todos los contactos.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResumenDeAnteriores))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmarAnterioresCommand))]
    private string _desdeTexto = string.Empty;

    /// <summary>Lo escrito en la fecha no se entiende.</summary>
    public bool FechaNoValida => !string.IsNullOrWhiteSpace(DesdeTexto) && LeerFecha(DesdeTexto) is null;

    private static DateTime? LeerFecha(string texto) =>
        DateTime.TryParseExact(
            texto.Trim(),
            ["yyyy-MM-dd", "yyyy-M-d", "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "d-M-yyyy"],
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var dia)
            ? dia
            : null;

    partial void OnDesdeTextoChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) Desde = null;
        else if (LeerFecha(value) is { } dia) Desde = dia;
    }

    /// <summary>Cuantos contactos se subirian con la fecha elegida. Nulo mientras se cuenta.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResumenDeAnteriores))]
    [NotifyPropertyChangedFor(nameof(TextoDeConfirmar))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmarAnterioresCommand))]
    private int? _aSubir;

    /// <summary>Lo que va a pasar si se confirma.</summary>
    public string ResumenDeAnteriores => FechaNoValida ? Textos.T("Ajustes.Subidas.Anteriores.FechaNoValida") : ASubir switch
    {
        null => Textos.T("Ajustes.Subidas.Recuento.Contando"),
        0 => Textos.T("Ajustes.Subidas.Anteriores.Nada"),
        { } n => Textos.F("Ajustes.Subidas.Anteriores.Resumen", n, Nombre),
    };

    /// <summary>Rotulo del boton de confirmar: «Subir 1.812 contactos».</summary>
    public string TextoDeConfirmar => Textos.F("Ajustes.Subidas.Anteriores.Confirmar", ASubir ?? 0);

    /// <summary>Quedan contactos anteriores en la cola: la subida esta en marcha.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayAnterioresQueOfrecer))]
    [NotifyCanExecuteChangedFor(nameof(CancelarAnterioresCommand))]
    private bool _anterioresEnMarcha;

    /// <summary>Por donde va la subida de anteriores, de 0 a 1.</summary>
    [ObservableProperty]
    private double _progreso;

    /// <summary>«Subiendo los anteriores: 250 de 1.812.»</summary>
    [ObservableProperty]
    private string _progresoTexto = string.Empty;

    /// <summary>Lo ultimo que ha pasado con la subida de anteriores.</summary>
    [ObservableProperty]
    private string _parteDeAnteriores = string.Empty;

    /// <summary>Abre la confirmacion y cuenta cuantos contactos se subirian.</summary>
    [RelayCommand]
    public async Task PedirAnterioresAsync()
    {
        ParteDeAnteriores = string.Empty;
        PidiendoAnteriores = true;
        await ContarAsync().ConfigureAwait(true);
    }

    /// <summary>Cierra la confirmacion sin subir nada.</summary>
    [RelayCommand]
    public void DescartarAnteriores()
    {
        PidiendoAnteriores = false;
        DesdeTexto = string.Empty;
        Desde = null;
    }

    /// <summary>Mete en la cola los contactos anteriores que faltan.</summary>
    [RelayCommand(CanExecute = nameof(HayAlgoQueSubir))]
    public async Task ConfirmarAnterioresAsync()
    {
        if (_subidas is null) return;
        var desde = DesdeUtc();
        PidiendoAnteriores = false;
        try
        {
            var encolados = await _subidas.EncolarAnterioresAsync(Medio, desde).ConfigureAwait(true);
            _totalDeLaTanda = encolados;
            _tandaCancelada = false;
            ParteDeAnteriores = encolados == 0
                ? Textos.T("Ajustes.Subidas.Anteriores.Nada")
                : Textos.F("Ajustes.Subidas.Anteriores.Encolados", encolados);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "No se han podido encolar los contactos anteriores de {Servicio}.", Nombre);
            ParteDeAnteriores = Textos.F("Ajustes.Subidas.ErrorAlSubir", ex.Message);
        }
        DesdeTexto = string.Empty;
        Desde = null;
    }

    /// <summary>Cancela la subida de anteriores: lo que faltaba sale de la cola.</summary>
    [RelayCommand(CanExecute = nameof(AnterioresEnMarcha))]
    public void CancelarAnteriores()
    {
        if (_subidas is null) return;
        _tandaCancelada = true;
        var quitados = _subidas.CancelarAnteriores(Medio);
        ParteDeAnteriores = Textos.F("Ajustes.Subidas.Anteriores.Cancelada", quitados);
    }

    private bool HayAlgoQueSubir() => ASubir is > 0 && !FechaNoValida;

    private DateTimeOffset? DesdeUtc() =>
        Desde is { } dia ? new DateTimeOffset(DateTime.SpecifyKind(dia.Date, DateTimeKind.Utc)) : null;

    private async Task ContarAsync()
    {
        if (_subidas is null) return;
        ASubir = null;
        var desde = DesdeUtc();
        try
        {
            var cuantos = await _subidas.ContarSinSubirAsync(Medio, desde).ConfigureAwait(true);

            // Si mientras se contaba han cambiado la fecha, vale la cuenta de la fecha nueva.
            if (desde == DesdeUtc()) ASubir = cuantos;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "No se han podido contar los contactos sin subir a {Servicio}.", Nombre);
            ASubir = 0;
        }
    }

    partial void OnDesdeChanged(DateTime? value)
    {
        if (PidiendoAnteriores) _ = ContarAsync();
    }

    /// <summary>Pone los valores sin que cuente como un cambio del operador.</summary>
    internal void Cargar(EstadoDeSubida estado)
    {
        _cargando = true;
        Activado = estado.Activado;
        Estado = estado.Texto;
        Semaforo = estado.Semaforo;
        _cargando = false;
    }

    /// <summary>Pone cuantos contactos anteriores quedan en la cola.</summary>
    internal void CargarAnteriores(int enCola)
    {
        if (enCola == 0)
        {
            if (AnterioresEnMarcha && !_tandaCancelada && _totalDeLaTanda > 0)
            {
                ParteDeAnteriores = Textos.F("Ajustes.Subidas.Anteriores.Terminada", _totalDeLaTanda);
            }
            _totalDeLaTanda = 0;
            AnterioresEnMarcha = false;
            Progreso = 0;
            ProgresoTexto = string.Empty;
            return;
        }

        // Tras cerrar y abrir el programa no se sabe de cuantos era la tanda: lo que queda.
        if (enCola > _totalDeLaTanda) _totalDeLaTanda = enCola;
        AnterioresEnMarcha = true;
        Progreso = (double)(_totalDeLaTanda - enCola) / _totalDeLaTanda;
        ProgresoTexto = Textos.F("Ajustes.Subidas.Anteriores.EnMarcha", _totalDeLaTanda - enCola, _totalDeLaTanda);
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
    private readonly PastillaDeSubida _pastillaDeFicha = new(Textos.T("Ajustes.Subidas.Ficha"));

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
            Servicios.Add(new FilaDeServicioDeSubida(medio, CambiarCasilla, this));
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

        cola.EstadoCambiado += (_, _) =>
        {
            EnLaInterfaz(Refrescar);
            PedirRecuento();
        };
        completador.EstadoCambiado += (_, _) => EnLaInterfaz(Refrescar);
        cola.CuadernoCambiado += (_, _) =>
        {
            EnLaInterfaz(AvisarDelCuaderno);
            PedirRecuento();
        };
        if (avisos is not null)
        {
            // Un contacto completado con QRZ despues de guardarlo: la rejilla tiene que
            // enseñar el nombre y el QTH que acaban de llegar.
            avisos.QsoGuardado += (_, e) =>
            {
                if (e.Tipo == TipoDeGuardado.Completado) EnLaInterfaz(AvisarDelCuaderno);
                else PedirRecuento();
            };
        }
        Refrescar();
        PedirRecuento();

        // Estados, cola y pastillas se vuelven a escribir en el idioma nuevo.
        Textos.AlCambiar(this, static vm => vm.EnLaInterfaz(vm.Refrescar));
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

    /// <summary>
    /// Los servicios que se ensenan en la lista de subidas: todos menos QRZ, que tiene su
    /// recuadro propio encima con la clave, la casilla y la prueba juntas.
    /// </summary>
    public IEnumerable<FilaDeServicioDeSubida> ServiciosSinQrz =>
        Servicios.Where(s => s.Medio != MedioDeConfirmacion.QrzCom);

    /// <summary>
    /// El recuento que esta en marcha, o el ultimo que se hizo. Las pruebas lo esperan; la
    /// pantalla no: se entera sola cuando acaba.
    /// </summary>
    public Task RecuentoEnCurso { get; private set; } = Task.CompletedTask;

    private readonly object _cerrojoDelRecuento = new();
    private bool _recontando;
    private bool _recontarOtraVez;

    /// <summary>
    /// Vuelve a contar, servicio por servicio, cuantos contactos estan subidos y cuantos no.
    /// </summary>
    /// <remarks>
    /// Va en segundo plano porque recorre el cuaderno entero. Si se pide mientras se esta
    /// contando no se lanza otro: se apunta y se repite al acabar, una sola vez.
    /// </remarks>
    public void PedirRecuento()
    {
        lock (_cerrojoDelRecuento)
        {
            if (_recontando)
            {
                _recontarOtraVez = true;
                return;
            }
            _recontando = true;
            RecuentoEnCurso = Task.Run(RecontarAsync);
        }
    }

    private async Task RecontarAsync()
    {
        while (true)
        {
            try
            {
                var recuentos = await _cola.RecuentosAsync().ConfigureAwait(false);
                EnLaInterfaz(() =>
                {
                    foreach (var fila in Servicios)
                    {
                        fila.Recuento = recuentos.FirstOrDefault(r => r.Medio == fila.Medio);
                    }
                });
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "No se ha podido contar lo subido a cada servicio.");
            }

            lock (_cerrojoDelRecuento)
            {
                if (!_recontarOtraVez)
                {
                    _recontando = false;
                    return;
                }
                _recontarOtraVez = false;
            }
        }
    }

    /// <summary>Cuantos contactos del cuaderno no estan subidos a un servicio ni en su cola.</summary>
    /// <param name="medio">Servicio.</param>
    /// <param name="desdeUtc">Solo desde esa fecha; nulo para todos.</param>
    public async Task<int> ContarSinSubirAsync(MedioDeConfirmacion medio, DateTimeOffset? desdeUtc)
    {
        var recuentos = await Task.Run(() => _cola.RecuentosAsync(desdeUtc)).ConfigureAwait(false);
        return recuentos.First(r => r.Medio == medio).SinSubir;
    }

    /// <summary>Mete en la cola los contactos anteriores que faltan en un servicio.</summary>
    /// <param name="medio">Servicio.</param>
    /// <param name="desdeUtc">Solo desde esa fecha; nulo para todos.</param>
    public Task<int> EncolarAnterioresAsync(MedioDeConfirmacion medio, DateTimeOffset? desdeUtc) =>
        Task.Run(() => _cola.EncolarAnterioresAsync(medio, desdeUtc));

    /// <summary>Cancela la subida de contactos anteriores a un servicio.</summary>
    /// <param name="medio">Servicio.</param>
    public int CancelarAnteriores(MedioDeConfirmacion medio) => _cola.CancelarAnteriores(medio);

    /// <summary>La clave de QRZ ha cambiado o se ha probado con exito: la cola puede seguir.</summary>
    public void ReanudarQrz()
    {
        _cola.Reanudar(MedioDeConfirmacion.QrzCom);
        EnLaInterfaz(Refrescar);
    }

    /// <summary>Pregunta a QRZ si la clave de API del libro vale.</summary>
    /// <param name="ct">Testigo de cancelacion.</param>
    public Task<PruebaDelCuadernoQrz> ProbarCuadernoDeQrzAsync(CancellationToken ct) =>
        _cuentas is null
            ? Task.FromResult(new PruebaDelCuadernoQrz(VeredictoDeQrz.SinDato, Textos.T("Ajustes.Qrz.SesionSimulada")))
            : Task.Run(() => _cuentas.CuadernoDeQrz().ProbarAsync(ct), ct);

    /// <summary>Pregunta a QRZ si el usuario y la contrasena de la web valen.</summary>
    /// <param name="ct">Testigo de cancelacion.</param>
    public Task<PruebaDeLaFichaQrz> ProbarFichaDeQrzAsync(CancellationToken ct) =>
        _cuentas is null
            ? Task.FromResult(new PruebaDeLaFichaQrz(VeredictoDeQrz.SinDato, Textos.T("Ajustes.Qrz.SesionSimulada")))
            : Task.Run(() => _cuentas.ConsultaDeQrz().ProbarAsync(ct), ct);

    /// <summary>Lo que hay en la cola.</summary>
    public ObservableCollection<FilaDeCola> Cola { get; } = [];

    /// <summary>La cola esta vacia.</summary>
    public bool ColaVacia => Cola.Count == 0;

    /// <summary>Filas de la cola que se ensenan como mucho.</summary>
    public const int LimiteDeLaCola = 100;

    /// <summary>«Se ensenan los 100 primeros de 1.812», o vacio si caben todos.</summary>
    [ObservableProperty]
    private string _colaRecortada = string.Empty;

    /// <summary>Hay cuentas que escribir (no con los puertos simulados).</summary>
    public bool HayCuentas => _cuentas is not null;

    /// <summary>Lo que vale un usuario vacio, para el texto de ayuda.</summary>
    public string UsuarioPorOmision => _cuentas?.IndicativoDelPerfil is { Length: > 0 } p
        ? Textos.F("Ajustes.Subidas.UsuarioPorOmisionCon", p)
        : Textos.T("Ajustes.Subidas.UsuarioPorOmision");

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
        Parte = Textos.T("Ajustes.Subidas.CuentasGuardadas");
        Refrescar();
        _cola.Despertar();
    }

    /// <summary>Sube ya todo lo pendiente, incluido lo que fallo.</summary>
    [RelayCommand(CanExecute = nameof(PuedeSubirAhora))]
    public async Task SubirAhoraAsync()
    {
        Subiendo = true;
        Parte = Textos.T("Ajustes.Subidas.Subiendo");
        try
        {
            var subidos = await _cola.ProcesarAsync(forzar: true).ConfigureAwait(true);
            Parte = subidos == 0
                ? Textos.T(_cola.Elementos.Count == 0 ? "Ajustes.Subidas.NadaPendiente" : "Ajustes.Subidas.NadaSubido")
                : Textos.F("Ajustes.Subidas.Subidos", subidos);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Falló «Subir ahora».");
            Parte = Textos.F("Ajustes.Subidas.ErrorAlSubir", ex.Message);
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
                ? string.Create(Textos.Cultura, $"{e.Nombre} {enCola}")
                : e.Nombre;
        }

        RefrescarFicha();

        var elementos = _cola.Elementos;
        foreach (var fila in Servicios)
        {
            fila.CargarAnteriores(elementos.Count(e => e.Medio == fila.Medio && e.Anterior));
        }

        Cola.Clear();
        ColaRecortada = elementos.Count > LimiteDeLaCola
            ? Textos.F("Ajustes.Subidas.Cola.Recortada", LimiteDeLaCola, elementos.Count)
            : string.Empty;

        // Lo que tiene un error va delante: es lo que el operador tiene que ver.
        foreach (var e in elementos
            .OrderByDescending(e => e.Fallido)
            .ThenByDescending(e => e.UltimoError is not null)
            .ThenBy(e => e.InicioUtc)
            .Take(LimiteDeLaCola))
        {
            var estado = e.Fallido ? Textos.F("Ajustes.Subidas.Cola.Fallo", e.UltimoError)
                : e.UltimoError is { } error ? Textos.F("Ajustes.Subidas.Cola.Reintento", e.Intentos, error)
                : Textos.T(e.Modificado ? "Ajustes.Subidas.Cola.PendienteCorregido" : "Ajustes.Subidas.Cola.Pendiente");
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
        _pastillaDeFicha.Etiqueta = Textos.T("Ajustes.Subidas.Ficha");
        if (!_ajustes.Servicios.CompletarConQrz)
        {
            _pastillaDeFicha.Semaforo = SemaforoDeSubida.Apagado;
            _pastillaDeFicha.Texto = Textos.T("Ajustes.Subidas.Ficha.Desactivada");
        }
        else if (!_completador.EstaDisponible)
        {
            _pastillaDeFicha.Semaforo = SemaforoDeSubida.Apagado;
            _pastillaDeFicha.Texto = Textos.T("Ajustes.Subidas.Ficha.SinContrasena");
        }
        else if (_completador.Problema is { } problema)
        {
            _pastillaDeFicha.Semaforo = SemaforoDeSubida.Ambar;
            _pastillaDeFicha.Texto = Textos.F("Ajustes.Subidas.Ficha.Problema", problema);
        }
        else
        {
            _pastillaDeFicha.Semaforo = SemaforoDeSubida.Verde;
            _pastillaDeFicha.Texto = Textos.F("Ajustes.Subidas.Ficha.Listo", _completador.Servicio)
                + (_completador.Aviso is { } aviso ? " " + Textos.F("Ajustes.Subidas.Ficha.AvisoDelServicio", aviso) : string.Empty);
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
