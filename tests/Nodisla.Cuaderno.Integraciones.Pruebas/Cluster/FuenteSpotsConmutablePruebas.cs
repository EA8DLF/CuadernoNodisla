using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Integraciones.Cluster;

namespace Nodisla.Cuaderno.Integraciones.Pruebas.Cluster;

/// <summary>Fuente de anuncios de mentira que apunta lo que le hacen.</summary>
internal sealed class FuenteDeMentira(string nombre) : IFuenteSpots
{
    private readonly List<string> _pasos = [];

    /// <summary>Lo que le han hecho, en orden.</summary>
    internal IReadOnlyList<string> Pasos => _pasos;

    /// <inheritdoc />
    public string Nombre { get; } = nombre;

    /// <inheritdoc />
    public EstadoDeConexion Estado { get; private set; } = EstadoDeConexion.Desconectado;

    /// <inheritdoc />
    public event EventHandler<EstadoDeConexion>? EstadoCambiado;

    /// <inheritdoc />
    public event EventHandler<Spot>? SpotRecibido;

    /// <inheritdoc />
    public event EventHandler<string>? LineaRecibida;

    /// <summary>Hace como que llega un anuncio.</summary>
    internal void Anunciar(string indicativo) => SpotRecibido?.Invoke(this, new Spot(
        Indicativo.Parse(indicativo),
        Frecuencia.DesdeMegahercios(14.074m),
        Indicativo.Parse("EA8DLF"),
        null,
        DateTimeOffset.UtcNow,
        Nombre));

    /// <summary>Hace como que llega una linea de servicio.</summary>
    internal void Decir(string linea) => LineaRecibida?.Invoke(this, linea);

    /// <summary>Hace como que cambia el estado.</summary>
    internal void Cambiar(EstadoDeConexion estado)
    {
        Estado = estado;
        EstadoCambiado?.Invoke(this, estado);
    }

    /// <inheritdoc />
    public Task ConectarAsync(CancellationToken ct = default)
    {
        _pasos.Add("conectar");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DesconectarAsync(CancellationToken ct = default)
    {
        _pasos.Add("desconectar");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task EnviarAsync(string orden, CancellationToken ct = default)
    {
        _pasos.Add($"enviar:{orden}");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _pasos.Add("soltar");
        return ValueTask.CompletedTask;
    }
}

/// <summary>El intermediario que deja cambiar de nodo sin cerrar el programa.</summary>
public class FuenteSpotsConmutablePruebas
{
    [Fact]
    public void Delega_el_nombre_y_el_estado_en_la_fuente_que_hay()
    {
        var dentro = new FuenteDeMentira("EA4RCH");
        var conmutable = new FuenteSpotsConmutable(dentro);

        conmutable.Nombre.Should().Be("EA4RCH");
        dentro.Cambiar(EstadoDeConexion.Conectado);
        conmutable.Estado.Should().Be(EstadoDeConexion.Conectado);
    }

    [Fact]
    public async Task Al_sustituir_desconecta_y_suelta_la_anterior()
    {
        var vieja = new FuenteDeMentira("vieja");
        var nueva = new FuenteDeMentira("nueva");
        var conmutable = new FuenteSpotsConmutable(vieja);

        await conmutable.SustituirAsync(nueva);

        // Dos conexiones a la vez no valen: los nodos cuentan las sesiones por indicativo.
        vieja.Pasos.Should().Equal("desconectar", "soltar");
        nueva.Pasos.Should().BeEmpty("conectar lo pide el operador, no el cambio de nodo");
        conmutable.Nombre.Should().Be("nueva");
    }

    [Fact]
    public async Task Los_anuncios_y_las_lineas_pasan_a_ser_los_de_la_fuente_nueva()
    {
        var vieja = new FuenteDeMentira("vieja");
        var nueva = new FuenteDeMentira("nueva");
        var conmutable = new FuenteSpotsConmutable(vieja);

        var anuncios = new List<string>();
        var lineas = new List<string>();
        conmutable.SpotRecibido += (_, spot) => anuncios.Add(spot.Indicativo.Valor);
        conmutable.LineaRecibida += (_, linea) => lineas.Add(linea);

        await conmutable.SustituirAsync(nueva);

        vieja.Anunciar("EA1AAA");
        vieja.Decir("de la vieja");
        nueva.Anunciar("EA8DLF");
        nueva.Decir("de la nueva");

        anuncios.Should().Equal("EA8DLF");
        lineas.Should().Equal("de la nueva");
    }

    [Fact]
    public async Task Avisa_del_cambio_de_fuente_y_del_estado_de_la_nueva()
    {
        var nueva = new FuenteDeMentira("nueva");
        var conmutable = new FuenteSpotsConmutable(new FuenteDeMentira("vieja"));

        IFuenteSpots? avisada = null;
        var estados = new List<EstadoDeConexion>();
        conmutable.FuenteCambiada += (_, fuente) => avisada = fuente;
        conmutable.EstadoCambiado += (_, estado) => estados.Add(estado);

        await conmutable.SustituirAsync(nueva);

        avisada.Should().BeSameAs(nueva);
        estados.Should().Equal(EstadoDeConexion.Desconectado);
    }

    [Fact]
    public async Task Soltar_el_intermediario_cierra_la_fuente_que_lleva_dentro()
    {
        var dentro = new FuenteDeMentira("dentro");
        var conmutable = new FuenteSpotsConmutable(dentro);

        await conmutable.DisposeAsync();

        dentro.Pasos.Should().Equal("desconectar", "soltar");
    }
}
