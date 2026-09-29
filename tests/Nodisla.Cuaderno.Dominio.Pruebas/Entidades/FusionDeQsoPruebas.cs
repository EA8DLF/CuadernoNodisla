using FluentAssertions;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Dominio.Pruebas.Entidades;

/// <summary>
/// Pruebas de la fusion de dos copias del mismo contacto en lo que toca a los campos sueltos:
/// el hueco se rellena, el dato distinto no se pisa en silencio y la contabilidad del cuaderno
/// no se toca.
/// </summary>
public sealed class FusionDeQsoPruebas
{
    // ── Un valor gana al hueco, en las dos direcciones ───────────────────────

    [Fact]
    public void El_texto_del_origen_rellena_el_hueco_del_destino()
    {
        var destino = AyudaDeFusion.Qso();
        var origen = AyudaDeFusion.Qso();
        origen.Name = "Ana";
        origen.Qth = "Las Palmas";

        var resultado = FusionDeQso.Fundir(destino, origen);

        destino.Name.Should().Be("Ana");
        destino.Qth.Should().Be("Las Palmas");
        resultado.HuboCambios.Should().BeTrue();
        resultado.Choques.Should().BeEmpty();
    }

    [Fact]
    public void El_texto_del_destino_sobrevive_al_hueco_del_origen()
    {
        var destino = AyudaDeFusion.Qso();
        destino.Name = "Ana";
        var origen = AyudaDeFusion.Qso();

        var resultado = FusionDeQso.Fundir(destino, origen);

        destino.Name.Should().Be("Ana");
        resultado.HuboCambios.Should().BeFalse();
        resultado.Choques.Should().BeEmpty();
    }

    [Fact]
    public void El_texto_en_blanco_del_origen_no_cuenta_como_valor()
    {
        var destino = AyudaDeFusion.Qso();
        destino.Name = "Ana";
        var origen = AyudaDeFusion.Qso();
        origen.Name = "   ";

        var resultado = FusionDeQso.Fundir(destino, origen);

        destino.Name.Should().Be("Ana");
        resultado.HuboCambios.Should().BeFalse();
    }

    [Fact]
    public void El_numero_anulable_del_origen_rellena_el_hueco_del_destino()
    {
        var destino = AyudaDeFusion.Qso();
        var origen = AyudaDeFusion.Qso();
        origen.TxPwr = 100;
        origen.Cqz = 33;
        origen.Ituz = 36;
        origen.AIndex = 7;
        origen.KIndex = 2;
        origen.Sfi = 142;

        var resultado = FusionDeQso.Fundir(destino, origen);

        destino.TxPwr.Should().Be(100);
        destino.Cqz.Should().Be(33);
        destino.Ituz.Should().Be(36);
        destino.AIndex.Should().Be(7);
        destino.KIndex.Should().Be(2);
        destino.Sfi.Should().Be(142);
        resultado.HuboCambios.Should().BeTrue();
        resultado.Choques.Should().BeEmpty();
    }

    [Fact]
    public void El_numero_anulable_del_destino_sobrevive_al_hueco_del_origen()
    {
        var destino = AyudaDeFusion.Qso();
        destino.TxPwr = 100;
        destino.Cqz = 33;
        var origen = AyudaDeFusion.Qso();

        var resultado = FusionDeQso.Fundir(destino, origen);

        destino.TxPwr.Should().Be(100);
        destino.Cqz.Should().Be(33);
        resultado.HuboCambios.Should().BeFalse();
    }

    [Fact]
    public void El_dxcc_cero_se_trata_como_hueco_en_las_dos_direcciones()
    {
        var destino = AyudaDeFusion.Qso();
        var origen = AyudaDeFusion.Qso();
        origen.Dxcc = 281;

        FusionDeQso.Fundir(destino, origen).HuboCambios.Should().BeTrue();
        destino.Dxcc.Should().Be(281);

        var alReves = FusionDeQso.Fundir(destino, AyudaDeFusion.Qso());

        destino.Dxcc.Should().Be(281);
        alReves.HuboCambios.Should().BeFalse();
    }

