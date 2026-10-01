using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Entidades;

namespace Nodisla.Cuaderno.Ui.Desarrollo;

/// <summary>
/// Historial de diplomas emitidos en memoria, para los puertos simulados y las pruebas. Numera
/// igual que la base de datos: correlativo por serie, sin distinguir mayusculas.
/// </summary>
public sealed class RepositorioDiplomasEmitidosEnMemoria : IRepositorioDiplomasEmitidos
{
    private readonly List<DiplomaEmitido> _emitidos = [];
    private readonly object _cerrojo = new();
    private long _proximoId = 1;

    /// <inheritdoc />
    public Task<int> SiguienteNumeroAsync(string serie, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serie);
        lock (_cerrojo)
        {
            return Task.FromResult(Siguiente(serie));
        }
    }

    /// <inheritdoc />
    public Task<DiplomaEmitido> EmitirAsync(DiplomaEmitido diploma, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(diploma);
        ArgumentException.ThrowIfNullOrWhiteSpace(diploma.Serie);
        lock (_cerrojo)
        {
            diploma.Id = _proximoId++;
            diploma.Numero = Siguiente(diploma.Serie);
            _emitidos.Add(Copia(diploma));
            return Task.FromResult(diploma);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<DiplomaEmitido>> ListarAsync(int limite = 500, CancellationToken ct = default)
    {
        lock (_cerrojo)
        {
            return Task.FromResult<IReadOnlyList<DiplomaEmitido>>(
                _emitidos.OrderByDescending(d => d.Id).Take(Math.Max(1, limite)).Select(Copia).ToList());
        }
    }

    /// <inheritdoc />
    public Task<DiplomaEmitido?> ObtenerAsync(long id, CancellationToken ct = default)
    {
        lock (_cerrojo)
        {
            return Task.FromResult(_emitidos.FirstOrDefault(d => d.Id == id) is { } d ? Copia(d) : null);
        }
    }

    /// <inheritdoc />
    public Task MarcarEnviadoAsync(long id, string correo, DateTimeOffset cuando, CancellationToken ct = default)
    {
        lock (_cerrojo)
        {
            var d = _emitidos.FirstOrDefault(e => e.Id == id)
                ?? throw new InvalidOperationException($"No existe ningún diploma emitido con identificador {id}.");
            d.Correo = correo.Trim();
            d.EnviadoUtc = cuando;
        }

        return Task.CompletedTask;
    }

    private int Siguiente(string serie) =>
        (_emitidos.Where(d => string.Equals(d.Serie, serie, StringComparison.OrdinalIgnoreCase)).Select(d => (int?)d.Numero).Max() ?? 0) + 1;

    private static DiplomaEmitido Copia(DiplomaEmitido d) => new()
    {
        Id = d.Id,
        Serie = d.Serie,
        Numero = d.Numero,
        Origen = d.Origen,
        Indicativo = d.Indicativo,
        Nombre = d.Nombre,
        NombreDelDiploma = d.NombreDelDiploma,
        Categoria = d.Categoria,
        EmitidoUtc = d.EmitidoUtc,
        PlantillaId = d.PlantillaId,
        PlantillaNombre = d.PlantillaNombre,
        Referencias = d.Referencias,
        Qsos = d.Qsos,
        Correo = d.Correo,
        EnviadoUtc = d.EnviadoUtc,
        Datos = d.Datos,
    };
}
