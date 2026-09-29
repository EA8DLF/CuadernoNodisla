using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Integraciones.Fldigi;

/// <summary>
/// FLDigi como modem externo de modos de texto corrido.
/// </summary>
/// <remarks>
/// <para>
/// FLDigi no avisa de nada por su cuenta: hay que preguntarle. Este modem lo sondea cada
/// poco con <c>rx.get_data</c> —que devuelve solo lo nuevo y vacia su buffer— y con
/// <c>main.get_trx_state</c>, y convierte eso en los eventos del puerto. Si se deja de
/// sondear, lo que FLDigi reciba mientras tanto se pierde.
/// </para>
/// <para>
/// <b>Transmitir pasa por el vigilante del PTT.</b> FLDigi acciona su propio PTT: si el
/// programa se cuelga con la cola llena, el equipo se queda en antena y la etapa final se
/// quema. Por eso, cuando se le da un <see cref="IVigilantePtt"/>, este modem pide antena
/// antes de mandar texto, late mientras FLDigi transmite y la suelta en cuanto vuelve a
/// recibir. Sin vigilante funciona igual, pero entonces es quien llama el responsable de
/// haber pedido la antena.
/// </para>
/// </remarks>
public sealed class ModemFldigi : IModemExterno
{
    private readonly PuenteFldigi _puente;
    private readonly IVigilantePtt? _ptt;
    private readonly SemaphoreSlim _antena = new(1, 1);

    private CancellationTokenSource? _cancelacion;
    private Task? _sondeo;
    private ITransmisionEnCurso? _transmision;
    private EstadoDelModem _estado = EstadoDelModem.Desconectado;

    /// <summary>Crea el modem sin conectarlo.</summary>
    /// <param name="opciones">Donde escucha el servidor XML-RPC de FLDigi.</param>
    /// <param name="ptt">
    /// Vigilante del PTT bajo el que se transmite. Si es nulo, quien llame se encarga.
    /// </param>
    /// <param name="http">Cliente HTTP a reutilizar. Si no se da, se crea uno propio.</param>
    public ModemFldigi(
        OpcionesFldigi? opciones = null,
        IVigilantePtt? ptt = null,
        System.Net.Http.HttpClient? http = null)
    {
        Opciones = opciones ?? new OpcionesFldigi();
        _puente = new PuenteFldigi(Opciones, http);
        _ptt = ptt;
    }

    /// <summary>Donde escucha FLDigi.</summary>
    public OpcionesFldigi Opciones { get; }

    /// <summary>Acceso directo al puente, para lo que el puerto no cubre.</summary>
    public PuenteFldigi Puente => _puente;

    /// <inheritdoc/>
    public string Nombre => "FLDigi";

    /// <inheritdoc/>
    public bool EstaConectado => _estado != EstadoDelModem.Desconectado;

    /// <inheritdoc/>
    public EstadoDelModem Estado => _estado;

    /// <inheritdoc/>
    public event EventHandler<string>? TextoRecibido;

    /// <inheritdoc/>
    public event EventHandler<EstadoDelModem>? EstadoCambiado;

    /// <inheritdoc/>
    /// <remarks>
    /// Comprueba que FLDigi esta al otro lado y arranca el sondeo. Si no contesta, lanza
    /// <see cref="ErrorXmlRpc"/>: aqui no hay nada que reintentar solo, porque un modem que
    /// no esta es un programa que el operador no ha abierto.
    /// </remarks>
    public async Task ConectarAsync(CancellationToken ct = default)
    {
        if (_sondeo is not null) return;

        await _puente.VersionAsync(ct).ConfigureAwait(false);

        _cancelacion = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var testigo = _cancelacion.Token;
        CambiarEstado(await _puente.LeerEstadoAsync(ct).ConfigureAwait(false) switch
        {
            EstadoDeTransmision.Transmitiendo => EstadoDelModem.Transmitiendo,
            EstadoDeTransmision.Sintonizando => EstadoDelModem.Sintonizando,
            _ => EstadoDelModem.Recibiendo,
        });
        _sondeo = Task.Run(() => SondearAsync(testigo), CancellationToken.None);
    }

    /// <inheritdoc/>
    public async Task DesconectarAsync(CancellationToken ct = default)
    {
        var sondeo = _sondeo;
        _sondeo = null;

        if (_cancelacion is not null) await _cancelacion.CancelAsync().ConfigureAwait(false);
        if (sondeo is not null)
        {
            try
            {
                await sondeo.WaitAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Esperado: el sondeo termina porque se ha cancelado.
            }
        }

        _cancelacion?.Dispose();
        _cancelacion = null;
        await SoltarAntenaAsync().ConfigureAwait(false);
        CambiarEstado(EstadoDelModem.Desconectado);
    }

