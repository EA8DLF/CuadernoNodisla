using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Nodisla.Cuaderno.Datos.Repositorios;
using Nodisla.Cuaderno.Dominio.Entidades;

namespace Nodisla.Cuaderno.Datos.Pruebas;

/// <summary>El historial de diplomas emitidos y su numeracion, sobre el esquema migrado de verdad.</summary>
public sealed class RepositorioDiplomasEmitidosPruebas : IAsyncLifetime
{
    private CuadernoDePrueba cuaderno = null!;
    private ContextoCuaderno contexto = null!;
    private RepositorioDiplomasEmitidos repositorio = null!;

    /// <inheritdoc/>
    public async Task InitializeAsync()
    {
        cuaderno = await CuadernoDePrueba.CrearAsync();
        contexto = cuaderno.CrearContexto();
        repositorio = new RepositorioDiplomasEmitidos(contexto);
    }

    /// <inheritdoc/>
    public async Task DisposeAsync()
    {
        await contexto.DisposeAsync();
        await cuaderno.DisposeAsync();
    }

    private static DiplomaEmitido Diploma(string serie, string indicativo = "EA1ABC") => new()
    {
        Serie = serie,
        Indicativo = indicativo,
        NombreDelDiploma = "Diploma de prueba",
        EmitidoUtc = FabricaDeContactos.Instante,
    };

    [Fact]
    public async Task La_numeracion_es_correlativa_por_serie_y_no_se_repite()
    {
        (await repositorio.SiguienteNumeroAsync("CLUB")).Should().Be(1);

        var a = await repositorio.EmitirAsync(Diploma("CLUB"));
        var b = await repositorio.EmitirAsync(Diploma("CLUB", "EA2XYZ"));
        var otra = await repositorio.EmitirAsync(Diploma("OTRA"));
        var c = await repositorio.EmitirAsync(Diploma("club"));

        a.Numero.Should().Be(1);
        b.Numero.Should().Be(2);
        otra.Numero.Should().Be(1, "cada serie lleva su numeracion");
        c.Numero.Should().Be(3, "la serie no distingue mayusculas");
        (await repositorio.SiguienteNumeroAsync("CLUB")).Should().Be(4);

        // Un segundo contexto (otra ventana) sigue la misma cuenta.
        await using var otroContexto = cuaderno.CrearContexto();
        (await new RepositorioDiplomasEmitidos(otroContexto).EmitirAsync(Diploma("CLUB"))).Numero.Should().Be(4);
    }

    [Fact]
    public async Task El_indice_unico_impide_dos_diplomas_con_el_mismo_numero()
    {
        await repositorio.EmitirAsync(Diploma("CLUB"));
        contexto.DiplomasEmitidos.Add(new DiplomaEmitido { Serie = "CLUB", Numero = 1, Indicativo = "EA9ZZZ", NombreDelDiploma = "x" });

        var guardar = () => contexto.SaveChangesAsync();
        await guardar.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Listar_obtener_y_marcar_enviado()
    {
        var a = await repositorio.EmitirAsync(Diploma("CLUB"));
        var b = await repositorio.EmitirAsync(Diploma("CLUB", "EA2XYZ"));
        var cuando = FabricaDeContactos.Instante.AddDays(1);

        await repositorio.MarcarEnviadoAsync(a.Id, "ea1abc@ejemplo.es", cuando);

        var lista = await repositorio.ListarAsync();
        lista.Select(d => d.Id).Should().Equal(b.Id, a.Id);
        var leido = (await repositorio.ObtenerAsync(a.Id))!;
        leido.Correo.Should().Be("ea1abc@ejemplo.es");
        leido.EnviadoUtc.Should().Be(cuando);
        leido.Origen.Should().Be(OrigenDeDiplomaEmitido.Emitido);
    }
}
