using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.CasosDeUso;
using Nodisla.Cuaderno.Aplicacion.Pruebas.Dobles;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Aplicacion.Pruebas;

public sealed class ConsultarTrabajadoAntesPruebas
{
    private readonly RepositorioQsoDoble _cuaderno = new();
    private readonly ConsultarTrabajadoAntes _caso;

    public ConsultarTrabajadoAntesPruebas()
    {
        _caso = new ConsultarTrabajadoAntes(_cuaderno);
    }

    [Fact]
    public async Task Indicativo_que_no_esta_en_el_cuaderno()
    {
        _cuaderno.Sembrar(Ayuda.Qso(call: "EA1ABC"));

        var resultado = await _caso.EjecutarAsync("EA9ZZZ");

        resultado.TrabajadoAntes.Should().BeFalse();
        resultado.Veces.Should().Be(0);
        resultado.Resumen.Should().Be("Indicativo nuevo, no está en el cuaderno.");
    }

    [Fact]
    public async Task Resume_los_contactos_anteriores()
    {
        _cuaderno.Sembrar(Ayuda.Qso(call: "EA1ABC", banda: "20m", mhz: 14.2m, modo: "SSB",
            inicioUtc: new DateTimeOffset(2025, 5, 1, 10, 0, 0, TimeSpan.Zero)));
        _cuaderno.Sembrar(Ayuda.Qso(call: "EA1ABC", banda: "40m", mhz: 7.03m, modo: "CW",
            inicioUtc: new DateTimeOffset(2026, 1, 9, 20, 15, 0, TimeSpan.Zero)));

        var resultado = await _caso.EjecutarAsync("ea1abc");

        resultado.TrabajadoAntes.Should().BeTrue();
        resultado.Veces.Should().Be(2);
        resultado.Indicativo.Valor.Should().Be("EA1ABC");
        resultado.UltimaVezUtc.Should().Be(new DateTimeOffset(2026, 1, 9, 20, 15, 0, TimeSpan.Zero));
        resultado.Bandas.Should().Equal("40m", "20m");
        resultado.Modos.Should().Equal("CW", "SSB");
        resultado.Resumen.Should().Contain("Trabajado antes 2 veces");
    }

    [Fact]
    public async Task Sabe_si_seria_nuevo_en_esa_banda_o_en_ese_modo()
    {
        _cuaderno.Sembrar(Ayuda.Qso(call: "EA1ABC", banda: "20m", mhz: 14.2m, modo: "SSB"));

        var resultado = await _caso.EjecutarAsync("EA1ABC");

        resultado.EsNuevoEnBanda(Banda.Parse("20m")).Should().BeFalse();
        resultado.EsNuevoEnBanda(Banda.Parse("10m")).Should().BeTrue();
        resultado.EsNuevoEnModo(Modo.Parse("SSB")).Should().BeFalse();
        resultado.EsNuevoEnModo(Modo.Parse("FT8")).Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("EA")]
    public async Task No_molesta_al_cuaderno_con_un_indicativo_a_medias(string? texto)
    {
        var resultado = await _caso.EjecutarAsync(texto);

        resultado.TrabajadoAntes.Should().BeFalse();
        _cuaderno.ConsultasDeTrabajadoAntes.Should().Be(0);
    }

    [Fact]
    public async Task Le_pide_al_cuaderno_un_tope_de_contactos()
    {
        _cuaderno.Sembrar(Ayuda.Qso(call: "EA1ABC"));

        await _caso.EjecutarAsync("EA1ABC");

        _cuaderno.MaximoPedido.Should().Be(ConsultarTrabajadoAntes.MaximoDeContactos);
    }

    [Fact]
    public async Task Cuando_llega_al_tope_lo_dice_con_un_o_mas_en_vez_de_mentir()
    {
        for (var i = 0; i < ConsultarTrabajadoAntes.MaximoDeContactos + 20; i++)
        {
            _cuaderno.Sembrar(Ayuda.Qso(
                call: "EA1ABC",
                inicioUtc: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddHours(i)));
        }

        var resultado = await _caso.EjecutarAsync("EA1ABC");

        resultado.HayMas.Should().BeTrue();
        resultado.Veces.Should().Be(ConsultarTrabajadoAntes.MaximoDeContactos);
        resultado.Resumen.Should().Contain("veces o más");
    }

    [Fact]
    public async Task Con_pocos_contactos_no_dice_o_mas()
    {
        _cuaderno.Sembrar(Ayuda.Qso(call: "EA1ABC"));

        var resultado = await _caso.EjecutarAsync("EA1ABC");

        resultado.HayMas.Should().BeFalse();
        resultado.Resumen.Should().StartWith("Trabajado antes 1 vez");
    }

    [Fact]
    public async Task La_ultima_vez_es_la_mas_reciente_aunque_se_alcance_el_tope()
    {
        var primera = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        for (var i = 0; i < ConsultarTrabajadoAntes.MaximoDeContactos + 5; i++)
        {
            _cuaderno.Sembrar(Ayuda.Qso(call: "EA1ABC", inicioUtc: primera.AddDays(i)));
        }

        var resultado = await _caso.EjecutarAsync("EA1ABC");

        resultado.UltimaVezUtc.Should().Be(primera.AddDays(ConsultarTrabajadoAntes.MaximoDeContactos + 4));
    }

    [Fact]
    public async Task Normaliza_lo_que_teclea_el_operador()
    {
        _cuaderno.Sembrar(Ayuda.Qso(call: "EA8DLF/P"));

        var resultado = await _caso.EjecutarAsync(" ea8dlf-p ");

        resultado.TrabajadoAntes.Should().BeTrue();
        resultado.Indicativo.Valor.Should().Be("EA8DLF/P");
    }
}
