using Nodisla.Cuaderno.Aplicacion.Puertos;

namespace Nodisla.Cuaderno.Propagacion.Prediccion;

/// <summary>
/// Motor de prediccion basado en la aproximacion propia de <see cref="ModeloMufLuf"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Esto no es VOACAP.</b> <see cref="EsAproximacion"/> vale siempre <see langword="true"/> y
/// el nombre lo dice con todas las letras, porque el operador tiene que saber que lo que esta
/// mirando es una estimacion. Una aproximacion honesta y bien etiquetada le sirve; una
/// prediccion con pinta de precisa que no lo es, le hace perder la banda.
/// </para>
/// <para>
/// El modelo, sus fuentes y sus fallos conocidos estan documentados en <see cref="ModeloMufLuf"/>.
/// </para>
/// </remarks>
/// <param name="opciones">Ajustes de antena, ruido y exigencia de senal.</param>
public sealed class MotorAproximacionNodisla(OpcionesPropagacion? opciones = null) : IMotorDePrediccion
{
    /// <summary>Nombre con el que se identifica este motor en pantalla.</summary>
    public const string NombreDelMotor = "Aproximación NODISLA (MUF/LUF, no es VOACAP)";

    private readonly OpcionesPropagacion ajustes = opciones ?? new OpcionesPropagacion();

    /// <inheritdoc/>
    public string Nombre => NombreDelMotor;

    /// <inheritdoc/>
    public bool EsAproximacion => true;

    /// <inheritdoc/>
    public Task<IReadOnlyList<PrediccionDeBanda>> PredecirAsync(
        SolicitudDePrediccion solicitud,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(Predecir(solicitud));
    }

    /// <summary>Predice el circuito banda por banda, sin red y sin esperas.</summary>
    /// <param name="solicitud">Datos del trayecto y del momento.</param>
    public IReadOnlyList<PrediccionDeBanda> Predecir(SolicitudDePrediccion solicitud)
    {
        ArgumentNullException.ThrowIfNull(solicitud);

        var condiciones = CondicionesDesde(solicitud.Indices);
        var aCiegas = solicitud.Indices is null;
        var geometria = ModeloMufLuf.Calcular(
            solicitud.Origen,
            solicitud.Destino,
            solicitud.MomentoUtc,
            condiciones);

        // Con potencia cero no hay nada que predecir; se toma un vatio para no calcular el
        // logaritmo de cero y la fiabilidad ya saldra baja sola.
        var potencia = Math.Max(1e-3, solicitud.PotenciaVatios);
        var potenciaDbw = 10.0 * Math.Log10(potencia);

        var resultado = new List<PrediccionDeBanda>(BandasDeTrabajo.Bandas.Count);
        foreach (var (banda, frecuencia) in BandasDeTrabajo.Bandas)
        {
            var absorcion = ModeloMufLuf.AbsorcionDb(
                solicitud.Origen,
                solicitud.Destino,
                solicitud.MomentoUtc,
                frecuencia,
                condiciones,
                geometria);

            var perdida = ModeloMufLuf.PerdidaDb(frecuencia, geometria, absorcion);
            var senal = potenciaDbw + (2.0 * ajustes.GananciaAntenaDbi) - perdida;
            var ruido = ModeloMufLuf.RuidoDbw(frecuencia, ajustes.AnchoDeBandaHz, ajustes.AmbienteDeRuido);
            var relacion = senal - ruido;

            var porMuf = ModeloMufLuf.ProbabilidadDeMuf(geometria.MufMhz, frecuencia);
            var porSenal = ModeloMufLuf.ProbabilidadDeSenal(relacion, ajustes.RelacionSenalRuidoRequeridaDb);
            var fiabilidad = Math.Clamp(porMuf * porSenal, 0.0, 1.0);

            // Por debajo de cierto punto la senal calculada deja de querer decir nada: no se
            // ensena una cifra, se ensena que no hay circuito.
            var hayAlgoQueDecir = relacion > ModeloMufLuf.RelacionSinSentidoDb;

            resultado.Add(new PrediccionDeBanda(
                banda,
                solicitud.MomentoUtc,
                fiabilidad,
                hayAlgoQueDecir ? Math.Round(senal, 1) : null,
                hayAlgoQueDecir ? Math.Round(relacion, 1) : null,
                geometria.Saltos)
            {
                // Sin indices se ha calculado con un Sol supuesto: no vale lo mismo y se dice.
                SinDatosSolares = aCiegas,

                // Cada prediccion lleva su firma. Aqui salen todas del mismo sitio, pero el dia
                // que haya un motor externo estas seran las que hayan caido de vuelta.
                Motor = NombreDelMotor,
                EsAproximacion = true,
            });
        }

        return resultado;
    }

    /// <summary>
    /// Pasa de los indices solares a lo que el modelo necesita, o a las condiciones supuestas si
    /// no hay indices.
    /// </summary>
    /// <param name="indices">Indices solares, o nulo.</param>
    /// <remarks>
    /// Cuando no hay indices se usan condiciones medias. Quien ensene el resultado tiene que
    /// decir que se calculo sin datos solares: no es lo mismo una estimacion con el flujo de hoy
    /// que una estimacion con un flujo inventado.
    /// </remarks>
    public static CondicionesIonosfericas CondicionesDesde(IndicesSolares? indices)
    {
        if (indices is null)
        {
            return CondicionesIonosfericas.Supuestas;
        }

        // Se prefiere el numero de manchas medido; si no lo hay, se deduce del flujo solar.
        var manchas = indices.ManchasSolares
                      ?? (indices.FlujoSolar is { } flujo
                          ? ModeloMufLuf.ManchasDesdeFlujo(flujo)
                          : CondicionesIonosfericas.Supuestas.ManchasSolares);

        var k = indices.IndiceK ?? (indices.Tormenta ? 5.0 : CondicionesIonosfericas.Supuestas.IndiceK);
        return new CondicionesIonosfericas(manchas, k, indices.Tormenta);
    }
}
