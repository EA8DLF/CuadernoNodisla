using Microsoft.Extensions.Logging;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Modos.Marco;

namespace Nodisla.Cuaderno.Modos.Wspr;

/// <summary>
/// WSPR en el modem propio.
/// </summary>
/// <remarks>
/// <para>
/// La ventana es el periodo completo de dos minutos que empieza en el minuto par, y la senal
/// arranca por convenio un segundo despues (<see cref="ParametrosWspr.ComienzoNominalSegundos"/>),
/// igual que en FT8 la ventana empieza en el segundo 0 y la senal en el 0,5. Asi una estacion
/// adelantada no se queda fuera, y las grabaciones de otros programas, que empiezan en el
/// minuto par, se decodifican tal cual.
/// </para>
/// <para>
/// El generador devuelve muestras; ponerlas en antena es cosa del modem y de su vigilante.
/// </para>
/// </remarks>
public sealed class ModoWspr : IModoDigital
{
    private readonly DecodificadorWspr _decodificador;

    /// <summary>Crea el modo con su decodificador.</summary>
    /// <param name="registro">Registro, si se quiere ver que pasa por dentro.</param>
    public ModoWspr(ILogger? registro = null)
        : this(new DecodificadorWspr(registro))
    {
    }

    /// <summary>Crea el modo sobre un decodificador ya configurado, para el banco.</summary>
    /// <param name="decodificador">El decodificador.</param>
    public ModoWspr(DecodificadorWspr decodificador)
    {
        ArgumentNullException.ThrowIfNull(decodificador);
        _decodificador = decodificador;
    }

    /// <summary>El decodificador, para consultarle los indicativos compuestos que conoce.</summary>
    public DecodificadorWspr Decodificador => _decodificador;

    /// <inheritdoc/>
    public double AmplitudDeSalida { get; set; } = IModoDigital.AmplitudDeSalidaPorDefecto;

    /// <inheritdoc/>
    public ModoDelModem Modo => ModoDelModem.Wspr;

    /// <inheritdoc/>
    public TimeSpan Periodo => TimeSpan.FromSeconds(ParametrosWspr.PeriodoSegundos);

    /// <inheritdoc/>
    public TimeSpan ArranqueDentroDelPeriodo => TimeSpan.Zero;

    /// <inheritdoc/>
    public TimeSpan ComienzoDeLaSenal => TimeSpan.FromSeconds(ParametrosWspr.ComienzoNominalSegundos);

    /// <inheritdoc/>
    public int FrecuenciaDeAnalisis => ParametrosWspr.FrecuenciaDeAnalisis;

    /// <inheritdoc/>
    /// <remarks>
    /// WSPR aprovecha el preludio: el decodificador sabe desde que segundo de la ventana viene la
    /// primera muestra y lo descuenta al medir el desfase, asi que una estacion adelantada no se
    /// pierde. Solo hace falta remuestrear a 12000.
    /// </remarks>
    public IReadOnlyList<DecodificacionPropia> DecodificarVentana(
        ReadOnlySpan<float> audio, int frecuenciaDeMuestreo, DateTimeOffset ventanaUtc, double segundosDelPrimerMuestreo, CancellationToken ct)
    {
        var muestras = frecuenciaDeMuestreo == FrecuenciaDeAnalisis
            ? audio
            : Senal.Remuestreador.Remuestrear(audio, frecuenciaDeMuestreo, FrecuenciaDeAnalisis);
        return _decodificador.Decodificar(muestras, ventanaUtc, segundosDelPrimerMuestreo, ct).Decodificaciones;
    }

    /// <inheritdoc/>
    public IReadOnlyList<DecodificacionPropia> Decodificar(ReadOnlySpan<float> audio, DateTimeOffset ventanaUtc, CancellationToken ct) =>
        _decodificador.Decodificar(audio, ventanaUtc, segundosDelPrimerMuestreo: 0, ct).Decodificaciones;

    /// <inheritdoc/>
    public float[] Generar(string mensaje, int tonoHz, int frecuenciaDeMuestreo)
    {
        if (!MensajeWspr.TryAnalizar(mensaje, out var m, out var motivo)) throw new FormatException(motivo);
        var tonos = CodificadorWspr.Tonos(m);
        return ModuladorWspr.Sintetizar(tonos, tonoHz, frecuenciaDeMuestreo, amplitud: AmplitudDeSalida);
    }
}
