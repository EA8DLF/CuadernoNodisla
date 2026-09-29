using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nodisla.Cuaderno.Aplicacion.CasosDeUso;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Servicios.Lotw;

namespace Nodisla.Cuaderno.Servicios.Subidas;

/// <summary>Color de la pastilla de un servicio en la barra de estado.</summary>
public enum SemaforoDeSubida
{
    /// <summary>Desactivado o sin credenciales: no se sube nada.</summary>
    Apagado,

    /// <summary>Activado y al dia.</summary>
    Verde,

    /// <summary>Hay contactos esperando turno o reintentando.</summary>
    Ambar,

    /// <summary>No se puede subir (falta TQSL, credenciales) o hay contactos que fallaron.</summary>
    Rojo,
}

/// <summary>Como esta la subida automatica a un servicio.</summary>
/// <param name="Medio">Servicio.</param>
/// <param name="Nombre">Nombre corto para la pastilla.</param>
/// <param name="Activado">El operador quiere subir ahi.</param>
/// <param name="Pendientes">Contactos esperando.</param>
/// <param name="Fallidos">Contactos que agotaron los reintentos automaticos.</param>
/// <param name="Semaforo">Color de la pastilla.</param>
/// <param name="Texto">Estado dicho con palabras, para la ayuda emergente.</param>
public sealed record EstadoDeSubida(
    MedioDeConfirmacion Medio,
    string Nombre,
    bool Activado,
    int Pendientes,
    int Fallidos,
    SemaforoDeSubida Semaforo,
    string Texto);

/// <summary>Un contacto esperando a subir a un servicio.</summary>
public sealed class ElementoDeCola
{
    /// <summary>Identificador del contacto en el cuaderno.</summary>
    public long QsoId { get; set; }

    /// <summary>Servicio al que va.</summary>
    public MedioDeConfirmacion Medio { get; set; }

    /// <summary>Indicativo, para ensenarlo en la lista sin ir a la base.</summary>
    public string Indicativo { get; set; } = string.Empty;

    /// <summary>Hora del contacto, para ensenarla en la lista.</summary>
    public DateTimeOffset InicioUtc { get; set; }

    /// <summary>Es el reenvio de un contacto modificado despues de subirlo.</summary>
    public bool Modificado { get; set; }

    /// <summary>Intentos hechos.</summary>
    public int Intentos { get; set; }

    /// <summary>Cuando toca el siguiente.</summary>
    public DateTimeOffset ProximoIntentoUtc { get; set; }

    /// <summary>Por que fallo el ultimo intento.</summary>
    public string? UltimoError { get; set; }

    /// <summary>Agoto los reintentos automaticos; solo se reintenta con «Subir ahora» o al arrancar.</summary>
    public bool Fallido { get; set; }
}

/// <summary>
/// Subida automatica de los contactos a LoTW, eQSL, Club Log y el cuaderno de QRZ.com.
/// </summary>
/// <remarks>
/// <para>
/// Escucha a <see cref="AvisosDeQsos"/>: todo contacto que se guarda, venga del formulario, de
/// Digital o de la ronda, entra en la cola de cada servicio activado. La cola se guarda en un
/// fichero JSON al lado del cuaderno, asi que sin red, con el servicio caido o cerrando el
/// programa no se pierde nada: se reintenta con espera creciente y otra vez al arrancar.
/// </para>
/// <para>
/// El estado de envio se escribe en el propio contacto (los campos ADIF
/// <c>LOTW_QSL_SENT</c>, <c>EQSL_QSL_SENT</c>, <c>CLUBLOG_QSO_UPLOAD_STATUS</c>,
/// <c>QRZCOM_QSO_UPLOAD_STATUS</c> y sus fechas), que es lo que pinta la columna QSL.
/// </para>
/// <para>
/// <b>Modificar despues.</b> Club Log y QRZ.com aceptan que se les mande otra vez el contacto
/// corregido y lo sustituyen. LoTW y eQSL no: lo firmado queda como se firmo. Por eso un
/// contacto modificado solo se reenvia a los dos primeros.
/// </para>
/// <para>
/// <b>LoTW sin TQSL no se reintenta en bucle.</b> Si falta TQSL o la ubicacion de estacion,
/// los contactos se quedan esperando, la pastilla se pone roja con el motivo, y en cuanto se
/// arregla salen solos.
/// </para>
/// </remarks>
public sealed class ColaDeSubidas : IDisposable
{
    /// <summary>Nombre del fichero de la cola en la carpeta de datos.</summary>
    public const string NombreDelFichero = "cola-de-subidas.json";

