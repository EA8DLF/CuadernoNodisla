using FluentAssertions;
using Nodisla.Cuaderno.Datos.Repositorios;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Datos.Pruebas;

/// <summary>Las rondas de control sobre el esquema de verdad, migrado en un fichero temporal.</summary>
public sealed class RepositorioRondasPruebas : IAsyncLifetime
{
    private CuadernoDePrueba cuaderno = null!;
    private ContextoCuaderno contexto = null!;
    private RepositorioRondas repositorio = null!;

    /// <inheritdoc/>
    public async Task InitializeAsync()
    {
        cuaderno = await CuadernoDePrueba.CrearAsync();
        contexto = cuaderno.CrearContexto();
        repositorio = new RepositorioRondas(contexto);
    }

    /// <inheritdoc/>
    public async Task DisposeAsync() => await cuaderno.DisposeAsync();

    private static RondaDeControl Ronda(string nombre = "Red de los domingos") => new()
    {
        Nombre = nombre,
        Band = Banda.Parse("40m"),
        Mode = Modo.Parse("SSB"),
        Freq = Frecuencia.DesdeMegahercios(7.110m),
        InicioUtc = FabricaDeContactos.Instante,
    };

    private static ParticipanteDeRonda Participante(long rondaId, string call = "EA1ABC") => new()
    {
        RondaId = rondaId,
        Call = Indicativo.Parse(call),
        EntradaUtc = FabricaDeContactos.Instante,
    };

    [Fact]
    public async Task AbrirYObtenerAbiertaDevuelveLaRonda()
    {
        var id = await repositorio.AbrirAsync(Ronda());

        var abierta = await repositorio.ObtenerAbiertaAsync();

        abierta.Should().NotBeNull();
        abierta!.Id.Should().Be(id);
        abierta.EstaAbierta.Should().BeTrue();
    }

    [Fact]
    public async Task CerrarHaceQueYaNoHayaRondaAbierta()
    {
        var id = await repositorio.AbrirAsync(Ronda());

        await repositorio.CerrarAsync(id, FabricaDeContactos.Instante.AddHours(1));

        (await repositorio.ObtenerAbiertaAsync()).Should().BeNull();
        var leida = await repositorio.ObtenerAsync(id);
        leida!.EstaAbierta.Should().BeFalse();
    }

    [Fact]
    public async Task AnadirParticipanteQuedaColgadoDeLaRonda()
    {
        var id = await repositorio.AbrirAsync(Ronda());

        await repositorio.AnadirParticipanteAsync(Participante(id));
        await repositorio.AnadirParticipanteAsync(Participante(id, "EA2XYZ"));

        var leida = await repositorio.ObtenerAsync(id);
        leida!.Participantes.Should().HaveCount(2);
        leida.Participantes.Select(p => p.Call.Valor).Should().Contain(["EA1ABC", "EA2XYZ"]);
    }

    [Fact]
    public async Task ActualizarParticipanteGuardaElRstYElTrabajado()
    {
        var id = await repositorio.AbrirAsync(Ronda());
        var idParticipante = await repositorio.AnadirParticipanteAsync(Participante(id));

        await using var otro = cuaderno.CrearContexto();
        var repoOtro = new RepositorioRondas(otro);
        var cargada = await repoOtro.ObtenerAsync(id);
        var participante = cargada!.Participantes.Single();
        participante.RstEnviado = Informe.Parse("59");
        participante.RstRecibido = Informe.Parse("57");
        participante.Trabajado = true;
        participante.QsoId = 42;
        await repoOtro.ActualizarParticipanteAsync(participante);

        await using var lectura = cuaderno.CrearContexto();
        var leida = await new RepositorioRondas(lectura).ObtenerAsync(id);
        var leido = leida!.Participantes.Single();
        leido.Id.Should().Be(idParticipante);
        leido.RstEnviado.Texto.Should().Be("59");
        leido.RstRecibido.Texto.Should().Be("57");
        leido.Trabajado.Should().BeTrue();
        leido.QsoId.Should().Be(42);
    }

    [Fact]
    public async Task EliminarParticipanteLoQuitaDeLaRonda()
    {
        var id = await repositorio.AbrirAsync(Ronda());
        var idParticipante = await repositorio.AnadirParticipanteAsync(Participante(id));

        await repositorio.EliminarParticipanteAsync(idParticipante);

        var leida = await repositorio.ObtenerAsync(id);
        leida!.Participantes.Should().BeEmpty();
    }

    [Fact]
    public async Task CerrarLaRondaNoBorraSusParticipantes()
    {
        var id = await repositorio.AbrirAsync(Ronda());
        await repositorio.AnadirParticipanteAsync(Participante(id));

        await repositorio.CerrarAsync(id, FabricaDeContactos.Instante.AddHours(1));

        var leida = await repositorio.ObtenerAsync(id);
        leida!.Participantes.Should().ContainSingle();
    }

    [Fact]
    public async Task ListarDevuelveLasRondasDeLaMasRecienteALaMasAntigua()
    {
        var primera = Ronda("Primera");
        primera.InicioUtc = FabricaDeContactos.Instante;
        await repositorio.AbrirAsync(primera);
        await repositorio.CerrarAsync(
            (await repositorio.ObtenerAbiertaAsync())!.Id, FabricaDeContactos.Instante.AddMinutes(30));

        var segunda = Ronda("Segunda");
        segunda.InicioUtc = FabricaDeContactos.Instante.AddHours(1);
        await repositorio.AbrirAsync(segunda);

        var todas = await repositorio.ListarAsync();

        todas.Should().HaveCount(2);
        todas[0].Nombre.Should().Be("Segunda");
        todas[1].Nombre.Should().Be("Primera");
    }
}
