using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Idiomas;
using Nodisla.Cuaderno.Radio.Ptt;

namespace Nodisla.Cuaderno.Servidores;

/// <summary>
/// La radio del Cuaderno vista por los programas externos: lo unico que tocan los dos
/// servidores, rigctld y TCI.
/// </summary>
/// <remarks>
/// <para>
/// El Cuaderno sigue siendo el dueño del equipo. Aqui se decide que puede hacer un cliente:
/// </para>
/// <list type="bullet">
/// <item>Leer siempre. Cambiar frecuencia, modo, VFO, split y potencia solo con «control»
/// permitido y nunca con el equipo en el aire.</item>
/// <item>El PTT de un cliente es una FUENTE de PTT mas, con su nombre: necesita el interruptor
/// general de TX externa y el pestillo «Permitir transmitir en esta sesion», y despues pasa por
/// el vigilante con todas sus salvaguardas (plan de banda, ROE, potencia maxima, dueño, no
/// rearmar y tiempo maximo).</item>
/// <item>Si el cliente se desconecta, se calla mas de <see cref="VentanaDeSilencio"/>, se cierra
/// el pestillo o se apaga la TX externa, el PTT se baja en el acto.</item>
/// <item>Cada peticion de transmitir queda en el registro, con quien la pidio.</item>
/// </list>
/// </remarks>
public sealed class RadioCompartida : IAsyncDisposable
{
    /// <summary>Peticiones de TX que se guardan en memoria para la pantalla.</summary>
    public const int PeticionesGuardadas = 100;

    private static readonly TimeSpan PasoDeVigilancia = TimeSpan.FromMilliseconds(200);

    private readonly IControlEquipo _control;
    private readonly IVigilantePtt _vigilante;
    private readonly Func<bool> _pestilloAbierto;
    private readonly Func<Frecuencia, int?> _potenciaMaximaDeBanda;
    private readonly ILogger _registro;
    private readonly TimeProvider _reloj;
    private readonly object _candado = new();
    private readonly List<ClienteExterno> _clientes = [];
    private readonly LinkedList<PeticionDeTx> _peticiones = new();
    private readonly SemaphoreSlim _turnoDelPtt = new(1, 1);
    private readonly IDisposable _altaDeFuente;
    private readonly Timer _vigilancia;
    private int _vigilando;
    private OpcionesDeServidores _opciones = new();
    private bool _desechada;

    /// <summary>Crea la radio compartida.</summary>
    /// <param name="control">El control del equipo del Cuaderno (el conmutable).</param>
    /// <param name="vigilante">El vigilante del PTT: el unico camino al aire.</param>
    /// <param name="pestilloAbierto">«Permitir transmitir en esta sesion» marcado.</param>
    /// <param name="potenciaMaximaDeBanda">Potencia maxima (W) de la banda de una frecuencia, o nula.</param>
    /// <param name="registro">Donde se anota; nulo, en ninguna parte.</param>
    /// <param name="reloj">Reloj; nulo, el del sistema.</param>
    public RadioCompartida(
        IControlEquipo control,
        IVigilantePtt vigilante,
        Func<bool> pestilloAbierto,
        Func<Frecuencia, int?>? potenciaMaximaDeBanda = null,
        ILogger? registro = null,
        TimeProvider? reloj = null)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(vigilante);
        ArgumentNullException.ThrowIfNull(pestilloAbierto);
        _control = control;
        _vigilante = vigilante;
        _pestilloAbierto = pestilloAbierto;
        _potenciaMaximaDeBanda = potenciaMaximaDeBanda ?? (_ => null);
        _registro = registro ?? NullLogger.Instance;
        _reloj = reloj ?? TimeProvider.System;

