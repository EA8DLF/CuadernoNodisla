using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nodisla.Cuaderno.Aplicacion.Puertos;

namespace Nodisla.Cuaderno.Propagacion.Solar;

/// <summary>
/// Guarda en un fichero los ultimos indices solares que se pudieron traer, para que el cuaderno
/// arranque con algo aunque no haya red.
/// </summary>
/// <remarks>
/// Lo guardado nunca se ensena como si fuera de ahora: la lectura conserva la fecha de la medida
/// y se marca como venida del fichero, de manera que la interfaz pueda decirlo.
/// </remarks>
/// <param name="ruta">Fichero donde se guarda.</param>
/// <param name="registro">Registro opcional.</param>
public sealed class CacheDeIndicesEnDisco(string ruta, ILogger<CacheDeIndicesEnDisco>? registro = null)
{
    private readonly ILogger registroEfectivo = registro ?? NullLogger<CacheDeIndicesEnDisco>.Instance;
    private readonly object cerrojo = new();

    private static readonly JsonSerializerOptions Formato = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    /// <summary>Fichero donde se guardan los indices.</summary>
    public string Ruta { get; } = ruta;

    /// <summary>Lee los indices guardados.</summary>
    /// <returns>Lo guardado, marcado como venido del fichero, o nulo si no hay nada legible.</returns>
    public LecturaDeIndices? Leer()
    {
        try
        {
            lock (cerrojo)
            {
                if (!File.Exists(Ruta))
                {
                    return null;
                }

                var json = File.ReadAllText(Ruta);
                var guardado = JsonSerializer.Deserialize<IndicesGuardados>(json, Formato);
                return guardado?.ALectura();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            registroEfectivo.LogWarning(ex, "No se pudo leer el cache de indices solares de {Ruta}.", Ruta);
            return null;
        }
    }

    /// <summary>Guarda unos indices recien traidos.</summary>
    /// <param name="lectura">Lo que se acaba de traer.</param>
    public void Guardar(LecturaDeIndices lectura)
    {
        try
        {
            lock (cerrojo)
            {
                var carpeta = Path.GetDirectoryName(Path.GetFullPath(Ruta));
                if (!string.IsNullOrEmpty(carpeta))
                {
                    Directory.CreateDirectory(carpeta);
                }

                var json = JsonSerializer.Serialize(IndicesGuardados.Desde(lectura), Formato);
                // Se escribe a un temporal y se mueve encima: asi un corte a media escritura no
                // deja el fichero a medias.
                var temporal = Ruta + ".tmp";
                File.WriteAllText(temporal, json);
                File.Move(temporal, Ruta, overwrite: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            registroEfectivo.LogWarning(ex, "No se pudo guardar el cache de indices solares en {Ruta}.", Ruta);
        }
    }

    /// <summary>Forma en que los indices viajan al fichero.</summary>
    private sealed record IndicesGuardados
    {
        public double? FlujoSolar { get; init; }

        public double? IndiceA { get; init; }

        public double? IndiceK { get; init; }

        public double? ManchasSolares { get; init; }

        public double? VientoSolarKmS { get; init; }

        public double? CampoBz { get; init; }

        public bool Tormenta { get; init; }

        public DateTimeOffset MedidoUtc { get; init; }

        public DateTimeOffset ObtenidoUtc { get; init; }

        public string Fuente { get; init; } = string.Empty;

        public static IndicesGuardados Desde(LecturaDeIndices lectura) => new()
        {
            FlujoSolar = lectura.Indices.FlujoSolar,
            IndiceA = lectura.Indices.IndiceA,
            IndiceK = lectura.Indices.IndiceK,
            ManchasSolares = lectura.Indices.ManchasSolares,
            VientoSolarKmS = lectura.Indices.VientoSolarKmS,
            CampoBz = lectura.Indices.CampoBz,
            Tormenta = lectura.Indices.Tormenta,
            MedidoUtc = lectura.Indices.MedidoUtc,
            ObtenidoUtc = lectura.ObtenidoUtc,
            Fuente = lectura.Fuente,
        };

        public LecturaDeIndices ALectura() => new(
            new IndicesSolares(
                FlujoSolar,
                IndiceA,
                IndiceK,
                ManchasSolares,
                VientoSolarKmS,
                CampoBz,
                Tormenta,
                MedidoUtc),
            ObtenidoUtc,
            OrigenDeLosIndices.Cache,
            string.IsNullOrEmpty(Fuente) ? "NOAA SWPC" : Fuente);
    }
}
