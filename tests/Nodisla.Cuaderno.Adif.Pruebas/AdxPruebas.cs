using System.Text;
using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;

namespace Nodisla.Cuaderno.Adif.Pruebas;

/// <summary>Lectura y escritura del formato ADX, la variante XML de ADIF.</summary>
public class AdxPruebas
{
    [Fact]
    public async Task Se_lee_un_adx_escrito_a_mano()
    {
        const string adx = """
            <?xml version="1.0" encoding="UTF-8"?>
            <ADX>
              <HEADER>
                <ADIF_VER>3.1.5</ADIF_VER>
                <PROGRAMID>OtroCuaderno</PROGRAMID>
              </HEADER>
              <RECORDS>
                <RECORD>
                  <CALL>EA8DLF</CALL>
                  <QSO_DATE>20260523</QSO_DATE>
                  <TIME_ON>095631</TIME_ON>
                  <BAND>20m</BAND>
                  <MODE>SSB</MODE>
                  <MY_CNTY>ESPAÑA</MY_CNTY>
                  <APP PROGRAMID="L4ONG" FIELDNAME="SUNSPOTS" TYPE="N">67</APP>
                </RECORD>
                <RECORD>
                  <CALL>EA8XYZ</CALL>
                  <BAND>40m</BAND>
                  <MODE>CW</MODE>
                </RECORD>
              </RECORDS>
            </ADX>
            """;

        var lectura = await Ayudas.LeerAsync(Encoding.UTF8.GetBytes(adx));

        lectura.Qsos.Should().HaveCount(2);
        lectura.ProgramaOrigen.Should().Be("OtroCuaderno");
        lectura.Qsos[0].Call.Valor.Should().Be("EA8DLF");
        lectura.Qsos[0].MyCnty.Should().Be("ESPAÑA");
        lectura.Qsos[0].InicioUtc.Should().Be(new DateTimeOffset(2026, 5, 23, 9, 56, 31, TimeSpan.Zero));
        lectura.Qsos[0].CamposExtra.Should().Contain(c => c.Nombre == "APP_L4ONG_SUNSPOTS" && c.Valor == "67");
        lectura.Qsos[1].Mode.Principal.Should().Be("CW");
    }

    [Fact]
    public async Task Se_escribe_un_adx_que_se_vuelve_a_leer_igual()
    {
        const string adi =
            "<ADIF_VER:5>3.1.5 <EOH>\n"
            + "<CALL:6>EA8DLF <QSO_DATE:8>20260523 <TIME_ON:6>095631 <BAND:3>20m <MODE:3>SSB "
            + "<MY_CNTY:7>ESPAÑA <COMMENT:16>Con < y con & ya <APP_L4ONG_SUNSPOTS:2>67 <EOR>\n";

        var original = await Ayudas.LeerAsync(adi);
        var adx = await Ayudas.ExportarBytesAsync(original.Qsos, new OpcionesAdif { Adx = true });

        var texto = Encoding.UTF8.GetString(adx);
        texto.Should().Contain("<ADX>").And.Contain("<RECORDS>");
        texto.Should().Contain("PROGRAMID=\"L4ONG\"");
        texto.Should().Contain("FIELDNAME=\"SUNSPOTS\"");

        var vuelta = await Ayudas.LeerAsync(adx);
        var qso = vuelta.Qsos.Should().ContainSingle().Subject;
        qso.Call.Valor.Should().Be("EA8DLF");
        qso.MyCnty.Should().Be("ESPAÑA");
        qso.Comentario.Should().Be("Con < y con & ya");
        qso.CamposExtra.Should().Contain(c => c.Nombre == "APP_L4ONG_SUNSPOTS" && c.Valor == "67");
    }

    [Fact]
    public async Task Un_adx_sin_registros_da_una_lectura_vacia()
    {
        const string adx = """
            <?xml version="1.0" encoding="UTF-8"?>
            <ADX><HEADER><ADIF_VER>3.1.5</ADIF_VER></HEADER><RECORDS /></ADX>
            """;

        var lectura = await Ayudas.LeerAsync(Encoding.UTF8.GetBytes(adx));
        lectura.Qsos.Should().BeEmpty();
        lectura.Cabecera.Should().ContainKey("ADIF_VER");
    }

    [Fact]
    public async Task Un_campo_vacio_en_adx_no_estorba()
    {
        const string adx = """
            <?xml version="1.0" encoding="UTF-8"?>
            <ADX><RECORDS><RECORD><CALL>EA8DLF</CALL><QTH /><NAME>LUIS</NAME></RECORD></RECORDS></ADX>
            """;

        var lectura = await Ayudas.LeerAsync(Encoding.UTF8.GetBytes(adx));
        lectura.Qsos.Should().ContainSingle();
        lectura.Qsos[0].Name.Should().Be("LUIS");
        lectura.Qsos[0].Qth.Should().BeNull();
    }
}
