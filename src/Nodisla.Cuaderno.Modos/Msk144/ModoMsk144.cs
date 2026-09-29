using Microsoft.Extensions.Logging;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Modos.Ft8;
using Nodisla.Cuaderno.Modos.Ldpc;
using Nodisla.Cuaderno.Modos.Marco;

namespace Nodisla.Cuaderno.Modos.Msk144;

/// <summary>
/// MSK144 como modo del modem propio.
/// </summary>
/// <remarks>
/// <para>
/// Se registra en <see cref="RegistroDeModos"/> con
/// <c>registro.Anadir(new ModoMsk144(TablaLdpc.Cargar(ParametrosMsk144.FicheroDeTablas, 128, 90), TablaLdpc.Cargar(ParametrosMsk144.FicheroDeTablasCortas, 32, 16)))</c>.
/// </para>
/// <para>
/// El periodo es de 15 segundos por omision; el protocolo admite tambien 5, 10 y 30, y para
/// eso esta el segundo constructor. Lo que cambia es solo cuando se corta y se vuelve a
/// escuchar: la trama y el decodificador son los mismos.
/// </para>
/// </remarks>
public sealed class ModoMsk144 : IModoDigital
{
    private readonly CodificadorMsk144 _codificador;
    private readonly DecodificadorMsk144 _decodificador;
    private readonly CatalogoDeIndicativos _catalogo = new();
    private readonly double _periodoSegundos;

    /// <summary>Crea el modo con las tablas de los dos codigos y el periodo de 15 segundos.</summary>
    public ModoMsk144(TablaLdpc tablaLarga, TablaLdpc tablaCorta, ILogger? registro = null)
        : this(tablaLarga, tablaCorta, ParametrosMsk144.PeriodoSegundos, registro)
    {
    }

    /// <summary>Crea el modo con un periodo concreto: 5, 10, 15 o 30 segundos.</summary>
    public ModoMsk144(TablaLdpc tablaLarga, TablaLdpc tablaCorta, double periodoSegundos, ILogger? registro = null)
    {
        if (periodoSegundos is not (5 or 10 or 15 or 30))
            throw new ArgumentOutOfRangeException(nameof(periodoSegundos), periodoSegundos, "MSK144 admite periodos de 5, 10, 15 y 30 segundos.");
        _codificador = new CodificadorMsk144(tablaLarga, tablaCorta);
        _decodificador = new DecodificadorMsk144(tablaLarga, tablaCorta, registro);
        _periodoSegundos = periodoSegundos;
    }

    /// <summary>El decodificador, para ajustarlo y para dar a conocer pares de mensajes cortos.</summary>
    public DecodificadorMsk144 Decodificador => _decodificador;

    /// <summary>El codificador.</summary>
    public CodificadorMsk144 Codificador => _codificador;

    /// <summary>Catalogo de indicativos con el que se resuelven los resumidos.</summary>
    public CatalogoDeIndicativos Catalogo => _catalogo;

    /// <summary>Se trabaja con los codigos de verdad, no con los de pruebas.</summary>
    public bool EsElCodigoReal => _codificador.EsElCodigoReal;

    /// <inheritdoc/>
    public ModoDelModem Modo => ModoDelModem.Msk144;

    /// <inheritdoc/>
    public TimeSpan Periodo => TimeSpan.FromSeconds(_periodoSegundos);

    /// <inheritdoc/>
    public TimeSpan ArranqueDentroDelPeriodo => TimeSpan.Zero;

    /// <inheritdoc/>
    public int FrecuenciaDeAnalisis => ParametrosMsk144.FrecuenciaDeAnalisis;

    /// <summary>
    /// Segundos que se emite en cada periodo: casi todo el periodo, que en dispersion
    /// meteorica no se sabe cuando va a reflejar el meteoro.
    /// </summary>
    public double SegundosDeEmision => _periodoSegundos - 0.5;

    /// <inheritdoc/>
    /// <remarks>
    /// Decodifica la ventana entera, pero cada ping sale con su instante en
    /// <see cref="DecodificacionPropia.DesfaseSegundos"/>. Para ensenar los pings en tiempo real,
    /// el modem puede llamar a <see cref="DecodificarTrozo"/> con cada bloque de audio.
    /// </remarks>
    public IReadOnlyList<DecodificacionPropia> Decodificar(ReadOnlySpan<float> audio, DateTimeOffset ventanaUtc, CancellationToken ct) =>
        _decodificador.Decodificar(audio, ParametrosMsk144.FrecuenciaDeAnalisis, ventanaUtc, _catalogo, 0, ct).Decodificaciones;

    /// <summary>
    /// Decodifica un bloque de audio que empieza en el segundo indicado de la ventana.
    /// </summary>
    /// <param name="audio">Muestras a <see cref="FrecuenciaDeAnalisis"/>.</param>
    /// <param name="ventanaUtc">Ventana a la que pertenece.</param>
    /// <param name="segundoDeInicio">Instante de la primera muestra respecto al comienzo de la ventana.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <remarks>
    /// Para no perder un ping que caiga en la costura entre dos bloques, conviene que cada
    /// bloque lleve solapado con el anterior al menos una trama (72 ms) y, si se quiere
    /// promediar, siete (medio segundo). Las repeticiones las quita el modem por texto e instante.
    /// </remarks>
    public IReadOnlyList<DecodificacionPropia> DecodificarTrozo(ReadOnlySpan<float> audio, DateTimeOffset ventanaUtc, double segundoDeInicio, CancellationToken ct = default) =>
        _decodificador.Decodificar(audio, ParametrosMsk144.FrecuenciaDeAnalisis, ventanaUtc, _catalogo, segundoDeInicio, ct).Decodificaciones;

    /// <inheritdoc/>
    public float[] Generar(string mensaje, int tonoHz, int frecuenciaDeMuestreo)
    {
        if (!_codificador.TryCodificar(mensaje, out var trama, out var motivo)) throw new FormatException(motivo);
        return ModuladorMsk.Sintetizar(trama, tonoHz, frecuenciaDeMuestreo, SegundosDeEmision, amplitud: 0.5);
    }
}
