using FluentAssertions;
using Dapper;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Diplomas.Calculo;
using Nodisla.Cuaderno.Diplomas.Catalogo;
using Nodisla.Cuaderno.Dominio.Entidades;

namespace Nodisla.Cuaderno.Diplomas.Pruebas;

/// <summary>El recurso del catalogo trae lo que dice traer y se lee entero.</summary>
public sealed class CatalogoPruebas
{
    private static readonly CatalogoDeDiplomas Catalogo = LectorDelRecurso.LeerCatalogo();

    [Fact]
    public void El_recurso_trae_los_87_diplomas_del_original()
    {
        Catalogo.Cabecera.Diplomas.Should().Be(87);
        Catalogo.Diplomas.Should().HaveCount(87);
    }

    [Fact]
    public void El_recurso_declara_las_390_variantes_y_las_553064_referencias()
    {
        Catalogo.Cabecera.Variantes.Should().Be(390);
        Catalogo.Cabecera.Referencias.Should().Be(553_064);
        Catalogo.Variantes.Values.Sum(v => v.Count).Should().Be(390);
    }

    [Fact]
    public void Los_diplomas_sin_clases_en_el_original_reciben_una_variante_sintetica()
    {
        // 75 de los 87 diplomas no traen ninguna fila en AwardConfig. Sin una variante no
        // habria nada que calcular, asi que el generador les pone una y la marca.
        var sinteticas = Catalogo.Variantes.Values.SelectMany(v => v).Count(v => v.Sintetica);
        sinteticas.Should().Be(75);

        Catalogo.Variantes["SOTA"].Should().ContainSingle()
            .Which.Sintetica.Should().BeTrue();
    }

    [Theory]
    [InlineData("DXCC", ClaseDeDiploma.PorCampo)]
    [InlineData("SOTA", ClaseDeDiploma.PorReferencia)]
    [InlineData("CCC", ClaseDeDiploma.PorIndicativo)]
    public void Cada_diploma_conserva_su_clase(string codigo, ClaseDeDiploma esperada) =>
        Catalogo.Diplomas[codigo].Clase.Should().Be(esperada);

    [Fact]
    public void El_dxcc_cuenta_por_la_entidad_y_admite_lotw_y_papel()
    {
        var dxcc = Catalogo.Diplomas["DXCC"];
        dxcc.Campo.Should().Be(CampoDeQso.Dxcc);
        dxcc.Exigencia.Should().Be(ExigenciaDeConfirmacion.Confirmado);
        dxcc.MediosValidos.Should().BeEquivalentTo(
            [MedioDeConfirmacion.Lotw, MedioDeConfirmacion.Papel]);
        dxcc.NombreParaMostrar.Should().Be("Centenario DXCC");
        dxcc.Gestor.Should().Be("ARRL");
    }

    [Fact]
    public void Hay_diplomas_que_solo_admiten_tarjeta_en_papel()
    {
        var soloPapel = Catalogo.Diplomas.Values
            .Where(p => p.MediosValidos.Count == 1 && p.MediosValidos[0] == MedioDeConfirmacion.Papel)
            .ToList();

        soloPapel.Should().NotBeEmpty("el original tiene 29 diplomas con ConfirmationMethod = QSL");
        soloPapel.Should().Contain(p => p.Codigo == "H26");
    }

    [Fact]
    public void Los_diplomas_que_valida_su_gestor_quedan_marcados()
    {
        // SOTA, POTA o IOTA se validan contra la base del gestor, que el cuaderno no puede
        // consultar. El motor no puede darlos por confirmados sin mas: tiene que contar de menos
        // y decir por que.
        Catalogo.Diplomas["SOTA"].ValidaElGestor.Should().BeTrue();
        Catalogo.Diplomas["SOTA"].MediosValidos.Should().BeEmpty();
    }

    [Fact]
    public void Todos_los_campos_que_pide_el_catalogo_se_saben_leer()
    {
        // Si el original trae un diploma que cuenta por un campo que el motor no sabe leer, se
        // marca y se dice por que, en vez de sacar un numero que parece un dato y no lo es.
        var sinCampo = Catalogo.Diplomas.Values
            .Where(p => p.Clase == ClaseDeDiploma.PorCampo && !p.Calculable)
            .ToList();

        sinCampo.Should().OnlyContain(p => !string.IsNullOrWhiteSpace(p.MotivoNoCalculable));
    }

    [Fact]
    public void El_siota_cuenta_por_el_campo_sig_info()
    {
        var siota = Catalogo.Diplomas["SIOTA"];
        siota.Clase.Should().Be(ClaseDeDiploma.PorCampo);
        siota.Campo.Should().Be(CampoDeQso.SigInfo);
        siota.Calculable.Should().BeTrue();
    }

