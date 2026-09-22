using FluentAssertions;
using Nodisla.Cuaderno.Servicios.Adif;
using Nodisla.Cuaderno.Servicios.Pruebas.Dobles;

namespace Nodisla.Cuaderno.Servicios.Pruebas;

/// <summary>Lectura y escritura del ADIF que va y viene de los servicios.</summary>
public class AdifLigeroPruebas
{
    private const string Informe = """
        Fichero de prueba
        <ADIF_VER:5>3.1.0 <PROGRAMID:7>LOG4OM2 <EOH>

        <CALL:5>EA1AB <BAND:3>20m <MODE:4>MFSK <SUBMODE:3>FT4 <QSO_DATE:8>20260501 <TIME_ON:6>121500 <QSL_RCVD:1>Y <EOR>
        <CALL:5>EA2CD <BAND:3>40m <MODE:2>CW <QSO_DATE:8>20260502 <TIME_ON:4>0830 <QSL_RCVD:1>N <EOR>
        """;

    [Fact]
    public void Lee_los_registros_y_deja_fuera_la_cabecera()
    {
        var registros = AdifLigero.LeerRegistros(Informe);

        registros.Should().HaveCount(2);
        registros[0]["CALL"].Should().Be("EA1AB");
        registros[0]["SUBMODE"].Should().Be("FT4");
        registros[1]["MODE"].Should().Be("CW");
        // La cabecera no debe colarse como si fuera un contacto.
        registros.Should().NotContain(r => r.ContainsKey("ADIF_VER"));
    }

    [Fact]
    public void Los_nombres_de_campo_no_distinguen_mayusculas()
    {
        var registros = AdifLigero.LeerRegistros("<eoh>\n<call:5>EA1AB <eor>");

        registros[0]["CALL"].Should().Be("EA1AB");
    }

    [Fact]
    public void Una_longitud_mal_contada_no_estropea_el_resto_del_registro()
    {
        // La longitud declarada es 9 pero el valor tiene 5: pasa con los acentos.
        var registros = AdifLigero.LeerRegistros("<eoh>\n<CALL:9>EA1AB <BAND:3>20m <EOR>");

        registros.Should().HaveCount(1);
        registros[0]["CALL"].Should().Be("EA1AB");
        registros[0]["BAND"].Should().Be("20m");
    }

    [Fact]
    public void Un_fichero_sin_cabecera_tambien_se_lee()
    {
        var registros = AdifLigero.LeerRegistros("<CALL:5>EA1AB <BAND:3>20m <EOR>");

        registros.Should().HaveCount(1);
    }

    [Fact]
    public void Lo_escrito_se_vuelve_a_leer_igual()
    {
        var qso = CuadernoDePrueba.Contacto("EA8DLF", "20m", "MFSK", "2026-05-01T12:15:00Z", submodo: "FT4");
        var registro = ConversorDeQso.Proyectar(qso, ["CALL", "BAND", "MODE", "SUBMODE", "QSO_DATE", "TIME_ON"]);

        var texto = AdifLigero.EscribirRegistros([registro]);
        var vuelta = AdifLigero.LeerRegistros(texto);

        vuelta.Should().HaveCount(1);
        vuelta[0]["CALL"].Should().Be("EA8DLF");
        vuelta[0]["MODE"].Should().Be("MFSK");
        vuelta[0]["SUBMODE"].Should().Be("FT4");
        vuelta[0]["QSO_DATE"].Should().Be("20260501");
        vuelta[0]["TIME_ON"].Should().Be("121500");
    }

    [Fact]
    public void La_proyeccion_solo_deja_pasar_los_campos_pedidos()
    {
        var qso = CuadernoDePrueba.Contacto("EA8DLF", "20m", "CW", "2026-05-01T12:15:00Z");
        qso.Comentario = "un comentario que eQSL no conoce";

        var registro = ConversorDeQso.Proyectar(qso, ["CALL", "BAND", "MODE"]);

        registro.Should().ContainKey("CALL");
        registro.Should().NotContainKey("COMMENT");
        registro.Should().NotContainKey("RST_SENT");
    }

    [Theory]
    [InlineData("20260501", "121500", "2026-05-01T12:15:00Z")]
    [InlineData("20260501", "1215", "2026-05-01T12:15:00Z")]
    [InlineData("20260501", "", "2026-05-01T00:00:00Z")]
    public void La_fecha_y_la_hora_de_adif_se_leen_en_utc(string fecha, string hora, string esperado)
    {
        var instante = LectorDeConfirmaciones.FechaHora(fecha, hora);

        instante.Should().Be(DateTimeOffset.Parse(esperado, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Una_fecha_imposible_no_se_inventa()
    {
        LectorDeConfirmaciones.FechaHora("20261301", "1200").Should().BeNull();
        LectorDeConfirmaciones.FechaHora(null, "1200").Should().BeNull();
    }
}
