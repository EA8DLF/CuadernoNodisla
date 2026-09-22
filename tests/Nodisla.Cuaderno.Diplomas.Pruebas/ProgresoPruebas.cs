using FluentAssertions;
using Nodisla.Cuaderno.Diplomas.Calculo;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Diplomas.Pruebas;

/// <summary>Cada clase de diploma cuenta lo que tiene que contar.</summary>
public sealed class ProgresoPruebas : IAsyncLifetime
{
    private readonly CuadernoDePrueba _cuaderno = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => _cuaderno.DisposeAsync().AsTask();

    [Fact]
    public async Task Por_campo_cuenta_entidades_distintas_y_no_contactos()
    {
        _cuaderno.AnadirQso("K1ABC", dxcc: 291);
        _cuaderno.AnadirQso("K2DEF", dxcc: 291);          // la misma entidad, no suma
        var canarias = _cuaderno.AnadirQso("EA8DLF", dxcc: 29);
        _cuaderno.AnadirConfirmacion(canarias, "LOTW", "Y");

        var progreso = await Motor().ProgresoAsync("DXCC", "MIXED");

        progreso.Trabajadas.Should().Be(2);
        progreso.Confirmadas.Should().Be(1);
        progreso.Objetivo.Should().Be(100);
        progreso.Faltan.Should().Be(99);
    }

    [Fact]
    public async Task Por_campo_respeta_la_banda_de_la_variante()
    {
        _cuaderno.AnadirQso("K1ABC", banda: "20m", dxcc: 291);
        _cuaderno.AnadirQso("EA8DLF", banda: "40m", dxcc: 29);

        (await Motor().ProgresoAsync("DXCC", "20m")).Trabajadas.Should().Be(1);
        (await Motor().ProgresoAsync("DXCC", "MIXED")).Trabajadas.Should().Be(2);
    }

    [Fact]
    public async Task Por_campo_solo_cuenta_valores_que_esten_en_el_catalogo()
    {
        _cuaderno.AnadirQso("K1ABC", dxcc: 291, state: "CA");
        _cuaderno.AnadirQso("K2DEF", dxcc: 291, state: "XX");   // no existe ese estado

        (await Motor().ProgresoAsync("WAS", "WAS")).Trabajadas.Should().Be(1);
    }

    [Fact]
    public async Task Por_campo_con_universo_abierto_cuenta_cualquier_prefijo()
    {
        _cuaderno.AnadirQso("EA8DLF", pfx: "EA8");
        _cuaderno.AnadirQso("DL1ABC", pfx: "DL1");
        _cuaderno.AnadirQso("DL2ABC", pfx: "DL1");

        // WPX no tiene lista de referencias: vale el prefijo que traiga el contacto.
        (await Motor().ProgresoAsync("WPX", "WPX-MIXED")).Trabajadas.Should().Be(2);
    }

    [Fact]
    public async Task Por_referencia_cuenta_las_referencias_del_corresponsal()
    {
        var uno = _cuaderno.AnadirQso("EA8DLF");
        _cuaderno.AnadirReferencia(uno, "IOTA", "AF-004");
        var dos = _cuaderno.AnadirQso("GM0ABC");
        _cuaderno.AnadirReferencia(dos, "IOTA", "EU-008");
        _cuaderno.AnadirConfirmacion(dos, "QSL", "Y");

        var progreso = await Motor().ProgresoAsync("IOTA", "IOTA_BASICS");

        progreso.Trabajadas.Should().Be(2);
        progreso.Confirmadas.Should().Be(1);
    }

    [Fact]
    public async Task Por_referencia_no_cuenta_mis_propias_activaciones()
    {
        var qso = _cuaderno.AnadirQso("EA8DLF");
        _cuaderno.AnadirReferencia(qso, "IOTA", "AF-004", propia: true);

        (await Motor().ProgresoAsync("IOTA", "IOTA_BASICS")).Trabajadas.Should().Be(0);
    }

    [Fact]
    public async Task Por_referencia_encuentra_los_programas_no_modelados()
    {
        // El Antarctica Award no tiene tipo propio en el dominio: se guarda como OTRA con el
        // nombre del programa al lado.
        var qso = _cuaderno.AnadirQso("RI1ANC");
        _cuaderno.AnadirReferencia(qso, "OTRA", "BY-01", programa: "AA");

        (await Motor().ProgresoAsync("AA", "GENERAL")).Trabajadas.Should().Be(1);
    }

    [Fact]
    public async Task Por_referencia_no_cuenta_una_referencia_que_no_este_en_el_catalogo()
    {
        var qso = _cuaderno.AnadirQso("EA8DLF");
        _cuaderno.AnadirReferencia(qso, "IOTA", "ZZ-999");

        (await Motor().ProgresoAsync("IOTA", "IOTA_BASICS")).Trabajadas.Should().Be(0);
    }

