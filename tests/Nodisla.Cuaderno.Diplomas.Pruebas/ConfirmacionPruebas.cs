using FluentAssertions;
using Dapper;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Diplomas.Calculo;
using Nodisla.Cuaderno.Diplomas.Catalogo;
using Nodisla.Cuaderno.Dominio.Entidades;

namespace Nodisla.Cuaderno.Diplomas.Pruebas;

/// <summary>
/// No todas las confirmaciones valen para todos los diplomas.
/// </summary>
/// <remarks>
/// Hay diplomas que solo admiten tarjeta en papel y otros que admiten LoTW, y <c>Verificado</c>
/// no es lo mismo que <c>Confirmado</c>. Tratarlas como equivalentes es la manera mas facil de
/// decirle a un operador que tiene un diploma que no tiene.
/// </remarks>
public sealed class ConfirmacionPruebas : IAsyncLifetime
{
    private readonly CuadernoDePrueba _cuaderno = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => _cuaderno.DisposeAsync().AsTask();

    [Fact]
    public async Task Un_diploma_de_papel_no_cuenta_una_confirmacion_de_lotw()
    {
        // El mismo campo del contacto (el estado) en dos diplomas: WAS admite LoTW y papel;
        // H26, el suizo, solo admite tarjeta en papel.
        var conLotw = _cuaderno.AnadirQso("HB9ABC", dxcc: 287, state: "AG");
        _cuaderno.AnadirConfirmacion(conLotw, "LOTW", "Y");
        var conPapel = _cuaderno.AnadirQso("HB9DEF", dxcc: 287, state: "AI");
        _cuaderno.AnadirConfirmacion(conPapel, "QSL", "Y");

        var suizo = await _cuaderno.Motor().ProgresoAsync("H26", "Switzerland75");

        suizo.Trabajadas.Should().Be(2);
        suizo.Confirmadas.Should().Be(1, "solo la tarjeta en papel cuenta para este diploma");
    }

    [Fact]
    public async Task Un_diploma_que_admite_lotw_cuenta_las_dos_vias()
    {
        var conLotw = _cuaderno.AnadirQso("K1ABC", dxcc: 291, state: "CA");
        _cuaderno.AnadirConfirmacion(conLotw, "LOTW", "Y");
        var conPapel = _cuaderno.AnadirQso("K2DEF", dxcc: 291, state: "NY");
        _cuaderno.AnadirConfirmacion(conPapel, "QSL", "Y");

        var was = await _cuaderno.Motor().ProgresoAsync("WAS", "WAS");

        was.Trabajadas.Should().Be(2);
        was.Confirmadas.Should().Be(2);
    }

    [Fact]
    public async Task Una_confirmacion_por_una_via_que_el_diploma_no_admite_no_cuenta()
    {
        var qso = _cuaderno.AnadirQso("K1ABC", dxcc: 291, state: "CA");
        _cuaderno.AnadirConfirmacion(qso, "EQSL", "Y");   // WAS no admite eQSL

        var was = await _cuaderno.Motor().ProgresoAsync("WAS", "WAS");

        was.Trabajadas.Should().Be(1);
        was.Confirmadas.Should().Be(0);
    }

    [Fact]
    public async Task Un_diploma_que_solo_admite_lotw_ignora_la_tarjeta()
    {
        var porLotw = _cuaderno.AnadirQso("K1ABC", dxcc: 291);
        _cuaderno.AnadirConfirmacion(porLotw, "LOTW", "Y");
        var porPapel = _cuaderno.AnadirQso("EA8DLF", dxcc: 29);
        _cuaderno.AnadirConfirmacion(porPapel, "QSL", "Y");

        var recuento = await ContarAsync(
            p => p with { MediosValidos = [MedioDeConfirmacion.Lotw] });

        recuento.Trabajadas.Should().Be(2);
        recuento.Confirmadas.Should().Be(1);
    }

    [Fact]
    public async Task Un_diploma_sin_vias_declaradas_acepta_cualquiera()
    {
        var porLotw = _cuaderno.AnadirQso("K1ABC", dxcc: 291);
        _cuaderno.AnadirConfirmacion(porLotw, "LOTW", "Y");
        var porPapel = _cuaderno.AnadirQso("EA8DLF", dxcc: 29);
        _cuaderno.AnadirConfirmacion(porPapel, "QSL", "Y");

        var recuento = await ContarAsync(p => p with { MediosValidos = [] });

        recuento.Confirmadas.Should().Be(2);
    }

    [Fact]
    public async Task Verificado_no_es_lo_mismo_que_confirmado()
    {
        var confirmado = _cuaderno.AnadirQso("K1ABC", dxcc: 291);
        _cuaderno.AnadirConfirmacion(confirmado, "LOTW", "Y");
        var verificado = _cuaderno.AnadirQso("EA8DLF", dxcc: 29);
        _cuaderno.AnadirConfirmacion(verificado, "LOTW", "V");

        var exigeConfirmacion = await ContarAsync(
            p => p with { Exigencia = ExigenciaDeConfirmacion.Confirmado });
        var exigeVerificacion = await ContarAsync(
            p => p with { Exigencia = ExigenciaDeConfirmacion.Verificado });

        // Una verificada vale tambien como confirmada, pero no al reves.
        exigeConfirmacion.Confirmadas.Should().Be(2);
        exigeVerificacion.Confirmadas.Should().Be(1);
    }

    [Fact]
    public async Task Un_diploma_que_solo_pide_haber_trabajado_no_mira_confirmaciones()
    {
        _cuaderno.AnadirQso("K1ABC", dxcc: 291);
        _cuaderno.AnadirQso("EA8DLF", dxcc: 29);

        var recuento = await ContarAsync(
            p => p with { Exigencia = ExigenciaDeConfirmacion.Trabajado });

        recuento.Trabajadas.Should().Be(2);
        recuento.Confirmadas.Should().Be(2);
    }

    [Theory]
    [InlineData("N")]
    [InlineData("Q")]
    [InlineData("R")]
    [InlineData("I")]
    public async Task Los_estados_que_no_son_confirmacion_no_cuentan(string estado)
    {
        var qso = _cuaderno.AnadirQso("K1ABC", dxcc: 291, state: "CA");
        _cuaderno.AnadirConfirmacion(qso, "LOTW", estado);

        (await _cuaderno.Motor().ProgresoAsync("WAS", "WAS")).Confirmadas.Should().Be(0);
    }

    /// <summary>
    /// Ejecuta el recuento de una variante retocada a mano. Sirve para probar combinaciones que
    /// el catalogo real no trae, como un diploma que solo admita LoTW.
    /// </summary>
    private async Task<RecuentoDeVariante> ContarAsync(
        Func<PremioDelCatalogo, PremioDelCatalogo> retocar)
    {
        var catalogo = LectorDelRecurso.LeerCatalogo();
        var premio = retocar(catalogo.Diplomas["DXCC"]);
        var variante = catalogo.Variantes["DXCC"].Single(
            v => v.Variante.Equals("MIXED", StringComparison.OrdinalIgnoreCase));

        var reglas = ReglasDeVariante.Construir(premio, variante);
        using var conexion = _cuaderno.AbrirConCatalogo();
        return await conexion.QuerySingleAsync<RecuentoDeVariante>(
            ConsultasDeProgreso.Recuento(reglas));
    }
}
