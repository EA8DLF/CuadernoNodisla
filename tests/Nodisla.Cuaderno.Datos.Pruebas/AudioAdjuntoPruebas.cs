using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Nodisla.Cuaderno.Datos.Repositorios;

namespace Nodisla.Cuaderno.Datos.Pruebas;

/// <summary>El audio adjunto a un contacto: la migracion y que se guarda y no se pierde al editar.</summary>
public sealed class AudioAdjuntoPruebas : IAsyncLifetime
{
    private CuadernoDePrueba cuaderno = null!;

    /// <inheritdoc/>
    public async Task InitializeAsync() => cuaderno = await CuadernoDePrueba.CrearAsync();

    /// <inheritdoc/>
    public async Task DisposeAsync() => await cuaderno.DisposeAsync();

    [Fact]
    public async Task La_migracion_anade_la_columna_audio_adjunto()
    {
        await using var conexion = await cuaderno.AbrirConexionAsync();
        await using var orden = conexion.CreateCommand();
        orden.CommandText = "SELECT COUNT(*) FROM pragma_table_info('qso') WHERE name = 'audio_adjunto';";
        (await orden.ExecuteScalarAsync()).Should().Be(1L);
    }

    [Fact]
    public void El_modelo_y_la_ultima_migracion_coinciden()
    {
        // Sin dotnet-ef a mano: si el modelo tuviera cambios sin migracion, el comparador de EF
        // los veria aqui.
        using var contexto = cuaderno.CrearContexto();
        var migraciones = contexto.GetService<IMigrationsAssembly>();
        var inicializador = contexto.GetService<IModelRuntimeInitializer>();
        var instantanea = migraciones.ModelSnapshot!.Model;
        if (instantanea is IMutableModel mutable) instantanea = mutable.FinalizeModel();
        instantanea = inicializador.Initialize(instantanea);

        var diferencias = contexto.GetService<IMigrationsModelDiffer>().GetDifferences(
            instantanea.GetRelationalModel(),
            contexto.GetService<IDesignTimeModel>().Model.GetRelationalModel());

        diferencias.Should().BeEmpty();
    }

    [Fact]
    public async Task El_audio_adjunto_se_guarda_y_sobrevive_a_una_edicion()
    {
        await using var contexto = cuaderno.CrearContexto();
        var repositorio = new RepositorioQso(contexto);
        var qso = FabricaDeContactos.Crear();
        qso.AudioAdjunto = "qso-20260523-095631-DL1ABC.wav";
        var id = await repositorio.AnadirAsync(qso);

        await using var otro = cuaderno.CrearContexto();
        var leido = await new RepositorioQso(otro).ObtenerAsync(id);
        leido!.AudioAdjunto.Should().Be("qso-20260523-095631-DL1ABC.wav");

        leido.Comentario = "Editado";
        await new RepositorioQso(otro).ActualizarAsync(leido);

        await using var tercero = cuaderno.CrearContexto();
        (await new RepositorioQso(tercero).ObtenerAsync(id))!.AudioAdjunto.Should().Be("qso-20260523-095631-DL1ABC.wav");
    }
}
