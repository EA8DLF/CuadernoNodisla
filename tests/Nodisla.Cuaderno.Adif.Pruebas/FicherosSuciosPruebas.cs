using System.Text;
using FluentAssertions;

namespace Nodisla.Cuaderno.Adif.Pruebas;

/// <summary>
/// Ficheros rotos a proposito. Ninguno puede abortar la importacion: se avisa en espanol y se
/// sigue con el registro siguiente.
/// </summary>
public class FicherosSuciosPruebas
{
    private const string Cabecera = "<ADIF_VER:5>3.1.5 <EOH>\n";

    [Fact]
    public async Task Un_fichero_vacio_no_revienta()
    {
        var lectura = await Ayudas.LeerAsync(string.Empty);
        lectura.Qsos.Should().BeEmpty();
        lectura.Avisos.Should().ContainSingle();
        lectura.Avisos[0].Mensaje.Should().Contain("ningun contacto");
    }

    [Fact]
    public async Task Un_fichero_solo_con_cabecera_no_da_contactos()
    {
        var lectura = await Ayudas.LeerAsync(Cabecera);
        lectura.Qsos.Should().BeEmpty();
        lectura.Cabecera.Should().ContainKey("ADIF_VER");
    }

    [Fact]
    public async Task Un_fichero_sin_cabecera_se_lee_igual()
    {
        var lectura = await Ayudas.LeerAsync(
            "<CALL:6>EA8DLF <QSO_DATE:8>20260101 <TIME_ON:4>1200 <BAND:3>20m <MODE:3>SSB <EOR>\n");

        lectura.Qsos.Should().ContainSingle();
        lectura.Qsos[0].Call.Valor.Should().Be("EA8DLF");
        lectura.Cabecera.Should().BeEmpty();
        lectura.ProgramaOrigen.Should().BeNull();
    }

    [Fact]
    public async Task El_preambulo_de_comentarios_antes_de_la_cabecera_se_ignora()
    {
        const string adif =
            "#++++++++++++++++++++++\n"
            + "#   Un programa cualquiera, version 1.0\n"
            + "#   creado: jueves, 3 de septiembre de 2026\n"
            + "#++++++++++++++++++++++\n\n"
            + "<ADIF_VER:5>3.1.5 <PROGRAMID:7>LOG4OM2 <EOH>\n\n"
            + "<CALL:6>EA8DLF <EOR>\n";

        var lectura = await Ayudas.LeerAsync(adif);
        lectura.Qsos.Should().ContainSingle();
        lectura.ProgramaOrigen.Should().Be("LOG4OM2");
    }

    [Fact]
    public async Task Un_registro_truncado_al_final_se_importa_con_lo_que_trae()
    {
        var lectura = await Ayudas.LeerAsync(
            Cabecera
            + "<CALL:6>EA8DLF <BAND:3>20m <EOR>\n"
            + "<CALL:5>EA8XX <BAND:3>40m <NAME:4>JOSE");

        lectura.Qsos.Should().HaveCount(2);
        lectura.Qsos[1].Name.Should().Be("JOSE");
        lectura.Avisos.Should().Contain(a => a.Mensaje.Contains("sin cerrar"));
    }

    [Fact]
    public async Task Un_campo_sin_cerrar_no_impide_leer_lo_anterior()
    {
        var lectura = await Ayudas.LeerAsync(
            Cabecera
            + "<CALL:6>EA8DLF <BAND:3>20m <EOR>\n"
            + "<CALL:5>EA8XX <NAME");

        lectura.Qsos.Should().HaveCount(2);
        lectura.Qsos[1].Call.Valor.Should().Be("EA8XX");
        lectura.Avisos.Should().Contain(a => a.Mensaje.Contains("etiqueta sin cerrar"));
    }

    [Fact]
    public async Task El_texto_sobrante_tras_la_longitud_declarada_se_descarta_con_aviso()
    {
        // Log4OM escribe el condado con un rotulo de adorno detras pero declara solo los diez
        // bytes del codigo. Manda la longitud, que es lo que dice el estandar.
        var lectura = await Ayudas.LeerAsync(
            Cabecera + "<CALL:6>EA8DLF <CNTY:10>CA,VENTURA // Ventura <NAME:4>JOSE <EOR>\n");

        lectura.Qsos[0].Cnty.Should().Be("CA,VENTURA");
        lectura.Qsos[0].Name.Should().Be("JOSE");
        lectura.Avisos.Should().Contain(a =>
            a.Campo == "CNTY" && !a.EsFatal && a.Mensaje.Contains("// Ventura"));

        // Descartado para el dato, pero no perdido: al exportar sale igual que entro.
        (await Ayudas.ExportarAsync(lectura.Qsos)).Should().Contain("<CNTY:10>CA,VENTURA // Ventura");
    }

