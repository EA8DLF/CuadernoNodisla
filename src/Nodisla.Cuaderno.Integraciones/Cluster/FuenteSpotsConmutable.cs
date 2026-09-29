using Nodisla.Cuaderno.Aplicacion.Puertos;

namespace Nodisla.Cuaderno.Integraciones.Cluster;

/// <summary>
/// Fuente de anuncios que se puede cambiar por otra sin cerrar el programa.
/// </summary>
/// <remarks>
/// <para>
/// Es el mismo truco que el intermediario del equipo y por el mismo motivo: el seguimiento del
/// cluster se monta una vez al arrancar y se engancha a la fuente. Sin intermediario, cambiar
/// de nodo —o el indicativo con el que se entra, o la contrasena— obligaria a reiniciar.
/// </para>
/// <para>
/// Al sustituir se <b>desconecta y se suelta</b> la fuente anterior. Nunca se dejan dos
/// conexiones abiertas al mismo tiempo: los nodos cuentan las sesiones por indicativo y echan
/// a la segunda.
/// </para>
/// </remarks>
public sealed class FuenteSpotsConmutable : IFuenteSpots
{
    private readonly SemaphoreSlim _puerta = new(1, 1);

    private volatile IFuenteSpots _actual;
    private bool _desechado;

    /// <summary>Monta el intermediario sobre una fuente de partida.</summary>
    /// <param name="inicial">Fuente de partida.</param>
    public FuenteSpotsConmutable(IFuenteSpots inicial)
    {
        ArgumentNullException.ThrowIfNull(inicial);
        _actual = inicial;
        Enganchar(inicial);
    }

    /// <summary>Fuente que hay ahora mismo detras.</summary>
    public IFuenteSpots Actual => _actual;

    /// <inheritdoc />
    public string Nombre => _actual.Nombre;

    /// <inheritdoc />
    public EstadoDeConexion Estado => _actual.Estado;

    /// <inheritdoc />
    public event EventHandler<EstadoDeConexion>? EstadoCambiado;

    /// <inheritdoc />
    public event EventHandler<Spot>? SpotRecibido;

    /// <inheritdoc />
    public event EventHandler<string>? LineaRecibida;

    /// <summary>Salta cuando se ha cambiado la fuente, diciendo cual es la nueva.</summary>
    public event EventHandler<IFuenteSpots>? FuenteCambiada;

    /// <inheritdoc />
    public Task ConectarAsync(CancellationToken ct = default) => _actual.ConectarAsync(ct);

    /// <inheritdoc />
    public Task DesconectarAsync(CancellationToken ct = default) => _actual.DesconectarAsync(ct);

    /// <inheritdoc />
    public Task EnviarAsync(string orden, CancellationToken ct = default) => _actual.EnviarAsync(orden, ct);

    /// <summary>
    /// Pone otra fuente en lugar de la que hay, cerrando la anterior.
    /// </summary>
    /// <param name="nueva">Fuente que pasa a estar puesta.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <returns>La tarea de la sustitucion.</returns>
    public async Task SustituirAsync(IFuenteSpots nueva, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(nueva);
        ObjectDisposedException.ThrowIf(_desechado, this);

        if (ReferenceEquals(nueva, _actual)) return;

        await _puerta.WaitAsync(ct).ConfigureAwait(false);
        IFuenteSpots anterior;
        try
        {
            anterior = _actual;
            Desenganchar(anterior);
            _actual = nueva;
            Enganchar(nueva);
        }
        finally
        {
            _puerta.Release();
        }

        await CerrarConCuidadoAsync(anterior).ConfigureAwait(false);

        FuenteCambiada?.Invoke(this, nueva);
        EstadoCambiado?.Invoke(this, nueva.Estado);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_desechado) return;
        _desechado = true;

        var fuente = _actual;
        Desenganchar(fuente);
        await CerrarConCuidadoAsync(fuente).ConfigureAwait(false);
        _puerta.Dispose();
    }

    private static async Task CerrarConCuidadoAsync(IFuenteSpots fuente)
    {
        try
        {
            await fuente.DesconectarAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Un nodo que no deja despedirse no puede impedir cambiar de nodo.
        }

        try
        {
            await fuente.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Idem.
        }
    }

    private void Enganchar(IFuenteSpots fuente)
    {
        fuente.EstadoCambiado += AlCambiarElEstado;
        fuente.SpotRecibido += AlLlegarUnSpot;
        fuente.LineaRecibida += AlLlegarUnaLinea;
    }

    private void Desenganchar(IFuenteSpots fuente)
    {
        fuente.EstadoCambiado -= AlCambiarElEstado;
        fuente.SpotRecibido -= AlLlegarUnSpot;
        fuente.LineaRecibida -= AlLlegarUnaLinea;
    }

    private void AlCambiarElEstado(object? origen, EstadoDeConexion estado) =>
        EstadoCambiado?.Invoke(this, estado);

    private void AlLlegarUnSpot(object? origen, Spot spot) => SpotRecibido?.Invoke(this, spot);

    private void AlLlegarUnaLinea(object? origen, string linea) => LineaRecibida?.Invoke(this, linea);
}