    [Fact]
    public void El_booleano_cierto_del_origen_rellena_el_falso_del_destino()
    {
        var destino = AyudaDeFusion.Qso();
        var origen = AyudaDeFusion.Qso();
        origen.SilentKey = true;
        origen.Swl = true;

        var resultado = FusionDeQso.Fundir(destino, origen);

        destino.SilentKey.Should().BeTrue();
        destino.Swl.Should().BeTrue();
        resultado.HuboCambios.Should().BeTrue();
        resultado.Choques.Should().BeEmpty();
    }

    [Fact]
    public void El_booleano_cierto_del_destino_no_lo_apaga_el_falso_del_origen()
    {
        var destino = AyudaDeFusion.Qso();
        destino.SilentKey = true;
        var origen = AyudaDeFusion.Qso();

        var resultado = FusionDeQso.Fundir(destino, origen);

        destino.SilentKey.Should().BeTrue();
        resultado.HuboCambios.Should().BeFalse();
        resultado.Choques.Should().BeEmpty();
    }

    [Fact]
    public void El_booleano_anulable_distingue_el_no_del_no_consta()
    {
        var destino = AyudaDeFusion.Qso();
        var origen = AyudaDeFusion.Qso();
        origen.QsoRandom = false;

        var resultado = FusionDeQso.Fundir(destino, origen);

        destino.QsoRandom.Should().BeFalse();
        resultado.HuboCambios.Should().BeTrue();
    }

    [Fact]
    public void Los_objetos_de_valor_del_origen_rellenan_los_huecos_del_destino()
    {
        var destino = AyudaDeFusion.Vacio();
        destino.InicioUtc = AyudaDeFusion.Instante;
        var origen = AyudaDeFusion.Qso();
        origen.RstSent = Informe.Parse("59");
        origen.RstRcvd = Informe.Parse("57");
        origen.Gridsquare = Locator.Parse("IL18QK");
        origen.StationCallsign = Indicativo.Parse("EA8DLF");
        origen.MyGridsquare = Locator.Parse("IL18QK");
        origen.FreqRx = Frecuencia.DesdeMegahercios(14.21m);
        origen.BandRx = Banda.Parse("20m");

        var resultado = FusionDeQso.Fundir(destino, origen);

        destino.Call.Valor.Should().Be("EA1ABC");
        destino.Band.Nombre.Should().Be("20m");
        destino.Mode.Principal.Should().Be("SSB");
        destino.Freq.Megahercios.Should().Be(14.2m);
        destino.FreqRx!.Value.Megahercios.Should().Be(14.21m);
        destino.BandRx.Nombre.Should().Be("20m");
        destino.RstSent.Texto.Should().Be("59");
        destino.RstRcvd.Texto.Should().Be("57");
        destino.Gridsquare.Valor.Should().Be("IL18QK");
        destino.StationCallsign.Valor.Should().Be("EA8DLF");
        destino.MyGridsquare.Valor.Should().Be("IL18QK");
        resultado.HuboCambios.Should().BeTrue();
        resultado.Choques.Should().BeEmpty();
    }

    [Fact]
    public void Los_objetos_de_valor_del_destino_sobreviven_a_los_huecos_del_origen()
    {
        var destino = AyudaDeFusion.Qso();
        destino.RstSent = Informe.Parse("59");
        destino.Gridsquare = Locator.Parse("IL18QK");
        var origen = AyudaDeFusion.Vacio();
        origen.InicioUtc = AyudaDeFusion.Instante;

        var resultado = FusionDeQso.Fundir(destino, origen);

        destino.Call.Valor.Should().Be("EA1ABC");
        destino.Band.Nombre.Should().Be("20m");
        destino.Freq.Megahercios.Should().Be(14.2m);
        destino.RstSent.Texto.Should().Be("59");
        destino.Gridsquare.Valor.Should().Be("IL18QK");
        resultado.HuboCambios.Should().BeFalse();
        resultado.Choques.Should().BeEmpty();
    }

