using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Modos.Ft8;
using Nodisla.Cuaderno.Modos.Marco;

namespace Nodisla.Cuaderno.Modos.Q65;

/// <summary>Cuentas de la ultima ventana, para el banco y para afinar.</summary>
/// <param name="Candidatas">Sitios con sincronismo que se intentaron.</param>
/// <param name="PalabrasValidas">Palabras que cerraron las ecuaciones del codigo.</param>
/// <param name="RechazadasPorElCrc">Palabras validas cuyo sello no cuadro.</param>
/// <param name="RechazadasPorVerosimilitud">Palabras con sello bueno que no se parecian a lo oido.</param>
/// <param name="ConAp">Decodificaciones que salieron con informacion a priori.</param>
/// <param name="Promediadas">Decodificaciones que salieron de la suma de varios periodos.</param>
public sealed record CuentasDeQ65(
    int Candidatas,
    int PalabrasValidas,
    int RechazadasPorElCrc,
    int RechazadasPorVerosimilitud,
    int ConAp,
    int Promediadas)
{
    /// <summary>Verosimilitud mas alta que se vio en una palabra rechazada, para medir el umbral.</summary>
    public double VerosimilitudMaximaRechazada { get; init; }

    /// <summary>Verosimilitud mas baja que se vio en una palabra aceptada.</summary>
    public double VerosimilitudMinimaAceptada { get; init; }
}

/// <summary>
/// Q65 en el modem propio: un submodo y un periodo por instancia.
/// </summary>
/// <remarks>
/// <para>
/// El recorrido de una ventana es: espectrograma con la rejilla del submodo; mapa de
/// sincronismo y candidatas; para cada candidata, energias por simbolo, probabilidades,
/// propagacion de creencias sobre GF(64), sello de 12 bits y comprobacion de verosimilitud.
/// Si el promediado esta puesto, el periodo se guarda, se suma su mapa de sincronismo al de
/// los anteriores y se vuelve a intentar en los sitios que destacan en la suma, con las
/// energias sumadas de todos los periodos guardados.
/// </para>
/// <para>
/// <b>Informacion a priori.</b> Si el operador da su indicativo, en las mejores candidatas se
/// prueba ademas suponiendo que el mensaje empieza por CQ, por su indicativo, o por su
/// indicativo y el del corresponsal. Cada suposicion fija bits y ayuda en el filo, pero es
/// tambien la fuente de mensajes inventados en este modo: por eso toda decodificacion, con o
/// sin a priori, pasa por el umbral de verosimilitud, medido en el banco con ruido puro.
/// </para>
/// <para>
/// <b>Arranque de las ventanas.</b> Las de 15, 30 y 60 s empiezan en el segundo cero (y 15, 30,
/// 45 las de 15 s). Las de 120 s empiezan en los minutos pares y las de 300 en los minutos
/// multiplos de cinco; eso no cabe en <see cref="ArranqueDentroDelPeriodo"/> y lo tiene que
/// alinear quien monte el modem. La senal emitida empieza <see cref="ComienzoNominalSegundos"/>
/// despues del comienzo de la ventana, como manda el protocolo, y <see cref="Generar"/> la
/// devuelve sin ese silencio delante, igual que el generador de FT8.
/// </para>
/// </remarks>
public sealed class ModoQ65 : IModoDigital
{
    private readonly CodigoQra _codigo;
    private readonly DecodificadorQra _decodificador;
    private readonly PromediadorDeQ65 _promediador = new();
    private readonly ILogger _registro;

    /// <summary>Monta el modo para un submodo y periodo.</summary>
    public ModoQ65(ParametrosDeQ65 parametros, TablasDeQ65 tablas, ILogger? registro = null)
    {
        ArgumentNullException.ThrowIfNull(parametros);
        ArgumentNullException.ThrowIfNull(tablas);
        Parametros = parametros;
        Tablas = tablas;
        _codigo = new CodigoQra(tablas);
        _decodificador = new DecodificadorQra(_codigo);
        _registro = registro ?? NullLogger.Instance;
        // Los espectrogramas guardados para promediar pesan en proporcion al periodo: unos
        // cinco megabytes en 60 s y mas de veinte en 300 s. Se guardan menos cuanto mas largos.
        _promediador.PeriodosMaximos = parametros.PeriodoSegundos >= 300 ? 3 : 6;
    }

