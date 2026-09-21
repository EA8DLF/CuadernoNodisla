using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Ui.Desarrollo;

/// <summary>
/// Perfiles de estacion en memoria, para arrancar la ventana sin la capa de datos.
/// </summary>
public sealed class RepositorioEstacionEnMemoria : IRepositorioEstacion
{
    private readonly List<Estacion> _estaciones;

    /// <summary>
    /// Crea dos perfiles de ejemplo: el de casa y uno portable. Con
    /// <paramref name="conPerfilesDeEjemplo"/> en falso arranca vacio, que es como se prueba
    /// el primer arranque.
    /// </summary>
    public RepositorioEstacionEnMemoria(bool conPerfilesDeEjemplo = true)
    {
        if (!conPerfilesDeEjemplo)
        {
            _estaciones = [];
            return;
        }

        _estaciones =
        [
            new Estacion
            {
                Id = 1,
                NombrePerfil = "Casa",
                StationCallsign = Indicativo.Parse("EA8DLF"),
                Operator = "EA8DLF",
                OwnerCallsign = "EA8DLF",
                MyName = "Jose",
                MyCity = "Santa Cruz de Tenerife",
                MyCountry = "Canary Islands",
                MyGridsquare = Locator.Parse("IL18SN"),
                MyCqZone = 33,
                MyItuZone = 36,
                MyDxcc = 29,
                MyRig = "Yaesu FT-991A",
                MyAntenna = "Dipolo multibanda",
                TxPwrDefecto = 100,
                Predeterminado = true,
            },
            new Estacion
            {
                Id = 2,
                NombrePerfil = "Portable Teide",
                StationCallsign = Indicativo.Parse("EA8DLF/P"),
                Operator = "EA8DLF",
                MyName = "Jose",
                MyCity = "Parque Nacional del Teide",
                MyCountry = "Canary Islands",
                MyGridsquare = Locator.Parse("IL28FC"),
                MyCqZone = 33,
                MyItuZone = 36,
                MyDxcc = 29,
                MyRig = "Yaesu FT-818",
                MyAntenna = "Vertical de hilo",
                TxPwrDefecto = 5,
            },
        ];
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Estacion>> TodasAsync(bool soloActivas = true, CancellationToken ct = default)
    {
        IReadOnlyList<Estacion> resultado = _estaciones.Where(e => !soloActivas || e.Activo).ToList();
        return Task.FromResult(resultado);
    }

    /// <inheritdoc />
    public Task<Estacion?> ObtenerAsync(long id, CancellationToken ct = default) =>
        Task.FromResult(_estaciones.FirstOrDefault(e => e.Id == id));

    /// <inheritdoc />
    public Task<Estacion?> PredeterminadaAsync(CancellationToken ct = default) =>
        Task.FromResult(_estaciones.FirstOrDefault(e => e.Predeterminado) ?? _estaciones.FirstOrDefault());

    /// <inheritdoc />
    public Task<long> AnadirAsync(Estacion estacion, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(estacion);
        estacion.Id = _estaciones.Count == 0 ? 1 : _estaciones.Max(e => e.Id) + 1;
        _estaciones.Add(estacion);
        return Task.FromResult(estacion.Id);
    }

    /// <inheritdoc />
    public Task ActualizarAsync(Estacion estacion, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(estacion);
        var i = _estaciones.FindIndex(e => e.Id == estacion.Id);
        if (i >= 0) _estaciones[i] = estacion;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task EstablecerPredeterminadaAsync(long id, CancellationToken ct = default)
    {
        foreach (var e in _estaciones) e.Predeterminado = e.Id == id;
        return Task.CompletedTask;
    }
}
