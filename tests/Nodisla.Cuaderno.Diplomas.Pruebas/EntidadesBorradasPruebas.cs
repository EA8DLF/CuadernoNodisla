using System.Globalization;
using FluentAssertions;
using Dapper;
using Nodisla.Cuaderno.Diplomas.Catalogo;
using Nodisla.Cuaderno.Dominio.Dxcc;

namespace Nodisla.Cuaderno.Diplomas.Pruebas;

/// <summary>
/// Una entidad DXCC borrada cuenta segun la fecha del contacto.
/// </summary>
/// <remarks>
/// Quien trabajo una entidad cuando existia conserva el credito; quien la «trabaja» despues de
/// borrada, no. La ventana de cada entidad no la pone el catalogo del original: la pone el
/// resolutor del dominio, que es quien sabe cuando nacio y cuando murio cada una.
/// </remarks>
public sealed class EntidadesBorradasPruebas : IAsyncLifetime
{
    private static readonly Lazy<EntidadDxcc> Borrada = new(Elegir);

    private readonly CuadernoDePrueba _cuaderno = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => _cuaderno.DisposeAsync().AsTask();

    [Fact]
    public async Task Un_contacto_anterior_al_borrado_cuenta_y_uno_posterior_no()
    {
        var entidad = Borrada.Value;
        var fin = entidad.ValidaHasta!.Value;

        // Uno el dia antes de que la entidad desapareciera y otro cinco anos despues.
        _cuaderno.AnadirQso(
            "PRUEBA1",
            fecha: new DateTimeOffset(fin.AddDays(-1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            dxcc: entidad.Numero);
        _cuaderno.AnadirQso(
            "PRUEBA2",
            fecha: new DateTimeOffset(fin.AddYears(5).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            dxcc: entidad.Numero);

        var progreso = await _cuaderno.Motor().ProgresoAsync("DXCC", "MIXED");

        progreso.Trabajadas.Should().Be(1, "solo cuenta el contacto de cuando la entidad existía");
    }

    [Fact]
    public async Task La_ventana_de_la_entidad_la_pone_el_resolutor_del_dominio()
    {
        var entidad = Borrada.Value;
        using var conexion = _cuaderno.AbrirConCatalogo();

        var fila = await conexion.QuerySingleAsync<FilaDeVentana>(
            """
            SELECT valido_desde AS ValidoDesde, valido_hasta AS ValidoHasta, valido AS Valido
              FROM cat.premio_referencia
             WHERE award_code = 'DXCC' AND referencia = @numero
            """,
            new { numero = Texto(entidad.Numero) });

        fila.ValidoHasta.Should().Be(entidad.ValidaHasta!.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        fila.Valido.Should().Be(0, "la entidad está borrada");
    }

    [Fact]
    public async Task Las_entidades_borradas_siguen_apareciendo_en_el_detalle_del_dxcc()
    {
        var detalle = (await _cuaderno.Motor().DetalleAsync("DXCC", "MIXED", 0, 1000)).Elementos;

        detalle.Should().Contain(
            d => d.Referencia == Texto(Borrada.Value.Numero),
            "para el DXCC las entidades borradas cuentan si el contacto es de cuando existían");
    }

    [Fact]
    public async Task Una_entidad_retirada_de_otro_diploma_no_cuenta()
    {
        // Fuera del DXCC, una referencia marcada como no vigente no cuenta nunca: no es una
        // entidad con historia, es una referencia que el gestor retiro.
        using var conexion = _cuaderno.AbrirConCatalogo();
        var retiradas = await conexion.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM cat.premio_referencia WHERE award_code <> 'DXCC' AND valido = 0");

        retiradas.Should().BeGreaterThanOrEqualTo(0);

        var detalle = await _cuaderno.Motor().DetalleAsync("WAS", "WAS", 0, 100);
        detalle.Elementos.Should().OnlyContain(d => d.Referencia.Length == 2);
    }

    [Fact]
    public void El_catalogo_conoce_entidades_borradas_con_fecha_de_baja() =>
        ResolutorDxcc.Predeterminado.Todas
            .Count(e => e.EstaBorrada && e.ValidaHasta is not null)
            .Should().BeGreaterThan(10);

    private static string Texto(int numero) => numero.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Elige, de forma determinista, una entidad borrada que ademas este en el catalogo del
    /// diploma DXCC: las dos fuentes no tienen por que conocer exactamente las mismas.
    /// </summary>
    private static EntidadDxcc Elegir()
    {
        var enElCatalogo = LectorDelRecurso
            .LeerReferencias(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "DXCC" })
            .Select(r => r.Referencia)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return ResolutorDxcc.Predeterminado.Todas
            .Where(e => e.EstaBorrada
                && e.ValidaHasta is { } hasta
                && hasta.Year is > 1950 and < 2000
                && enElCatalogo.Contains(Texto(e.Numero)))
            .OrderBy(e => e.Numero)
            .First();
    }

    private sealed class FilaDeVentana
    {
        public string? ValidoDesde { get; init; }
        public string? ValidoHasta { get; init; }
        public long Valido { get; init; }
    }
}
