using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.CasosDeUso;
using Nodisla.Cuaderno.Aplicacion.Pruebas.Dobles;

namespace Nodisla.Cuaderno.Aplicacion.Pruebas;

public sealed class EliminarQsoPruebas
{
    private readonly RepositorioQsoDoble _cuaderno = new();

    [Fact]
    public async Task Borra_el_contacto_que_existe()
    {
        var qso = _cuaderno.Sembrar(Ayuda.Qso());
        var caso = new EliminarQso(_cuaderno);

        var borrado = await caso.EjecutarAsync(qso.Id);

        borrado.Should().BeTrue();
        _cuaderno.Contenido.Should().BeEmpty();
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-3L)]
    [InlineData(77L)]
    public async Task Devuelve_falso_si_el_contacto_no_esta(long id)
    {
        _cuaderno.Sembrar(Ayuda.Qso());
        var caso = new EliminarQso(_cuaderno);

        var borrado = await caso.EjecutarAsync(id);

        borrado.Should().BeFalse();
        _cuaderno.Contenido.Should().ContainSingle();
    }
}
