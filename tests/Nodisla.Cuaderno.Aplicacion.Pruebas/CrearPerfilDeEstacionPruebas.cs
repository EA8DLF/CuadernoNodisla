using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.CasosDeUso;
using Nodisla.Cuaderno.Aplicacion.Pruebas.Dobles;

namespace Nodisla.Cuaderno.Aplicacion.Pruebas;

public sealed class CrearPerfilDeEstacionPruebas
{
    private readonly RepositorioEstacionDoble _estaciones = new();
    private readonly CrearPerfilDeEstacion _caso;

    public CrearPerfilDeEstacionPruebas()
    {
        _caso = new CrearPerfilDeEstacion(_estaciones);
    }

    [Fact]
    public async Task Al_principio_no_hay_ningun_perfil()
    {
        (await _caso.HayAlgunoAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Crea_el_perfil_y_lo_deja_como_predeterminado()
    {
        var resultado = await _caso.EjecutarAsync(new PeticionDePerfil
        {
            Indicativo = "ea8dlf",
            Localizador = "il18sn",
            NombreOperador = "Jose",
            Localidad = "Santa Cruz de Tenerife",
        });

        resultado.Correcto.Should().BeTrue();
        resultado.Creado.Should().NotBeNull();
        resultado.Creado!.StationCallsign.Valor.Should().Be("EA8DLF");
        resultado.Creado.MyGridsquare.Valor.Should().Be("IL18SN");
        resultado.Creado.Predeterminado.Should().BeTrue();
        resultado.Creado.NombrePerfil.Should().Be(CrearPerfilDeEstacion.NombrePorOmision);

        (await _caso.HayAlgunoAsync()).Should().BeTrue();
        (await _estaciones.PredeterminadaAsync())!.StationCallsign.Valor.Should().Be("EA8DLF");
    }

    [Fact]
    public async Task Respeta_el_nombre_de_perfil_que_se_le_da()
    {
        var resultado = await _caso.EjecutarAsync(new PeticionDePerfil
        {
            Indicativo = "EA8DLF/P",
            NombrePerfil = "  Portable Teide  ",
        });

        resultado.Correcto.Should().BeTrue();
        resultado.Creado!.NombrePerfil.Should().Be("Portable Teide");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Rechaza_el_perfil_sin_indicativo(string indicativo)
    {
        var resultado = await _caso.EjecutarAsync(new PeticionDePerfil { Indicativo = indicativo });

        resultado.Correcto.Should().BeFalse();
        resultado.Errores.Should().Contain("Falta el indicativo de la estación.");
        (await _caso.HayAlgunoAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Rechaza_el_indicativo_que_no_tiene_forma_valida()
    {
        var resultado = await _caso.EjecutarAsync(new PeticionDePerfil { Indicativo = "XXXX" });

        resultado.Correcto.Should().BeFalse();
        resultado.Errores.Should().ContainSingle();
    }

    [Fact]
    public async Task Rechaza_el_localizador_que_no_es_valido()
    {
        var resultado = await _caso.EjecutarAsync(new PeticionDePerfil
        {
            Indicativo = "EA8DLF",
            Localizador = "ZZ99ZZ",
        });

        resultado.Correcto.Should().BeFalse();
        resultado.Errores.Should().Contain(e => e.Contains("localizador", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task El_localizador_es_opcional()
    {
        var resultado = await _caso.EjecutarAsync(new PeticionDePerfil { Indicativo = "EA8DLF" });

        resultado.Correcto.Should().BeTrue();
        resultado.Creado!.MyGridsquare.EsVacio.Should().BeTrue();
    }

    [Fact]
    public async Task El_perfil_recien_creado_ya_sirve_para_registrar_contactos()
    {
        await _caso.EjecutarAsync(new PeticionDePerfil { Indicativo = "EA8DLF", Localizador = "IL18SN" });

        var cuaderno = new RepositorioQsoDoble();
        var registrar = new RegistrarQso(cuaderno, _estaciones);

        var resultado = await registrar.EjecutarAsync(new PeticionDeRegistro { Qso = Ayuda.Qso() });

        resultado.Correcto.Should().BeTrue();
        cuaderno.Contenido.Should().ContainSingle()
            .Which.StationCallsign.Valor.Should().Be("EA8DLF");
    }
}
