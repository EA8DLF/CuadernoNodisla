using Nodisla.Cuaderno.Aplicacion.Puertos;
using Serilog;

namespace Nodisla.Cuaderno.Ui.Desarrollo;

/// <summary>
/// Vigilante del PTT provisional, para poder montar la pantalla de operacion antes de que
/// llegue el de verdad.
/// </summary>
/// <remarks>
/// <b>Esto no es el vigilante definitivo.</b> El bueno lo escribe quien hace la capa de radio
/// y tiene que estar probado antes de la primera transmision real: un PTT que no se suelta
/// quema la etapa final del equipo o el amplificador, y es el unico fallo de este programa que
/// destroza cosas fisicas.
///
/// Aun asi, este cumple el contrato entero —tiempo maximo, latido, excepcion, cierre y boton
/// de panico— porque la interfaz tiene que poder confiar en el desde el primer dia. Con el
/// equipo simulado detras no hay nada que quemar; el dia que detras haya un equipo de verdad,
/// aqui ya no puede estar esta clase.
/// </remarks>
public sealed class VigilantePttDeDesarrollo : IVigilantePtt, IAsyncDisposable
{
    private readonly IControlEquipo _equipo;
    private readonly SemaphoreSlim _turno = new(1, 1);
    private Transmision? _enCurso;

    /// <summary>Monta el vigilante sobre un equipo.</summary>
    /// <param name="equipo">Equipo al que se le pide y se le suelta el PTT.</param>
    public VigilantePttDeDesarrollo(IControlEquipo equipo)
    {
        ArgumentNullException.ThrowIfNull(equipo);
        _equipo = equipo;
    }

    /// <inheritdoc />
    public event EventHandler<MotivoDeSuelta>? PttSoltado;

    /// <inheritdoc />
    public TimeSpan TiempoMaximo { get; } = TimeSpan.FromMinutes(5);

    /// <inheritdoc />
    public TimeSpan TiempoSinLatido { get; } = TimeSpan.FromSeconds(10);

    /// <inheritdoc />
    public bool EnAntena => _enCurso is { EnAntena: true };

    /// <inheritdoc />
    public async Task<ITransmisionEnCurso> PedirAntenaAsync(string motivo, CancellationToken ct = default)
    {
        await _turno.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (EnAntena) throw new InvalidOperationException("Ya hay una transmisión en curso.");

            Log.Information("Se pide el PTT: {Motivo}.", motivo);
            await _equipo.PonerPttAsync(true, ct).ConfigureAwait(false);

            var transmision = new Transmision(this);
            _enCurso = transmision;
            return transmision;
        }
        finally
        {
            _turno.Release();
        }
    }

    /// <inheritdoc />
    public async Task SoltarYaAsync(MotivoDeSuelta motivo = MotivoDeSuelta.Panico)
    {
        var transmision = _enCurso;
        _enCurso = null;

        if (transmision is not null) transmision.Cerrar();

        try
        {
            // Sin testigo de cancelacion a proposito: soltar el PTT no se cancela nunca.
            await _equipo.PonerPttAsync(false).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se ha podido soltar el PTT. Motivo de la suelta: {Motivo}.", motivo);
            throw;
        }
        finally
        {
            Log.Information("PTT soltado. Motivo: {Motivo}.", motivo);
            PttSoltado?.Invoke(this, motivo);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        // Al cerrar la aplicacion el PTT se suelta pase lo que pase.
        if (EnAntena) await SoltarYaAsync(MotivoDeSuelta.Cierre).ConfigureAwait(false);
        _turno.Dispose();
    }

    /// <summary>Una transmision viva: mientras no se libere, el equipo esta en antena.</summary>
    private sealed class Transmision : ITransmisionEnCurso
    {
        private readonly VigilantePttDeDesarrollo _vigilante;
        private readonly CancellationTokenSource _corte = new();
        private readonly DateTimeOffset _comienzo = DateTimeOffset.UtcNow;
        private DateTimeOffset _ultimoLatido = DateTimeOffset.UtcNow;

        public Transmision(VigilantePttDeDesarrollo vigilante)
        {
            _vigilante = vigilante;
            _ = VigilarAsync(_corte.Token);
        }

        public bool EnAntena { get; private set; } = true;

        public void Latir() => _ultimoLatido = DateTimeOffset.UtcNow;

        public async ValueTask DisposeAsync()
        {
            if (!EnAntena) return;
            await _vigilante.SoltarYaAsync(MotivoDeSuelta.Normal).ConfigureAwait(false);
        }

        /// <summary>Cierra la transmision sin volver a llamar al vigilante.</summary>
        public void Cerrar()
        {
            EnAntena = false;
            _corte.Cancel();
            _corte.Dispose();
        }

        /// <summary>
        /// Mira cada segundo si hay que soltar: por tiempo agotado o porque quien transmite
        /// ha dejado de dar senales de vida.
        /// </summary>
        private async Task VigilarAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);

                    var ahora = DateTimeOffset.UtcNow;

                    if (ahora - _comienzo > _vigilante.TiempoMaximo)
                    {
                        await _vigilante.SoltarYaAsync(MotivoDeSuelta.TiempoAgotado).ConfigureAwait(false);
                        return;
                    }

                    if (ahora - _ultimoLatido > _vigilante.TiempoSinLatido)
                    {
                        await _vigilante.SoltarYaAsync(MotivoDeSuelta.SinLatido).ConfigureAwait(false);
                        return;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // La transmision termino como estaba previsto.
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Fallo vigilando el PTT; se suelta por si acaso.");
                await _vigilante.SoltarYaAsync(MotivoDeSuelta.Excepcion).ConfigureAwait(false);
            }
        }
    }
}