    [Fact]
    public void Las_variantes_declaran_la_familia_de_modos_y_no_un_modo_suelto()
    {
        Variante("DXCC", "CW").Clase.Should().Be(ClaseDeModo.Telegrafia);
        Variante("DXCC", "Phone").Clase.Should().Be(ClaseDeModo.Fonia);
        Variante("DXCC", "DIGITAL").Clase.Should().Be(ClaseDeModo.Digital);
        Variante("DXCC", "MIXED").Clase.Should().Be(ClaseDeModo.Cualquiera);
    }

    [Fact]
    public void El_objetivo_solo_se_fija_cuando_el_reglamento_lo_dice()
    {
        Variante("DXCC", "MIXED").Objetivo.Should().Be(100);
        Variante("WAS", "WAS").Objetivo.Should().Be(50);
        // La clase de 10 metros del DXCC no fija ningun numero: inventarlo enganaria.
        Variante("DXCC", "10m").Objetivo.Should().BeNull();
    }

    [Fact]
    public void Los_creditos_que_el_propio_catalogo_declara_se_convierten_en_objetivo() =>
        Variante("VUCC", "VUCC_144").Objetivo.Should().Be(100);

    [Fact]
    public void Las_referencias_se_leen_en_flujo_sin_cargarlas_todas()
    {
        var soloDxcc = LectorDelRecurso
            .LeerReferencias(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "DXCC" })
            .ToList();

        soloDxcc.Should().HaveCount(402);
        soloDxcc.Should().OnlyContain(r => r.Codigo == "DXCC");
        soloDxcc.Should().Contain(r => r.Referencia == "291" && r.Descripcion.Contains("United States"));
    }

    [Fact]
    public void La_lista_de_dxcc_permitidos_llega_ya_partida_en_numeros()
    {
        var pota = LectorDelRecurso
            .LeerReferencias(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "POTA" })
            .First(r => r.Referencia == "AD-0001");

        // En el original esto es el texto '#203#' dentro de una columna de 3.000 caracteres.
        pota.DxccPermitidos.Should().BeEquivalentTo([203]);
    }

    private static VarianteDelCatalogo Variante(string codigo, string variante) =>
        Catalogo.Variantes[codigo].Single(
            v => v.Variante.Equals(variante, StringComparison.OrdinalIgnoreCase));
}

/// <summary>El catalogo compilado deja <c>AllowedDXCC</c> en una tabla indexada de verdad.</summary>
public sealed class NormalizacionDeDxccPruebas : IAsyncLifetime
{
    private readonly CuadernoDePrueba _cuaderno = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => _cuaderno.DisposeAsync().AsTask();

    [Fact]
    public async Task La_lista_de_texto_se_convierte_en_filas_consultables()
    {
        await _cuaderno.Motor().PrepararAsync();
        using var conexion = _cuaderno.AbrirConCatalogo();

        // Una referencia con varias entidades permitidas: lo que el original guarda como
        // '#4#;#165#;…' dentro de una sola columna tiene que estar aqui fila a fila.
        var esperada = LectorDelRecurso
            .LeerReferencias(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "IOTA" })
            .First(r => r.DxccPermitidos.Count > 1);

        var guardadas = (await conexion.QueryAsync<int>(
            "SELECT dxcc FROM cat.premio_dxcc_permitido WHERE award_code = 'IOTA' AND referencia = @ref",
            new { @ref = esperada.Referencia })).ToList();

        guardadas.Should().BeEquivalentTo(esperada.DxccPermitidos);
    }

    [Fact]
    public async Task Se_normaliza_una_fila_por_entidad_y_referencia()
    {
        await _cuaderno.Motor().PrepararAsync();
        using var conexion = _cuaderno.AbrirConCatalogo();

        var delRecurso = LectorDelRecurso
            .LeerReferencias(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "AA" })
            .Sum(r => r.DxccPermitidos.Distinct().Count());

        var enLaTabla = await conexion.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM cat.premio_dxcc_permitido WHERE award_code = 'AA'");

        enLaTabla.Should().Be(delRecurso).And.BeGreaterThan(0);
    }

    [Fact]
    public async Task La_tabla_se_puede_recorrer_por_entidad_con_indice()
    {
        await _cuaderno.Motor().PrepararAsync();
        using var conexion = _cuaderno.AbrirConCatalogo();

        var indices = (await conexion.QueryAsync<string>(
            "SELECT name FROM cat.sqlite_master WHERE type = 'index'")).ToList();
        indices.Should().Contain("ix_pdxcc_dxcc");

        // Con el indice, «que referencias se pueden activar desde esta entidad» es una consulta;
        // en el original habria que recorrer 553.064 listas de texto en memoria.
        var plan = (await conexion.QueryAsync<FilaDePlan>(
            "EXPLAIN QUERY PLAN SELECT referencia FROM cat.premio_dxcc_permitido WHERE dxcc = 281"))
            .Select(f => f.Detail ?? string.Empty)
            .ToList();
        string.Join(" ", plan).Should().Contain("ix_pdxcc_dxcc");
    }

    private sealed class FilaDePlan
    {
        public string? Detail { get; init; }
    }
}