    /// <inheritdoc/>
    public Task<Frecuencia> LeerFrecuenciaAsync(CancellationToken ct = default) =>
        _puente.LeerFrecuenciaAsync(ct);

    /// <inheritdoc/>
    public async Task PonerFrecuenciaAsync(Frecuencia frecuencia, CancellationToken ct = default) =>
        await _puente.PonerFrecuenciaAsync(frecuencia, ct).ConfigureAwait(false);

    /// <inheritdoc/>
    public Task<Modo> LeerModoAsync(CancellationToken ct = default) => _puente.LeerModoAsync(ct);

    /// <inheritdoc/>
    public Task PonerModoAsync(Modo modo, CancellationToken ct = default) =>
        _puente.PonerModoAsync(modo, ct);

    /// <inheritdoc/>
    /// <remarks>
    /// FLDigi lista sus modems por nombre propio (<c>BPSK31</c>, <c>RTTY-45</c>); aqui solo
    /// salen los que se pueden traducir a un modo ADIF, porque los demas no se podrian
    /// registrar en el cuaderno.
    /// </remarks>
    public async Task<IReadOnlyList<Modo>> ModosDisponiblesAsync(CancellationToken ct = default)
    {
        var modems = await _puente.ListarModemsAsync(ct).ConfigureAwait(false);
        var modos = new List<Modo>();
        foreach (var m in modems)
        {
            var modo = PuenteFldigi.ModoDeModem(m);
            if (!modo.EsVacio && !modos.Contains(modo)) modos.Add(modo);
        }
        return modos;
    }

    /// <inheritdoc/>
    public async Task EnviarTextoAsync(string texto, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(texto);
        if (texto.Length == 0) return;

        await PedirAntenaAsync(ct).ConfigureAwait(false);
        try
        {
            await _puente.EnviarTextoAsync(texto, ct).ConfigureAwait(false);
            await _puente.TransmitirAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            // Si no se ha llegado a transmitir, la antena no se queda pedida.
            await SoltarAntenaAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task AbortarTransmisionAsync(CancellationToken ct = default)
    {
        try
        {
            await _puente.AbortarAsync(ct).ConfigureAwait(false);
            await _puente.VaciarTextoAsync(ct).ConfigureAwait(false);
            await _puente.RecibirAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            await SoltarAntenaAsync().ConfigureAwait(false);
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
        await _puente.DisposeAsync().ConfigureAwait(false);
        _antena.Dispose();
    }

    /// <summary>Sondea FLDigi hasta que se desconecta.</summary>
    private async Task SondearAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var texto = await _puente.LeerRecibidoAsync(ct).ConfigureAwait(false);
                if (texto.Length > 0) TextoRecibido?.Invoke(this, texto);

                var estado = await _puente.LeerEstadoAsync(ct).ConfigureAwait(false);
                await ApuntarEstadoAsync(estado).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (ErrorXmlRpc)
            {
                // FLDigi se ha cerrado o no contesta: se suelta la antena y se sigue
                // sondeando, que el operador puede volver a abrirlo.
                await SoltarAntenaAsync().ConfigureAwait(false);
                CambiarEstado(EstadoDelModem.Desconectado);
            }

            try
            {
                await Task.Delay(Opciones.Sondeo, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>Apunta el estado que dice FLDigi y mantiene o suelta la antena.</summary>
    private async Task ApuntarEstadoAsync(EstadoDeTransmision estado)
    {
        switch (estado)
        {
            case EstadoDeTransmision.Transmitiendo:
                CambiarEstado(EstadoDelModem.Transmitiendo);
                _transmision?.Latir();
                break;

            case EstadoDeTransmision.Sintonizando:
                CambiarEstado(EstadoDelModem.Sintonizando);
                _transmision?.Latir();
                break;

            case EstadoDeTransmision.Recibiendo:
                CambiarEstado(EstadoDelModem.Recibiendo);
                // FLDigi ha terminado de mandar la cola: ya no hace falta la antena.
                await SoltarAntenaAsync().ConfigureAwait(false);
                break;

            default:
                break;
        }
    }

    private async Task PedirAntenaAsync(CancellationToken ct)
    {
        if (_ptt is null) return;

        await _antena.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _transmision ??= await _ptt
                .PedirAntenaAsync($"{Nombre}: texto en cola", ct)
                .ConfigureAwait(false);
        }
        finally
        {
            _antena.Release();
        }
    }

    private async Task SoltarAntenaAsync()
    {
        if (_transmision is null) return;

        await _antena.WaitAsync().ConfigureAwait(false);
        try
        {
            var transmision = _transmision;
            _transmision = null;
            if (transmision is not null) await transmision.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            _antena.Release();
        }
    }

    private void CambiarEstado(EstadoDelModem nuevo)
    {
        if (_estado == nuevo) return;
        _estado = nuevo;
        EstadoCambiado?.Invoke(this, nuevo);
    }
}
