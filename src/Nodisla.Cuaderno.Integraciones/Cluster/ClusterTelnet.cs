using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Dxcc;
using Nodisla.Cuaderno.Idiomas;

namespace Nodisla.Cuaderno.Integraciones.Cluster;

/// <summary>
/// Cluster de DX por Telnet: conecta, se identifica, manda el guion de arranque y va
/// sacando los anuncios que llegan.
/// </summary>
/// <remarks>
/// Telnet crudo, no SSH ni websocket. Al conectar, el nodo pide el indicativo y a veces una
/// contrasenia; despues se envia el guion de arranque (los <c>set/</c> de siempre).
///
/// Reconectar es parte del trabajo, no un extra: un cluster se cae a menudo y de madrugada.
/// La espera entre reintentos crece hasta un tope para no castigar a un servidor que ya esta
/// teniendo un mal dia. Lo unico que no se reintenta es un rechazo del indicativo, porque
/// insistir no lo va a arreglar.
/// </remarks>
public sealed partial class ClusterTelnet : IFuenteSpots, IFuenteConDiagnostico
{
    private readonly OpcionesCluster _opciones;
    private readonly AnalizadorSpot _analizador;
    private readonly FiltroTelnet _filtro = new();
    private readonly SemaphoreSlim _envio = new(1, 1);
    private readonly Random _azar = new();

    private TaskCompletionSource _primerIntento =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private CancellationTokenSource? _cancelacion;
    private Task? _bucle;
    private TcpClient? _cliente;
    private Stream? _flujo;
    private EstadoDeConexion _estado = EstadoDeConexion.Desconectado;
    private Fase _fase = Fase.EsperandoAviso;
    private int _indiceDelGuion;
    private int _lineasDeAcceso;

    /// <summary>
    /// Cuantas lineas del principio de la conexion se miran buscando un rechazo del
    /// indicativo. Pasadas esas, lo que llega son anuncios y mensajes de la gente.
    /// </summary>
    private const int LineasVigiladas = 20;

    /// <summary>En que punto del acceso esta la conexion.</summary>
    private enum Fase
    {
        /// <summary>Esperando a que el nodo pida el indicativo.</summary>
        EsperandoAviso,

        /// <summary>Indicativo enviado; esperando a que pida la contrasenia.</summary>
        EsperandoContrasena,

        /// <summary>Dentro: guion enviado y recibiendo anuncios.</summary>
        Dentro,
    }

    /// <summary>Crea el cluster sin conectarlo.</summary>
    /// <param name="opciones">Datos del nodo y del acceso.</param>
    /// <param name="resolutor">Resolutor DXCC para rellenar la entidad de cada anuncio.</param>
    public ClusterTelnet(OpcionesCluster opciones, IResolutorDxcc? resolutor = null)
    {
        ArgumentNullException.ThrowIfNull(opciones);
        _opciones = opciones;
        _analizador = new AnalizadorSpot(opciones.Nombre, resolutor);
    }

    /// <inheritdoc/>
    public string Nombre => _opciones.Nombre;

    /// <inheritdoc/>
    public EstadoDeConexion Estado => _estado;

    /// <summary>Numero de reconexiones hechas desde que se arranco.</summary>
    public int Reconexiones { get; private set; }

    /// <inheritdoc/>
    public string? UltimoError { get; private set; }

    /// <inheritdoc/>
    public event EventHandler<EstadoDeConexion>? EstadoCambiado;

    /// <inheritdoc/>
    public event EventHandler<Spot>? SpotRecibido;

    /// <inheritdoc/>
    public event EventHandler<string>? LineaRecibida;

    /// <summary>
    /// Salta con cada anuncio, con todo lo que se saco de la linea.
    /// </summary>
    /// <remarks>
    /// Va mas alla del contrato: el <c>Spot</c> no tiene sitio para las referencias de POTA,
    /// SOTA o IOTA ni para la velocidad en CW, y aqui si.
    /// </remarks>
    public event EventHandler<AnuncioDeCluster>? AnuncioRecibido;

    /// <inheritdoc/>
    /// <remarks>
    /// Vuelve en cuanto termina el primer intento de conexion, haya salido bien o mal: si
    /// salio mal, el cluster se queda reintentando por su cuenta. Para saber si hay conexion
    /// hay que mirar <see cref="Estado"/>.
    /// </remarks>
    public Task ConectarAsync(CancellationToken ct = default)
    {
        if (_bucle is not null) return _primerIntento.Task.WaitAsync(ct);

        _primerIntento = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _cancelacion = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var testigo = _cancelacion.Token;
        _bucle = Task.Run(() => BucleAsync(testigo), CancellationToken.None);
        return _primerIntento.Task.WaitAsync(ct);
    }