    [Fact]
    public void La_frecuencia_cero_se_trata_como_hueco()
    {
        var destino = AyudaDeFusion.Qso(mhz: 0m);
        var origen = AyudaDeFusion.Qso(mhz: 14.074m);

        var resultado = FusionDeQso.Fundir(destino, origen);

        destino.Freq.Megahercios.Should().Be(14.074m);
        resultado.Choques.Should().BeEmpty();
    }

    [Fact]
    public void La_fecha_anulable_del_origen_rellena_el_hueco_del_destino()
    {
        var destino = AyudaDeFusion.Qso();
        var origen = AyudaDeFusion.Qso();
        origen.FinUtc = AyudaDeFusion.Instante.AddMinutes(3);

        var resultado = FusionDeQso.Fundir(destino, origen);

        destino.FinUtc.Should().Be(AyudaDeFusion.Instante.AddMinutes(3));
        resultado.HuboCambios.Should().BeTrue();
        resultado.Choques.Should().BeEmpty();
    }

    [Fact]
    public void La_fecha_anulable_del_destino_sobrevive_al_hueco_del_origen()
    {
        var destino = AyudaDeFusion.Qso();
        destino.FinUtc = AyudaDeFusion.Instante.AddMinutes(3);
        var origen = AyudaDeFusion.Qso();

        var resultado = FusionDeQso.Fundir(destino, origen);

        destino.FinUtc.Should().Be(AyudaDeFusion.Instante.AddMinutes(3));
        resultado.HuboCambios.Should().BeFalse();
    }

    // ── Dos valores distintos no se pisan en silencio ────────────────────────

    [Fact]
    public void El_texto_distinto_deja_choque_y_gana_el_destino()
    {
        var destino = AyudaDeFusion.Qso();
        destino.Name = "Ana";
        var origen = AyudaDeFusion.Qso();
        origen.Name = "Luis";

        var resultado = FusionDeQso.Fundir(destino, origen);

        destino.Name.Should().Be("Ana");
        resultado.HuboCambios.Should().BeFalse();
        var choque = resultado.Choques.Should().ContainSingle().Subject;
        choque.Campo.Should().Be("NAME");
        choque.Conservado.Should().Be("Ana");
        choque.Descartado.Should().Be("Luis");
        choque.ClaveNatural.Should().Be(destino.ClaveNatural);
    }

    [Fact]
    public void El_numero_distinto_deja_choque_con_los_dos_valores_en_texto()
    {
        var destino = AyudaDeFusion.Qso();
        destino.TxPwr = 100;
        var origen = AyudaDeFusion.Qso();
        origen.TxPwr = 50;

        var resultado = FusionDeQso.Fundir(destino, origen);

        destino.TxPwr.Should().Be(100);
        var choque = resultado.Choques.Should().ContainSingle().Subject;
        choque.Campo.Should().Be("TX_PWR");
        choque.Conservado.Should().Be("100");
        choque.Descartado.Should().Be("50");
    }

    [Fact]
    public void Los_indices_de_propagacion_distintos_dejan_un_choque_cada_uno()
    {
        var destino = AyudaDeFusion.Qso();
        destino.AIndex = 7;
        destino.KIndex = 2;
        var origen = AyudaDeFusion.Qso();
        origen.AIndex = 9;
        origen.KIndex = 3;

        var resultado = FusionDeQso.Fundir(destino, origen);

        resultado.Choques.Select(c => c.Campo).Should().Equal("A_INDEX", "K_INDEX");
        resultado.HuboCambios.Should().BeFalse();
    }