    /// <summary>Intentos automaticos antes de dar un contacto por fallido.</summary>
    public const int IntentosMaximos = 8;

    /// <summary>Cada cuanto se vuelve a mirar si un servicio bloqueado ya se puede usar.</summary>
    public static readonly TimeSpan RevisionDeBloqueados = TimeSpan.FromMinutes(5);

    private static readonly TimeSpan PrimeraEspera = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan EsperaMaxima = TimeSpan.FromMinutes(60);

    /// <summary>Los servicios a los que se sube, por orden de la barra.</summary>
    public static readonly IReadOnlyList<MedioDeConfirmacion> Medios =
        [MedioDeConfirmacion.Lotw, MedioDeConfirmacion.Eqsl, MedioDeConfirmacion.ClubLog, MedioDeConfirmacion.QrzCom];

    private static readonly JsonSerializerOptions Formato = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly IRepositorioQso _cuaderno;
    private readonly Func<IReadOnlyList<IServicioQsl>> _servicios;
    private readonly Func<MedioDeConfirmacion, bool?> _activadoPorElOperador;
    private readonly string? _ruta;
    private readonly Func<DateTimeOffset> _ahora;
    private readonly Func<TimeSpan, CancellationToken, Task> _esperar;
    private readonly ILogger _log;
    private readonly SemaphoreSlim _cerrojo = new(1, 1);
    private readonly List<ElementoDeCola> _cola = [];
    private readonly System.Collections.Concurrent.ConcurrentDictionary<MedioDeConfirmacion, string> _bloqueos = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<MedioDeConfirmacion, string> _ultimosErrores = new();
    private readonly object _senalCerrojo = new();
    private TaskCompletionSource _senal = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private CancellationTokenSource? _parar;
    private Task _trabajo = Task.CompletedTask;
    private AvisosDeQsos? _avisos;

    /// <summary>Crea la cola y carga lo que quedo pendiente la ultima vez.</summary>
    /// <param name="cuaderno">Repositorio de contactos.</param>
    /// <param name="servicios">
    /// Los servicios, pedidos cada vez: asi un usuario o una contrasena escritos en Ajustes
    /// valen sin reiniciar.
    /// </param>
    /// <param name="activadoPorElOperador">
    /// La casilla de Ajustes de cada servicio. Nulo quiere decir «lo que salga de las
    /// credenciales»: activado si las hay.
    /// </param>
    /// <param name="ruta">Fichero de la cola; nulo para no guardarla (pruebas).</param>
    /// <param name="ahora">Reloj, sustituible en las pruebas.</param>
    /// <param name="esperar">Como esperar, sustituible en las pruebas.</param>
    /// <param name="log">Registro de trazas.</param>
    public ColaDeSubidas(
        IRepositorioQso cuaderno,
        Func<IReadOnlyList<IServicioQsl>> servicios,
        Func<MedioDeConfirmacion, bool?>? activadoPorElOperador = null,
        string? ruta = null,
        Func<DateTimeOffset>? ahora = null,
        Func<TimeSpan, CancellationToken, Task>? esperar = null,
        ILogger<ColaDeSubidas>? log = null)
    {
        _cuaderno = cuaderno ?? throw new ArgumentNullException(nameof(cuaderno));
        _servicios = servicios ?? throw new ArgumentNullException(nameof(servicios));
        _activadoPorElOperador = activadoPorElOperador ?? (_ => null);
        _ruta = ruta;
        _ahora = ahora ?? (() => DateTimeOffset.UtcNow);
        _esperar = esperar ?? Task.Delay;
        _log = log ?? NullLogger<ColaDeSubidas>.Instance;
        Cargar();
    }

