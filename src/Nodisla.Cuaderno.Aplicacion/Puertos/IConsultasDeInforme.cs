using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Aplicacion.Puertos;

/// <summary>Contactos y confirmaciones agrupados por banda.</summary>
/// <param name="Band">Banda ADIF.</param>
/// <param name="Contactos">Contactos hechos en esa banda.</param>
/// <param name="Confirmados">Contactos con alguna confirmacion recibida que cuente para diplomas.</param>
public sealed record ResumenPorBanda(string Band, int Contactos, int Confirmados);

/// <summary>Contactos y confirmaciones agrupados por modo principal.</summary>
/// <param name="Mode">Modo principal ADIF.</param>
/// <param name="Contactos">Contactos hechos en ese modo.</param>
/// <param name="Confirmados">Contactos con alguna confirmacion recibida que cuente para diplomas.</param>
public sealed record ResumenPorModo(string Mode, int Contactos, int Confirmados);

/// <summary>Una casilla de la matriz de entidades DXCC por banda.</summary>
/// <param name="Dxcc">Numero de entidad DXCC.</param>
/// <param name="Band">Banda ADIF.</param>
/// <param name="Trabajados">Contactos con esa entidad en esa banda.</param>
/// <param name="Confirmados">Cuantos estan confirmados por el servicio consultado.</param>
public sealed record CasillaDxcc(int Dxcc, string Band, int Trabajados, int Confirmados);

/// <summary>Respuesta al «¿esto es nuevo?» que se muestra mientras se registra un contacto.</summary>
/// <param name="Visto">La entidad ya estaba en el cuaderno.</param>
/// <param name="VistoEnBanda">Ya estaba en esa banda.</param>
/// <param name="VistoEnHueco">Ya estaba en esa banda y ese modo.</param>
public sealed record Novedad(bool Visto, bool VistoEnBanda, bool VistoEnHueco);

/// <summary>Cifras generales del cuaderno.</summary>
/// <param name="Contactos">Contactos registrados.</param>
/// <param name="Indicativos">Indicativos distintos.</param>
/// <param name="Entidades">Entidades DXCC distintas.</param>
/// <param name="PrimeroUtc">Instante del contacto mas antiguo, o nulo si el cuaderno esta vacio.</param>
/// <param name="UltimoUtc">Instante del contacto mas reciente, o nulo si el cuaderno esta vacio.</param>
public sealed record TotalesDelCuaderno(
    int Contactos,
    int Indicativos,
    int Entidades,
    DateTimeOffset? PrimeroUtc,
    DateTimeOffset? UltimoUtc);

/// <summary>
/// Consultas de informe y agregaciones sobre el cuaderno.
/// </summary>
/// <remarks>
/// Va aparte de <see cref="IRepositorioQso"/> a proposito: el repositorio trabaja con contactos
/// y este puerto con cifras. Las pantallas de estadisticas y diplomas agregan decenas de miles
/// de filas, y la implementacion las resuelve en una consulta cada una en vez de recorrer el
/// cuaderno en memoria, que es justo lo que hace el programa original.
/// </remarks>
public interface IConsultasDeInforme
{
    /// <summary>Contactos y confirmaciones por banda, de la banda mas usada a la que menos.</summary>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <returns>Una fila por banda.</returns>
    Task<IReadOnlyList<ResumenPorBanda>> PorBandaAsync(CancellationToken ct = default);

    /// <summary>Contactos y confirmaciones por modo principal.</summary>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <returns>Una fila por modo.</returns>
    Task<IReadOnlyList<ResumenPorModo>> PorModoAsync(CancellationToken ct = default);

    /// <summary>Matriz de entidades DXCC por banda con el estado de confirmacion de un servicio.</summary>
    /// <param name="medio">Servicio de confirmacion que se tiene en cuenta.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <returns>Una fila por entidad y banda.</returns>
    Task<IReadOnlyList<CasillaDxcc>> MatrizDxccPorBandaAsync(
        MedioDeConfirmacion medio = MedioDeConfirmacion.Lotw,
        CancellationToken ct = default);

    /// <summary>
    /// Responde de una vez si una entidad DXCC es nueva, nueva en la banda o nueva en el hueco
    /// de banda y modo. Es lo que colorea el aviso mientras se registra el contacto.
    /// </summary>
    /// <param name="dxcc">Entidad DXCC.</param>
    /// <param name="banda">Banda del contacto.</param>
    /// <param name="modo">Modo del contacto.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <returns>Que partes ya estaban en el cuaderno.</returns>
    Task<Novedad> ConsultarNovedadAsync(
        int dxcc,
        Banda banda,
        Modo modo,
        CancellationToken ct = default);

    /// <summary>Cifras generales para la pantalla de resumen.</summary>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <returns>Los totales del cuaderno.</returns>
    Task<TotalesDelCuaderno> TotalesAsync(CancellationToken ct = default);
}
