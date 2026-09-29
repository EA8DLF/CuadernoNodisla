using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Aplicacion.Puertos;

/// <summary>Region de la IARU. Canarias y Espana estan en la Region 1.</summary>
public enum RegionIaru
{
    Region1 = 1,
    Region2 = 2,
    Region3 = 3,
}

/// <summary>Que se puede hacer en un tramo del bandplan.</summary>
public enum UsoDelTramo
{
    /// <summary>Telegrafia.</summary>
    Cw,
    /// <summary>Modos digitales de banda estrecha.</summary>
    DigitalEstrecho,
    /// <summary>Modos digitales de banda ancha.</summary>
    DigitalAncho,
    /// <summary>Fonia.</summary>
    Fonia,
    /// <summary>Fonia y modos de imagen.</summary>
    FoniaEImagen,
    /// <summary>Balizas.</summary>
    Baliza,
    /// <summary>Todos los modos.</summary>
    Todos,
    /// <summary>Tramo con uso reservado o restringido.</summary>
    Reservado,
}

/// <summary>Un tramo del bandplan.</summary>
/// <param name="Banda">Banda a la que pertenece.</param>
/// <param name="Inferior">Frecuencia inferior del tramo.</param>
/// <param name="Superior">Frecuencia superior del tramo.</param>
/// <param name="Uso">Que se hace en el tramo.</param>
/// <param name="Descripcion">Texto para mostrar, en espanol.</param>
/// <param name="AnchoMaximoHz">Ancho de banda maximo admitido, si el plan lo fija.</param>
public sealed record TramoDeBanda(
    Banda Banda,
    Frecuencia Inferior,
    Frecuencia Superior,
    UsoDelTramo Uso,
    string Descripcion,
    int? AnchoMaximoHz)
{
    /// <summary>Indica si la frecuencia cae dentro del tramo.</summary>
    public bool Contiene(Frecuencia frecuencia) =>
        frecuencia >= Inferior && frecuencia <= Superior;
}

/// <summary>Lo que el bandplan dice sobre una frecuencia concreta.</summary>
/// <param name="Frecuencia">Frecuencia consultada.</param>
/// <param name="Tramo">Tramo en el que cae. Nulo si esta fuera de toda banda.</param>
/// <param name="DentroDeBanda">La frecuencia esta en una banda de radioaficionado.</param>
/// <param name="ModoEncaja">
/// El modo que se pretende usar encaja con el uso del tramo. Nulo si no se pregunto por un modo.
/// </param>
/// <param name="Aviso">
/// Mensaje en espanol para el operador cuando algo no cuadra. Nulo si todo esta en orden.
/// </param>
public sealed record ConsultaDeBandplan(
    Frecuencia Frecuencia,
    TramoDeBanda? Tramo,
    bool DentroDeBanda,
    bool? ModoEncaja,
    string? Aviso);

/// <summary>
/// El bandplan: que se puede hacer en cada tramo de cada banda.
/// </summary>
/// <remarks>
/// <para>
/// Sirve para avisar al operador antes de que transmita donde no debe. El aviso es eso, un
/// aviso: <b>el programa no impide operar</b>. Hay motivos legitimos para estar fuera del plan
/// —escuchar, una autorizacion especial, un equipo abierto— y un cuaderno que se niegue a
/// registrar un contacto por eso es un cuaderno que miente sobre lo que ocurrio.
/// </para>
/// <para>
/// La region importa: EA8 es <see cref="RegionIaru.Region1"/>, donde 40 metros acaba en 7.200
/// y no en 7.300. Dar por supuesta la Region 2 es un error clasico de los programas hechos
/// al otro lado del Atlantico.
/// </para>
/// </remarks>
public interface IBandplan
{
    /// <summary>Region para la que se responde.</summary>
    RegionIaru Region { get; }

    /// <summary>Tramos de una banda, ordenados de menor a mayor frecuencia.</summary>
    IReadOnlyList<TramoDeBanda> TramosDe(Banda banda);

    /// <summary>
    /// Consulta una frecuencia, opcionalmente para un modo concreto.
    /// </summary>
    /// <param name="frecuencia">Frecuencia a consultar.</param>
    /// <param name="modo">Modo que se pretende usar, o vacio para no comprobarlo.</param>
    ConsultaDeBandplan Consultar(Frecuencia frecuencia, Modo modo = default);

    /// <summary>
    /// Frecuencias con nombre propio: llamadas de emergencia, centros de actividad,
    /// frecuencias de los modos digitales.
    /// </summary>
    IReadOnlyList<(Frecuencia Frecuencia, string Descripcion)> FrecuenciasSenaladas(Banda banda);
}