    /// <summary>Algo ha cambiado: pendientes, errores o colores.</summary>
    public event EventHandler? EstadoCambiado;

    /// <summary>Se ha subido algo y el cuaderno tiene estados nuevos que pintar.</summary>
    public event EventHandler? CuadernoCambiado;

    /// <summary>Copia de lo que hay en la cola.</summary>
    public IReadOnlyList<ElementoDeCola> Elementos
    {
        get
        {
            lock (_cola) return _cola.Select(Copiar).ToList();
        }
    }

    /// <summary>Empieza a escuchar los contactos que se guardan.</summary>
    /// <param name="avisos">El punto comun de guardado.</param>
    public void Escuchar(AvisosDeQsos avisos)
    {
        ArgumentNullException.ThrowIfNull(avisos);
        if (_avisos is not null) _avisos.QsoGuardado -= AlGuardarse;
        _avisos = avisos;
        avisos.QsoGuardado += AlGuardarse;
    }

    /// <summary>
    /// Pone en marcha el trabajo en segundo plano. Lo que quedo pendiente de la ultima vez se
    /// intenta enseguida, tambien lo que habia fallado.
    /// </summary>
    public void Arrancar()
    {
        if (_parar is not null) return;
        lock (_cola)
        {
            var ahora = _ahora();
            foreach (var e in _cola)
            {
                e.ProximoIntentoUtc = ahora;
                if (e.Fallido)
                {
                    e.Fallido = false;
                    e.Intentos = 0;
                }
            }
        }

        _parar = new CancellationTokenSource();
        var ct = _parar.Token;
        _trabajo = Task.Run(() => BucleAsync(ct), ct);
    }

    /// <summary>Para el trabajo en segundo plano. Lo pendiente queda en el fichero.</summary>
    public void Detener()
    {
        if (_parar is null) return;
        _parar.Cancel();
        try
        {
            _trabajo.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException)
        {
            // Cancelado: es lo que se ha pedido.
        }
        _parar.Dispose();
        _parar = null;
    }

    /// <summary>Despierta al trabajo para que mire la cola ya.</summary>
    public void Despertar()
    {
        lock (_senalCerrojo) _senal.TrySetResult();
    }

    /// <summary>El operador quiere subir a ese servicio (casilla de Ajustes o credenciales).</summary>
    /// <param name="servicio">Servicio.</param>
    public bool EstaActivado(IServicioQsl servicio)
    {
        ArgumentNullException.ThrowIfNull(servicio);
        return _activadoPorElOperador(servicio.Medio) ?? SeguroConfigurado(servicio);
    }

    /// <summary>
    /// Mete un contacto en la cola de cada servicio activado y marca en el contacto que esta
    /// pendiente.
    /// </summary>
    /// <param name="qso">Contacto ya guardado, con su identificador.</param>
    /// <param name="modificado">Se ha modificado despues de guardarlo (F2).</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    public async Task EncolarAsync(Qso qso, bool modificado, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(qso);
        if (qso.Id <= 0) return;

        var entran = new List<MedioDeConfirmacion>();
        await _cerrojo.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            foreach (var servicio in Servicios())
            {
                if (!EstaActivado(servicio)) continue;
                var medio = servicio.Medio;

                bool yaEnCola;
                lock (_cola) yaEnCola = _cola.Any(e => e.QsoId == qso.Id && e.Medio == medio);

                if (modificado)
                {
                    // Si aun no habia salido, la subida pendiente ya se llevara lo nuevo.
                    if (yaEnCola || !AdmiteModificar(medio) || !SeHabiaSubido(qso, medio)) continue;
                }
                else if (yaEnCola)
                {
                    continue;
                }

                lock (_cola)
                {
                    _cola.Add(new ElementoDeCola
                    {
                        QsoId = qso.Id,
                        Medio = medio,
                        Indicativo = qso.Call.Valor,
                        InicioUtc = qso.InicioUtc,
                        Modificado = modificado,
                        ProximoIntentoUtc = _ahora(),
                    });
                }
                entran.Add(medio);
            }

