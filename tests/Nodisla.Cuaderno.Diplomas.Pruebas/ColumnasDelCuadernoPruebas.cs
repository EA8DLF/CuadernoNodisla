using FluentAssertions;
using Nodisla.Cuaderno.Diplomas.Catalogo;

namespace Nodisla.Cuaderno.Diplomas.Pruebas;

/// <summary>
/// El catálogo puede pedir un campo que el esquema del cuaderno todavía no tenga.
/// </summary>
/// <remarks>
/// El catálogo de diplomas se actualiza por su cuenta y la base del operador puede ser más
/// vieja. Un diploma que revienta la consulta es peor que uno que dice que no se puede
/// calcular, así que el motor mira qué columnas hay de verdad antes de preguntar.
/// </remarks>
public sealed class ColumnasDelCuadernoPruebas : IAsyncLifetime
{
    private readonly CuadernoDePrueba _cuaderno = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => _cuaderno.DisposeAsync().AsTask();

    [Fact]
    public async Task Con_la_columna_el_diploma_se_consulta()
    {
        var qso = _cuaderno.AnadirQso("EA8DLF");
        _cuaderno.Ejecutar($"UPDATE qso SET sig = 'SIOTA', sig_info = 'EA-0001' WHERE id = {qso}");

        var progreso = await _cuaderno.Motor().ProgresoAsync("SIOTA", "GENERAL");

        // Se consulta, pero el catálogo de este diploma viene sin referencias, así que la cifra
        // no es firme y hay que decirlo: un cero que parece un dato engaña igual que un número.
        progreso.Trabajadas.Should().Be(0);
        progreso.EsFirme.Should().BeFalse();
        progreso.PorQueNoEsFirme.Should().Contain("ninguna referencia");
    }

    [Fact]
    public async Task Sin_la_columna_el_diploma_no_se_calcula_y_se_dice_por_que()
    {
        // Un cuaderno anterior a que el modelo guardara SIG_INFO.
        _cuaderno.Ejecutar("ALTER TABLE qso DROP COLUMN sig_info");

        var progreso = await _cuaderno.Motor().ProgresoAsync("SIOTA", "GENERAL");

        progreso.Trabajadas.Should().Be(0);
        progreso.EsFirme.Should().BeFalse();
        progreso.PorQueNoEsFirme.Should().Contain("sig_info");
    }

    [Fact]
    public async Task El_detalle_de_un_diploma_sin_columna_sale_vacio_y_sin_reventar()
    {
        _cuaderno.Ejecutar("ALTER TABLE qso DROP COLUMN sig_info");

        var detalle = await _cuaderno.Motor().DetalleAsync("SIOTA", "GENERAL", 0, 50);

        detalle.Elementos.Should().BeEmpty();
        detalle.TotalFiltrado.Should().Be(0);
    }

    [Fact]
    public void El_motor_sabe_que_columnas_necesita_cada_diploma()
    {
        var catalogo = LectorDelRecurso.LeerCatalogo();

        Calculo.ConsultasDeProgreso.ColumnasQueNecesita(catalogo.Diplomas["SIOTA"])
            .Should().BeEquivalentTo(["sig_info"]);
        Calculo.ConsultasDeProgreso.ColumnasQueNecesita(catalogo.Diplomas["USA-CA"])
            .Should().BeEquivalentTo(["cnty", "state"], "cuenta por el condado con el estado delante");
        Calculo.ConsultasDeProgreso.ColumnasQueNecesita(catalogo.Diplomas["SOTA"])
            .Should().BeEmpty("cuenta por referencia, no por un campo del contacto");
    }
}
