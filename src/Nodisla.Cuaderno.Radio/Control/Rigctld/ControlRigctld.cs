using System.ComponentModel;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Idiomas;
using Nodisla.Cuaderno.Radio.Ptt;

namespace Nodisla.Cuaderno.Radio.Control.Rigctld;

/// <summary>
/// Control del equipo por el demonio <c>rigctld</c> de Hamlib. Es la via preferida.
/// </summary>
/// <remarks>
/// El estado se lee del equipo, nunca se recuerda: el dial fisico manda tanto como el programa,
/// y si el operador mueve el VFO a mano el cuaderno tiene que enterarse. El sondeo va en su
/// propia tarea, no hace cola detras de las ordenes del operador y se salta las pasadas en las
/// que el canal esta ocupado, para no inundar el puerto serie.
/// </remarks>
public sealed class ControlRigctld : IControlEquipo, IPttDirecto, ISueltaDeEmergenciaPtt, IAvisaDePerdidaDeComunicacion, IManipuladorCw
{
    private readonly OpcionesRigctld _opciones;
    private readonly ILogger _registro;
    private readonly ClienteRigctld _cliente;
    private readonly LanzadorDeRigctld? _lanzador;
    private readonly object _candado = new();

    private CancellationTokenSource? _ctsSondeo;
    private Task? _sondeo;
    private EstadoDelEquipo _estado = EstadoDelEquipo.Desconectado;
    private bool _pttPedido;
    private bool _elEquipoInformaDelPtt = true;
    private int _pasadas;
    private bool _desechado;
    private bool _conectadoAlgunaVez;

    /// <summary>Crea el control con los ajustes indicados.</summary>
    /// <param name="opciones">Donde esta el demonio y como hablarle.</param>
    /// <param name="registro">Donde anotar lo que pasa.</param>
    public ControlRigctld(OpcionesRigctld? opciones = null, ILogger? registro = null)
    {
        _opciones = opciones ?? new OpcionesRigctld();
        _registro = registro ?? NullLogger.Instance;
        _cliente = new ClienteRigctld(_opciones.Maquina, _opciones.Puerto, _opciones.EsperaDeOrden, _registro);
        _lanzador = _opciones.LanzarElDemonio ? new LanzadorDeRigctld(_opciones, _registro) : null;
        ViasDeSuelta = ConstruirViasDeSuelta();
    }

    /// <inheritdoc />
    public ViaDeControl Via => ViaDeControl.Rigctld;

    /// <inheritdoc />
    public EstadoDelEquipo Estado
    {
        get
        {
            lock (_candado)
            {
                return _estado;
            }
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<ViaDeSuelta> ViasDeSuelta { get; }

    /// <inheritdoc />
    public event EventHandler<EstadoDelEquipo>? EstadoCambiado;

    /// <inheritdoc />
    public event EventHandler<string>? ComunicacionPerdida;

    /// <inheritdoc />
    public async Task ConectarAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_desechado, this);

        if (_lanzador is not null)
        {
            await _lanzador.AsegurarEnMarchaAsync(ct).ConfigureAwait(false);
        }

        await _cliente.ConectarAsync(ct).ConfigureAwait(false);
        Volatile.Write(ref _conectadoAlgunaVez, true);
        await LeerEstadoAsync(ct).ConfigureAwait(false);

        lock (_candado)
        {
            if (_sondeo is not null)
            {
                return;
            }

            _ctsSondeo = new CancellationTokenSource();
            _sondeo = Task.Run(() => SondearSiempreAsync(_ctsSondeo.Token), CancellationToken.None);
        }
    }

