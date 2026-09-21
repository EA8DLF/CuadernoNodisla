using FluentAssertions;
using Nodisla.Cuaderno.Dominio.Entidades;

namespace Nodisla.Cuaderno.Dominio.Pruebas.Entidades;

/// <summary>
/// Pruebas de las dos colecciones que se unen al fundir: las referencias de programas de
/// activacion y los campos ADIF que el modelo no representa con una propiedad propia.
/// </summary>
public sealed class FusionDeColeccionesPruebas
{
    // ── Referencias ──────────────────────────────────────────────────────────

    [Fact]
    public void Las_referencias_que_solo_tiene_el_origen_se_anaden()
    {
        var destino = AyudaDeFusion.Qso();
        destino.Referencias.Add(Referencia(TipoDeReferencia.Iota, "EU-004"));

        var origen = AyudaDeFusion.Qso();
        origen.Referencias.Add(Referencia(TipoDeReferencia.Pota, "ES-0012"));

        var resultado = FusionDeQso.Fundir(destino, origen);

        destino.Referencias.Select(r => r.Codigo).Should().Equal("EU-004", "ES-0012");
        resultado.HuboCambios.Should().BeTrue();
        resultado.Choques.Should().BeEmpty();
    }

    [Fact]
    public void La_referencia_repetida_no_se_duplica()
    {
        var destino = AyudaDeFusion.Qso();
        destino.Referencias.Add(Referencia(TipoDeReferencia.Iota, "EU-004"));

        var origen = AyudaDeFusion.Qso();
        origen.Referencias.Add(Referencia(TipoDeReferencia.Iota, "EU-004"));

        var resultado = FusionDeQso.Fundir(destino, origen);

        destino.Referencias.Should().ContainSingle();
        resultado.HuboCambios.Should().BeFalse();
    }

    [Fact]
    public void El_codigo_de_referencia_se_compara_sin_distinguir_mayusculas()
    {
        var destino = AyudaDeFusion.Qso();
        destino.Referencias.Add(Referencia(TipoDeReferencia.Sota, "EA8/GC-001"));

        var origen = AyudaDeFusion.Qso();
        origen.Referencias.Add(Referencia(TipoDeReferencia.Sota, "ea8/gc-001"));

        var resultado = FusionDeQso.Fundir(destino, origen);

        destino.Referencias.Should().ContainSingle().Which.Codigo.Should().Be("EA8/GC-001");
        resultado.HuboCambios.Should().BeFalse();
    }

    [Fact]
    public void El_mismo_codigo_en_otro_programa_es_otra_referencia()
    {
        var destino = AyudaDeFusion.Qso();
        destino.Referencias.Add(Referencia(TipoDeReferencia.Iota, "EU-004"));

        var origen = AyudaDeFusion.Qso();
        origen.Referencias.Add(Referencia(TipoDeReferencia.Wwff, "EU-004"));

        FusionDeQso.Fundir(destino, origen);

        destino.Referencias.Should().HaveCount(2);
    }

    [Fact]
    public void El_mismo_codigo_del_otro_lado_es_otra_referencia()
    {
        var destino = AyudaDeFusion.Qso();
        destino.Referencias.Add(Referencia(TipoDeReferencia.Pota, "ES-0012"));

        var origen = AyudaDeFusion.Qso();
        origen.Referencias.Add(Referencia(TipoDeReferencia.Pota, "ES-0012", LadoDeReferencia.Propia));

        FusionDeQso.Fundir(destino, origen);

        destino.Referencias.Should().HaveCount(2);
        destino.Referencias.Select(r => r.Lado).Should().Equal(
            LadoDeReferencia.Corresponsal, LadoDeReferencia.Propia);
    }

    [Fact]
    public void El_nombre_del_programa_tambien_se_compara_sin_distinguir_mayusculas()
    {
        var destino = AyudaDeFusion.Qso();
        var mia = Referencia(TipoDeReferencia.Otra, "12345");
        mia.NombrePrograma = "Castillos";
        destino.Referencias.Add(mia);

        var origen = AyudaDeFusion.Qso();
        var suya = Referencia(TipoDeReferencia.Otra, "12345");
        suya.NombrePrograma = "CASTILLOS";
        origen.Referencias.Add(suya);

        var resultado = FusionDeQso.Fundir(destino, origen);

        destino.Referencias.Should().ContainSingle();
        resultado.HuboCambios.Should().BeFalse();
    }

    [Fact]
    public void La_referencia_anadida_es_una_copia_y_no_el_objeto_del_origen()
    {
        var destino = AyudaDeFusion.Qso();
        var origen = AyudaDeFusion.Qso();
        var suya = Referencia(TipoDeReferencia.Pota, "ES-0012");
        suya.Descripcion = "Parque de Tamadaba";
        origen.Referencias.Add(suya);

        FusionDeQso.Fundir(destino, origen);

        var anadida = destino.Referencias.Should().ContainSingle().Subject;
        anadida.Should().NotBeSameAs(suya);
        anadida.Codigo.Should().Be("ES-0012");
        anadida.Descripcion.Should().Be("Parque de Tamadaba");
    }