            if (entran.Count == 0) return;
            Guardar();
            await MarcarAsync(qso.Id, entran, subido: false, ct).ConfigureAwait(false);
        }
        finally
        {
            _cerrojo.Release();
        }

        Avisar();
        CuadernoCambiado?.Invoke(this, EventArgs.Empty);
        Despertar();
    }

    /// <summary>
    /// Sube lo que toque. Con <paramref name="forzar"/>, todo lo pendiente sin esperar su
    /// turno, incluidos los fallidos: es el boton «Subir ahora».
    /// </summary>
    /// <param name="forzar">No respetar las esperas.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <returns>Cuantos contactos se han subido bien.</returns>
    public async Task<int> ProcesarAsync(bool forzar = false, CancellationToken ct = default)
    {
        var subidos = 0;
        await _cerrojo.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var ahora = _ahora();
            List<ElementoDeCola> tocan;
            lock (_cola)
            {
                if (forzar)
                {
                    foreach (var e in _cola.Where(e => e.Fallido))
                    {
                        e.Fallido = false;
                        e.Intentos = 0;
                    }
                }
                tocan = _cola.Where(e => !e.Fallido && (forzar || e.ProximoIntentoUtc <= ahora)).ToList();
            }

            var servicios = Servicios();
            foreach (var grupo in tocan.GroupBy(e => e.Medio))
            {
                ct.ThrowIfCancellationRequested();
                var servicio = servicios.FirstOrDefault(s => s.Medio == grupo.Key);
                if (servicio is null || !EstaActivado(servicio)) continue;

                if (MotivoDeBloqueo(servicio) is { } motivo)
                {
                    _bloqueos[grupo.Key] = motivo;
                    continue;
                }
                _bloqueos.TryRemove(grupo.Key, out _);

                subidos += await SubirGrupoAsync(servicio, grupo.ToList(), ct).ConfigureAwait(false);
            }

            Guardar();
        }
        finally
        {
            _cerrojo.Release();
        }

        Avisar();
        if (subidos > 0) CuadernoCambiado?.Invoke(this, EventArgs.Empty);
        return subidos;
    }

    /// <summary>Estado de cada servicio, para las pastillas y el apartado de Ajustes.</summary>
    public IReadOnlyList<EstadoDeSubida> Estados()
    {
        var servicios = Servicios();
        var estados = new List<EstadoDeSubida>();
        foreach (var medio in Medios)
        {
            var servicio = servicios.FirstOrDefault(s => s.Medio == medio);
            var nombre = NombreCorto(medio);
            int pendientes, fallidos;
            string? error;
            lock (_cola)
            {
                pendientes = _cola.Count(e => e.Medio == medio && !e.Fallido);
                fallidos = _cola.Count(e => e.Medio == medio && e.Fallido);
                error = _cola.Where(e => e.Medio == medio).Select(e => e.UltimoError).LastOrDefault(t => t is not null);
            }
            error ??= _ultimosErrores.GetValueOrDefault(medio);

            var activado = servicio is not null && EstaActivado(servicio);
            var bloqueo = servicio is not null && activado ? MotivoDeBloqueo(servicio) : null;

            SemaforoDeSubida semaforo;
            string texto;
            if (!activado)
            {
                semaforo = pendientes + fallidos > 0 ? SemaforoDeSubida.Ambar : SemaforoDeSubida.Apagado;
                texto = servicio is null || !SeguroConfigurado(servicio)
                    ? $"{nombre}: subida automática sin configurar (faltan usuario o credenciales en Ajustes)."
                    : $"{nombre}: subida automática desactivada en Ajustes.";
                if (pendientes + fallidos > 0) texto += $" {pendientes + fallidos} contacto(s) en espera.";
            }
            else if (bloqueo is not null)
            {
                semaforo = SemaforoDeSubida.Rojo;
                texto = pendientes > 0 ? $"{nombre}: {pendientes} pendiente(s). {bloqueo}" : bloqueo;
            }
            else if (fallidos > 0)
            {
                semaforo = SemaforoDeSubida.Rojo;
                texto = $"{nombre}: {fallidos} contacto(s) no se han podido subir"
                    + (pendientes > 0 ? $" y {pendientes} esperan turno" : string.Empty)
                    + $". Último error: {error}. Use «Subir ahora» en Ajustes.";
            }
            else if (pendientes > 0)
            {
                semaforo = SemaforoDeSubida.Ambar;
                texto = $"{nombre}: {pendientes} contacto(s) pendiente(s) de subir."
                    + (error is null ? string.Empty : $" Último error: {error}. Se reintenta solo.");
            }
            else
            {
                semaforo = SemaforoDeSubida.Verde;
                texto = $"{nombre}: al día. Los contactos se suben solos al registrarlos.";
            }

            estados.Add(new EstadoDeSubida(medio, nombre, activado, pendientes, fallidos, semaforo, texto));
        }
        return estados;
    }

    /// <summary>Por que no se puede subir ahora a ese servicio, o nulo si se puede.</summary>
    /// <param name="servicio">Servicio.</param>
    public static string? MotivoDeBloqueo(IServicioQsl servicio)
    {
        ArgumentNullException.ThrowIfNull(servicio);
        try
        {
            if (servicio.PuedeSubir) return null;
            if (servicio is ServicioLotw lotw && lotw.MotivoDeNoPoderSubir is { } motivo) return motivo;
            return $"No se puede subir a {NombreCorto(servicio.Medio)}: faltan el usuario o las credenciales en Ajustes.";
        }
        catch (Exception ex)
        {
            return $"No se ha podido comprobar {NombreCorto(servicio.Medio)}: {ex.Message}";
        }
    }

    /// <summary>El servicio acepta que se le vuelva a mandar un contacto corregido.</summary>
    /// <param name="medio">Servicio.</param>
    public static bool AdmiteModificar(MedioDeConfirmacion medio) =>
        medio is MedioDeConfirmacion.ClubLog or MedioDeConfirmacion.QrzCom;

    /// <summary>Nombre corto del servicio, el de la pastilla.</summary>
    /// <param name="medio">Servicio.</param>
    public static string NombreCorto(MedioDeConfirmacion medio) => medio switch
    {
        MedioDeConfirmacion.Lotw => "LoTW",
        MedioDeConfirmacion.Eqsl => "eQSL",
        MedioDeConfirmacion.ClubLog => "Club Log",
        MedioDeConfirmacion.QrzCom => "QRZ",
        _ => medio.ToString(),
    };

    /// <inheritdoc />
    public void Dispose()
    {
        if (_avisos is not null) _avisos.QsoGuardado -= AlGuardarse;
        Detener();
        _cerrojo.Dispose();
    }

    private void AlGuardarse(object? remitente, QsoGuardadoEventArgs e)
    {
        if (e.Tipo == TipoDeGuardado.Completado) return;
        _ = EncolarSinFallarAsync(e.Qso, e.Tipo == TipoDeGuardado.Modificado);
    }

    private async Task EncolarSinFallarAsync(Qso qso, bool modificado)
    {
        try
        {
            await EncolarAsync(qso, modificado).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "No se ha podido encolar el contacto con {Indicativo}.", qso.Call.Valor);
        }
    }

    private async Task BucleAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            Task senal;
            lock (_senalCerrojo)
            {
                if (_senal.Task.IsCompleted)
                {
                    _senal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                }
                senal = _senal.Task;
            }

            try
            {
                await ProcesarAsync(forzar: false, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Fallo inesperado en la cola de subidas.");
            }

            var espera = SiguienteEspera();
            using var cancelar = CancellationTokenSource.CreateLinkedTokenSource(ct);
            try
            {
                await Task.WhenAny(senal, _esperar(espera, cancelar.Token)).ConfigureAwait(false);
            }
            finally
            {
                await cancelar.CancelAsync().ConfigureAwait(false);
            }
        }
    }

    private TimeSpan SiguienteEspera()
    {
        var ahora = _ahora();
        DateTimeOffset? proximo;
        lock (_cola)
        {
            proximo = _cola.Where(e => !e.Fallido && !_bloqueos.ContainsKey(e.Medio))
                .Select(e => (DateTimeOffset?)e.ProximoIntentoUtc)
                .Min();
        }
        if (proximo is null) return RevisionDeBloqueados;
        var falta = proximo.Value - ahora;
        if (falta < TimeSpan.FromSeconds(1)) falta = TimeSpan.FromSeconds(1);
        return falta < RevisionDeBloqueados ? falta : RevisionDeBloqueados;
    }

    private async Task<int> SubirGrupoAsync(
        IServicioQsl servicio, List<ElementoDeCola> elementos, CancellationToken ct)
    {
        var qsos = new List<Qso>();
        var porClave = new Dictionary<string, List<ElementoDeCola>>(StringComparer.Ordinal);
        foreach (var e in elementos)
        {
            var qso = await _cuaderno.ObtenerAsync(e.QsoId, ct).ConfigureAwait(false);
            if (qso is null)
            {
                // Borrado del cuaderno mientras esperaba: ya no hay nada que subir.
                lock (_cola) _cola.Remove(e);
                continue;
            }
            if (!porClave.TryGetValue(qso.ClaveNatural, out var lista))
            {
                porClave[qso.ClaveNatural] = lista = [];
                qsos.Add(qso);
            }
            lista.Add(e);
        }
        if (qsos.Count == 0) return 0;

        ResultadoDeSubida resultado;
        try
        {
            resultado = await servicio.SubirAsync(qsos, progreso: null, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (InvalidOperationException ex)
        {
            // Faltan credenciales: no mejora por insistir. Queda bloqueado hasta que cambien.
            _bloqueos[servicio.Medio] = ex.Message;
            foreach (var e in elementos) e.UltimoError = ex.Message;
            return 0;
        }
        catch (Exception ex)
        {
            _log.LogInformation("{Servicio} no ha aceptado la subida: {Motivo}", servicio.Nombre, ex.Message);
            foreach (var e in elementos) Aplazar(e, ex.Message);
            return 0;
        }

        var bien = new List<long>();
        foreach (var qso in qsos)
        {
            var suyos = porClave[qso.ClaveNatural];
            if (resultado.Motivos.TryGetValue(qso.ClaveNatural, out var motivo))
            {
                foreach (var e in suyos) Aplazar(e, motivo);
                continue;
            }

            lock (_cola)
            {
                foreach (var e in suyos) _cola.Remove(e);
            }
            bien.Add(qso.Id);
        }

        if (resultado.Motivos.Count == 0) _ultimosErrores.TryRemove(servicio.Medio, out _);
        foreach (var id in bien)
        {
            await MarcarAsync(id, [servicio.Medio], subido: true, ct).ConfigureAwait(false);
        }

        _log.LogInformation("{Servicio}: {Bien} contacto(s) subido(s), {Mal} rechazado(s).{Motivos}",
            servicio.Nombre, bien.Count, qsos.Count - bien.Count,
            resultado.Motivos.Count == 0 ? string.Empty : " Motivo: " + string.Join(" | ", resultado.Motivos.Values.Distinct()));
        return bien.Count;
    }

    private void Aplazar(ElementoDeCola e, string motivo)
    {
        e.Intentos++;
        e.UltimoError = motivo;
        _ultimosErrores[e.Medio] = motivo;
        if (e.Intentos >= IntentosMaximos)
        {
            e.Fallido = true;
            return;
        }
        e.ProximoIntentoUtc = _ahora() + EsperaDe(e.Intentos);
    }

    /// <summary>Espera antes del siguiente intento: 1, 2, 4… minutos, hasta una hora.</summary>
    /// <param name="intentosHechos">Intentos fallidos hasta ahora.</param>
    public static TimeSpan EsperaDe(int intentosHechos)
    {
        if (intentosHechos <= 0) return TimeSpan.Zero;
        var minutos = PrimeraEspera.TotalMinutes * Math.Pow(2, Math.Min(intentosHechos - 1, 10));
        return TimeSpan.FromMinutes(Math.Min(minutos, EsperaMaxima.TotalMinutes));
    }

    /// <summary>
    /// Escribe en el contacto el estado de envio. Se relee de la base para no pisar lo que se
    /// haya cambiado mientras tanto.
    /// </summary>
    private async Task MarcarAsync(
        long qsoId, IReadOnlyList<MedioDeConfirmacion> medios, bool subido, CancellationToken ct)
    {
        try
        {
            var qso = await _cuaderno.ObtenerAsync(qsoId, ct).ConfigureAwait(false);
            if (qso is null) return;

            var cambios = false;
            foreach (var medio in medios)
            {
                var confirmacion = qso.Confirmaciones.FirstOrDefault(c => c.Medio == medio);
                if (subido)
                {
                    confirmacion ??= Nueva(qso, medio);
                    confirmacion.Enviado = EstadoDeConfirmacion.Confirmado;
                    confirmacion.EnviadoUtc = _ahora();
                    cambios = true;
                    continue;
                }

                // Pendiente: LoTW y eQSL lo dicen con la Q de «en cola»; Club Log y QRZ solo
                // tienen letra para «modificado despues de subir» (la M), y se usa solo ahi.
                if (medio is MedioDeConfirmacion.Lotw or MedioDeConfirmacion.Eqsl)
                {
                    confirmacion ??= Nueva(qso, medio);
                    if (confirmacion.Enviado is EstadoDeConfirmacion.Ninguno)
                    {
                        confirmacion.Enviado = EstadoDeConfirmacion.Pendiente;
                        cambios = true;
                    }
                }
                else if (confirmacion is { Enviado: EstadoDeConfirmacion.Confirmado or EstadoDeConfirmacion.Verificado })
                {
                    confirmacion.Enviado = EstadoDeConfirmacion.Pendiente;
                    cambios = true;
                }
            }

            if (cambios) await _cuaderno.ActualizarAsync(qso, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "No se ha podido anotar el estado de envío del contacto {Id}.", qsoId);
        }
    }

    private static QsoConfirmacion Nueva(Qso qso, MedioDeConfirmacion medio)
    {
        var nueva = new QsoConfirmacion { Medio = medio, QsoId = qso.Id };
        qso.Confirmaciones.Add(nueva);
        return nueva;
    }

    private static bool SeHabiaSubido(Qso qso, MedioDeConfirmacion medio) =>
        qso.Confirmaciones.Any(c => c.Medio == medio
            && c.Enviado is EstadoDeConfirmacion.Confirmado or EstadoDeConfirmacion.Verificado
                or EstadoDeConfirmacion.Pendiente);

    private static bool SeguroConfigurado(IServicioQsl servicio)
    {
        try
        {
            return servicio.EstaConfigurado;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private List<IServicioQsl> Servicios()
    {
        try
        {
            return _servicios().Where(s => Medios.Contains(s.Medio)).ToList();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "No se han podido montar los servicios de subida.");
            return [];
        }
    }

    private void Avisar() => EstadoCambiado?.Invoke(this, EventArgs.Empty);

    private void Cargar()
    {
        if (_ruta is null || !File.Exists(_ruta)) return;
        try
        {
            var leidos = JsonSerializer.Deserialize<List<ElementoDeCola>>(File.ReadAllText(_ruta), Formato);
            if (leidos is not null) _cola.AddRange(leidos);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "No se ha podido leer la cola de subidas; se empieza vacía.");
        }
    }

    private void Guardar()
    {
        if (_ruta is null) return;
        try
        {
            string texto;
            lock (_cola) texto = JsonSerializer.Serialize(_cola, Formato);
            var carpeta = Path.GetDirectoryName(_ruta);
            if (!string.IsNullOrEmpty(carpeta)) Directory.CreateDirectory(carpeta);
            var temporal = _ruta + ".nuevo";
            File.WriteAllText(temporal, texto);
            File.Move(temporal, _ruta, overwrite: true);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "No se ha podido guardar la cola de subidas.");
        }
    }

    private static ElementoDeCola Copiar(ElementoDeCola e) => new()
    {
        QsoId = e.QsoId,
        Medio = e.Medio,
        Indicativo = e.Indicativo,
        InicioUtc = e.InicioUtc,
        Modificado = e.Modificado,
        Intentos = e.Intentos,
        ProximoIntentoUtc = e.ProximoIntentoUtc,
        UltimoError = e.UltimoError,
        Fallido = e.Fallido,
    };
}
