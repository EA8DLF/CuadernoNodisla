using System.Text;
using FluentAssertions;
using Nodisla.Cuaderno.Adif;
using Nodisla.Cuaderno.Dominio.Entidades;

namespace Nodisla.Cuaderno.Adif.Pruebas;

/// <summary>
/// Cada una de las trampas de formato encontradas en el ADIF real de Log4OM, con su caso.
/// </summary>
public class TrampasPruebas
{
    private const string Cabecera = "<ADIF_VER:5>3.1.5 <PROGRAMID:7>LOG4OM2 <EOH>\n";

    // ── Trampa 1: coordenadas en grados y minutos ────────────────────────────

    [Theory]
    [InlineData("N027 58.950", 27.9825)]
    [InlineData("W015 23.583", -15.39305)]
    [InlineData("N000 00.000", 0.0)]
    [InlineData("S033 27.000", -33.45)]
    [InlineData("E000 00.000", 0.0)]
    public void Las_coordenadas_de_adif_se_leen_en_grados_y_minutos(string texto, double esperado)
    {
        ConversionesAdif.TryLeerCoordenada(texto, out var grados).Should().BeTrue();
        grados.Should().BeApproximately(esperado, 0.00001);
    }

    [Theory]
    [InlineData(27.9825, "N027 58.950")]
    [InlineData(-33.45, "S033 27.000")]
    [InlineData(0.0, "N000 00.000")]
    public void Las_latitudes_se_escriben_como_las_espera_adif(double grados, string esperado) =>
        ConversionesAdif.EscribirLatitud(grados).Should().Be(esperado);

    [Theory]
    [InlineData(-15.39305, "W015 23.583")]
    [InlineData(15.39305, "E015 23.583")]
    public void Las_longitudes_se_escriben_como_las_espera_adif(double grados, string esperado) =>
        ConversionesAdif.EscribirLongitud(grados).Should().Be(esperado);

    [Fact]
    public void Los_minutos_que_redondean_a_sesenta_suben_el_grado() =>
        ConversionesAdif.EscribirLatitud(27.99999999).Should().Be("N028 00.000");

    [Fact]
    public async Task Las_coordenadas_del_fichero_real_van_y_vuelven_igual()
    {
        var adif = Cabecera
            + "<CALL:6>EA8DLF <QSO_DATE:8>20260523 <TIME_ON:6>095631 <BAND:3>20m <MODE:3>SSB "
            + "<LAT:11>N027 58.950 <LON:11>W015 23.583 "
            + "<MY_LAT:11>N027 58.950 <MY_LON:11>W015 23.583 <EOR>\n";

        var lectura = await Ayudas.LeerAsync(adif);
        var qso = lectura.Qsos.Should().ContainSingle().Subject;
        qso.Lat.Should().BeApproximately(27.9825, 0.00001);
        qso.Lon.Should().BeApproximately(-15.39305, 0.00001);

        var salida = await Ayudas.ExportarAsync(lectura.Qsos);
        salida.Should().Contain("<LAT:11>N027 58.950");
        salida.Should().Contain("<MY_LON:11>W015 23.583");
    }

    // ── Trampa 2: longitudes en bytes, y longitudes que mienten ──────────────

    [Fact]
    public async Task La_longitud_se_cuenta_en_bytes_no_en_caracteres()
    {
        // «ESPAÑA» son seis caracteres pero siete bytes en UTF-8.
        var adif = Cabecera + "<CALL:6>EA8DLF <MY_CNTY:7>ESPAÑA <QTH:5>TELDE <EOR>\n";

        var lectura = await Ayudas.LeerAsync(adif);
        lectura.Qsos[0].MyCnty.Should().Be("ESPAÑA");
        lectura.Qsos[0].Qth.Should().Be("TELDE");
    }

    [Fact]
    public async Task Se_tolera_la_longitud_contada_en_caracteres_como_hace_log4om()
    {
        // Log4OM declara 6 donde el estandar pide 7: hay que leerlo igual y sin quejarse.
        var adif = Cabecera + "<CALL:6>EA8DLF <MY_CNTY:6>ESPAÑA <QTH:5>TELDE <EOR>\n";

        var lectura = await Ayudas.LeerAsync(adif);
        lectura.Qsos[0].MyCnty.Should().Be("ESPAÑA");
        lectura.Qsos[0].Qth.Should().Be("TELDE");
        lectura.Avisos.Should().BeEmpty("es un dialecto conocido, no un fichero roto");
    }

