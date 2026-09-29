using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.CasosDeUso;
using Nodisla.Cuaderno.Aplicacion.Pruebas.Dobles;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Aplicacion.Pruebas;

public sealed class EditarQsoPruebas
{
    private readonly RepositorioQsoDoble _cuaderno = new();
    private readonly RepositorioEstacionDoble _estaciones = new();
    private readonly EditarQso _caso;

    public EditarQsoPruebas()
    {
        _estaciones.Sembrar(Ayuda.Estacion());
        _caso = new EditarQso(_cuaderno, _estaciones);
    }

    [Fact]
    public async Task Guarda_los_cambios_del_contacto()
    {
        var original = _cuaderno.Sembrar(Ayuda.Qso());
        original.StationCallsign = Indicativo.Parse("EA8DLF");
        original.Name = "Pepe";
        original.Qth = "Santa Cruz";

        var resultado = await _caso.EjecutarAsync(new PeticionDeEdicion { Qso = original });

        resultado.Correcto.Should().BeTrue();
        _cuaderno.Contenido.Should().ContainSingle().Which.Name.Should().Be("Pepe");
    }

    [Fact]
    public async Task Conserva_el_identificador_estable_y_la_fecha_de_creacion()
    {
        var original = _cuaderno.Sembrar(Ayuda.Qso());
        original.StationCallsign = Indicativo.Parse("EA8DLF");
        var uuid = original.Uuid;
        var creado = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        original.CreadoUtc = creado;

        var modificado = Ayuda.Qso(call: "EA2XYZ");
        modificado.Id = original.Id;
        modificado.StationCallsign = Indicativo.Parse("EA8DLF");
        modificado.Uuid = Guid.NewGuid();

        var resultado = await _caso.EjecutarAsync(new PeticionDeEdicion { Qso = modificado });

        resultado.Correcto.Should().BeTrue();
        modificado.Uuid.Should().Be(uuid);
        modificado.CreadoUtc.Should().Be(creado);
        modificado.ModificadoUtc.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Avisa_de_que_el_contacto_ya_no_existe()
    {
        var qso = Ayuda.Qso();
        qso.Id = 99;

        var resultado = await _caso.EjecutarAsync(new PeticionDeEdicion { Qso = qso });

        resultado.NoEncontrado.Should().BeTrue();
        resultado.Correcto.Should().BeFalse();
    }

    [Fact]
    public async Task Rechaza_el_contacto_sin_identificador()
    {
        var resultado = await _caso.EjecutarAsync(new PeticionDeEdicion { Qso = Ayuda.Qso() });

        resultado.Correcto.Should().BeFalse();
        resultado.Errores.Should().ContainSingle();
    }

    [Fact]
    public async Task Rechaza_los_cambios_que_dejan_el_contacto_invalido()
    {
        var original = _cuaderno.Sembrar(Ayuda.Qso());
        original.StationCallsign = Indicativo.Parse("EA8DLF");
        original.Mode = Modo.Vacio;

        var resultado = await _caso.EjecutarAsync(new PeticionDeEdicion { Qso = original });

        resultado.Correcto.Should().BeFalse();
        resultado.Errores.Should().Contain("Falta el modo.");
    }

    [Fact]
    public async Task Reaplica_el_perfil_de_estacion_si_se_pide_otro()
    {
        var otra = Ayuda.Estacion("Portable Teide", predeterminado: false);
        otra.StationCallsign = Indicativo.Parse("EA8DLF/P");
        _estaciones.Sembrar(otra);

        var original = _cuaderno.Sembrar(Ayuda.Qso());
        original.StationCallsign = Indicativo.Parse("EA8DLF");

        var resultado = await _caso.EjecutarAsync(
            new PeticionDeEdicion { Qso = original, EstacionId = otra.Id });

        resultado.Correcto.Should().BeTrue();
        original.StationCallsign.Valor.Should().Be("EA8DLF/P");
    }
}
