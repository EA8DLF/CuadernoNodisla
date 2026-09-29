using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Dominio.Dxcc;

/// <summary>
/// Identifica la entidad DXCC de un indicativo teniendo en cuenta la fecha del contacto.
/// </summary>
/// <remarks>
/// El orden de decision es el del oficio:
/// <list type="number">
///   <item>excepcion nominal: si la tabla nombra el indicativo entero, manda sin discusion;</item>
///   <item>prefijo mas largo que encaje y este vigente ese dia;</item>
///   <item>si nada encaja, se devuelve desconocido y se marca para revisar.</item>
/// </list>
/// La busqueda recorre un arbol de prefijos, de modo que el coste depende de lo que mide
/// el indicativo y no de las 29.000 reglas del catalogo: se puede lanzar con cada tecla.
/// </remarks>
public sealed class ResolutorDxcc : IResolutorDxcc
{
    private readonly CatalogoDxcc _catalogo;

    /// <summary>Crea un resolutor sobre un catalogo concreto.</summary>
    public ResolutorDxcc(CatalogoDxcc catalogo)
    {
        ArgumentNullException.ThrowIfNull(catalogo);
        _catalogo = catalogo;
    }

    /// <summary>Resolutor sobre la tabla de paises incrustada en el programa.</summary>
    public static ResolutorDxcc Predeterminado { get; } = new(CatalogoDxcc.Predeterminado);

    /// <summary>Catalogo que esta usando este resolutor.</summary>
    public CatalogoDxcc Catalogo => _catalogo;

    /// <inheritdoc/>
    public IReadOnlyList<EntidadDxcc> Todas => _catalogo.Todas;

    /// <inheritdoc/>
    public DateOnly? FechaDeLosDatos => _catalogo.FechaDeLosDatos;

    /// <inheritdoc/>
    public EntidadDxcc? PorNumero(int numero) => _catalogo.PorNumero(numero);

    /// <summary>Identifica la entidad de un indicativo con la fecha de hoy.</summary>
    public ResultadoDxcc Resolver(Indicativo indicativo) =>
        Resolver(indicativo, DateOnly.FromDateTime(DateTime.UtcNow));

    /// <inheritdoc/>
    public ResultadoDxcc Resolver(Indicativo indicativo, DateOnly fecha)
    {
        if (indicativo.EsVacio) return ResultadoDxcc.Desconocido;

        var a = AnalizadorIndicativo.Analizar(indicativo.Valor);

        // 1. Excepciones nominales: el indicativo entero, y despues sin los anadidos
        //    que solo dicen como se opera.
        var excepcion = _catalogo.Excepcion(a.Completo)
                        ?? (a.Nucleo != a.Completo ? _catalogo.Excepcion(a.Nucleo) : null);
        if (excepcion is not null && Aceptable(excepcion, fecha, out var entidadExcepcion))
        {
            return Componer(excepcion, entidadExcepcion, a, exacta: true);
        }

        // 2. Movil maritimo o aeronautico: no hay entidad que valga.
        if (a.SinEntidad || a.Clave.Length == 0) return ResultadoDxcc.Desconocido;

        // 3. Prefijo mas largo vigente ese dia.
        Span<int> nodos = stackalloc int[16];
        var n = _catalogo.Arbol.Camino(a.Clave, nodos);
        for (var i = n - 1; i >= 0; i--)
        {
            foreach (var regla in _catalogo.Arbol.ReglasDe(nodos[i]))
            {
                if (Aceptable(regla, fecha, out var entidad))
                {
                    return Componer(regla, entidad, a, exacta: false);
                }
            }
        }

        return ResultadoDxcc.Desconocido;
    }

    /// <summary>Una regla sirve si su ventana cubre la fecha y la entidad existia ese dia.</summary>
    private bool Aceptable(ReglaDxcc regla, DateOnly fecha, out EntidadDxcc entidad)
    {
        entidad = null!;
        if (!regla.EsValidaEn(fecha)) return false;
        var e = _catalogo.PorNumero(regla.Dxcc);
        if (e is null || !e.EraValidaEn(fecha)) return false;
        entidad = e;
        return true;
    }

    private static ResultadoDxcc Componer(
        ReglaDxcc regla, EntidadDxcc entidad, IndicativoAnalizado analisis, bool exacta) => new()
        {
            Entidad = entidad,
            ZonaCq = regla.ZonaCq ?? entidad.ZonaCq,
            ZonaItu = regla.ZonaItu ?? entidad.ZonaItu,
            Continente = regla.Continente ?? entidad.Continente,
            Coordenada = regla.Latitud is { } lat && regla.Longitud is { } lon
                ? new Coordenada(lat, lon)
                : entidad.Coordenada,
            PrefijoCoincidente = regla.Clave,
            EsCoincidenciaExacta = exacta,
            NecesitaRevision = !exacta && analisis.Ambiguo,
        };
}