    [Fact]
    public async Task La_longitud_que_se_pasa_se_corrige_leyendo_hasta_donde_haya()
    {
        // La longitud declarada no cabe en lo que queda de fichero: no hay nada que contar, se
        // lee lo que hay y se avisa en lugar de descartar el contacto.
        var adif = Cabecera + "<CALL:6>EA8DLF <QTH:200>LAS PALMAS";

        var lectura = await Ayudas.LeerAsync(adif);
        lectura.Qsos[0].Qth.Should().Be("LAS PALMAS");
        lectura.Avisos.Should().Contain(a => a.Campo == "QTH" && !a.EsFatal);
    }

    [Fact]
    public async Task Cuando_la_longitud_cabe_manda_ella_y_el_sobrante_se_descarta()
    {
        var adif = Cabecera + "<CALL:6>EA8DLF <QTH:2>LAS PALMAS <NAME:4>JOSE <EOR>\n";

        var lectura = await Ayudas.LeerAsync(adif);
        lectura.Qsos[0].Qth.Should().Be("LA");
        lectura.Qsos[0].Name.Should().Be("JOSE");
        lectura.Avisos.Should().Contain(a => a.Campo == "QTH" && !a.EsFatal);
    }

    [Fact]
    public async Task El_valor_que_contiene_un_signo_de_menor_que_no_se_parte()
    {
        var adif = Cabecera + "<CALL:6>EA8DLF <COMMENT:13>3 < 5 y 7 > 4 <NAME:4>JOSE <EOR>\n";

        var lectura = await Ayudas.LeerAsync(adif);
        lectura.Qsos[0].Comentario.Should().Be("3 < 5 y 7 > 4");
        lectura.Qsos[0].Name.Should().Be("JOSE");
    }

    // ── Trampa 3: JSON embebido de Log4OM ────────────────────────────────────

    [Fact]
    public async Task El_json_de_confirmaciones_se_traduce_a_la_tabla_de_confirmaciones()
    {
        const string json =
            "[{\"CT\":\"QSL\",\"S\":\"No\",\"R\":\"No\",\"SV\":\"Electronic\",\"RV\":\"Electronic\"},"
            + "{\"CT\":\"LOTW\",\"S\":\"Yes\",\"R\":\"Yes\",\"SV\":\"Electronic\",\"RV\":\"Electronic\","
            + "\"RD\":\"2023-03-11T00:00:00Z\"}]";
        var adif = Cabecera
            + $"<CALL:6>EA8DLF <APP_L4ONG_QSO_CONFIRMATIONS:{Encoding.UTF8.GetByteCount(json)}>{json} <EOR>\n";

        var lectura = await Ayudas.LeerAsync(adif);
        var qso = lectura.Qsos[0];

        var lotw = qso.Confirmaciones.Single(c => c.Medio == MedioDeConfirmacion.Lotw);
        lotw.Enviado.Should().Be(EstadoDeConfirmacion.Confirmado);
        lotw.Recibido.Should().Be(EstadoDeConfirmacion.Confirmado);
        lotw.EstaConfirmada.Should().BeTrue();
        lotw.RecibidoUtc.Should().Be(new DateTimeOffset(2023, 3, 11, 0, 0, 0, TimeSpan.Zero));

        var papel = qso.Confirmaciones.Single(c => c.Medio == MedioDeConfirmacion.Papel);
        papel.Recibido.Should().Be(EstadoDeConfirmacion.Ninguno);
        papel.Via.Should().Be(ViaDeEnvio.Electronico);

        // Y el JSON original se devuelve tal cual al exportar.
        var salida = await Ayudas.ExportarAsync(lectura.Qsos);
        salida.Should().Contain(json);
    }

