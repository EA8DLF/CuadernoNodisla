using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.CasosDeUso;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Aplicacion.Pruebas;

/// <summary>El perfil «Casa» sin localizador se completa con el de sus contactos, sin pisar nada.</summary>
public class CompletarPerfilDeEstacionPruebas
{
    private static Estacion Casa() => new()
    {
        NombrePerfil = "Casa",
        StationCallsign = Indicativo.Crudo("EA8DLF"),
        MyCity = "VILLA",
    };

    private static Qso Contacto(string estacion) => new()
    {
        StationCallsign = Indicativo.Crudo(estacion),
        MyGridsquare = Locator.Parse("JN00AA"),
        MyCity = "OTRA",
        MyRig = "YAESU FT-710",
        MyDxcc = 29,
    };

    [Fact]
    public void Rellena_el_localizador_y_lo_vacio_pero_no_pisa_lo_puesto()
    {
        var perfil = Casa();

        var rellenados = CompletarPerfilDeEstacion.DesdeContacto(perfil, Contacto("EA8DLF"));

        perfil.MyGridsquare.Valor.Should().Be("JN00AA");
        perfil.MyRig.Should().Be("YAESU FT-710");
        perfil.MyDxcc.Should().Be(29);
        perfil.MyCity.Should().Be("VILLA", "lo que el operador puso no se toca");
        rellenados.Should().Contain("MY_GRIDSQUARE").And.NotContain("MY_CITY");
    }

    [Fact]
    public void Un_contacto_de_otra_estacion_no_rellena_nada()
    {
        var perfil = Casa();

        CompletarPerfilDeEstacion.DesdeContacto(perfil, Contacto("EA8XX")).Should().BeEmpty();
        perfil.MyGridsquare.EsVacio.Should().BeTrue();
    }
}
