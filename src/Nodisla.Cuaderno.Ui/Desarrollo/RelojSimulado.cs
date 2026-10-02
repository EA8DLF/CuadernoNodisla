using Nodisla.Cuaderno.Idiomas;
using Nodisla.Cuaderno.Aplicacion.Puertos;

namespace Nodisla.Cuaderno.Ui.Desarrollo;

/// <summary>
/// Un reloj de mentira, para ver la pantalla sin hablar con ningun servidor de hora.
/// </summary>
/// <remarks>
/// <para>
/// Arranca con el desvio que de verdad tenia esta maquina —un segundo y pico atrasada— y no
/// con cero, a proposito: lo que hay que poder ver y probar es el <b>aviso</b>, no el caso
/// bonito. Un reloj simulado que siempre estuviera en hora dejaria sin probar justo la parte
/// que importa.
/// </para>
/// <para>
/// Poner en hora aqui no toca el reloj del ordenador. Faltaria mas.
/// </para>
/// </remarks>
public sealed class RelojSimulado : IRelojDelModem, ISincronizadorDeHora
{
    /// <summary>Desvio de partida: el que llego a tener esta maquina en tres dias.</summary>
    public const double DesvioDePartidaMs = -1290;

    private readonly object _cerrojo = new();
    private DesvioDelReloj _desvio = new(
        DesvioDePartidaMs,
        "simulado",
        DateTimeOffset.UtcNow,
        EsFiable: true);

    /// <inheritdoc />
    public DateTimeOffset Ahora => DateTimeOffset.UtcNow.AddMilliseconds(-Desvio.DesvioMs);

    /// <inheritdoc />
    public DesvioDelReloj Desvio
    {
        get { lock (_cerrojo) return _desvio; }
    }

    /// <inheritdoc />
    public EstadoDelReloj Estado => Componer(Desvio);

    /// <inheritdoc />
    public event EventHandler<EstadoDelReloj>? DesvioMedido;

    /// <inheritdoc />
    public bool SePuedePonerEnHora => true;

    /// <inheritdoc />
    public DateTimeOffset? UltimaSincronizacion { get; private set; }

    /// <inheritdoc />
    public string InstruccionesParaHacerloAMano =>
        "w32tm /config /manualpeerlist:\"hora.roa.es\" /syncfromflags:manual /update";

    /// <inheritdoc />
    public Task<DesvioDelReloj> MedirAsync(bool forzar = false, CancellationToken ct = default)
    {
        var medida = Desvio with { MedidoUtc = DateTimeOffset.UtcNow };
        lock (_cerrojo) _desvio = medida;

        DesvioMedido?.Invoke(this, Componer(medida));
        return Task.FromResult(medida);
    }

    /// <inheritdoc />
    public DateTimeOffset ProximaVentana(TimeSpan periodo)
    {
        if (periodo <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(periodo));

        var pulsos = Ahora.UtcTicks;
        return new DateTimeOffset(pulsos - (pulsos % periodo.Ticks) + periodo.Ticks, TimeSpan.Zero);
    }

    /// <inheritdoc />
    public Task<ResultadoDePuestaEnHora> PonerElRelojEnHoraAsync(CancellationToken ct = default)
    {
        var antes = Desvio.DesvioMs;
        var puesto = new DesvioDelReloj(12, "simulado", DateTimeOffset.UtcNow, EsFiable: true);
        lock (_cerrojo) _desvio = puesto;
        UltimaSincronizacion = DateTimeOffset.UtcNow;

        DesvioMedido?.Invoke(this, Componer(puesto));

        return Task.FromResult(new ResultadoDePuestaEnHora(
            Hecho: true,
            ViaDeSincronizacion.HoraPuestaAMano,
            Textos.T("Dialogos.Simulado.RelojPuesto"),
            Detalle: null,
            antes,
            puesto.DesvioMs));
    }

    /// <inheritdoc />
    public Task<ResultadoDePuestaEnHora> ConfigurarServicioDeHoraAsync(CancellationToken ct = default) =>
        Task.FromResult(new ResultadoDePuestaEnHora(
            Hecho: true,
            ViaDeSincronizacion.ServicioConfigurado,
            Textos.T("Dialogos.Simulado.ServicioDeHora"),
            InstruccionesParaHacerloAMano,
            Desvio.DesvioMs,
            DesvioDespuesMs: null));

    /// <summary>
    /// Pone el veredicto en palabras con el MISMO veredicto del reloj real y sus umbrales de
    /// fabrica (200 ms y un segundo): asi el simulado dice lo mismo, en el mismo idioma, que el
    /// de verdad.
    /// </summary>
    private static EstadoDelReloj Componer(DesvioDelReloj desvio) =>
        Nodisla.Cuaderno.Audio.Reloj.VeredictoDelReloj.Componer(desvio, new Nodisla.Cuaderno.Audio.Reloj.OpcionesDelReloj());
}