    /// <summary>Submodo y periodo.</summary>
    public ParametrosDeQ65 Parametros { get; }

    /// <summary>Tablas del protocolo con las que trabaja.</summary>
    public TablasDeQ65 Tablas { get; }

    /// <summary>Catalogo de indicativos para resolver los que viajan resumidos.</summary>
    public CatalogoDeIndicativos Catalogo { get; } = new();

    /// <summary>Segundos tras el comienzo de la ventana en que tiene que empezar la senal emitida.</summary>
    public double ComienzoNominalSegundos => Parametros.ComienzoNominalSegundos;

    /// <summary>Candidatas de sincronismo que se intentan por ventana.</summary>
    public int CandidatasPorVentana { get; set; } = 20;

    /// <summary>Candidatas, de las mejores, en las que se prueba ademas con informacion a priori.</summary>
    public int CandidatasConAp { get; set; } = 4;

    /// <summary>Puntuacion de sincronismo minima, en desviaciones tipicas del ruido.</summary>
    public double UmbralDeSincronismo { get; set; } = 4.5;

    /// <summary>
    /// Verosimilitud minima de una palabra para darla por buena, en desviaciones tipicas.
    /// </summary>
    /// <remarks>
    /// Medido en el banco con ruido puro y las tres mascaras de a priori puestas; la cifra
    /// medida y el margen estan en <c>resultados-q65.md</c>. Es el freno que no se negocia.
    /// </remarks>
    public double UmbralDeVerosimilitud { get; set; } = 10.0;

    /// <summary>Factor minimo de la exponencial de las probabilidades.</summary>
    public double FactorMinimo { get; set; } = 0.55;

    /// <summary>Factor maximo de la exponencial de las probabilidades.</summary>
    public double FactorMaximo { get; set; } = 0.92;

    /// <summary>Indicativo propio, para la informacion a priori. Nulo la desactiva.</summary>
    public string? MiIndicativo { get; set; }

    /// <summary>Indicativo del corresponsal, para la informacion a priori mas atrevida.</summary>
    public string? IndicativoDx { get; set; }

    /// <summary>Sumar periodos cuando uno solo no llega.</summary>
    public bool UsarPromediado { get; set; } = true;

    /// <summary>Candidatas del mapa sumado que se intentan por ventana.</summary>
    public int CandidatasAPromediar { get; set; } = 4;

    /// <summary>Periodos que guarda el promediado.</summary>
    public int PeriodosAPromediar
    {
        get => _promediador.PeriodosMaximos;
        set => _promediador.PeriodosMaximos = value;
    }

    /// <summary>Cuentas de la ultima ventana.</summary>
    public CuentasDeQ65 UltimasCuentas { get; private set; } = new(0, 0, 0, 0, 0, 0);

    /// <inheritdoc/>
    public ModoDelModem Modo => ModoDelModem.Q65;

    /// <inheritdoc/>
    public TimeSpan Periodo => TimeSpan.FromSeconds(Parametros.PeriodoSegundos);

    /// <inheritdoc/>
    public TimeSpan ArranqueDentroDelPeriodo => TimeSpan.Zero;

    /// <inheritdoc/>
    public TimeSpan ComienzoDeLaSenal => TimeSpan.FromSeconds(ComienzoNominalSegundos);

    /// <inheritdoc/>
    public void Reiniciar()
    {
        LimpiarPromedio();
        Catalogo.Olvidar();
    }

    /// <inheritdoc/>
    public int FrecuenciaDeAnalisis => ParametrosDeQ65.FrecuenciaDeAnalisis;

    /// <summary>Olvida lo acumulado para el promediado.</summary>
    public void LimpiarPromedio() => _promediador.Limpiar();

