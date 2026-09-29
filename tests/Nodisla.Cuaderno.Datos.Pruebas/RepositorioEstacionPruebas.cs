using FluentAssertions;
using Nodisla.Cuaderno.Datos.Repositorios;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Datos.Pruebas;

/// <summary>Los perfiles de estacion.</summary>
public sealed class RepositorioEstacionPruebas : IAsyncLifetime
{
    private CuadernoDePrueba cuaderno = null!;
    private ContextoCuaderno contexto = null!;
    private RepositorioEstacion repositorio = null!;

    /// <inheritdoc/>
    public async Task InitializeAsync()
    {
        cuaderno = await CuadernoDePrueba.CrearAsync();
        contexto = cuaderno.CrearContexto();
        repositorio = new RepositorioEstacion(contexto);
    }

    /// <inheritdoc/>
    public async Task DisposeAsync() => await cuaderno.DisposeAsync();

    private static Estacion Perfil(string nombre, string indicativo = "EA8DLF") => new()
    {
        NombrePerfil = nombre,
        StationCallsign = Indicativo.Parse(indicativo),
        MyGridsquare = Locator.Parse("IL18qk"),
        MyCity = "Las Palmas",
        TxPwrDefecto = 100,
    };

    [Fact]
    public async Task AnadirYObtenerDevuelveElPerfil()
    {
        var id = await repositorio.AnadirAsync(Perfil("Casa"));

        var leido = await repositorio.ObtenerAsync(id);

        leido.Should().NotBeNull();
        leido!.NombrePerfil.Should().Be("Casa");
        leido.StationCallsign.Valor.Should().Be("EA8DLF");
        leido.MyGridsquare.Valor.Should().Be("IL18QK");
        leido.Activo.Should().BeTrue();
    }

    [Fact]
    public async Task TodasPuedeDejarFueraLosPerfilesRetirados()
    {
        await repositorio.AnadirAsync(Perfil("Casa"));
        var retirado = Perfil("Portable viejo");
        retirado.Activo = false;
        await repositorio.AnadirAsync(retirado);

        (await repositorio.TodasAsync()).Should().HaveCount(1);
        (await repositorio.TodasAsync(soloActivas: false)).Should().HaveCount(2);
    }

    [Fact]
    public async Task ActualizarGuardaLosCambios()
    {
        var id = await repositorio.AnadirAsync(Perfil("Casa"));

        await using var otro = cuaderno.CrearContexto();
        var repoOtro = new RepositorioEstacion(otro);
        var cargado = await repoOtro.ObtenerAsync(id);
        cargado!.MyRig = "IC-7300";
        cargado.MyAntenna = "Hexbeam";
        await repoOtro.ActualizarAsync(cargado);

        // Se lee desde un contexto nuevo: el que ya tenia el perfil cargado devolveria su copia.
        await using var lectura = cuaderno.CrearContexto();
        var leido = await new RepositorioEstacion(lectura).ObtenerAsync(id);
        leido!.MyRig.Should().Be("IC-7300");
        leido.MyAntenna.Should().Be("Hexbeam");
    }

    [Fact]
    public async Task EstablecerPredeterminadaDesmarcaLaAnterior()
    {
        var casa = await repositorio.AnadirAsync(Perfil("Casa"));
        var teide = await repositorio.AnadirAsync(Perfil("Portable Teide"));

        await repositorio.EstablecerPredeterminadaAsync(casa);
        (await repositorio.PredeterminadaAsync())!.Id.Should().Be(casa);

        await repositorio.EstablecerPredeterminadaAsync(teide);

        var predeterminada = await repositorio.PredeterminadaAsync();
        predeterminada!.Id.Should().Be(teide);

        var todas = await repositorio.TodasAsync();
        todas.Count(e => e.Predeterminado).Should().Be(1);
    }

    [Fact]
    public async Task PredeterminadaDevuelveElUnicoPerfilCuandoNoHayNingunoMarcado()
    {
        var id = await repositorio.AnadirAsync(Perfil("Casa"));

        (await repositorio.PredeterminadaAsync())!.Id.Should().Be(id);

        await repositorio.AnadirAsync(Perfil("Portable Teide"));
        (await repositorio.PredeterminadaAsync()).Should().BeNull();
    }

    [Fact]
    public async Task EstablecerPredeterminadaAvisaSiElPerfilNoExiste()
    {
        var marcar = async () => await repositorio.EstablecerPredeterminadaAsync(999);

        await marcar.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ElPerfilSeCopiaSobreElContactoAlRegistrarlo()
    {
        var estacion = Perfil("Casa");
        await repositorio.AnadirAsync(estacion);

        var qso = FabricaDeContactos.Crear();
        estacion.AplicarA(qso);

        var repoQso = new RepositorioQso(contexto);
        var id = await repoQso.AnadirAsync(qso);

        await using var otro = cuaderno.CrearContexto();
        var leido = await new RepositorioQso(otro).ObtenerAsync(id);

        leido!.EstacionId.Should().Be(estacion.Id);
        leido.MyGridsquare.Valor.Should().Be("IL18QK");
        leido.MyCity.Should().Be("Las Palmas");
    }
}