    // ── Campos extra ─────────────────────────────────────────────────────────

    [Fact]
    public void Los_campos_extra_que_solo_tiene_el_origen_se_anaden()
    {
        var destino = AyudaDeFusion.Qso();
        destino.CamposExtra.Add(Extra("APP_LOG4OM_QSO_ID", "111"));

        var origen = AyudaDeFusion.Qso();
        origen.CamposExtra.Add(Extra("APP_LOG4OM_EQSL_STATUS", "AG"));

        var resultado = FusionDeQso.Fundir(destino, origen);

        destino.CamposExtra.Select(e => e.Nombre).Should().Equal(
            "APP_LOG4OM_QSO_ID", "APP_LOG4OM_EQSL_STATUS");
        resultado.HuboCambios.Should().BeTrue();
        resultado.Choques.Should().BeEmpty();
    }

    [Fact]
    public void El_campo_extra_con_el_mismo_nombre_y_el_mismo_valor_no_se_duplica()
    {
        var destino = AyudaDeFusion.Qso();
        destino.CamposExtra.Add(Extra("APP_LOG4OM_QSO_ID", "111"));

        var origen = AyudaDeFusion.Qso();
        origen.CamposExtra.Add(Extra("app_log4om_qso_id", "111"));

        var resultado = FusionDeQso.Fundir(destino, origen);

        destino.CamposExtra.Should().ContainSingle();
        resultado.HuboCambios.Should().BeFalse();
        resultado.Choques.Should().BeEmpty();
    }

    [Fact]
    public void El_campo_extra_con_el_mismo_nombre_y_distinto_valor_es_choque_y_no_duplicado()
    {
        var destino = AyudaDeFusion.Qso();
        destino.CamposExtra.Add(Extra("APP_LOG4OM_QSO_ID", "111"));

        var origen = AyudaDeFusion.Qso();
        origen.CamposExtra.Add(Extra("APP_LOG4OM_QSO_ID", "222"));

        var resultado = FusionDeQso.Fundir(destino, origen);

        destino.CamposExtra.Should().ContainSingle().Which.Valor.Should().Be("111");
        var choque = resultado.Choques.Should().ContainSingle().Subject;
        choque.Campo.Should().Be("APP_LOG4OM_QSO_ID");
        choque.Conservado.Should().Be("111");
        choque.Descartado.Should().Be("222");
        resultado.HuboCambios.Should().BeFalse();
    }

    [Fact]
    public void El_campo_extra_anadido_conserva_el_tipo_adif()
    {
        var destino = AyudaDeFusion.Qso();
        var origen = AyudaDeFusion.Qso();
        var suyo = Extra("APP_CUADERNO_FECHA", "20240518");
        suyo.TipoAdif = "D";
        origen.CamposExtra.Add(suyo);

        FusionDeQso.Fundir(destino, origen);

        var anadido = destino.CamposExtra.Should().ContainSingle().Subject;
        anadido.Should().NotBeSameAs(suyo);
        anadido.TipoAdif.Should().Be("D");
    }

    [Fact]
    public void Las_dos_colecciones_y_las_confirmaciones_se_unen_en_la_misma_fusion()
    {
        var destino = AyudaDeFusion.Qso()
            .Con(AyudaDeFusion.Confirmacion(MedioDeConfirmacion.Papel, recibido: EstadoDeConfirmacion.Pendiente));
        destino.Referencias.Add(Referencia(TipoDeReferencia.Iota, "EU-004"));
        destino.CamposExtra.Add(Extra("APP_LOG4OM_QSO_ID", "111"));

        var origen = AyudaDeFusion.Qso()
            .Con(AyudaDeFusion.Confirmacion(MedioDeConfirmacion.Lotw, recibido: EstadoDeConfirmacion.Verificado));
        origen.Referencias.Add(Referencia(TipoDeReferencia.Pota, "ES-0012"));
        origen.CamposExtra.Add(Extra("APP_LOG4OM_EQSL_STATUS", "AG"));

        var resultado = FusionDeQso.Fundir(destino, origen);

        destino.Confirmaciones.Should().HaveCount(2);
        destino.Referencias.Should().HaveCount(2);
        destino.CamposExtra.Should().HaveCount(2);
        resultado.HuboCambios.Should().BeTrue();
        resultado.RecuperoConfirmacion.Should().BeTrue();
        resultado.Choques.Should().BeEmpty();
    }

    private static QsoReferencia Referencia(
        TipoDeReferencia tipo,
        string codigo,
        LadoDeReferencia lado = LadoDeReferencia.Corresponsal) => new()
        {
            Tipo = tipo,
            Codigo = codigo,
            Lado = lado,
        };

    private static QsoCampoExtra Extra(string nombre, string valor) => new()
    {
        Nombre = nombre,
        Valor = valor,
    };
}
