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
            var calculo = CalcularEnFrecuencia(
                solicitud,
                geometria,
                condiciones,
                potenciaDbw,
                frecuencia,
                ajustes.AnchoDeBandaHz,
                ajustes.RelacionSenalRuidoRequeridaDb);

            resultado.Add(new PrediccionDeBanda(
                banda,
                solicitud.MomentoUtc,
                calculo.Fiabilidad,
                calculo.SenalDbw,
                calculo.RelacionSenalRuido,
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
    /// Predice el circuito en una frecuencia concreta, no en la de trabajo de la banda.
    /// </summary>
    /// <remarks>
    /// Es lo que usa el cluster para decir, fila a fila, si la estacion anunciada deberia
    /// oirse desde aqui en la frecuencia en la que la anuncian. El ancho de banda y la relacion
    /// senal-ruido exigida van aparte porque dependen del modo: un FT8 se decodifica veinte
    /// decibelios por debajo de donde una fonia ya no se entiende.
    /// </remarks>
    /// <param name="solicitud">Datos del trayecto y del momento.</param>
    /// <param name="frecuenciaMhz">Frecuencia anunciada.</param>
    /// <param name="anchoDeBandaHz">Ancho de banda del receptor; nulo para el de los ajustes.</param>
    /// <param name="relacionRequeridaDb">Relacion senal-ruido suficiente; nula para la de los ajustes.</param>
    public PrevisionEnFrecuencia PredecirEnFrecuencia(
        SolicitudDePrediccion solicitud,
        double frecuenciaMhz,
        double? anchoDeBandaHz = null,
        double? relacionRequeridaDb = null)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frecuenciaMhz);

        var condiciones = CondicionesDesde(solicitud.Indices);
        var geometria = ModeloMufLuf.Calcular(
            solicitud.Origen,
            solicitud.Destino,
            solicitud.MomentoUtc,
            condiciones);
        var potenciaDbw = 10.0 * Math.Log10(Math.Max(1e-3, solicitud.PotenciaVatios));

        var calculo = CalcularEnFrecuencia(
            solicitud,
            geometria,
            condiciones,
            potenciaDbw,
            frecuenciaMhz,
            anchoDeBandaHz ?? ajustes.AnchoDeBandaHz,
            relacionRequeridaDb ?? ajustes.RelacionSenalRuidoRequeridaDb);

        return new PrevisionEnFrecuencia(
            frecuenciaMhz,
            calculo.Fiabilidad,
            calculo.RelacionSenalRuido,
            Math.Round(geometria.MufMhz, 1),
            geometria.Saltos,
            solicitud.Indices is null);
    }

    /// <summary>El presupuesto del enlace en una frecuencia, con la geometria ya hecha.</summary>
    private (double Fiabilidad, double? SenalDbw, double? RelacionSenalRuido) CalcularEnFrecuencia(
        SolicitudDePrediccion solicitud,
        GeometriaIonosferica geometria,
        CondicionesIonosfericas condiciones,
        double potenciaDbw,
        double frecuencia,
        double anchoDeBandaHz,
        double requeridaDb)
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
        var ruido = ModeloMufLuf.RuidoDbw(frecuencia, anchoDeBandaHz, ajustes.AmbienteDeRuido);
        var relacion = senal - ruido;

        var porMuf = ModeloMufLuf.ProbabilidadDeMuf(geometria.MufMhz, frecuencia);
        var porSenal = ModeloMufLuf.ProbabilidadDeSenal(relacion, requeridaDb);
        var fiabilidad = Math.Clamp(porMuf * porSenal, 0.0, 1.0);

        // Hay dos formas de que la cifra de senal no quiera decir nada, y las dos acaban en
        // nulo: que la absorcion se haya comido el circuito, y que la frecuencia este por
        // encima de la MUF, donde la onda no vuelve y el presupuesto de perdidas sigue dando
        // un numero grande de una senal que no llega.
        var hayAlgoQueDecir = relacion > ModeloMufLuf.RelacionSinSentidoDb
                              && porMuf >= ModeloMufLuf.ProbabilidadMinimaDeModo;

        return (
            fiabilidad,
            hayAlgoQueDecir ? Math.Round(senal, 1) : null,
            hayAlgoQueDecir ? Math.Round(relacion, 1) : null);
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

/// <summary>Prevision del circuito en una frecuencia concreta.</summary>
/// <param name="FrecuenciaMhz">Frecuencia para la que se ha calculado.</param>
/// <param name="Fiabilidad">Probabilidad de que el circuito funcione, de 0 a 1.</param>
/// <param name="RelacionSenalRuido">Relacion senal-ruido prevista, o nula si no significa nada.</param>
/// <param name="MufMhz">MUF mediana del trayecto.</param>
/// <param name="Saltos">Saltos por la capa F2.</param>
/// <param name="SinDatosSolares">Se calculo con condiciones supuestas, sin indices.</param>
public sealed record PrevisionEnFrecuencia(
    double FrecuenciaMhz,
    double Fiabilidad,
    double? RelacionSenalRuido,
    double MufMhz,
    int Saltos,
    bool SinDatosSolares);
