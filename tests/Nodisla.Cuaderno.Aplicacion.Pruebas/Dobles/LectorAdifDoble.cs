using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Entidades;

namespace Nodisla.Cuaderno.Aplicacion.Pruebas.Dobles;

/// <summary>
/// Lector de ADIF en memoria: el «fichero» es la lista de plantillas que se le pasa.
/// </summary>
/// <remarks>
/// Cada plantilla es una funcion y no un contacto ya hecho a proposito. Importar el mismo
/// fichero dos veces es una prueba obligatoria, y el caso de uso se queda con los objetos que
/// le entrega el lector: si las dos lecturas devolvieran los mismos, la segunda estaria
/// fundiendo un contacto consigo mismo y la prueba no probaria nada.
/// </remarks>
public sealed class LectorAdifDoble(params Func<Qso>[] plantillas) : ILectorAdif
{
    private readonly Func<Qso>[] _plantillas = plantillas ?? [];

    /// <summary>Avisos que devuelve la lectura, como los del analizador de verdad.</summary>
    public IReadOnlyList<AvisoAdif> Avisos { get; set; } = [];

    /// <summary>Veces que se ha leido el fichero.</summary>
    public int Lecturas { get; private set; }

    public Task<LecturaAdif> LeerAsync(Stream origen, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(origen);
        Lecturas++;

        IReadOnlyList<Qso> qsos = _plantillas.Select(p => p()).ToList();
        return Task.FromResult(new LecturaAdif { Qsos = qsos, Avisos = Avisos });
    }
}