    [Theory]
    [InlineData("CALL")]
    [InlineData("BAND")]
    [InlineData("FREQ")]
    [InlineData("RST_SENT")]
    [InlineData("GRIDSQUARE")]
    public void Los_objetos_de_valor_distintos_dejan_choque(string campo)
    {
        var destino = AyudaDeFusion.Qso();
        destino.RstSent = Informe.Parse("59");
        destino.Gridsquare = Locator.Parse("IL18QK");

        var origen = AyudaDeFusion.Qso(
            call: campo == "CALL" ? "EA1XYZ" : "EA1ABC",
            banda: campo == "BAND" ? "40m" : "20m",
            mhz: campo == "FREQ" ? 14.074m : 14.2m);
        origen.RstSent = Informe.Parse(campo == "RST_SENT" ? "55" : "59");
        origen.Gridsquare = Locator.Parse(campo == "GRIDSQUARE" ? "IN80" : "IL18QK");

        var resultado = FusionDeQso.Fundir(destino, origen);

        resultado.Choques.Should().ContainSingle().Which.Campo.Should().Be(campo);
    }

    [Fact]
    public void El_indicativo_distinto_conserva_el_del_destino_en_el_choque()
    {
        var destino = AyudaDeFusion.Qso(call: "EA1ABC");
        var origen = AyudaDeFusion.Qso(call: "EA1XYZ");

        var resultado = FusionDeQso.Fundir(destino, origen);

        destino.Call.Valor.Should().Be("EA1ABC");
        var choque = resultado.Choques.Should().ContainSingle().Subject;
        choque.Conservado.Should().Be("EA1ABC");
        choque.Descartado.Should().Be("EA1XYZ");
    }

    [Fact]
    public void La_frecuencia_distinta_se_describe_en_el_choque_con_punto_decimal()
    {
        var destino = AyudaDeFusion.Qso(mhz: 14.2m);
        var origen = AyudaDeFusion.Qso(mhz: 14.074m);

        var choque = FusionDeQso.Fundir(destino, origen).Choques.Should().ContainSingle().Subject;

        choque.Conservado.Should().Be("14.2 MHz");
        choque.Descartado.Should().Be("14.074 MHz");
    }

    [Fact]
    public void La_fecha_distinta_se_describe_en_el_choque_en_utc()
    {
        var destino = AyudaDeFusion.Qso();
        destino.FinUtc = AyudaDeFusion.Instante.AddMinutes(1);
        var origen = AyudaDeFusion.Qso();
        origen.FinUtc = AyudaDeFusion.Instante.AddMinutes(2);

        var resultado = FusionDeQso.Fundir(destino, origen);

        destino.FinUtc.Should().Be(AyudaDeFusion.Instante.AddMinutes(1));
        var choque = resultado.Choques.Should().ContainSingle().Subject;
        choque.Campo.Should().Be("TIME_OFF");
        choque.Conservado.Should().Be("2024-05-18 21:15:37");
        choque.Descartado.Should().Be("2024-05-18 21:16:37");
    }

    [Fact]
    public void El_choque_lleva_la_clave_natural_del_contacto_afectado()
    {
        var destino = AyudaDeFusion.Qso();
        destino.Comentario = "Primer contacto";
        var origen = AyudaDeFusion.Qso();
        origen.Comentario = "Segundo contacto";

        var resultado = FusionDeQso.Fundir(destino, origen);

        resultado.Choques.Should().ContainSingle()
            .Which.ClaveNatural.Should().Be("EA1ABC|20m|SSB|2024-05-18 21:14:37");
    }

    // ── El submodo no es un choque ───────────────────────────────────────────

    [Fact]
    public void El_submodo_del_origen_completa_el_modo_a_secas_del_destino()
    {
        var destino = AyudaDeFusion.Qso(modo: "MFSK", banda: "20m", mhz: 14.08m);
        var origen = AyudaDeFusion.Qso(modo: "MFSK/FT4", banda: "20m", mhz: 14.08m);

        var resultado = FusionDeQso.Fundir(destino, origen);

        destino.Mode.ToString().Should().Be("MFSK/FT4");
        resultado.HuboCambios.Should().BeTrue();
        resultado.Choques.Should().BeEmpty();
    }