    [Fact]
    public async Task Un_registro_sin_ningun_campo_se_descarta_con_aviso()
    {
        var lectura = await Ayudas.LeerAsync(
            Cabecera + "<EOR>\n<CALL:6>EA8DLF <EOR>\n");

        lectura.Qsos.Should().ContainSingle();
        lectura.Avisos.Should().Contain(a => a.EsFatal && a.Mensaje.Contains("vacio"));
    }

    [Fact]
    public async Task Un_valor_que_no_se_entiende_no_tira_el_registro()
    {
        var lectura = await Ayudas.LeerAsync(
            Cabecera
            + "<CALL:6>EA8DLF <BAND:6>chorra <MODE:6>INVENT <FREQ:3>abc <DXCC:2>xx "
            + "<GRIDSQUARE:3>XXX <LAT:4>hola <NAME:4>JOSE <EOR>\n");

        var qso = lectura.Qsos.Should().ContainSingle().Subject;
        qso.Name.Should().Be("JOSE");
        qso.Band.EsVacia.Should().BeTrue();
        qso.Mode.Principal.Should().Be("INVENT", "un modo desconocido se conserva, no se descarta");
        lectura.Avisos.Should().HaveCountGreaterThanOrEqualTo(5);
        lectura.Avisos.Should().NotContain(a => a.EsFatal);
    }

    [Fact]
    public async Task Todo_lo_que_no_se_entiende_vuelve_a_salir_al_exportar()
    {
        const string adif =
            "<ADIF_VER:5>3.1.5 <EOH>\n"
            + "<CALL:6>EA8DLF <BAND:6>chorra <FREQ:3>abc <DXCC:2>xx <GRIDSQUARE:3>XXX "
            + "<LAT:4>hola <RARO:6>lo mio <EOR>\n";

        var lectura = await Ayudas.LeerAsync(adif);
        var salida = await Ayudas.ExportarAsync(lectura.Qsos);

        salida.Should().Contain("<BAND:6>chorra");
        salida.Should().Contain("<FREQ:3>abc");
        salida.Should().Contain("<DXCC:2>xx");
        salida.Should().Contain("<GRIDSQUARE:3>XXX");
        salida.Should().Contain("<LAT:4>hola");
        salida.Should().Contain("<RARO:6>lo mio");
    }

    [Fact]
    public async Task Un_campo_repetido_se_avisa_y_se_queda_el_primero()
    {
        var lectura = await Ayudas.LeerAsync(
            Cabecera + "<CALL:6>EA8DLF <NAME:4>JOSE <NAME:5>MARIA <EOR>\n");

        lectura.Qsos[0].Name.Should().Be("JOSE");
        lectura.Avisos.Should().Contain(a => a.Campo == "NAME" && a.Mensaje.Contains("repetido"));

        // El valor descartado tampoco se pierde: se guarda con un nombre que no colisiona.
        lectura.Qsos[0].CamposExtra.Should().Contain(c =>
            c.Nombre == "APP_NODISLA_REPETIDO_2_NAME" && c.Valor == "MARIA");
        (await Ayudas.ExportarAsync(lectura.Qsos)).Should().Contain("<APP_NODISLA_REPETIDO_2_NAME:5>MARIA");
    }

    [Fact]
    public async Task Las_etiquetas_valen_en_minusculas()
    {
        var lectura = await Ayudas.LeerAsync(
            "<adif_ver:5>3.1.5 <eoh>\n<call:6>EA8DLF <band:3>20m <eor>\n");

        lectura.Qsos.Should().ContainSingle();
        lectura.Qsos[0].Band.Nombre.Should().Be("20m");
    }

    [Fact]
    public async Task Un_adx_mal_formado_avisa_y_no_lanza()
    {
        const string adx =
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n"
            + "<ADX><RECORDS><RECORD><CALL>EA8DLF</CALL></RECORD><RECORD><CALL>EA8XX";

        var lectura = await Ayudas.LeerAsync(Encoding.UTF8.GetBytes(adx));
        lectura.Qsos.Should().ContainSingle();
        lectura.Avisos.Should().Contain(a => a.EsFatal && a.Mensaje.Contains("mal formado"));
    }

    [Fact]
    public async Task Un_fichero_grande_no_se_atraganta_con_un_campo_enorme()
    {
        var largo = new string('X', 300_000);
        var adif = Cabecera
            + $"<CALL:6>EA8DLF <NOTES:{largo.Length}>{largo} <EOR>\n"
            + "<CALL:5>EA8XX <EOR>\n";

        var lectura = await Ayudas.LeerAsync(adif);
        lectura.Qsos.Should().HaveCount(2);
        lectura.Qsos[0].Notas.Should().HaveLength(300_000);
    }
}