    /// <inheritdoc/>
    public async Task DesconectarAsync(CancellationToken ct = default)
    {
        var bucle = _bucle;
        _bucle = null;

        if (_cancelacion is not null)
        {
            await _cancelacion.CancelAsync().ConfigureAwait(false);
        }
        Cerrar();

        if (bucle is not null)
        {
            try
            {
                await bucle.WaitAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Esperado: el bucle termina porque se ha cancelado.
            }
        }

        _cancelacion?.Dispose();
        _cancelacion = null;
        CambiarEstado(EstadoDeConexion.Desconectado);
    }

    /// <inheritdoc/>
    public async Task EnviarAsync(string orden, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(orden);
        var flujo = _flujo ?? throw new InvalidOperationException(
            Textos.F("Servicios.Cluster.OrdenSinConexion", _opciones.Nombre));

        var bytes = Encoding.ASCII.GetBytes(orden.TrimEnd('\r', '\n') + "\r\n");
        await _envio.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await flujo.WriteAsync(bytes, ct).ConfigureAwait(false);
            await flujo.FlushAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _envio.Release();
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        try
        {
            await DesconectarAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Cerrar nunca debe estropear el cierre del programa.
        }
        _envio.Dispose();
    }

    private async Task BucleAsync(CancellationToken ct)
    {
        var intento = 0;

        while (!ct.IsCancellationRequested)
        {
            var conecto = false;
            try
            {
                CambiarEstado(intento == 0 ? EstadoDeConexion.Conectando : EstadoDeConexion.Reintentando);
                await AbrirAsync(ct).ConfigureAwait(false);
                conecto = true;
                if (intento > 0) Reconexiones++;
                intento = 0;
                _primerIntento.TrySetResult();
                await LeerAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException
                or InvalidOperationException)
            {
                if (_estado != EstadoDeConexion.Fallido) UltimoError = ex.Message;
                Avisar(Textos.F("Servicios.Cluster.ConexionPerdida", _opciones.Nombre, ex.Message));
            }
            finally
            {
                Cerrar();
            }

            _primerIntento.TrySetResult();
            if (ct.IsCancellationRequested) break;
            if (_estado == EstadoDeConexion.Fallido) break;
            if (!_opciones.ReconectarSolo && conecto) break;

            intento++;
            var espera = EsperaDeReintento(intento);
            CambiarEstado(EstadoDeConexion.Reintentando);
            Avisar(Textos.F("Servicios.Cluster.Reintentando", _opciones.Nombre, espera.TotalSeconds));
            try
            {
                await Task.Delay(espera, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        if (_estado != EstadoDeConexion.Fallido) CambiarEstado(EstadoDeConexion.Desconectado);
    }

    /// <summary>Abre el socket y deja la conexion lista para leer.</summary>
    private async Task AbrirAsync(CancellationToken ct)
    {
        _filtro.Reiniciar();
        _fase = Fase.EsperandoAviso;
        _indiceDelGuion = 0;
        _lineasDeAcceso = 0;

        var cliente = new TcpClient();
        using (var limite = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            limite.CancelAfter(_opciones.EsperaDeConexion);
            try
            {
                await cliente.ConnectAsync(_opciones.Servidor, _opciones.Puerto, limite.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                cliente.Dispose();
                throw new SocketException((int)SocketError.TimedOut);
            }
            catch
            {
                cliente.Dispose();
                throw;
            }
        }

        _cliente = cliente;
        _flujo = cliente.GetStream();
    }

    /// <summary>Lee del socket hasta que la conexion se cierra o se cancela.</summary>
    private async Task LeerAsync(CancellationToken ct)
    {
        var flujo = _flujo ?? throw new InvalidOperationException("No hay conexión.");
        var buffer = new byte[4096];
        Task<int>? lectura = null;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                lectura ??= flujo.ReadAsync(buffer, ct).AsTask();

                var limite = _fase == Fase.Dentro ? _opciones.SilencioMaximo : _opciones.EsperaDelAviso;

                // El reloj se cancela en cuanto llegan datos: si no, cada lectura dejaria
                // un temporizador de quince minutos colgando, y un cluster movido son
                // muchas lecturas por minuto.
                using var reloj = CancellationTokenSource.CreateLinkedTokenSource(ct);
                var cual = await Task.WhenAny(lectura, Task.Delay(limite, reloj.Token)).ConfigureAwait(false);
                await reloj.CancelAsync().ConfigureAwait(false);

                if (cual != lectura)
                {
                    ct.ThrowIfCancellationRequested();
                    if (_fase == Fase.Dentro)
                    {
                        // Ni un byte en mucho rato: la conexion esta muerta aunque el socket
                        // siga abierto. Mas vale reconectar que quedarse mirando.
                        UltimoError = Textos.F("Servicios.Cluster.SinDatos", _opciones.Nombre);
                        Avisar(UltimoError);
                        return;
                    }
                    // El nodo no ha pedido nada: se sigue con el guion de todas formas.
                    await SeguirElGuionAsync(ct).ConfigureAwait(false);
                    continue;
                }

                var leidos = await lectura.ConfigureAwait(false);
                lectura = null;
                if (leidos == 0) return;

                _filtro.Anadir(buffer.AsSpan(0, leidos));
                await ProcesarAsync(ct).ConfigureAwait(false);
            }
        }
        finally
        {
            // La lectura que quedo viva muere al cerrar el flujo; su excepcion no interesa.
            if (lectura is not null)
            {
                _ = lectura.ContinueWith(
                    static t => _ = t.Exception,
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted,
                    TaskScheduler.Default);
            }
        }
    }

    /// <summary>Reparte lo que ha llegado: anuncios por un lado y todo lo demas por otro.</summary>
    private async Task ProcesarAsync(CancellationToken ct)
    {
        while (_filtro.TryLeerLinea(out var linea))
        {
            // El rechazo del indicativo llega justo despues de darlo, y a veces cuando ya
            // nos creemos dentro. Se vigila solo al principio para no confundirlo con el
            // comentario de un anuncio.
            if (_lineasDeAcceso < LineasVigiladas)
            {
                _lineasDeAcceso++;
                if (EsRechazo(linea))
                {
                    Avisar(linea);
                    Avisar(Textos.F("Servicios.Cluster.IndicativoRechazado", _opciones.Nombre));
                    UltimoError = linea.Trim();
                    CambiarEstado(EstadoDeConexion.Fallido);
                    throw new IOException(Textos.T("Servicios.Cluster.IndicativoRechazadoExcepcion"));
                }
            }

            var anuncio = _analizador.Analizar(linea, DateTimeOffset.UtcNow);
            if (anuncio is not null)
            {
                // Un anuncio significa que ya estamos dentro, aunque no hayamos visto el
                // saludo del nodo.
                if (_fase != Fase.Dentro) await SeguirElGuionAsync(ct).ConfigureAwait(false);

                // Lo que llega de una red de escucha automatica es de maquina aunque la linea
                // no lo diga: asi se puede esconder con el filtro de skimmers.
                if (_opciones.EsSkimmer && !anuncio.Spot.EsDeEscuchaAutomatica)
                {
                    anuncio = anuncio with { Spot = anuncio.Spot with { EsDeEscuchaAutomatica = true } };
                }

                SpotRecibido?.Invoke(this, anuncio.Spot);
                AnuncioRecibido?.Invoke(this, anuncio);
            }
            else
            {
                Avisar(linea);
                await MirarSiPideAlgoAsync(linea, esPendiente: false, ct).ConfigureAwait(false);
            }
        }

        // Los avisos del nodo llegan sin salto de linea, asi que hay que mirar tambien lo
        // que todavia no es una linea entera.
        if (_fase != Fase.Dentro)
        {
            await MirarSiPideAlgoAsync(_filtro.Pendiente, esPendiente: true, ct).ConfigureAwait(false);
        }
    }

    /// <summary>Contesta al nodo cuando pide el indicativo o la contrasenia.</summary>
    /// <param name="texto">Lo que ha mandado el nodo.</param>
    /// <param name="esPendiente">
    /// El texto es el aviso a medias que todavia no es una linea. Al contestarlo hay que
    /// sacarlo del buffer, o se queda pegado delante del siguiente anuncio.
    /// </param>
    /// <param name="ct">Testigo de cancelacion.</param>
    private async Task MirarSiPideAlgoAsync(string texto, bool esPendiente, CancellationToken ct)
    {
        if (texto.Length == 0) return;

        switch (_fase)
        {
            case Fase.EsperandoAviso when PideIndicativo().IsMatch(texto):
                if (esPendiente) Avisar(_filtro.TomarPendiente().TrimEnd());
                await EnviarIndicativoAsync(ct).ConfigureAwait(false);
                break;

            case Fase.EsperandoContrasena when PideContrasena().IsMatch(texto):
                if (esPendiente) Avisar(_filtro.TomarPendiente().TrimEnd());
                await EnviarContrasenaAsync(ct).ConfigureAwait(false);
                break;

            default:
                break;
        }
    }

    /// <summary>Avanza el acceso cuando el nodo no pide nada o ya no queda nada que pedir.</summary>
    private async Task SeguirElGuionAsync(CancellationToken ct)
    {
        switch (_fase)
        {
            case Fase.EsperandoAviso:
                await EnviarIndicativoAsync(ct).ConfigureAwait(false);
                break;

            case Fase.EsperandoContrasena:
                await EnviarContrasenaAsync(ct).ConfigureAwait(false);
                break;

            default:
                break;
        }
    }

    /// <summary>
    /// Envia el indicativo. Si no hay contrasenia que dar, se da el acceso por hecho y se
    /// manda el resto del guion.
    /// </summary>
    private async Task EnviarIndicativoAsync(CancellationToken ct)
    {
        await EnviarAsync(_opciones.IndicativoDeAcceso, ct).ConfigureAwait(false);

        if (string.IsNullOrEmpty(_opciones.Contrasena))
        {
            await EntrarAsync(ct).ConfigureAwait(false);
        }
        else
        {
            _fase = Fase.EsperandoContrasena;
        }
    }

    private async Task EnviarContrasenaAsync(CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(_opciones.Contrasena))
        {
            await EnviarAsync(_opciones.Contrasena, ct).ConfigureAwait(false);
        }
        await EntrarAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Da el acceso por hecho y envia el resto del guion de arranque.</summary>
    private async Task EntrarAsync(CancellationToken ct)
    {
        _fase = Fase.Dentro;
        UltimoError = null;
        CambiarEstado(EstadoDeConexion.Conectado);

        var guion = _opciones.GuionDeArranque;
        for (; _indiceDelGuion < guion.Count; _indiceDelGuion++)
        {
            var orden = guion[_indiceDelGuion].Trim();
            if (orden.Length == 0) continue;
            if (orden.StartsWith("//", StringComparison.Ordinal)) continue;
            if (string.Equals(orden, OpcionesCluster.MarcaIndicativo, StringComparison.OrdinalIgnoreCase)) continue;
            if (string.Equals(orden, OpcionesCluster.MarcaContrasena, StringComparison.OrdinalIgnoreCase)) continue;

            await EnviarAsync(orden, ct).ConfigureAwait(false);
        }
    }

    /// <summary>Espera antes del reintento numero dado, con un poco de azar.</summary>
    /// <remarks>
    /// El azar evita que todos los programas del mundo vuelvan a llamar a la vez al mismo
    /// nodo en cuanto se levanta.
    /// </remarks>
    private TimeSpan EsperaDeReintento(int intento)
    {
        var factor = Math.Pow(_opciones.FactorDeReintento, Math.Max(0, intento - 1));
        var ms = _opciones.EsperaPrimerReintento.TotalMilliseconds * factor;
        ms = Math.Min(ms, _opciones.EsperaMaximaReintento.TotalMilliseconds);
        lock (_azar)
        {
            ms *= 0.8 + (_azar.NextDouble() * 0.4);
        }
        return TimeSpan.FromMilliseconds(Math.Max(1, ms));
    }

    private void Cerrar()
    {
        var flujo = _flujo;
        _flujo = null;
        var cliente = _cliente;
        _cliente = null;

        try { flujo?.Dispose(); } catch (ObjectDisposedException) { /* ya estaba cerrado */ }
        try { cliente?.Dispose(); } catch (ObjectDisposedException) { /* ya estaba cerrado */ }

        _filtro.Reiniciar();
        _fase = Fase.EsperandoAviso;
    }

    private void CambiarEstado(EstadoDeConexion nuevo)
    {
        if (_estado == nuevo) return;
        _estado = nuevo;
        EstadoCambiado?.Invoke(this, nuevo);
    }

    private void Avisar(string linea)
    {
        if (linea.Length > 0) LineaRecibida?.Invoke(this, linea);
    }

    /// <summary>El nodo esta pidiendo el indicativo.</summary>
    [GeneratedRegex(
        @"(login|call ?sign|enter your call|your call|please enter)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PideIndicativo();

    /// <summary>El nodo esta pidiendo la contrasenia.</summary>
    [GeneratedRegex(@"(password|passwd)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PideContrasena();

    /// <summary>El nodo ha rechazado el acceso y reintentar no lo va a arreglar.</summary>
    [GeneratedRegex(
        @"(invalid call|not a valid call|callsign not|not registered|rejected|denied|banned|is not allowed)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Rechazo();

    private static bool EsRechazo(string linea) => Rechazo().IsMatch(linea);
}