    [Fact]
    public void Dos_submodos_distintos_del_mismo_modo_si_son_choque()
    {
        var destino = AyudaDeFusion.Qso(modo: "MFSK/FT4", banda: "20m", mhz: 14.08m);
        var origen = AyudaDeFusion.Qso(modo: "MFSK/FT8", banda: "20m", mhz: 14.08m);

        var resultado = FusionDeQso.Fundir(destino, origen);

        destino.Mode.ToString().Should().Be("MFSK/FT4");
        var choque = resultado.Choques.Should().ContainSingle().Subject;
        choque.Campo.Should().Be("MODE");
        choque.Conservado.Should().Be("MFSK/FT4");
        choque.Descartado.Should().Be("MFSK/FT8");
    }

    [Fact]
    public void Dos_modos_principales_distintos_son_choque()
    {
        var destino = AyudaDeFusion.Qso(modo: "SSB");
        var origen = AyudaDeFusion.Qso(modo: "CW");

        var resultado = FusionDeQso.Fundir(destino, origen);

        destino.Mode.Principal.Should().Be("SSB");
        resultado.Choques.Should().ContainSingle().Which.Campo.Should().Be("MODE");
    }

    [Fact]
    public void El_modo_vacio_del_destino_lo_pone_el_origen()
    {
        var destino = AyudaDeFusion.Vacio();
        destino.InicioUtc = AyudaDeFusion.Instante;
        var origen = AyudaDeFusion.Qso(modo: "MFSK/FT4", banda: "20m", mhz: 14.08m);

        var resultado = FusionDeQso.Fundir(destino, origen);

        destino.Mode.ToString().Should().Be("MFSK/FT4");
        resultado.Choques.Should().BeEmpty();
    }

    [Fact]
    public void El_mismo_modo_con_submodo_en_los_dos_no_cambia_nada_ni_anota_choque()
    {
        var destino = AyudaDeFusion.Qso(modo: "MFSK/FT4", banda: "20m", mhz: 14.08m);
        var origen = AyudaDeFusion.Qso(modo: "MFSK/FT4", banda: "20m", mhz: 14.08m);

        var resultado = FusionDeQso.Fundir(destino, origen);

        destino.Mode.ToString().Should().Be("MFSK/FT4");
        resultado.HuboCambios.Should().BeFalse();
        resultado.Choques.Should().BeEmpty();
    }

    [Fact]
    public void El_modo_a_secas_del_origen_no_borra_el_submodo_del_destino_ni_anota_choque()
    {
        var destino = AyudaDeFusion.Qso(modo: "MFSK/FT4", banda: "20m", mhz: 14.08m);
        var origen = AyudaDeFusion.Qso(modo: "MFSK", banda: "20m", mhz: 14.08m);

        var resultado = FusionDeQso.Fundir(destino, origen);

        destino.Mode.ToString().Should().Be("MFSK/FT4");
        resultado.HuboCambios.Should().BeFalse();
        resultado.Choques.Should().BeEmpty();
    }

    [Fact]
    public void El_submodo_sobrevive_se_funda_en_el_sentido_que_se_funda()
    {
        var conSubmodo = () => AyudaDeFusion.Qso(modo: "MFSK/FT4", banda: "20m", mhz: 14.08m);
        var aSecas = () => AyudaDeFusion.Qso(modo: "MFSK", banda: "20m", mhz: 14.08m);

        var unSentido = conSubmodo();
        var primera = FusionDeQso.Fundir(unSentido, aSecas());

        var elOtro = aSecas();
        var segunda = FusionDeQso.Fundir(elOtro, conSubmodo());

        elOtro.Mode.Should().Be(unSentido.Mode);
        elOtro.Mode.ToString().Should().Be("MFSK/FT4");
        primera.Choques.Should().BeEmpty();
        segunda.Choques.Should().BeEmpty();
    }

