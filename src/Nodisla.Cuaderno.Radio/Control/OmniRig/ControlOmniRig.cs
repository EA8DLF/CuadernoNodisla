using System.ComponentModel;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Radio.Ptt;

namespace Nodisla.Cuaderno.Radio.Control.OmniRig;

/// <summary>Ajustes del control por OmniRig.</summary>
public sealed class OpcionesOmniRig
{
    /// <summary>Cual de los dos equipos de OmniRig se usa: 1 o 2.</summary>
    public int NumeroDeEquipo { get; set; } = 1;

    /// <summary>Cada cuanto se pregunta a OmniRig como esta el equipo.</summary>
    public TimeSpan IntervaloDeSondeo { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Como traducir los modos entre el equipo y el cuaderno.</summary>
    public TraductorDeModos Traductor { get; set; } = TraductorDeModos.PorOmision;
}

/// <summary>
/// Control del equipo por OmniRig. Es la segunda via, para quien ya tenga OmniRig montado.
/// </summary>
/// <remarks>
/// OmniRig es un servidor COM fuera de proceso, asi que este control funciona desde una
/// aplicacion de 64 bits sin puentes ni recompilaciones: ver <see cref="OmniRigPorCom"/>.
/// La via preferida sigue siendo <c>rigctld</c>, que soporta muchos mas equipos y no depende
/// de un programa de terceros corriendo en la maquina.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class ControlOmniRig : IControlEquipo, IPttDirecto, ISueltaDeEmergenciaPtt
{
    private readonly OpcionesOmniRig _opciones;
    private readonly IOmniRigCrudo _omni;
    private readonly ILogger _registro;
    private readonly object _candado = new();

    private CancellationTokenSource? _ctsSondeo;
    private Task? _sondeo;
    private EstadoDelEquipo _estado = EstadoDelEquipo.Desconectado;
    private bool _desechado;

    /// <summary>Crea el control sobre un OmniRig concreto.</summary>
    /// <param name="omni">Acceso a OmniRig; si es nulo, se usa el COM de verdad.</param>
    /// <param name="opciones">Ajustes.</param>
    /// <param name="registro">Donde anotar lo que pasa.</param>
    public ControlOmniRig(
        IOmniRigCrudo? omni = null,
        OpcionesOmniRig? opciones = null,
        ILogger? registro = null)
    {
        _opciones = opciones ?? new OpcionesOmniRig();
        _omni = omni ?? new OmniRigPorCom(_opciones.NumeroDeEquipo);
        _registro = registro ?? NullLogger.Instance;

        ViasDeSuelta =
        [
            new ViaDeSuelta(
                "OmniRig: Tx = RX",
                _ =>
                {
                    _omni.Tx = ParametrosOmniRig.Rx;
                    return Task.CompletedTask;
                },
                () => _omni.Tx = ParametrosOmniRig.Rx),
            new ViaDeSuelta(
                "OmniRig: volver a abrir el objeto COM y Tx = RX",
                _ =>
                {
                    _omni.Abrir();
                    _omni.Tx = ParametrosOmniRig.Rx;
                    return Task.CompletedTask;
                },
                () =>
                {
                    _omni.Abrir();
                    _omni.Tx = ParametrosOmniRig.Rx;
                }),
        ];
    }

    /// <inheritdoc />
    public ViaDeControl Via => ViaDeControl.OmniRig;

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
    public Task ConectarAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_desechado, this);
        ct.ThrowIfCancellationRequested();

        _omni.Abrir();
        if (_omni.Estado != EstadoOmniRig.EnLinea)
        {
            _registro.LogWarning("OmniRig contesta pero el equipo no está en línea (estado {Estado}).", _omni.Estado);
        }

        LeerEstado();

        lock (_candado)
        {
            if (_sondeo is null)
            {
                _ctsSondeo = new CancellationTokenSource();
                _sondeo = Task.Run(() => SondearSiempreAsync(_ctsSondeo.Token), CancellationToken.None);
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
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
                _registro.LogDebug(ex, "El sondeo de OmniRig no terminó a tiempo.");
            }
        }

        cts?.Dispose();

        try
        {
            _omni.Tx = ParametrosOmniRig.Rx;
        }
        catch (Exception ex)
        {
            _registro.LogWarning(ex, "No se pudo bajar el PTT de OmniRig al desconectar.");
        }