    /// <inheritdoc />
    /// <remarks>Antes de cerrar nada se baja el PTT: cerrar en antena es lo peor que se puede hacer.</remarks>
    public async Task DesconectarAsync(CancellationToken ct = default)
    {
        CancellationTokenSource? cts;
        Task? sondeo;
        lock (_candado)
        {
            cts = _ctsSondeo;
            sondeo = _sondeo;
            _ctsSondeo = null;
            _sondeo = null;
        }

        if (cts is not null)
        {
            await cts.CancelAsync().ConfigureAwait(false);
        }

        if (sondeo is not null)
        {
            try
            {
                await sondeo.WaitAsync(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _registro.LogDebug(ex, "El sondeo no terminó a tiempo.");
            }
        }

        cts?.Dispose();

        await BajarElPttComoSeaAsync(ct).ConfigureAwait(false);

        _cliente.Cerrar();
        _lanzador?.Matar();
        Actualizar(estado => estado with { Conectado = false, Transmitiendo = false });
    }

    /// <inheritdoc />
    public async Task PonerFrecuenciaAsync(Frecuencia frecuencia, CancellationToken ct = default)
    {
        var hercios = frecuencia.Hercios.ToString(CultureInfo.InvariantCulture);
        await MandarAsync($"\\set_freq {hercios}", ct).ConfigureAwait(false);
        await LeerEstadoAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task PonerModoAsync(Modo modo, CancellationToken ct = default)
    {
        var nombre = _opciones.Traductor.AlEquipo(modo, Estado.Frecuencia)
            ?? throw new ArgumentException(Textos.F("Servicios.Radio.EquipoModoDesconocido", modo), nameof(modo));

        // El ancho cero deja el que tenga el equipo para ese modo.
        await MandarAsync($"\\set_mode {nombre} 0", ct).ConfigureAwait(false);
        await LeerEstadoAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Subir el PTT por aqui se salta el vigilante, asi que se rechaza; bajarlo se permite siempre.
    /// </summary>
    /// <param name="transmitir">Verdadero para subir el PTT.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <returns>La tarea de la orden.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    Task IControlEquipo.PonerPttAsync(bool transmitir, CancellationToken ct) =>
        GuardiaDelPtt.Rechazar(transmitir, () => ((IPttDirecto)this).PonerPttDirectoAsync(false, ct));

    /// <inheritdoc />
    async Task IPttDirecto.PonerPttDirectoAsync(bool transmitir, CancellationToken ct)
    {
        if (!transmitir && NoHayNadaQueBajar())
        {
            _registro.LogDebug("rigctld no está conectado y no hay PTT pedido: no hay nada que bajar.");
            return;
        }

        // Telegrafia en marcha: se para el manipulador antes de bajar el PTT.
        if (!transmitir) await PararElManipuladorSiHaceFaltaAsync(ct).ConfigureAwait(false);

        await MandarAsync(transmitir ? "\\set_ptt 1" : "\\set_ptt 0", ct).ConfigureAwait(false);
        Volatile.Write(ref _pttPedido, transmitir);
        Actualizar(estado => estado with { Transmitiendo = transmitir });
    }

    // ── Telegrafia por el manipulador del equipo, a traves de Hamlib (IManipuladorCw) ────────
    //
    // «\send_morse TEXTO» (el resto de la linea es el texto), «\stop_morse» (Hamlib 4) y
    // «\set_level KEYSPD n». Que el equipo de detras sepa hacerlo depende de su controlador en
    // Hamlib: si no, rigctld contesta con un RPRT negativo y se dice.

    /// <summary>Caracteres por orden <c>send_morse</c>: trozos cortos para que parar corte pronto.</summary>
    public const int LetrasPorOrdenDeMorse = 30;

    private int _manipulando;

    /// <inheritdoc />
    public string? PorQueNoManipula => !Estado.Conectado ? Textos.T("Servicios.Radio.Cw.Desconectado") : null;

    /// <inheritdoc />
    public int LetrasPorOrden => LetrasPorOrdenDeMorse;

    /// <inheritdoc />
    public int WpmMinima => 5;

    /// <inheritdoc />
    public int WpmMaxima => 60;

    /// <inheritdoc />
    public async Task PonerVelocidadAsync(int wpm, CancellationToken ct = default)
    {
        if (PorQueNoManipula is { } porque) throw new InvalidOperationException(porque);
        await MandarAsync(string.Create(CultureInfo.InvariantCulture, $"\\set_level KEYSPD {Math.Clamp(wpm, WpmMinima, WpmMaxima)}"), ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ManipularAsync(string texto, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(texto);
        if (PorQueNoManipula is { } porque) throw new InvalidOperationException(porque);
        if (!Volatile.Read(ref _pttPedido))
        {
            throw new InvalidOperationException(Textos.T("Servicios.Radio.Cw.SinAntena"));
        }

        // Lo mismo que entiende el FT-710: letras, cifras y poca puntuacion. Nada de saltos de
        // linea, que cortarian la orden y mandarian el resto como otra.
        var limpio = Ft710.OrdenesFt710.TextoParaElManipulador(texto);
        if (limpio.Length == 0) return;
        Volatile.Write(ref _manipulando, 1);
        await MandarAsync("\\send_morse " + limpio, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task PararManipuladorAsync(CancellationToken ct = default)
    {
        if (_cliente.Conectado) await MandarAsync("\\stop_morse", ct).ConfigureAwait(false);
        Volatile.Write(ref _manipulando, 0);
    }

    private async Task PararElManipuladorSiHaceFaltaAsync(CancellationToken ct)
    {
        if (Volatile.Read(ref _manipulando) == 0 || !_cliente.Conectado) return;
        try
        {
            await MandarAsync("\\stop_morse", ct).ConfigureAwait(false);
            Volatile.Write(ref _manipulando, 0);
        }
        catch (Exception ex)
        {
            _registro.LogWarning(ex, "rigctld no ha parado el manipulador (stop_morse) antes de bajar el PTT.");
        }
    }

    /// <summary>
    /// Dice si una orden de bajar el PTT no tiene a quien llegar ni nada que bajar: sin
    /// conexion, sin PTT pedido y, o nunca conectado, o el control ya desechado (que al
    /// desecharse ya bajo el PTT por todas sus vias). Evita el «¡PTT PEGADO!» falso del
    /// vigilante que se cierra despues del control (01-10-2026). Con PTT pedido no se cumple.
    /// </summary>
    private bool NoHayNadaQueBajar() =>
        !_cliente.Conectado
        && !Volatile.Read(ref _pttPedido)
        && (Volatile.Read(ref _desechado) || !Volatile.Read(ref _conectadoAlgunaVez));

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_desechado)
        {
            return;
        }

        _desechado = true;

        try
        {
            await DesconectarAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _registro.LogWarning(ex, "Fallo al desconectar de rigctld.");
        }

        await _cliente.DisposeAsync().ConfigureAwait(false);
        _lanzador?.Dispose();
    }

    /// <summary>Lee del equipo frecuencia, modo y PTT y actualiza el estado.</summary>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <returns>El estado recien leido.</returns>
    public async Task<EstadoDelEquipo> LeerEstadoAsync(CancellationToken ct = default)
    {
        var frecuencia = Estado.Frecuencia;
        var modo = Estado.Modo;
        var vfo = Estado.Vfo;
        var transmitiendo = Volatile.Read(ref _pttPedido);
        double? senal = Estado.SenalRecibida;

        var respuestaFrecuencia = await _cliente.OrdenAsync("\\get_freq", ct).ConfigureAwait(false);
        if (respuestaFrecuencia.Bien && respuestaFrecuencia.Entero("Frequency") is { } hercios)
        {
            frecuencia = Frecuencia.DesdeHercios(hercios);
        }

        var respuestaModo = await _cliente.OrdenAsync("\\get_mode", ct).ConfigureAwait(false);
        if (respuestaModo.Bien && respuestaModo.Valor("Mode") is { } nombreModo)
        {
            modo = _opciones.Traductor.DesdeElEquipo(nombreModo);
        }

        if (Volatile.Read(ref _elEquipoInformaDelPtt))
        {
            var respuestaPtt = await _cliente.OrdenAsync("\\get_ptt", ct).ConfigureAwait(false);
            if (respuestaPtt.Bien && respuestaPtt.Entero("PTT") is { } valorPtt)
            {
                transmitiendo = valorPtt != 0;
            }
            else
            {
                // Hay equipos que no saben decir si estan en antena: se anota y se deja de preguntar.
                Volatile.Write(ref _elEquipoInformaDelPtt, false);
                _registro.LogInformation("El equipo no informa del estado del PTT; se usará lo que haya pedido el vigilante.");
            }
        }

        var respuestaVfo = await _cliente.OrdenAsync("\\get_vfo", ct).ConfigureAwait(false);
        if (respuestaVfo.Bien)
        {
            vfo = respuestaVfo.Valor("VFO") ?? vfo;
        }

        if (!transmitiendo)
        {
            var respuestaSenal = await _cliente.OrdenAsync("\\get_level STRENGTH", ct).ConfigureAwait(false);
            if (respuestaSenal.Bien
                && double.TryParse(
                    respuestaSenal.ValorSuelto(),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var decibelios))
            {
                // Hamlib da la señal en dB respecto a S9; cada punto S son 6 dB.
                senal = Math.Clamp(9d + (decibelios / 6d), 0d, 60d);
            }
        }

        return Actualizar(estado => estado with
        {
            Conectado = true,
            Transmitiendo = transmitiendo,
            Frecuencia = frecuencia,
            Modo = modo,
            Vfo = vfo,
            SenalRecibida = senal,
        });
    }

    private async Task SondearSiempreAsync(CancellationToken ct)
    {
        var esperaDeReconexion = _opciones.EsperaDeReconexion;
        using var reloj = new PeriodicTimer(_opciones.IntervaloDeSondeo);

        while (await EsperarSiguientePasadaAsync(reloj, ct).ConfigureAwait(false))
        {
            try
            {
                if (!_cliente.Conectado)
                {
                    Actualizar(estado => estado with { Conectado = false });
                    await Task.Delay(esperaDeReconexion, ct).ConfigureAwait(false);
                    await _cliente.ConectarAsync(ct).ConfigureAwait(false);
                    await LeerEstadoAsync(ct).ConfigureAwait(false);
                    esperaDeReconexion = _opciones.EsperaDeReconexion;
                    continue;
                }

                await SondearUnaPasadaAsync(ct).ConfigureAwait(false);
                esperaDeReconexion = _opciones.EsperaDeReconexion;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _registro.LogWarning(ex, "Se perdió la conexión con rigctld.");
                _cliente.Cerrar();
                var estabaEnAntena = Volatile.Read(ref _pttPedido);
                Volatile.Write(ref _pttPedido, false);
                Actualizar(estado => estado with { Conectado = false, Transmitiendo = false });

                if (estabaEnAntena)
                {
                    // Si se pierde el equipo en mitad de una transmision, el vigilante tiene que
                    // enterarse: no puede quedarse creyendo que sigue en antena.
                    ComunicacionPerdida?.Invoke(this, "se ha perdido la conexión con rigctld");
                }

                // Se va espaciando el reintento para no machacar la maquina si el demonio no vuelve.
                esperaDeReconexion = Doblar(esperaDeReconexion, _opciones.EsperaMaximaDeReconexion);
            }
        }
    }

    private static async Task<bool> EsperarSiguientePasadaAsync(PeriodicTimer reloj, CancellationToken ct)
    {
        try
        {
            return await reloj.WaitForNextTickAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private async Task SondearUnaPasadaAsync(CancellationToken ct)
    {
        var enAntena = Volatile.Read(ref _pttPedido);

        // Mientras se transmite solo se pregunta por el PTT: el dial no se mueve y el puerto
        // serie tiene cosas mejores que hacer.
        if (enAntena)
        {
            if (Volatile.Read(ref _elEquipoInformaDelPtt))
            {
                var respuesta = await _cliente.OrdenSiEstaLibreAsync("\\get_ptt", ct).ConfigureAwait(false);
                if (respuesta is { Bien: true } && respuesta.Entero("PTT") is { } valor)
                {
                    Actualizar(estado => estado with { Transmitiendo = valor != 0 });
                }
            }

            return;
        }

        var pasada = Interlocked.Increment(ref _pasadas);
        var frecuencia = await _cliente.OrdenSiEstaLibreAsync("\\get_freq", ct).ConfigureAwait(false);
        if (frecuencia is null)
        {
            // El canal estaba ocupado con una orden del operador: esta pasada se salta.
            return;
        }

        if (frecuencia.Bien && frecuencia.Entero("Frequency") is { } hercios)
        {
            var nueva = Frecuencia.DesdeHercios(hercios);
            Actualizar(estado => estado with { Conectado = true, Frecuencia = nueva });
        }

        var modo = await _cliente.OrdenSiEstaLibreAsync("\\get_mode", ct).ConfigureAwait(false);
        if (modo is { Bien: true } && modo.Valor("Mode") is { } nombre)
        {
            var traducido = _opciones.Traductor.DesdeElEquipo(nombre);
            Actualizar(estado => estado with { Modo = traducido });
        }

        if (Volatile.Read(ref _elEquipoInformaDelPtt))
        {
            var ptt = await _cliente.OrdenSiEstaLibreAsync("\\get_ptt", ct).ConfigureAwait(false);
            if (ptt is { Bien: true } && ptt.Entero("PTT") is { } valorPtt)
            {
                Actualizar(estado => estado with { Transmitiendo = valorPtt != 0 });
            }
        }

        // La señal cambia a cada instante y no vale para el cuaderno: basta mirarla de vez en cuando.
        if (pasada % 4 != 0)
        {
            return;
        }

        var senal = await _cliente.OrdenSiEstaLibreAsync("\\get_level STRENGTH", ct).ConfigureAwait(false);
        if (senal is { Bien: true }
            && double.TryParse(
                senal.ValorSuelto(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var decibelios))
        {
            var unidadesS = Math.Clamp(9d + (decibelios / 6d), 0d, 60d);
            Actualizar(estado => estado with { SenalRecibida = unidadesS });
        }
    }

    /// <summary>
    /// Baja el PTT antes de cerrar, pase lo que pase con el canal.
    /// </summary>
    /// <remarks>
    /// Cerrar con el equipo en antena es lo peor que puede hacer este programa, asi que no vale
    /// con intentarlo por la via normal y anotar el fallo: si el canal se ha roto —cancelar el
    /// sondeo puede dejar el socket inservible—, se prueban las vias de emergencia, las mismas
    /// que usa el vigilante.
    /// </remarks>
    private async Task BajarElPttComoSeaAsync(CancellationToken ct)
    {
        if (!_cliente.Conectado
            && !Volatile.Read(ref _conectadoAlgunaVez)
            && !Volatile.Read(ref _pttPedido))
        {
            // Nunca se conecto y no hay PTT pedido: no se abre un socket a nadie solo para
            // mandarle «set_ptt 0», igual que en el FT-710 (27-09-2026).
            return;
        }

        try
        {
            if (_cliente.Conectado)
            {
                await MandarAsync("\\set_ptt 0", ct).ConfigureAwait(false);
                return;
            }
        }
        catch (Exception ex)
        {
            _registro.LogWarning(ex, "No se pudo bajar el PTT por la vía normal antes de desconectar.");
        }

        foreach (var via in ViasDeSuelta)
        {
            try
            {
                using var espera = new CancellationTokenSource(_opciones.EsperaDeOrden);
                await via.SoltarAsync(espera.Token).ConfigureAwait(false);
                _registro.LogInformation("PTT abajo antes de desconectar por «{Via}».", via.Nombre);
                return;
            }
            catch (Exception ex)
            {
                _registro.LogWarning(ex, "Falló la vía «{Via}» al bajar el PTT antes de desconectar.", via.Nombre);
            }
        }

        _registro.LogError("No se ha podido bajar el PTT antes de desconectar por ninguna vía.");
    }

    private async Task<RespuestaRigctld> MandarAsync(string orden, CancellationToken ct)
    {
        var respuesta = await _cliente.OrdenAsync(orden, ct).ConfigureAwait(false);
        if (!respuesta.Bien)
        {
            throw new InvalidOperationException(
                Textos.F("Servicios.Radio.RigctldRechaza", orden, respuesta.Codigo));
        }

        return respuesta;
    }

    private EstadoDelEquipo Actualizar(Func<EstadoDelEquipo, EstadoDelEquipo> cambio)
    {
        EstadoDelEquipo nuevo;
        bool avisar;
        lock (_candado)
        {
            var anterior = _estado;
            nuevo = cambio(anterior) with { LeidoUtc = DateTimeOffset.UtcNow };
            _estado = nuevo;
            avisar = anterior with { LeidoUtc = default } != nuevo with { LeidoUtc = default };
        }

        if (avisar)
        {
            EstadoCambiado?.Invoke(this, nuevo);
        }

        return nuevo;
    }

    private static TimeSpan Doblar(TimeSpan espera, TimeSpan tope)
    {
        var doble = espera + espera;
        return doble > tope ? tope : doble;
    }

    private List<ViaDeSuelta> ConstruirViasDeSuelta()
    {
        var milisegundos = (int)Math.Clamp(_opciones.EsperaDeOrden.TotalMilliseconds, 250d, 5000d);
        var vias = new List<ViaDeSuelta>
        {
            new(
                "rigctld: socket nuevo",
                _ =>
                {
                    ClienteRigctld.SoltarPttDeGolpe(_opciones.Maquina, _opciones.Puerto, milisegundos);
                    return Task.CompletedTask;
                },
                () => ClienteRigctld.SoltarPttDeGolpe(_opciones.Maquina, _opciones.Puerto, milisegundos)),
            new(
                "rigctld: reconectar y bajar el PTT",
                async ct =>
                {
                    await _cliente.ConectarAsync(ct).ConfigureAwait(false);
                    await MandarAsync("\\set_ptt 0", ct).ConfigureAwait(false);
                }),
        };

        if (_lanzador is not null && _opciones.MatarElDemonioComoUltimoRecurso)
        {
            vias.Add(new ViaDeSuelta(
                "rigctld: matar el demonio para que suelte el puerto serie",
                _ =>
                {
                    _lanzador.Matar();
                    return Task.CompletedTask;
                },
                () => _lanzador.Matar()));
        }

        return vias;
    }
}
