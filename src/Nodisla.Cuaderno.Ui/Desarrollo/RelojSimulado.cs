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
            "Reloj puesto en hora (simulado): no se ha tocado el reloj del ordenador.",
            Detalle: null,
            antes,
            puesto.DesvioMs));
    }

    /// <inheritdoc />
    public Task<ResultadoDePuestaEnHora> ConfigurarServicioDeHoraAsync(CancellationToken ct = default) =>
        Task.FromResult(new ResultadoDePuestaEnHora(
            Hecho: true,
            ViaDeSincronizacion.ServicioConfigurado,
            "Servicio de hora configurado (simulado).",
            InstruccionesParaHacerloAMano,
            Desvio.DesvioMs,
            DesvioDespuesMs: null));

    /// <summary>
    /// Pone el veredicto en palabras con los mismos umbrales de fabrica que el reloj real.
    /// </summary>
    /// <remarks>
    /// Se escribe aqui y no se llama al modulo de audio porque este ensamblado no lo necesita
    /// para nada mas, y porque los umbrales de fabrica son los que son: 200 ms y un segundo.
    /// </remarks>
    private static EstadoDelReloj Componer(DesvioDelReloj desvio)
    {
        var cuanto = Math.Abs(desvio.DesvioMs);
        var sentido = desvio.DesvioMs >= 0 ? "adelantado" : "atrasado";
        var cantidad = cuanto >= 1000
            ? string.Create(System.Globalization.CultureInfo.CurrentCulture, $"{cuanto / 1000.0:0.00} s")
            : string.Create(System.Globalization.CultureInfo.CurrentCulture, $"{cuanto:0} ms");
        var paraMostrar = cuanto >= 1000
            ? string.Create(System.Globalization.CultureInfo.CurrentCulture, $"{desvio.DesvioMs / 1000.0:+0.00;-0.00} s")
            : string.Create(System.Globalization.CultureInfo.CurrentCulture, $"{desvio.DesvioMs:+0;-0} ms");

        if (cuanto <= 200)
        {
            return new EstadoDelReloj(
                desvio,
                CalidadDelReloj.Bien,
                $"El reloj está en hora ({cantidad} {sentido}).",
                string.Empty,
                paraMostrar);
        }

        if (cuanto < 1000)
        {
            return new EstadoDelReloj(
                desvio,
                CalidadDelReloj.Regular,
                $"El reloj está {cantidad} {sentido}.",
                "Con este desvío FT8 empieza a decodificar peor: conviene sincronizar el reloj.",
                paraMostrar);
        }

        return new EstadoDelReloj(
            desvio,
            CalidadDelReloj.FueraDeVentana,
            $"El reloj está {cantidad} {sentido}.",
            "Con este desvío no se decodifica casi nada y, sobre todo, se transmite fuera de ventana, "
                + "molestando a los demás sin enterarse. Hay que sincronizar el reloj antes de transmitir.",
            paraMostrar);
    }
}