    [Fact]
    public async Task El_json_de_referencias_se_traduce_a_la_tabla_de_referencias()
    {
        const string json =
            "[{\"AC\":\"DXCC\",\"R\":\"281\",\"G\":\"EU\",\"SUB\":[],\"GRA\":[]},"
            + "{\"AC\":\"POTA\",\"R\":\"PT-0361\",\"G\":\"PT-13\",\"S\":2,\"SUB\":[],\"GRA\":[]}]";
        var adif = Cabecera
            + $"<CALL:6>EA8DLF <APP_L4ONG_QSO_AWARD_REFERENCES:{Encoding.UTF8.GetByteCount(json)}>{json} <EOR>\n";

        var lectura = await Ayudas.LeerAsync(adif);
        var qso = lectura.Qsos[0];

        qso.Referencias.Should().ContainSingle(
            "solo POTA es una referencia visitable; DXCC es progreso calculado");
        qso.Referencias[0].Tipo.Should().Be(TipoDeReferencia.Pota);
        qso.Referencias[0].Codigo.Should().Be("PT-0361");
        qso.Referencias[0].Lado.Should().Be(LadoDeReferencia.Corresponsal);

        var salida = await Ayudas.ExportarAsync(lectura.Qsos);
        salida.Should().Contain(json);
    }

    [Fact]
    public async Task Un_json_roto_no_tumba_el_registro()
    {
        const string json = "[{\"CT\":\"QSL\",";
        var adif = Cabecera
            + $"<CALL:6>EA8DLF <APP_L4ONG_QSO_CONFIRMATIONS:{json.Length}>{json} <NAME:4>JOSE <EOR>\n";

        var lectura = await Ayudas.LeerAsync(adif);
        lectura.Qsos.Should().ContainSingle();
        lectura.Qsos[0].Name.Should().Be("JOSE");
        lectura.Avisos.Should().Contain(a => a.Campo == JsonLog4Om.CampoConfirmaciones && !a.EsFatal);
        (await Ayudas.ExportarAsync(lectura.Qsos)).Should().Contain(json);
    }

    // ── Trampa 4: la antena del log de Log4OM es MY_ANTENNA ──────────────────

    [Fact]
    public async Task La_antena_se_guarda_y_se_exporta_como_my_antenna()
    {
        var lectura = await Ayudas.LeerAsync(
            Cabecera + "<CALL:6>EA8DLF <MY_ANTENNA:11>ECOMET HF-7 <EOR>\n");
        lectura.Qsos[0].MyAntenna.Should().Be("ECOMET HF-7");
        (await Ayudas.ExportarAsync(lectura.Qsos)).Should().Contain("<MY_ANTENNA:11>ECOMET HF-7");
    }

    [Fact]
    public async Task Un_campo_antenna_suelto_se_acepta_y_se_normaliza()
    {
        var lectura = await Ayudas.LeerAsync(Cabecera + "<CALL:6>EA8DLF <ANTENNA:6>DIPOLO <EOR>\n");
        lectura.Qsos[0].MyAntenna.Should().Be("DIPOLO");
        (await Ayudas.ExportarAsync(lectura.Qsos)).Should().Contain("<MY_ANTENNA:6>DIPOLO");
    }

    // ── Trampa 5: codificaciones mezcladas ───────────────────────────────────

    [Fact]
    public async Task Un_fichero_con_utf8_y_ansi_mezclados_se_lee_entero()
    {
        var bytes = new List<byte>();
        bytes.AddRange(Encoding.UTF8.GetBytes(Cabecera));
        bytes.AddRange(Encoding.UTF8.GetBytes("<CALL:6>EA8DLF "));
        bytes.AddRange(Encoding.UTF8.GetBytes("<QTH:6>MOGÁN "));          // UTF-8
        bytes.AddRange(Encoding.UTF8.GetBytes("<NAME:6>"));
        bytes.AddRange(Encoding.Latin1.GetBytes("JOSÉ M"));                // ANSI en el mismo fichero
        bytes.AddRange(Encoding.UTF8.GetBytes(" <EOR>\n"));

        var lectura = await Ayudas.LeerAsync(bytes.ToArray());
        lectura.Qsos.Should().ContainSingle();
        lectura.Qsos[0].Qth.Should().Be("MOGÁN");
        lectura.Qsos[0].Name.Should().Be("JOSÉ M");
    }

    [Fact]
    public async Task La_marca_de_orden_de_bytes_no_estorba()
    {
        var bytes = new List<byte> { 0xEF, 0xBB, 0xBF };
        bytes.AddRange(Encoding.UTF8.GetBytes(Cabecera + "<CALL:6>EA8DLF <EOR>\n"));

        var lectura = await Ayudas.LeerAsync(bytes.ToArray());
        lectura.Qsos.Should().ContainSingle();
        lectura.Qsos[0].Call.Valor.Should().Be("EA8DLF");
    }
}