    // ── Lo que no se toca ────────────────────────────────────────────────────

    [Fact]
    public void La_contabilidad_del_cuaderno_no_la_toca_la_fusion()
    {
        var destino = AyudaDeFusion.Qso();
        destino.Id = 7;
        destino.CreadoUtc = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var uuid = destino.Uuid;

        var origen = AyudaDeFusion.Qso();
        origen.Id = 99;
        origen.Name = "Ana";
        origen.CreadoUtc = new DateTimeOffset(2025, 9, 9, 12, 0, 0, TimeSpan.Zero);

        var resultado = FusionDeQso.Fundir(destino, origen);

        resultado.HuboCambios.Should().BeTrue();
        destino.Id.Should().Be(7);
        destino.Uuid.Should().Be(uuid);
        destino.Uuid.Should().NotBe(origen.Uuid);
        destino.CreadoUtc.Should().Be(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void El_origen_no_se_modifica_al_fundir()
    {
        var destino = AyudaDeFusion.Qso();
        destino.Name = "Ana";
        destino.Comentario = "Del destino";
        var origen = AyudaDeFusion.Qso();
        origen.Notas = "Del origen";

        FusionDeQso.Fundir(destino, origen);

        origen.Name.Should().BeNull();
        origen.Comentario.Should().BeNull();
        origen.Notas.Should().Be("Del origen");
    }

    // ── HuboCambios ──────────────────────────────────────────────────────────

    [Fact]
    public void No_hubo_cambios_cuando_el_origen_no_aporta_nada()
    {
        var destino = AyudaDeFusion.Qso();
        destino.Name = "Ana";
        destino.TxPwr = 100;
        destino.Con(AyudaDeFusion.Confirmacion(
            MedioDeConfirmacion.Lotw, recibido: EstadoDeConfirmacion.Confirmado));

        var origen = AyudaDeFusion.Qso();

        var resultado = FusionDeQso.Fundir(destino, origen);

        resultado.HuboCambios.Should().BeFalse();
        resultado.RecuperoConfirmacion.Should().BeFalse();
        resultado.Choques.Should().BeEmpty();
    }

    [Fact]
    public void No_hubo_cambios_cuando_las_dos_copias_son_identicas()
    {
        var destino = AyudaDeFusion.Qso();
        destino.Name = "Ana";
        destino.Con(AyudaDeFusion.Confirmacion(
            MedioDeConfirmacion.Papel,
            recibido: EstadoDeConfirmacion.Confirmado,
            recibidoUtc: AyudaDeFusion.Instante));

        var resultado = FusionDeQso.Fundir(destino, destino.Clon());

        resultado.HuboCambios.Should().BeFalse();
        resultado.Choques.Should().BeEmpty();
    }

    [Fact]
    public void Un_solo_dato_nuevo_basta_para_que_hubiera_cambios()
    {
        var destino = AyudaDeFusion.Qso();
        var origen = AyudaDeFusion.Qso();
        origen.Notas = "Via buro";

        FusionDeQso.Fundir(destino, origen).HuboCambios.Should().BeTrue();
    }

    [Fact]
    public void Un_choque_a_solas_no_cuenta_como_cambio()
    {
        var destino = AyudaDeFusion.Qso();
        destino.Comentario = "Uno";
        var origen = AyudaDeFusion.Qso();
        origen.Comentario = "Otro";

        var resultado = FusionDeQso.Fundir(destino, origen);

        resultado.HuboCambios.Should().BeFalse();
        resultado.Choques.Should().ContainSingle();
    }

    [Fact]
    public void Fundir_exige_los_dos_contactos()
    {
        var qso = AyudaDeFusion.Qso();

        var sinDestino = () => FusionDeQso.Fundir(null!, qso);
        var sinOrigen = () => FusionDeQso.Fundir(qso, null!);

        sinDestino.Should().Throw<ArgumentNullException>();
        sinOrigen.Should().Throw<ArgumentNullException>();
    }
}
