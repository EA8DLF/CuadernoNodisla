using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.CasosDeUso;
using Nodisla.Cuaderno.Aplicacion.Pruebas.Dobles;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Aplicacion.Pruebas;

public sealed class RegistrarQsoPruebas
{
    private readonly RepositorioQsoDoble _cuaderno = new();
    private readonly RepositorioEstacionDoble _estaciones = new();
    private readonly RegistrarQso _caso;

    public RegistrarQsoPruebas()
    {
        _estaciones.Sembrar(Ayuda.Estacion());
        _caso = new RegistrarQso(_cuaderno, _estaciones);
    }

    [Fact]
    public async Task Guarda_el_contacto_y_aplica_el_perfil_de_estacion()
    {
        var qso = Ayuda.Qso();

        var resultado = await _caso.EjecutarAsync(new PeticionDeRegistro { Qso = qso });

        resultado.Correcto.Should().BeTrue();
        resultado.Id.Should().BeGreaterThan(0);
        _cuaderno.Contenido.Should().ContainSingle();
        qso.StationCallsign.Valor.Should().Be("EA8DLF");
        qso.MyGridsquare.Valor.Should().Be("IL18QK");
        qso.EstacionId.Should().Be(1);
    }

    [Theory]
    [InlineData("SSB", "59")]
    [InlineData("CW", "599")]
    [InlineData("RTTY", "599")]
    public async Task Pone_el_informe_por_omision_del_modo(string modo, string esperado)
    {
        var qso = Ayuda.Qso(modo: modo, banda: "20m", mhz: 14.07m);

        await _caso.EjecutarAsync(new PeticionDeRegistro { Qso = qso });

        qso.RstSent.Texto.Should().Be(esperado);
        qso.RstRcvd.Texto.Should().Be(esperado);
    }

    [Fact]
    public async Task Respeta_el_informe_que_ha_tecleado_el_operador()
    {
        var qso = Ayuda.Qso();
        qso.RstSent = Informe.Parse("55");

        await _caso.EjecutarAsync(new PeticionDeRegistro { Qso = qso });

        qso.RstSent.Texto.Should().Be("55");
        qso.RstRcvd.Texto.Should().Be("59");
    }

    [Fact]
    public async Task Deduce_la_banda_a_partir_de_la_frecuencia()
    {
        var qso = Ayuda.Qso(mhz: 7.1m);
        qso.Band = Banda.Vacia;

        var resultado = await _caso.EjecutarAsync(new PeticionDeRegistro { Qso = qso });

        resultado.Correcto.Should().BeTrue();
        qso.Band.Nombre.Should().Be("40m");
    }

    [Fact]
    public async Task Pone_la_hora_actual_si_no_se_dio()
    {
        var qso = Ayuda.Qso();
        qso.InicioUtc = default;

        await _caso.EjecutarAsync(new PeticionDeRegistro { Qso = qso });

        qso.InicioUtc.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Rechaza_el_contacto_sin_indicativo()
    {
        var qso = Ayuda.Qso();
        qso.Call = Indicativo.Vacio;

        var resultado = await _caso.EjecutarAsync(new PeticionDeRegistro { Qso = qso });

        resultado.Correcto.Should().BeFalse();
        resultado.Errores.Should().Contain(e => e.Contains("indicativo del corresponsal", StringComparison.Ordinal));
        _cuaderno.Contenido.Should().BeEmpty();
    }

    [Fact]
    public async Task Rechaza_la_frecuencia_que_cae_fuera_de_la_banda()
    {
        var qso = Ayuda.Qso(banda: "20m", mhz: 7.1m);

        var resultado = await _caso.EjecutarAsync(new PeticionDeRegistro { Qso = qso });

        resultado.Correcto.Should().BeFalse();
        resultado.Errores.Should().ContainSingle()
            .Which.Should().Contain("no está dentro de la banda");
    }

    [Fact]
    public async Task Rechaza_el_contacto_sin_banda_ni_frecuencia()
    {
        var qso = Ayuda.Qso();
        qso.Band = Banda.Vacia;
        qso.Freq = Frecuencia.Cero;

        var resultado = await _caso.EjecutarAsync(new PeticionDeRegistro { Qso = qso });

        resultado.Errores.Should().Contain("Falta la banda o la frecuencia.");
    }

    [Fact]
    public async Task Avisa_del_duplicado_y_no_lo_guarda()
    {
        _cuaderno.Sembrar(Ayuda.Qso());

        var resultado = await _caso.EjecutarAsync(new PeticionDeRegistro { Qso = Ayuda.Qso() });

        resultado.Correcto.Should().BeFalse();
        resultado.Duplicado.Should().NotBeNull();
        _cuaderno.Contenido.Should().ContainSingle();
    }

    [Fact]
    public async Task Guarda_el_duplicado_si_el_operador_insiste()
    {
        _cuaderno.Sembrar(Ayuda.Qso());

        var resultado = await _caso.EjecutarAsync(
            new PeticionDeRegistro { Qso = Ayuda.Qso(), AdmitirDuplicado = true });

        resultado.Correcto.Should().BeTrue();
        _cuaderno.Contenido.Should().HaveCount(2);
    }

    [Fact]
    public async Task Exige_un_perfil_de_estacion()
    {
        var caso = new RegistrarQso(_cuaderno, new RepositorioEstacionDoble());

        var resultado = await caso.EjecutarAsync(new PeticionDeRegistro { Qso = Ayuda.Qso() });

        resultado.Correcto.Should().BeFalse();
        resultado.Errores.Should().Contain(e => e.Contains("perfil de estación", StringComparison.Ordinal));
    }
}
