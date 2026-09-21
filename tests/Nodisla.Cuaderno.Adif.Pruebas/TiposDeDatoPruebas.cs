using FluentAssertions;
using Nodisla.Cuaderno.Adif;
using Nodisla.Cuaderno.Dominio.Entidades;

namespace Nodisla.Cuaderno.Adif.Pruebas;

/// <summary>Los tipos de dato de ADIF: fechas, horas, numeros, logicos y enumeraciones.</summary>
public class TiposDeDatoPruebas
{
    private const string Cabecera = "<ADIF_VER:5>3.1.5 <EOH>\n";

    [Theory]
    [InlineData("20260523", "095631", "2026-05-23T09:56:31Z")]
    [InlineData("20260523", "0956", "2026-05-23T09:56:00Z")]
    [InlineData("19700101", "000000", "1970-01-01T00:00:00Z")]
    [InlineData("20260523", null, "2026-05-23T00:00:00Z")]
    public void La_fecha_y_la_hora_se_combinan_en_utc(string fecha, string? hora, string esperado)
    {
        ConversionesAdif.TryCombinarUtc(fecha, hora, out var instante).Should().BeTrue();
        instante.Should().Be(DateTimeOffset.Parse(esperado, System.Globalization.CultureInfo.InvariantCulture));
        instante.Offset.Should().Be(TimeSpan.Zero);
    }

    [Theory]
    [InlineData("2026052")]
    [InlineData("20261345")]
    [InlineData("")]
    [InlineData("hola")]
    public void Una_fecha_imposible_no_se_acepta(string fecha) =>
        ConversionesAdif.TryLeerFecha(fecha, out _).Should().BeFalse();

    [Theory]
    [InlineData("2460")]
    [InlineData("12345")]
    [InlineData("12:30")]
    public void Una_hora_imposible_no_se_acepta(string hora) =>
        ConversionesAdif.TryLeerHora(hora, out _).Should().BeFalse();

    [Fact]
    public async Task La_hora_de_cuatro_digitos_se_lee_y_se_escribe_con_segundos()
    {
        var lectura = await Ayudas.LeerAsync(
            Cabecera + "<CALL:6>EA8DLF <QSO_DATE:8>20260101 <TIME_ON:4>1200 <EOR>\n");

        lectura.Qsos[0].InicioUtc.Should().Be(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));

