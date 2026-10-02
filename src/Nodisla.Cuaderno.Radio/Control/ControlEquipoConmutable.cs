using System.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Radio.Ptt;

using Nodisla.Cuaderno.Idiomas;

namespace Nodisla.Cuaderno.Radio.Control;

/// <summary>
/// Control de equipo que se puede cambiar por otro sin cerrar el programa.
/// </summary>
/// <remarks>
/// <para>
/// Es el <b>unico</b> <see cref="IControlEquipo"/> que ve el resto de la aplicacion. Detras hay
/// siempre uno de verdad —el del FT-710, el de Hamlib, el de OmniRig o el que no tiene equipo—
/// y este lo sustituye cuando el operador cambia la via en los ajustes.
/// </para>
/// <para>
/// <b>Sustituir es una maniobra delicada</b>, porque el que se va puede estar en antena. El
/// orden es siempre el mismo y no se salta: bajar el PTT del que se va, desconectarlo,
/// soltarlo, y solo entonces poner el nuevo. Si algo de eso falla se anota y se sigue: dejar el
/// control viejo puesto porque no se ha podido cerrar limpiamente seria peor.
/// </para>
/// <para>
/// El vigilante del PTT se engancha a este objeto y no al de detras, y le pregunta cada vez que
/// tiene que soltar —<see cref="ISueltaDeEmergenciaPtt.ViasDeSuelta"/> se consulta en el
/// momento, no se guarda—, asi que las vias de emergencia que se prueban son siempre las del
/// equipo que hay ahora.
/// </para>
/// </remarks>
public sealed class ControlEquipoConmutable
    : IControlEquipoConmutable, IPttDirecto, ISueltaDeEmergenciaPtt, IAvisaDePerdidaDeComunicacion
{
    private readonly ILogger _registro;
    private readonly SemaphoreSlim _puerta = new(1, 1);

    private volatile IControlEquipo _actual;
    private bool _desechado;

    /// <summary>Monta el intermediario sobre un control de partida.</summary>
    /// <param name="inicial">Control de partida; si es nulo, se empieza sin equipo.</param>
    /// <param name="registro">Donde anotar los cambios de via.</param>
    public ControlEquipoConmutable(IControlEquipo? inicial = null, ILogger? registro = null)
    {
        _registro = registro ?? NullLogger.Instance;
        _actual = inicial ?? new ControlNulo(_registro);
        Enganchar(_actual);
    }

    /// <inheritdoc />
    public IControlEquipo Actual => _actual;

    /// <inheritdoc />
    public ViaDeControl Via => _actual.Via;

    /// <inheritdoc />
    public EstadoDelEquipo Estado => _actual.Estado;

    /// <inheritdoc />
    public IReadOnlyList<ViaDeSuelta> ViasDeSuelta =>
        _actual is ISueltaDeEmergenciaPtt emergencia ? emergencia.ViasDeSuelta : [];

    /// <inheritdoc />
    public event EventHandler<EstadoDelEquipo>? EstadoCambiado;

    /// <inheritdoc />
    public event EventHandler<IControlEquipo>? ControlCambiado;

    /// <inheritdoc />
    public event EventHandler<string>? ComunicacionPerdida;

    /// <inheritdoc />
    public Task ConectarAsync(CancellationToken ct = default) => _actual.ConectarAsync(ct);

    /// <inheritdoc />
    public Task DesconectarAsync(CancellationToken ct = default) => _actual.DesconectarAsync(ct);

    /// <inheritdoc />
    public Task PonerFrecuenciaAsync(Frecuencia frecuencia, CancellationToken ct = default) =>
        _actual.PonerFrecuenciaAsync(frecuencia, ct);

    /// <inheritdoc />
    public Task PonerModoAsync(Modo modo, CancellationToken ct = default) =>
        _actual.PonerModoAsync(modo, ct);

    /// <summary>
    /// Subir el PTT por aqui sigue estando vetado, igual que en los controles de verdad.
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
        var control = _actual;

        if (control is IPttDirecto directo) return directo.PonerPttDirectoAsync(transmitir, ct);

        // Un control que no sepa subir el PTT no puede transmitir; bajarlo, en cambio, siempre
        // se intenta, que es lo que no puede fallar nunca.
        return transmitir
            ? Task.FromException(new InvalidOperationException(
                Textos.T("Servicios.Radio.SinPtt")))
            : control.PonerPttAsync(false, ct);
    }

    /// <summary>
    /// Pone otro control en lugar del que hay, cerrando el anterior con cuidado.
    /// </summary>
    /// <remarks>
    /// Quien llama deberia haber soltado antes el PTT por el vigilante, para que el vigilante
    /// se entere; aqui se vuelve a bajar de todas formas, porque el equipo que se va no puede
    /// quedarse en antena aunque el programa se haya hecho un lio con el estado.
    /// </remarks>
    /// <param name="nuevo">Control que pasa a estar puesto.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <returns>La tarea de la sustitucion.</returns>
    public async Task SustituirAsync(IControlEquipo nuevo, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(nuevo);
        ObjectDisposedException.ThrowIf(_desechado, this);

        if (ReferenceEquals(nuevo, _actual)) return;

        await _puerta.WaitAsync(ct).ConfigureAwait(false);
        IControlEquipo anterior;
        try
        {
            anterior = _actual;
            Desenganchar(anterior);
            _actual = nuevo;
            Enganchar(nuevo);
        }
        finally
        {
            _puerta.Release();
        }

        _registro.LogInformation(
            "Se cambia el control del equipo: de {Anterior} a {Nuevo}.",
            anterior.Via,
            nuevo.Via);

        // El anterior se cierra DESPUES de haber puesto el nuevo: si cerrar se atasca —un
        // puerto serie que no responde—, la aplicacion ya esta hablando con el control nuevo.
        await CerrarConCuidadoAsync(anterior).ConfigureAwait(false);

        ControlCambiado?.Invoke(this, nuevo);
        EstadoCambiado?.Invoke(this, nuevo.Estado);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_desechado) return;
        _desechado = true;

        var control = _actual;
        Desenganchar(control);
        await CerrarConCuidadoAsync(control).ConfigureAwait(false);
        _puerta.Dispose();
    }

    /// <summary>
    /// Baja el PTT, desconecta y suelta un control, sin dejar que un fallo pare el cambio.
    /// </summary>
    private async Task CerrarConCuidadoAsync(IControlEquipo control)
    {
        try
        {
            if (control is IPttDirecto directo)
            {
                await directo.PonerPttDirectoAsync(false, CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _registro.LogWarning(ex, "No se ha podido bajar el PTT del control que se sustituye.");
        }

        try
        {
            await control.DesconectarAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _registro.LogWarning(ex, "No se ha podido desconectar el control que se sustituye.");
        }

        try
        {
            await control.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _registro.LogWarning(ex, "No se ha podido soltar el control que se sustituye.");
        }
    }

    private void Enganchar(IControlEquipo control)
    {
        control.EstadoCambiado += AlCambiarElEstado;
        if (control is IAvisaDePerdidaDeComunicacion avisador)
        {
            avisador.ComunicacionPerdida += AlPerderseLaComunicacion;
        }
    }

    private void Desenganchar(IControlEquipo control)
    {
        control.EstadoCambiado -= AlCambiarElEstado;
        if (control is IAvisaDePerdidaDeComunicacion avisador)
        {
            avisador.ComunicacionPerdida -= AlPerderseLaComunicacion;
        }
    }

    /// <summary>
    /// Repite hacia fuera lo que dice el control de dentro, pero <b>como si lo dijera este</b>.
    /// </summary>
    /// <remarks>
    /// El origen se cambia a proposito: quien escucha se engancho a este objeto y tiene que
    /// poder comparar el origen con el suyo sin saber que hay un intermediario.
    /// </remarks>
    private void AlCambiarElEstado(object? origen, EstadoDelEquipo estado) =>
        EstadoCambiado?.Invoke(this, estado);

    private void AlPerderseLaComunicacion(object? origen, string porque) =>
        ComunicacionPerdida?.Invoke(this, porque);
}