    [Fact]
    public async Task Por_indicativo_casa_tanto_patrones_como_indicativos_enteros()
    {
        _cuaderno.AnadirQso("3B8ABC");    // casa con el patron 3B8*
        _cuaderno.AnadirQso("3B8XYZ");    // el mismo patron, no suma
        _cuaderno.AnadirQso("VK3ABC");    // casa con otro patron de la Commonwealth
        _cuaderno.AnadirQso("DL1ABC");    // no casa con ninguno

        (await Motor().ProgresoAsync("CCC", "GENERAL")).Trabajadas.Should().Be(2);
    }

    [Fact]
    public async Task El_detalle_lista_el_universo_y_marca_lo_hecho()
    {
        var qso = _cuaderno.AnadirQso("K1ABC", dxcc: 291, state: "CA");
        _cuaderno.AnadirConfirmacion(qso, "LOTW", "Y");

        var detalle = await Motor().DetalleAsync("WAS", "WAS");

        detalle.Should().HaveCount(50, "el catálogo trae los 50 estados");
        var california = detalle.Single(d => d.Referencia == "CA");
        california.Trabajada.Should().BeTrue();
        california.Confirmada.Should().BeTrue();
        california.PrimerQsoId.Should().Be(qso);
        california.Nombre.Should().Be("California");
        detalle.Count(d => !d.Trabajada).Should().Be(49);
    }

    [Fact]
    public async Task Que_aporta_avisa_de_entidad_nueva_al_teclear()
    {
        _cuaderno.AnadirQso("K1ABC", dxcc: 291);

        var avisos = await Motor().QueAportaAsync(
            Indicativo.Crudo("EA8DLF"), Banda.Parse("20m"), Modo.Crudo("SSB"));

        avisos.Should().Contain(a => a.Contains("Centenario DXCC") && a.Contains("nuevo"));
    }

    [Fact]
    public async Task Que_aporta_distingue_lo_trabajado_sin_confirmar()
    {
        _cuaderno.AnadirQso("EA8ABC", dxcc: 29);   // trabajado pero sin confirmar

        var avisos = await Motor().QueAportaAsync(
            Indicativo.Crudo("EA8DLF"), Banda.Parse("20m"), Modo.Crudo("SSB"));

        avisos.Should().Contain(a => a.Contains("sin confirmar"));
        avisos.Should().NotContain(a => a.Contains("Centenario DXCC") && a.Contains("es nuevo"));
    }

    [Fact]
    public async Task Que_aporta_calla_en_las_variantes_que_no_admiten_el_hueco()
    {
        _cuaderno.AnadirQso("K1ABC", banda: "20m", dxcc: 291);

        var motor = Motor(o =>
        {
            o.MisDiplomas.Clear();
            o.MisDiplomas.Add("DXCC/20m");
        });

        var enVeinte = await motor.QueAportaAsync(
            Indicativo.Crudo("EA8DLF"), Banda.Parse("20m"), Modo.Crudo("SSB"));
        var enCuarenta = await motor.QueAportaAsync(
            Indicativo.Crudo("EA8DLF"), Banda.Parse("40m"), Modo.Crudo("SSB"));

        enVeinte.Should().NotBeEmpty();
        enCuarenta.Should().BeEmpty();
    }

    [Fact]
    public async Task El_diploma_que_no_se_sabe_calcular_cuenta_cero_y_dice_por_que()
    {
        var motivo = await Motor().PorQueNoSeCalculaAsync("SIOTA", "GENERAL");
        motivo.Should().NotBeNullOrWhiteSpace();

        var progreso = await Motor().ProgresoAsync("SIOTA", "GENERAL");
        progreso.Trabajadas.Should().Be(0);
        progreso.Confirmadas.Should().Be(0);
    }

    [Theory]
    [InlineData("EA8DLF", "EA8")]
    [InlineData("DL1ABC", "DL1")]
    [InlineData("W1AW/2", "W2")]
    [InlineData("EA8/DL1ABC", "EA8")]
    [InlineData("F/DL1ABC", "F0")]
    [InlineData("DL1ABC/P", "DL1")]
    [InlineData("3DA0RU", "3DA0")]
    public void El_prefijo_wpx_sigue_las_reglas_del_reglamento(string indicativo, string esperado) =>
        PrefijoWpx.De(Indicativo.Crudo(indicativo)).Should().Be(esperado);

    private MotorDeDiplomas Motor(Action<OpcionesDeDiplomas>? ajustar = null) =>
        _cuaderno.Motor(o =>
        {
            o.MisDiplomas.Add("DXCC/MIXED");
            o.MisDiplomas.Add("WAS/WAS");
            o.MisDiplomas.Add("CCC/GENERAL");
            ajustar?.Invoke(o);
        });
}