    /// <inheritdoc/>
    public IReadOnlyList<DecodificacionPropia> Decodificar(ReadOnlySpan<float> audio, DateTimeOffset ventanaUtc, CancellationToken ct)
    {
        var p = Parametros;
        var minimo = (int)Math.Round((p.ComienzoNominalSegundos + p.DesfaseMinimoSegundos + p.DuracionDeLaSenalSegundos) * ParametrosDeQ65.FrecuenciaDeAnalisis);
        if (audio.Length < minimo) return [];

        var espectrograma = EspectrogramaDeQ65.Calcular(audio, p);
        var mapa = SincronizadorDeQ65.Mapa(espectrograma, Tablas);
        var candidatas = mapa.Buscar(CandidatasPorVentana, UmbralDeSincronismo);
        var mascaras = DemoduladorDeQ65.MascarasDisponibles(MiIndicativo, IndicativoDx);

        var salida = new List<DecodificacionPropia>();
        var cuentas = new Cuentas();
        var palabra = new int[CodigoQra.Longitud];

        for (var i = 0; i < candidatas.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var candidata = candidatas[i];
            var medida = DemoduladorDeQ65.Medir(espectrograma, candidata);
            var resultado = Intentar(medida, i < CandidatasConAp ? mascaras : [], palabra, cuentas);
            if (resultado is not null) Anadir(salida, resultado, candidata, ventanaUtc, cuentas);
        }

        // Promediado: se guarda este periodo, se suma su mapa a los anteriores y se buscan
        // candidatas en la suma, que es donde una senal floja acaba destacando. Lo que ya salio
        // en este periodo se salta.
        if (UsarPromediado)
        {
            _promediador.Anadir(espectrograma, mapa);
            if (_promediador.Periodos >= 2 && _promediador.Mapa is { } sumado)
            {
                var decodificado = false;
                foreach (var candidata in sumado.Buscar(CandidatasAPromediar, UmbralDeSincronismo))
                {
                    ct.ThrowIfCancellationRequested();
                    if (salida.Any(d => Math.Abs(d.TonoHz - (candidata.Casilla * p.HzPorCasilla)) < p.EspaciadoDeTonosHz)) continue;
                    var suma = _promediador.Medir(candidata);
                    var resultado = Intentar(suma, mascaras, palabra, cuentas);
                    if (resultado is null) continue;
                    if (Anadir(salida, resultado with { EsRecuperacionProfunda = true }, candidata, ventanaUtc, cuentas))
                    {
                        cuentas.Promediadas++;
                        decodificado = true;
                    }
                }
                // Como en el programa de referencia: lo que ya salio de la suma no se sigue sumando.
                if (decodificado) _promediador.Limpiar();
            }
        }

        UltimasCuentas = new CuentasDeQ65(candidatas.Count, cuentas.Validas, cuentas.PorCrc, cuentas.PorVerosimilitud, cuentas.ConAp, cuentas.Promediadas)
        {
            VerosimilitudMaximaRechazada = cuentas.MaximaRechazada,
            VerosimilitudMinimaAceptada = cuentas.MinimaAceptada,
        };
        return salida;
    }

    private sealed class Cuentas
    {
        public int Validas;
        public int PorCrc;
        public int PorVerosimilitud;
        public int ConAp;
        public int Promediadas;
        public double MaximaRechazada = double.NegativeInfinity;
        public double MinimaAceptada = double.PositiveInfinity;
    }

    private sealed record Decodificada(int[] Palabra, double Verosimilitud, double Decibelios, string Ap)
    {
        public bool EsRecuperacionProfunda { get; init; }
    }

    private bool Anadir(List<DecodificacionPropia> salida, Decodificada resultado, CandidataDeQ65 candidata, DateTimeOffset ventanaUtc, Cuentas cuentas)
    {
        var decodificacion = Componer(resultado, candidata, ventanaUtc);
        if (decodificacion is null) return false;
        if (salida.Any(d => d.Texto == decodificacion.Texto && Math.Abs(d.TonoHz - decodificacion.TonoHz) < Parametros.EspaciadoDeTonosHz * 2)) return false;
        if (resultado.Ap.Length > 0) cuentas.ConAp++;
        salida.Add(decodificacion);
        return true;
    }

