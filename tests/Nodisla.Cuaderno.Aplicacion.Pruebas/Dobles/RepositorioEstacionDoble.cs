using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Entidades;

namespace Nodisla.Cuaderno.Aplicacion.Pruebas.Dobles;

/// <summary>Perfiles de estacion en memoria para las pruebas.</summary>
public sealed class RepositorioEstacionDoble : IRepositorioEstacion
{
    private readonly List<Estacion> _estaciones = [];
    private long _siguienteId = 1;

    /// <summary>Mete un perfil directamente y devuelve el mismo objeto ya con identificador.</summary>
    public Estacion Sembrar(Estacion estacion)
    {
        ArgumentNullException.ThrowIfNull(estacion);
        estacion.Id = _siguienteId++;
        _estaciones.Add(estacion);
        return estacion;
    }

    public Task<IReadOnlyList<Estacion>> TodasAsync(bool soloActivas = true, CancellationToken ct = default)
    {
        IReadOnlyList<Estacion> resultado = _estaciones.Where(e => !soloActivas || e.Activo).ToList();
        return Task.FromResult(resultado);
    }

    public Task<Estacion?> ObtenerAsync(long id, CancellationToken ct = default) =>
        Task.FromResult(_estaciones.FirstOrDefault(e => e.Id == id));

    public Task<Estacion?> PredeterminadaAsync(CancellationToken ct = default) =>
        Task.FromResult(_estaciones.FirstOrDefault(e => e.Predeterminado) ?? _estaciones.FirstOrDefault());

    public Task<long> AnadirAsync(Estacion estacion, CancellationToken ct = default) =>
        Task.FromResult(Sembrar(estacion).Id);

    public Task ActualizarAsync(Estacion estacion, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(estacion);
        var i = _estaciones.FindIndex(e => e.Id == estacion.Id);
        if (i >= 0) _estaciones[i] = estacion;
        return Task.CompletedTask;
    }

    public Task EstablecerPredeterminadaAsync(long id, CancellationToken ct = default)
    {
        foreach (var e in _estaciones) e.Predeterminado = e.Id == id;
        return Task.CompletedTask;
    }
}