        Actualizar(estado => estado with { Conectado = false, Transmitiendo = false });
    }

    /// <inheritdoc />
    public Task PonerFrecuenciaAsync(Frecuencia frecuencia, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _omni.Frecuencia = frecuencia.Hercios;
        LeerEstado();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task PonerModoAsync(Modo modo, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var nombre = _opciones.Traductor.AlEquipo(modo, Estado.Frecuencia)
            ?? throw new ArgumentException($"No sé cómo pedirle a OmniRig el modo {modo}.", nameof(modo));

        _omni.Modo = ANumeroDeOmniRig(nombre);
        LeerEstado();
        return Task.CompletedTask;
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
    Task IPttDirecto.PonerPttDirectoAsync(bool transmitir, CancellationToken ct)
    {
        _omni.Tx = transmitir ? ParametrosOmniRig.Tx : ParametrosOmniRig.Rx;
        Actualizar(estado => estado with { Transmitiendo = transmitir });
        return Task.CompletedTask;
    }

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
            _registro.LogWarning(ex, "Fallo al desconectar de OmniRig.");
        }

        _omni.Dispose();
    }

    /// <summary>Traduce el modo de OmniRig al nombre que entiende el traductor.</summary>
    /// <param name="valor">Constante <c>PM_</c> devuelta por OmniRig.</param>
    /// <returns>Nombre del modo al estilo Hamlib.</returns>
    public static string NombreDeModo(int valor) => valor switch
    {
        ParametrosOmniRig.SsbSuperior => "USB",
        ParametrosOmniRig.SsbInferior => "LSB",
        ParametrosOmniRig.CwSuperior or ParametrosOmniRig.CwInferior => "CW",
        ParametrosOmniRig.DatosSuperior => "PKTUSB",
        ParametrosOmniRig.DatosInferior => "PKTLSB",
        ParametrosOmniRig.Am => "AM",
        ParametrosOmniRig.Fm => "FM",
        _ => string.Empty,
    };

    /// <summary>Traduce el nombre del modo a la constante <c>PM_</c> de OmniRig.</summary>
    /// <param name="nombre">Nombre al estilo Hamlib, por ejemplo <c>USB</c>.</param>
    /// <returns>La constante correspondiente.</returns>
    /// <exception cref="ArgumentException">Si OmniRig no maneja ese modo.</exception>
    public static int ANumeroDeOmniRig(string nombre) => nombre.ToUpperInvariant() switch
    {
        "USB" => ParametrosOmniRig.SsbSuperior,
        "LSB" => ParametrosOmniRig.SsbInferior,
        "CW" => ParametrosOmniRig.CwSuperior,
        "PKTUSB" or "DATA-U" or "DIGU" => ParametrosOmniRig.DatosSuperior,
        "PKTLSB" or "DATA-L" or "DIGL" => ParametrosOmniRig.DatosInferior,
        "AM" => ParametrosOmniRig.Am,
        "FM" or "PKTFM" => ParametrosOmniRig.Fm,
        _ => throw new ArgumentException($"OmniRig no maneja el modo {nombre}.", nameof(nombre)),
    };

    private async Task SondearSiempreAsync(CancellationToken ct)
    {
        using var reloj = new PeriodicTimer(_opciones.IntervaloDeSondeo);
        while (true)
        {
            try
            {
                if (!await reloj.WaitForNextTickAsync(ct).ConfigureAwait(false))
                {
                    return;
                }

                LeerEstado();
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _registro.LogWarning(ex, "Fallo al sondear OmniRig.");
                Actualizar(estado => estado with { Conectado = false });
            }
        }
    }

    private void LeerEstado()
    {
        var enLinea = _omni.Estado == EstadoOmniRig.EnLinea;
        var hercios = enLinea ? _omni.Frecuencia : 0L;
        var modo = enLinea ? _opciones.Traductor.DesdeElEquipo(NombreDeModo(_omni.Modo)) : Modo.Vacio;
        var transmitiendo = _omni.Tx == ParametrosOmniRig.Tx;

        Actualizar(estado => estado with
        {
            Conectado = enLinea,
            Transmitiendo = transmitiendo,
            Frecuencia = hercios > 0 ? Frecuencia.DesdeHercios(hercios) : estado.Frecuencia,
            Modo = modo.EsVacio ? estado.Modo : modo,
            Vfo = _omni.TipoDeEquipo,
        });
    }

    private void Actualizar(Func<EstadoDelEquipo, EstadoDelEquipo> cambio)
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
    }
}
