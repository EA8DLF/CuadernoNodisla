using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Propagacion.Prediccion;

/// <summary>Lo que hay que saber para predecir un circuito.</summary>
/// <param name="Origen">Estacion que transmite.</param>
/// <param name="Destino">Estacion a la que se quiere llegar.</param>
/// <param name="PotenciaVatios">Potencia de transmision.</param>
/// <param name="MomentoUtc">Momento para el que se predice.</param>
/// <param name="Indices">Indices solares, o nulo si no se pudieron traer.</param>
public sealed record SolicitudDePrediccion(
    Coordenada Origen,
    Coordenada Destino,
    double PotenciaVatios,
    DateTimeOffset MomentoUtc,
    IndicesSolares? Indices);

/// <summary>
/// Un motor capaz de predecir que bandas abren en un trayecto.
/// </summary>
/// <remarks>
/// Existe para que se pueda enchufar un motor de verdad -VOACAP o ITURHFProp como proceso
/// externo- el dia que se disponga de uno, sin tocar nada mas. Cada motor tiene que decir su
/// nombre y si lo que da es una aproximacion: eso llega tal cual a la pantalla del operador.
/// </remarks>
public interface IMotorDePrediccion
{
    /// <summary>Nombre del motor, tal como se ensena al operador.</summary>
    string Nombre { get; }

    /// <summary>Lo que devuelve es una estimacion, no el calculo de un motor reconocido.</summary>
    bool EsAproximacion { get; }

    /// <summary>Predice el circuito banda por banda.</summary>
    /// <param name="solicitud">Datos del trayecto y del momento.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <remarks>
    /// Quien implemente esto tiene que firmar <b>cada</b> <see cref="PrediccionDeBanda"/> con su
    /// <see cref="PrediccionDeBanda.Motor"/> y su <see cref="PrediccionDeBanda.EsAproximacion"/>,
    /// no fiarse de lo que diga el motor en conjunto. Un motor externo que se cae a mitad y
    /// termina el trabajo con una estimacion tiene que devolver esas bandas marcadas como
    /// estimacion, aunque el motor se llame VOACAP.
    /// </remarks>
    Task<IReadOnlyList<PrediccionDeBanda>> PredecirAsync(
        SolicitudDePrediccion solicitud,
        CancellationToken ct = default);
}