        // La forma corta se conserva literal para que la ida y vuelta no cambie el fichero.
        (await Ayudas.ExportarAsync(lectura.Qsos)).Should().Contain("<TIME_ON:4>1200");
    }

    [Fact]
    public async Task La_hora_de_fin_se_combina_con_su_propia_fecha()
    {
        var lectura = await Ayudas.LeerAsync(
            Cabecera
            + "<CALL:6>EA8DLF <QSO_DATE:8>20260101 <TIME_ON:6>235900 "
            + "<QSO_DATE_OFF:8>20260102 <TIME_OFF:6>000500 <EOR>\n");

        var qso = lectura.Qsos[0];
        qso.FinUtc.Should().Be(new DateTimeOffset(2026, 1, 2, 0, 5, 0, TimeSpan.Zero));
        qso.Duracion.Should().Be(TimeSpan.FromMinutes(6));
    }

    [Fact]
    public async Task Sin_fecha_de_fin_se_usa_la_del_contacto()
    {
        var lectura = await Ayudas.LeerAsync(
            Cabecera + "<CALL:6>EA8DLF <QSO_DATE:8>20260101 <TIME_ON:6>120000 <TIME_OFF:6>120500 <EOR>\n");

        lectura.Qsos[0].FinUtc.Should().Be(new DateTimeOffset(2026, 1, 1, 12, 5, 0, TimeSpan.Zero));
    }

    [Theory]
    [InlineData("MFSK", "FT4", "MFSK", "FT4")]
    [InlineData("SSB", "USB", "SSB", "USB")]
    [InlineData("FT8", null, "FT8", null)]
    [InlineData("DIGITALVOICE", "DMR", "DIGITALVOICE", "DMR")]
    [InlineData("CW", null, "CW", null)]
    [InlineData(null, "FT4", "MFSK", "FT4")]
    [InlineData("INVENTADO", null, "INVENTADO", null)]
    public async Task El_modo_y_el_submodo_se_leen_en_pareja(
        string? modo, string? submodo, string principal, string? subEsperado)
    {
        var texto = Cabecera + "<CALL:6>EA8DLF "
            + (modo is null ? string.Empty : $"<MODE:{modo.Length}>{modo} ")
            + (submodo is null ? string.Empty : $"<SUBMODE:{submodo.Length}>{submodo} ")
            + "<EOR>\n";

        var lectura = await Ayudas.LeerAsync(texto);
        lectura.Qsos[0].Mode.Principal.Should().Be(principal);
        lectura.Qsos[0].Mode.Submodo.Should().Be(subEsperado);
    }

    [Theory]
    [InlineData("Y", EstadoDeConfirmacion.Confirmado)]
    [InlineData("N", EstadoDeConfirmacion.Ninguno)]
    [InlineData("R", EstadoDeConfirmacion.Solicitado)]
    [InlineData("Q", EstadoDeConfirmacion.Pendiente)]
    [InlineData("I", EstadoDeConfirmacion.Rechazado)]
    [InlineData("V", EstadoDeConfirmacion.Verificado)]
    public void Los_estados_de_qsl_se_traducen(string letra, EstadoDeConfirmacion esperado)
    {
        ConfirmacionesAdif.TryLeerEstado(letra, esSubida: false, out var estado).Should().BeTrue();
        estado.Should().Be(esperado);
    }

    [Fact]
    public async Task Las_qsl_de_papel_y_electronicas_llegan_a_la_tabla_de_confirmaciones()
    {
        var lectura = await Ayudas.LeerAsync(
            Cabecera
            + "<CALL:6>EA8DLF <QSL_SENT:1>Y <QSL_RCVD:1>N <QSL_SENT_VIA:1>B "
            + "<QSLSDATE:8>20260101 <LOTW_QSL_SENT:1>Y <LOTW_QSL_RCVD:1>Y <LOTW_QSLRDATE:8>20260210 "
            + "<EQSL_QSL_SENT:1>N <EQSL_QSL_RCVD:1>N <CLUBLOG_QSO_UPLOAD_STATUS:1>Y <EOR>\n");

        var qso = lectura.Qsos[0];
        var papel = qso.Confirmaciones.Single(c => c.Medio == MedioDeConfirmacion.Papel);
        papel.Enviado.Should().Be(EstadoDeConfirmacion.Confirmado);
        papel.Recibido.Should().Be(EstadoDeConfirmacion.Ninguno);
        papel.Via.Should().Be(ViaDeEnvio.Buro);
        papel.EnviadoUtc.Should().Be(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        var lotw = qso.Confirmaciones.Single(c => c.Medio == MedioDeConfirmacion.Lotw);
        lotw.EstaConfirmada.Should().BeTrue();
        lotw.RecibidoUtc.Should().Be(new DateTimeOffset(2026, 2, 10, 0, 0, 0, TimeSpan.Zero));

        qso.Confirmaciones.Single(c => c.Medio == MedioDeConfirmacion.ClubLog)
            .Enviado.Should().Be(EstadoDeConfirmacion.Confirmado);

        var salida = await Ayudas.ExportarAsync(lectura.Qsos);
        salida.Should().Contain("<QSL_SENT:1>Y");
        salida.Should().Contain("<QSL_SENT_VIA:1>B");
        salida.Should().Contain("<LOTW_QSLRDATE:8>20260210");
        salida.Should().Contain("<CLUBLOG_QSO_UPLOAD_STATUS:1>Y");
    }

    [Fact]
    public async Task La_qsl_verificada_se_distingue_de_la_meramente_confirmada()
    {
        var lectura = await Ayudas.LeerAsync(Cabecera + "<CALL:6>EA8DLF <LOTW_QSL_RCVD:1>V <EOR>\n");

        var confirmacion = lectura.Qsos[0].Confirmaciones.Single();
        confirmacion.Recibido.Should().Be(EstadoDeConfirmacion.Verificado);
        confirmacion.EstaConfirmada.Should().BeTrue();
        confirmacion.EstaVerificada.Should().BeTrue();

        // Y la «V» vuelve sola, sin necesidad de conservar la letra aparte.
        lectura.Qsos[0].CamposExtra.Should().NotContain(c => c.Nombre == "LOTW_QSL_RCVD");
        (await Ayudas.ExportarAsync(lectura.Qsos)).Should().Contain("<LOTW_QSL_RCVD:1>V");
    }

    [Fact]
    public async Task La_qsl_confirmada_sin_verificar_sigue_saliendo_como_y()
    {
        var lectura = await Ayudas.LeerAsync(Cabecera + "<CALL:6>EA8DLF <LOTW_QSL_RCVD:1>Y <EOR>\n");

        var confirmacion = lectura.Qsos[0].Confirmaciones.Single();
        confirmacion.EstaConfirmada.Should().BeTrue();
        confirmacion.EstaVerificada.Should().BeFalse();
        (await Ayudas.ExportarAsync(lectura.Qsos)).Should().Contain("<LOTW_QSL_RCVD:1>Y");
    }

    [Fact]
    public async Task Los_campos_de_antena_condiciones_y_naturaleza_del_contacto_van_y_vuelven()
    {
        const string adif =
            "<ADIF_VER:5>3.1.5 <EOH>\n"
            + "<CALL:6>EA8DLF <ANT_AZ:2>22 <ANT_EL:1>0 <DISTANCE:7>1785.37 <A_INDEX:1>6 "
            + "<K_INDEX:4>1.33 <SFI:3>124 <SWL:1>N <QSO_COMPLETE:3>NIL <QSO_RANDOM:1>Y "
            + "<QSLMSG:12>Gracias Jose <MY_NAME:10>JOSE MARIA <IOTA:6>AF-004 "
            + "<IOTA_ISLAND_ID:4>1234 <EOR>\n";

        var lectura = await Ayudas.LeerAsync(adif);
        var qso = lectura.Qsos.Should().ContainSingle().Subject;

        qso.AntAz.Should().Be(22);
        qso.AntEl.Should().Be(0);
        qso.Distance.Should().Be(1785.37);
        qso.AIndex.Should().Be(6);
        qso.KIndex.Should().Be(1.33);
        qso.Sfi.Should().Be(124);
        qso.Swl.Should().BeFalse();
        qso.QsoComplete.Should().Be("NIL", "ADIF admite NIL y ? ademas de Y y N");
        qso.QsoRandom.Should().BeTrue();
        qso.QslMsg.Should().Be("Gracias Jose");
        qso.MyName.Should().Be("JOSE MARIA");
        qso.IotaIslandId.Should().Be("1234");

        // Ninguno de estos campos necesita ya conservarse aparte.
        qso.CamposExtra.Should().BeEmpty();

        var originales = await Ayudas.CamposCrudosAsync(adif);
        var devueltos = await Ayudas.CamposCrudosAsync(await Ayudas.ExportarAsync(lectura.Qsos));
        foreach (var campo in originales[0])
        {
            devueltos[0].Valor(campo.Nombre).Should().Be(campo.Valor, "el campo {0}", campo.Nombre);
        }
    }

    [Fact]
    public async Task La_distancia_declarada_en_el_fichero_no_pisa_a_la_calculada()
    {
        var lectura = await Ayudas.LeerAsync(
            Cabecera
            + "<CALL:6>EA8DLF <DISTANCE:3>999 <LAT:11>N027 58.950 <LON:11>W015 23.583 "
            + "<MY_LAT:11>N028 58.950 <MY_LON:11>W015 23.583 <EOR>\n");

        var qso = lectura.Qsos[0];
        qso.Distance.Should().Be(999, "es lo que declaraba el fichero");
        qso.DistanciaKm.Should().BeApproximately(111.2, 1.0, "es la que sale de las coordenadas");
    }

    [Fact]
    public async Task Las_referencias_de_activacion_van_y_vuelven()
    {
        var lectura = await Ayudas.LeerAsync(
            Cabecera
            + "<CALL:6>EA8DLF <IOTA:6>AF-004 <MY_IOTA:6>AF-004 <SOTA_REF:10>EA8/GC-001 "
            + "<POTA_REF:7>ES-0001 <MY_POTA_REF:7>ES-0002 <WWFF_REF:9>EAFF-0001 "
            + "<SIG:3>WCA <SIG_INFO:8>EA-00123 <EOR>\n");

        var qso = lectura.Qsos[0];
        qso.Referencias.Should().Contain(r =>
            r.Tipo == TipoDeReferencia.Iota && r.Lado == LadoDeReferencia.Corresponsal && r.Codigo == "AF-004");
        qso.Referencias.Should().Contain(r =>
            r.Tipo == TipoDeReferencia.Iota && r.Lado == LadoDeReferencia.Propia);
        qso.Referencias.Should().Contain(r => r.Tipo == TipoDeReferencia.Sota);
        qso.Referencias.Should().Contain(r =>
            r.Tipo == TipoDeReferencia.Pota && r.Lado == LadoDeReferencia.Propia && r.Codigo == "ES-0002");
        qso.Referencias.Should().Contain(r => r.Tipo == TipoDeReferencia.Wwff);
        qso.Referencias.Should().Contain(r =>
            r.Tipo == TipoDeReferencia.Otra && r.NombrePrograma == "WCA" && r.Codigo == "EA-00123");

        var salida = await Ayudas.ExportarAsync(lectura.Qsos);
        salida.Should().Contain("<IOTA:6>AF-004");
        salida.Should().Contain("<MY_POTA_REF:7>ES-0002");
        salida.Should().Contain("<SIG_INFO:8>EA-00123");
    }

    [Fact]
    public async Task Los_campos_del_estandar_que_el_modelo_cubre_van_y_vuelven()
    {
        const string adif =
            "<ADIF_VER:5>3.1.5 <EOH>\n"
            + "<CALL:6>EA8DLF <QSO_DATE:8>20260523 <TIME_ON:6>095631 <QSO_DATE_OFF:8>20260523 "
            + "<TIME_OFF:6>095637 <BAND:3>20m <BAND_RX:3>40m <MODE:4>MFSK <SUBMODE:3>FT4 "
            + "<FREQ:9>14.074000 <FREQ_RX:8>7.074000 <RST_SENT:3>-05 <RST_RCVD:3>+02 "
            + "<TX_PWR:2>50 <RX_PWR:2>10 <PROP_MODE:3>SAT <SAT_NAME:5>AO-91 <SAT_MODE:2>UV "
            + "<NAME:4>JOSE <ADDRESS:11>Calle Falsa <QTH:5>TELDE <EMAIL:14>ea8dlf@qrz.com "
            + "<WEB:15>https://qrz.com <GRIDSQUARE:6>IL27hx <GRIDSQUARE_EXT:2>aa "
            + "<LAT:11>N027 58.950 <LON:11>W015 23.583 <ALTITUDE:3>250 <CONT:2>AF "
            + "<COUNTRY:14>Canary Islands <DXCC:2>29 <CQZ:2>33 <ITUZ:2>36 <PFX:3>EA8 "
            + "<STATE:2>GC <CNTY:6>ESPAÑA <REGION:2>IS <DARC_DOK:3>A01 <AGE:2>45 "
            + "<RIG:6>FT-710 <SILENT_KEY:1>Y <EQ_CALL:6>EA8DLF <CONTACTED_OP:6>EA8XYZ "
            + "<QSL_VIA:6>EA8URE <STATION_CALLSIGN:6>EA8DLF <OPERATOR:6>EA8DLF "
            + "<OWNER_CALLSIGN:6>EA8DLF <MY_GRIDSQUARE:6>IL27hx <MY_CITY:5>TELDE "
            + "<MY_STATE:2>GC <MY_CNTY:7>ESPAÑA <MY_COUNTRY:14>Canary Islands <MY_DXCC:2>29 "
            + "<MY_CQ_ZONE:2>33 <MY_ITU_ZONE:2>36 <MY_LAT:11>N027 58.950 <MY_LON:11>W015 23.583 "
            + "<MY_ALTITUDE:2>40 <MY_RIG:12>YAESU FT-710 <MY_ANTENNA:11>ECOMET HF-7 "
            + "<CONTEST_ID:8>CQ-WW-SS <STX:3>001 <STX_STRING:2>GC <SRX:3>002 <SRX_STRING:2>MD "
            + "<COMMENT:9>Un apunte <NOTES:9>Reservado <EOR>\n";

        var lectura = await Ayudas.LeerAsync(adif);
        var qso = lectura.Qsos.Should().ContainSingle().Subject;

        qso.Call.Valor.Should().Be("EA8DLF");
        qso.Band.Nombre.Should().Be("20m");
        qso.BandRx.Nombre.Should().Be("40m");
        qso.Mode.ToString().Should().Be("MFSK/FT4");
        qso.Freq.Megahercios.Should().Be(14.074m);
        qso.FreqRx!.Value.Megahercios.Should().Be(7.074m);
        qso.RstSent.Decibelios.Should().Be(-5);
        qso.RstRcvd.Decibelios.Should().Be(2);
        qso.TxPwr.Should().Be(50);
        qso.RxPwr.Should().Be(10);
        qso.PropMode.Should().Be("SAT");
        qso.SatName.Should().Be("AO-91");
        qso.Gridsquare.Valor.Should().Be("IL27HX");
        qso.GridsquareExt.Should().Be("aa");
        qso.Altitude.Should().Be(250);
        qso.Dxcc.Should().Be(29);
        qso.Cqz.Should().Be(33);
        qso.Ituz.Should().Be(36);
        qso.State.Should().Be("GC");
        qso.Cnty.Should().Be("ESPAÑA");
        qso.Region.Should().Be("IS");
        qso.DarcDok.Should().Be("A01");
        qso.Age.Should().Be(45);
        qso.Rig.Should().Be("FT-710");
        qso.SilentKey.Should().BeTrue();
        qso.EqCall.Should().Be("EA8DLF");
        qso.ContactedOp.Should().Be("EA8XYZ");
        qso.QslVia.Should().Be("EA8URE");
        qso.StationCallsign.Valor.Should().Be("EA8DLF");
        qso.MyGridsquare.Valor.Should().Be("IL27HX");
        qso.MyAltitude.Should().Be(40);
        qso.MyAntenna.Should().Be("ECOMET HF-7");
        qso.ContestId.Should().Be("CQ-WW-SS");
        qso.Stx.Should().Be(1);
        qso.Srx.Should().Be(2);
        qso.SrxString.Should().Be("MD");
        qso.Comentario.Should().Be("Un apunte");
        qso.Notas.Should().Be("Reservado");
        qso.DistanciaKm.Should().NotBeNull();

        var originales = await Ayudas.CamposCrudosAsync(adif);
        var devueltos = await Ayudas.CamposCrudosAsync(await Ayudas.ExportarAsync(lectura.Qsos));
        foreach (var campo in originales[0])
        {
            devueltos[0].Valor(campo.Nombre).Should().Be(campo.Valor, "el campo {0}", campo.Nombre);
        }
    }

    [Fact]
    public void El_indicador_de_tipo_del_campo_se_conserva()
    {
        var campos = MapeoAdif.EscribirContacto(new Qso
        {
            CamposExtra = { new QsoCampoExtra { Nombre = "APP_X_Y", Valor = "1", TipoAdif = "N" } },
        });
        campos.Should().Contain(c => c.Nombre == "APP_X_Y" && c.TipoAdif == "N");
    }

    [Fact]
    public async Task El_indicador_de_tipo_sobrevive_a_la_ida_y_vuelta()
    {
        var lectura = await Ayudas.LeerAsync(Cabecera + "<CALL:6>EA8DLF <APP_X_MIO:3:N>123 <EOR>\n");
        lectura.Qsos[0].CamposExtra.Should().Contain(c => c.Nombre == "APP_X_MIO" && c.TipoAdif == "N");
        (await Ayudas.ExportarAsync(lectura.Qsos)).Should().Contain("<APP_X_MIO:3:N>123");
    }

    [Fact]
    public async Task Sin_campos_extra_se_escribe_la_version_canonica()
    {
        var lectura = await Ayudas.LeerAsync(Cabecera + "<CALL:6>EA8DLF <FREQ:6>14.074 <APP_X_Y:1>1 <EOR>\n");

        var conExtras = await Ayudas.ExportarAsync(lectura.Qsos);
        conExtras.Should().Contain("<FREQ:6>14.074").And.Contain("<APP_X_Y:1>1");

        var sinExtras = await Ayudas.ExportarAsync(
            lectura.Qsos, new Aplicacion.Puertos.OpcionesAdif { IncluirCamposExtra = false });
        sinExtras.Should().Contain("<FREQ:9>14.074000").And.NotContain("APP_X_Y");
    }
}