        // Una sola fuente para todos los clientes: tras un corte de seguridad, el vigilante no
        // rearma mientras algun cliente siga pidiendo PTT.
        _altaDeFuente = vigilante.RegistrarFuente(Textos.T("Servicios.Servidor.Fuente"), AlgunoPideTx);
        _vigilancia = new Timer(_ => _ = VigilarAsync(), null, PasoDeVigilancia, PasoDeVigilancia);
    }

    /// <summary>
    /// Lo que puede estar callado un cliente con el PTT arriba antes de bajarselo. WSJT-X sondea
    /// cada segundo; diez sin oir nada es que se ha colgado.
    /// </summary>
    public TimeSpan VentanaDeSilencio { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Los ajustes en uso.</summary>
    public OpcionesDeServidores Opciones => Volatile.Read(ref _opciones);

    /// <summary>El control del equipo.</summary>
    public IControlEquipo Control => _control;

    /// <summary>El equipo de verdad que hay detras del conmutable.</summary>
    public IControlEquipo Real => (_control as IControlEquipoConmutable)?.Actual ?? _control;

    /// <summary>El estado del equipo.</summary>
    public EstadoDelEquipo Estado => _control.Estado;

    /// <summary>Si el equipo esta en el aire, por quien sea.</summary>
    public bool EnElAire => _vigilante.EnAntena || _control.Estado.Transmitiendo;

    /// <summary>Salta cuando entra, sale o cambia un cliente.</summary>
    public event EventHandler? ClientesCambiados;

    /// <summary>Salta con cada peticion de transmitir.</summary>
    public event EventHandler<PeticionDeTx>? TxPedida;

    /// <summary>Los clientes conectados.</summary>
    public IReadOnlyList<FotoDeCliente> Clientes
    {
        get
        {
            lock (_candado) return [.. _clientes.Select(c => c.Foto())];
        }
    }

    /// <summary>Las ultimas peticiones de transmitir, la mas reciente primero.</summary>
    public IReadOnlyList<PeticionDeTx> Peticiones
    {
        get
        {
            lock (_candado) return [.. _peticiones];
        }
    }

    /// <summary>Cambia los ajustes. Si se quita la TX externa, se baja el PTT de los clientes.</summary>
    /// <param name="opciones">Los nuevos.</param>
    public void Aplicar(OpcionesDeServidores opciones)
    {
        ArgumentNullException.ThrowIfNull(opciones);
        Volatile.Write(ref _opciones, opciones.Copiar().Acotar());
        _ = VigilarAsync();
    }

    // ── Clientes ──────────────────────────────────────────────────────────────────────────

    internal ClienteExterno? Alta(ProtocoloExterno protocolo, IPEndPoint remoto, Func<Task> cerrar)
    {
        var cliente = new ClienteExterno(protocolo, remoto, cerrar, _reloj.GetUtcNow());
        lock (_candado)
        {
            if (_desechada || _clientes.Count(c => c.Protocolo == protocolo) >= OpcionesDeServidores.MaximoDeClientes) return null;
            _clientes.Add(cliente);
        }

        _registro.LogInformation("Programa externo conectado: {Cliente}.", cliente.Nombre);
        Avisar();
        return cliente;
    }

    /// <summary>Da de baja a un cliente: si tenia el PTT arriba, se baja en el acto.</summary>
    internal async Task BajaAsync(ClienteExterno cliente)
    {
        bool estaba;
        lock (_candado) estaba = _clientes.Remove(cliente);
        cliente.PideTx = false;
        await SoltarAsync(cliente, Textos.T("Servicios.Servidor.Suelta.Desconexion")).ConfigureAwait(false);
        if (estaba)
        {
            _registro.LogInformation("Programa externo desconectado: {Cliente}.", cliente.Nombre);
            Avisar();
        }
    }

    /// <summary>Echa a un cliente (boton «Desconectar» de la pantalla).</summary>
    /// <param name="id">El cliente.</param>
    /// <returns>Verdadero si estaba.</returns>
    public async Task<bool> DesconectarAsync(Guid id)
    {
        ClienteExterno? cliente;
        lock (_candado) cliente = _clientes.Find(c => c.Id == id);
        if (cliente is null) return false;

        // Primero el PTT, despues la conexion.
        cliente.PideTx = false;
        await SoltarAsync(cliente, Textos.T("Servicios.Servidor.Suelta.Expulsado")).ConfigureAwait(false);
        try
        {
            await cliente.Cerrar().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _registro.LogDebug(ex, "Al cerrar la conexion de {Cliente}.", cliente.Nombre);
        }

        await BajaAsync(cliente).ConfigureAwait(false);
        return true;
    }

    /// <summary>Anota una orden del cliente: cuenta como latido.</summary>
    internal void Orden(ClienteExterno cliente, Respuesta respuesta)
    {
        cliente.Actividad();
        cliente.Contar(!respuesta.EsHecha && respuesta.Resultado != ResultadoDeOrden.NoDisponible);
        if (cliente.Transmision is { } tx && tx.EnAntena) tx.Latir();
    }

    // ── Lectura ───────────────────────────────────────────────────────────────────────────

    /// <summary>Los VFO, si el equipo tiene dos.</summary>
    public EstadoDeLosVfos? Vfos => (Real as IEquipoConDosVfos)?.Vfos;

    /// <summary>El VFO activo.</summary>
    public NombreDeVfo VfoActivo
    {
        get
        {
            if (Vfos is { } v && (v.A.EsElActivo || v.B.EsElActivo)) return v.B.EsElActivo ? NombreDeVfo.B : NombreDeVfo.A;
            return Estado.Vfo?.Trim().ToUpperInvariant() is "B" or "VFOB" or "VFO-B" ? NombreDeVfo.B : NombreDeVfo.A;
        }
    }

    /// <summary>Si el equipo esta en split.</summary>
    public bool Split => Vfos?.Split ?? false;

    /// <summary>El VFO por el que se transmite.</summary>
    public NombreDeVfo VfoDeTransmision => Split && Vfos is { } v ? v.DeTransmision.Nombre : VfoActivo;

    /// <summary>Frecuencia y modo de un VFO (si no hay dos, los del equipo).</summary>
    /// <param name="vfo">El VFO.</param>
    /// <returns>Frecuencia, modo y ancho.</returns>
    public (Frecuencia Frecuencia, Modo Modo, int? Ancho) DelVfo(NombreDeVfo vfo)
    {
        if (Vfos is { } v)
        {
            var uno = vfo == NombreDeVfo.A ? v.A : v.B;
            if (!uno.Frecuencia.EsCero) return (uno.Frecuencia, uno.Modo.EsVacio ? Estado.Modo : uno.Modo, uno.AnchoDeFiltroHz);
        }

        return (Estado.Frecuencia, Estado.Modo, null);
    }

    /// <summary>La potencia maxima del equipo en vatios, si se sabe.</summary>
    public double? PotenciaMaximaDelEquipo =>
        Real is IEquipoAvanzado a && a.Mandos.Contains(MandoDeEquipo.Potencia) ? a.Rango(MandoDeEquipo.Potencia)?.Maximo : null;

    /// <summary>Lee la potencia configurada, en vatios.</summary>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <returns>Los vatios, o nulo si el equipo no lo dice.</returns>
    public async Task<double?> LeerPotenciaAsync(CancellationToken ct = default)
    {
        if (Real is IEquipoAvanzado a && a.Mandos.Contains(MandoDeEquipo.Potencia))
        {
            try
            {
                return await a.LeerMandoAsync(MandoDeEquipo.Potencia, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _registro.LogDebug(ex, "No se pudo leer la potencia para un programa externo.");
            }
        }

        return Estado.PotenciaVatios;
    }

    // ── Cambios en la radio ───────────────────────────────────────────────────────────────

    /// <summary>Pone la frecuencia de un VFO (nulo: el activo).</summary>
    public Task<Respuesta> PonerFrecuenciaAsync(ClienteExterno cliente, Frecuencia frecuencia, NombreDeVfo? vfo = null, CancellationToken ct = default)
    {
        if (frecuencia.EsCero || frecuencia.Megahercios > 2_000m) return Task.FromResult(new Respuesta(ResultadoDeOrden.Invalido));
        return CambiarAsync(cliente, async () =>
        {
            if (vfo is { } nombre && nombre != VfoActivo)
            {
                if (Real is not IEquipoConDosVfos dos) return new Respuesta(ResultadoDeOrden.NoDisponible, Textos.T("Servicios.Servidor.NoDisponible"));
                await dos.PonerFrecuenciaDeAsync(nombre, frecuencia, ct).ConfigureAwait(false);
            }
            else
            {
                await _control.PonerFrecuenciaAsync(frecuencia, ct).ConfigureAwait(false);
            }

            return Respuesta.Hecha;
        });
    }

    /// <summary>Pone el modo de un VFO (nulo: el activo).</summary>
    public Task<Respuesta> PonerModoAsync(ClienteExterno cliente, Modo modo, NombreDeVfo? vfo = null, CancellationToken ct = default)
    {
        if (modo.EsVacio) return Task.FromResult(new Respuesta(ResultadoDeOrden.Invalido));
        return CambiarAsync(cliente, async () =>
        {
            if (vfo is { } nombre && nombre != VfoActivo)
            {
                if (Real is not IEquipoConDosVfos dos) return new Respuesta(ResultadoDeOrden.NoDisponible, Textos.T("Servicios.Servidor.NoDisponible"));
                await dos.PonerModoDeAsync(nombre, modo, ct).ConfigureAwait(false);
            }
            else
            {
                await _control.PonerModoAsync(modo, ct).ConfigureAwait(false);
            }

            return Respuesta.Hecha;
        });
    }

    /// <summary>Cambia el VFO activo.</summary>
    public Task<Respuesta> PonerVfoAsync(ClienteExterno cliente, NombreDeVfo vfo, CancellationToken ct = default)
    {
        if (vfo == VfoActivo) return Task.FromResult(Respuesta.Hecha);
        return CambiarAsync(cliente, async () =>
        {
            if (Real is not IEquipoConDosVfos dos) return new Respuesta(ResultadoDeOrden.NoDisponible, Textos.T("Servicios.Servidor.NoDisponible"));
            await dos.PonerVfoActivoAsync(vfo, ct).ConfigureAwait(false);
            return Respuesta.Hecha;
        });
    }

    /// <summary>Pone o quita el split.</summary>
    public Task<Respuesta> PonerSplitAsync(ClienteExterno cliente, bool split, CancellationToken ct = default)
    {
        if (split == Split) return Task.FromResult(Respuesta.Hecha);
        return CambiarAsync(cliente, async () =>
        {
            if (Real is not IEquipoAvanzado a || !a.Mandos.Contains(MandoDeEquipo.Split))
            {
                return new Respuesta(ResultadoDeOrden.NoDisponible, Textos.T("Servicios.Servidor.NoDisponible"));
            }

            await a.EscribirMandoAsync(MandoDeEquipo.Split, split ? 1 : 0, ct).ConfigureAwait(false);
            return Respuesta.Hecha;
        });
    }

    /// <summary>
    /// Pone la potencia, en vatios, recortada a la maxima de la banda en la que esta el equipo.
    /// </summary>
    public Task<Respuesta> PonerPotenciaAsync(ClienteExterno cliente, double vatios, CancellationToken ct = default)
    {
        if (!double.IsFinite(vatios) || vatios < 0) return Task.FromResult(new Respuesta(ResultadoDeOrden.Invalido));
        return CambiarAsync(cliente, async () =>
        {
            if (Real is not IEquipoAvanzado a || !a.Mandos.Contains(MandoDeEquipo.Potencia))
            {
                return new Respuesta(ResultadoDeOrden.NoDisponible, Textos.T("Servicios.Servidor.NoDisponible"));
            }

            var rango = a.Rango(MandoDeEquipo.Potencia);
            var valor = rango is null ? vatios : Math.Clamp(vatios, rango.Minimo, rango.Maximo);
            if (_potenciaMaximaDeBanda(DelVfo(VfoDeTransmision).Frecuencia) is { } maximo && valor > maximo)
            {
                _registro.LogInformation("{Cliente} pide {Pedida} W; se deja en {Maximo} W, el maximo de la banda.", cliente.Nombre, valor, maximo);
                valor = maximo;
            }

            if (rango is { Paso: > 0 }) valor = Math.Round(valor / rango.Paso) * rango.Paso;
            await a.EscribirMandoAsync(MandoDeEquipo.Potencia, valor, ct).ConfigureAwait(false);
            return Respuesta.Hecha;
        });
    }

    private async Task<Respuesta> CambiarAsync(ClienteExterno cliente, Func<Task<Respuesta>> cambio)
    {
        if (!Opciones.PermitirControl) return new Respuesta(ResultadoDeOrden.Rechazado, Textos.T("Servicios.Servidor.ControlApagado"));
        if (!Estado.Conectado) return new Respuesta(ResultadoDeOrden.Rechazado, Textos.T("Servicios.Servidor.SinEquipo"));

        // Con el equipo en el aire no se mueve nada: el plan de banda se miro al subir el PTT.
        if (EnElAire) return new Respuesta(ResultadoDeOrden.Rechazado, Textos.T("Servicios.Servidor.EnElAire"));

        try
        {
            return await cambio().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _registro.LogWarning(ex, "Orden de {Cliente} fallida.", cliente.Nombre);
            return new Respuesta(ResultadoDeOrden.Fallo, ex.Message);
        }
    }

    // ── PTT ───────────────────────────────────────────────────────────────────────────────

    /// <summary>Un cliente pide subir o bajar el PTT.</summary>
    /// <param name="cliente">El cliente.</param>
    /// <param name="transmitir">Subir (verdadero) o bajar.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <returns>El resultado.</returns>
    public async Task<Respuesta> PttAsync(ClienteExterno cliente, bool transmitir, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(cliente);
        if (!transmitir)
        {
            // Bajar se puede siempre, pero solo lo propio: el PTT de otra fuente no lo suelta un cliente.
            cliente.PideTx = false;
            await SoltarAsync(cliente, Textos.T("Servicios.Servidor.Suelta.Normal")).ConfigureAwait(false);
            return Respuesta.Hecha;
        }

        await _turnoDelPtt.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (cliente.Transmision is { EnAntena: true }) return Respuesta.Hecha;

            var opciones = Opciones;
            string? porque = null;
            if (!opciones.PermitirTx) porque = Textos.T("Servicios.Servidor.TxApagada");
            else if (!PestilloAbierto()) porque = Textos.T("Servicios.Servidor.PestilloCerrado");
            if (porque is not null) return Anotar(cliente, false, porque);

            // Desde aqui el cliente cuenta como fuente pulsada hasta que mande soltar.
            cliente.PideTx = true;

            if (_vigilante.EnAntena)
            {
                return Anotar(cliente, false, Textos.F("Servicios.Servidor.OtraFuente", _vigilante.FuenteEnAntena ?? "?"));
            }

            try
            {
                var tx = await _vigilante.PedirAntenaAsync(cliente.Nombre, ct).ConfigureAwait(false);
                cliente.Transmision = tx;
                cliente.Actividad();
                Avisar();
                return Anotar(cliente, true, Textos.T("Servicios.Servidor.Concedida"));
            }
            catch (TransmisionBloqueadaException bloqueo)
            {
                return Anotar(cliente, false, bloqueo.Message);
            }
            catch (InvalidOperationException ocupado)
            {
                return Anotar(cliente, false, ocupado.Message);
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
            {
                _registro.LogWarning(ex, "No se pudo dar el PTT a {Cliente}.", cliente.Nombre);
                Anotar(cliente, false, ex.Message);
                return new Respuesta(ResultadoDeOrden.Fallo, ex.Message);
            }
        }
        finally
        {
            _turnoDelPtt.Release();
        }
    }

    private bool PestilloAbierto()
    {
        try
        {
            return _pestilloAbierto();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Si no se sabe, cerrado: lo prudente.
            _registro.LogWarning(ex, "No se ha podido leer el pestillo de transmitir.");
            return false;
        }
    }

    private Respuesta Anotar(ClienteExterno cliente, bool concedida, string detalle)
    {
        var estado = Estado;
        var (frecuencia, modo, _) = DelVfo(VfoDeTransmision);
        if (frecuencia.EsCero) frecuencia = estado.Frecuencia;
        var peticion = new PeticionDeTx(_reloj.GetUtcNow(), cliente.Nombre, frecuencia, modo, concedida, detalle);
        lock (_candado)
        {
            _peticiones.AddFirst(peticion);
            while (_peticiones.Count > PeticionesGuardadas) _peticiones.RemoveLast();
        }

        if (concedida)
        {
            _registro.LogWarning("PTT concedido a {Cliente} en {Frecuencia} {Modo}.", cliente.Nombre, frecuencia, modo);
        }
        else
        {
            _registro.LogWarning("PTT denegado a {Cliente} en {Frecuencia} {Modo}: {Motivo}", cliente.Nombre, frecuencia, modo, detalle);
        }

        TxPedida?.Invoke(this, peticion);
        return concedida ? Respuesta.Hecha : new Respuesta(ResultadoDeOrden.Rechazado, detalle);
    }

    private async Task SoltarAsync(ClienteExterno cliente, string porque)
    {
        var tx = Interlocked.Exchange(ref cliente.Transmision, null);
        if (tx is null) return;
        var estabaEnElAire = tx.EnAntena;
        try
        {
            await tx.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // El vigilante ya ha probado las vias de emergencia y avisado si el PTT se ha pegado.
            _registro.LogError(ex, "Al bajar el PTT de {Cliente}.", cliente.Nombre);
        }

        if (estabaEnElAire)
        {
            _registro.LogWarning("PTT de {Cliente} bajado: {Motivo}.", cliente.Nombre, porque);
            lock (_candado)
            {
                _peticiones.AddFirst(new PeticionDeTx(_reloj.GetUtcNow(), cliente.Nombre, Estado.Frecuencia, Estado.Modo, false, porque));
                while (_peticiones.Count > PeticionesGuardadas) _peticiones.RemoveLast();
            }
        }

        Avisar();
    }

    private bool AlgunoPideTx()
    {
        lock (_candado) return _clientes.Exists(c => c.PideTx);
    }

    /// <summary>
    /// Cada 200 ms: late por los clientes que transmiten y estan vivos, y baja el PTT de los que
    /// se han callado, de los que ha cortado el vigilante y de todos si se cierra el pestillo o se
    /// apaga la TX externa.
    /// </summary>
    private async Task VigilarAsync()
    {
        if (Interlocked.Exchange(ref _vigilando, 1) != 0) return;
        try
        {
            List<ClienteExterno> enTx;
            lock (_candado) enTx = _clientes.Where(c => c.Transmision is not null).ToList();
            if (enTx.Count == 0) return;

            var permitido = Opciones.PermitirTx && PestilloAbierto();
            foreach (var cliente in enTx)
            {
                var tx = cliente.Transmision;
                if (tx is null) continue;
                string? porque = null;
                if (!tx.EnAntena) porque = Textos.T("Servicios.Servidor.Suelta.Vigilante");
                else if (!permitido) porque = Textos.T("Servicios.Servidor.Suelta.Permiso");
                else if (Stopwatch.GetElapsedTime(cliente.UltimaActividad) > VentanaDeSilencio) porque = Textos.T("Servicios.Servidor.Suelta.Silencio");

                if (porque is null)
                {
                    tx.Latir();
                }
                else
                {
                    await SoltarAsync(cliente, porque).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _registro.LogError(ex, "Fallo vigilando el PTT de los programas externos.");
        }
        finally
        {
            Volatile.Write(ref _vigilando, 0);
        }
    }

    private void Avisar()
    {
        try
        {
            ClientesCambiados?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _registro.LogDebug(ex, "Un oyente de los clientes externos ha fallado.");
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        List<ClienteExterno> todos;
        lock (_candado)
        {
            if (_desechada) return;
            _desechada = true;
            todos = [.. _clientes];
        }

        await _vigilancia.DisposeAsync().ConfigureAwait(false);
        foreach (var c in todos)
        {
            c.PideTx = false;
            await SoltarAsync(c, Textos.T("Servicios.Servidor.Suelta.Desconexion")).ConfigureAwait(false);
        }

        _altaDeFuente.Dispose();
    }
}