    /// <summary>Prueba sin a priori y despues con cada mascara, y devuelve la primera palabra buena.</summary>
    private Decodificada? Intentar(MedidaDeQ65 medida, List<MascaraAp> mascaras, int[] palabra, Cuentas cuentas)
    {
        var relacion = DemoduladorDeQ65.SenalRuidoDelSincronismo(medida, Tablas);
        var factor = Math.Clamp(relacion / (1 + relacion), FactorMinimo, FactorMaximo);

        for (var a = -1; a < mascaras.Count; a++)
        {
            var mascara = a < 0 ? null : mascaras[a];
            var intrinsecas = DemoduladorDeQ65.Intrinsecas(medida, Tablas, factor, mascara);
            if (!_decodificador.TryDecodificar(intrinsecas, palabra, out _)) continue;
            cuentas.Validas++;

            if (!Crc12DeQ65.EsValido(palabra.AsSpan(0, CodigoQra.SimbolosDeInformacion), Tablas.PolinomioDelCrc)
                || (palabra[MensajeDeQ65.SimbolosDelMensaje - 1] & 1) != 0)
            {
                cuentas.PorCrc++;
                continue;
            }

            var verosimilitud = DemoduladorDeQ65.Verosimilitud(medida, palabra, Tablas);
            if (verosimilitud < UmbralDeVerosimilitud)
            {
                cuentas.PorVerosimilitud++;
                cuentas.MaximaRechazada = Math.Max(cuentas.MaximaRechazada, verosimilitud);
                _registro.LogDebug("Q65: palabra con sello bueno rechazada por verosimilitud {Verosimilitud:0.0} (a priori: {Ap}).", verosimilitud, mascara?.Nombre ?? "ninguno");
                continue;
            }

            cuentas.MinimaAceptada = Math.Min(cuentas.MinimaAceptada, verosimilitud);
            var db = DemoduladorDeQ65.RelacionSenalRuidoDb(medida, palabra, Tablas, Parametros);
            return new Decodificada((int[])palabra.Clone(), verosimilitud, db, mascara?.Nombre ?? string.Empty);
        }
        return null;
    }

    private DecodificacionPropia? Componer(Decodificada d, CandidataDeQ65 candidata, DateTimeOffset ventanaUtc)
    {
        if (!MensajeDeQ65.TryBitsDeLaInformacion(d.Palabra.AsSpan(0, CodigoQra.SimbolosDeInformacion), Tablas.PolinomioDelCrc, out var bits)) return null;
        if (!MensajeDe77Bits.TryDesempaquetar(bits, Catalogo, out var mensaje)) return null;
        if (mensaje.TieneIndicativoSinResolver) return null;

        Catalogo.Recordar(mensaje.Llamante.Valor);
        var p = Parametros;
        var segundosDeLaColumna = (double)candidata.Columna * p.Salto / ParametrosDeQ65.FrecuenciaDeAnalisis;
        var desfase = segundosDeLaColumna - p.ComienzoNominalSegundos;
        var tono = candidata.Casilla * p.HzPorCasilla;

        return new DecodificacionPropia(
            mensaje.Texto,
            (int)Math.Round(d.Decibelios),
            Math.Round(desfase, 2),
            (int)Math.Round(tono),
            ModoDelModem.Q65,
            ventanaUtc)
        {
            Llamante = mensaje.Llamante,
            Llamado = mensaje.Llamado,
            Locator = mensaje.Locator,
            EsCq = mensaje.EsCq,
            EsRecuperacionProfunda = d.EsRecuperacionProfunda,
        };
    }

    /// <inheritdoc/>
    public float[] Generar(string mensaje, int tonoHz, int frecuenciaDeMuestreo)
    {
        if (!MensajeDeQ65.TryTonosDe(mensaje, _codigo, out var tonos, out var motivo))
            throw new FormatException(motivo);
        return GeneradorDeSenalDeQ65.Sintetizar(Parametros, tonos, tonoHz, frecuenciaDeMuestreo, amplitud: 0.5);
    }

    /// <summary>Los 85 tonos de un mensaje, para el banco y las pruebas.</summary>
    public bool TryTonos(string mensaje, out byte[] tonos, out string motivo) =>
        MensajeDeQ65.TryTonosDe(mensaje, _codigo, out tonos, out motivo);
}
