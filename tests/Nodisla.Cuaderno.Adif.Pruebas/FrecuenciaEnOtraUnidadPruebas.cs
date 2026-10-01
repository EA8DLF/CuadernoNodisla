using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;

namespace Nodisla.Cuaderno.Adif.Pruebas;

/// <summary>
/// Log4OM exportó contactos de 70 cm con <c>FREQ:0.433</c> (433 MHz tecleados como kHz).
/// Al importar se corrige cuando la banda lo deja claro, y se avisa.
/// </summary>
public class FrecuenciaEnOtraUnidadPruebas
{
    private const string Cabecera = "<ADIF_VER:5>3.1.5 <PROGRAMID:7>LOG4OM2 <EOH>\n";

    private static string Registro(string banda, string freq) =>
        Cabecera
        + $"<CALL:6>EG1CPI <QSO_DATE:8>20260523 <TIME_ON:6>095631 <BAND:{banda.Length}>{banda} "
        + $"<MODE:2>FM <FREQ:{freq.Length}>{freq} <EOR>\n";

    [Fact]
    public async Task Setenta_centimetros_escritos_en_kilohercios_se_corrigen_con_aviso()
    {
        var lectura = await Ayudas.LeerAsync(Registro("70cm", "0.433"));

        lectura.Qsos[0].Freq.Megahercios.Should().Be(433m);
        lectura.Avisos.Should().Contain(a => a.Campo == "FREQ" && a.Nivel == NivelDeAviso.Advertencia);
    }

    [Fact]
    public async Task Veinte_metros_escritos_en_kilohercios_se_corrigen()
    {
        var lectura = await Ayudas.LeerAsync(Registro("20m", "14186"));

        lectura.Qsos[0].Freq.Megahercios.Should().Be(14.186m);
    }

    [Fact]
    public async Task Una_frecuencia_que_ya_cae_en_su_banda_no_se_toca_ni_se_avisa()
    {
        var lectura = await Ayudas.LeerAsync(Registro("70cm", "433.5"));

        lectura.Qsos[0].Freq.Megahercios.Should().Be(433.5m);
        lectura.Avisos.Should().NotContain(a => a.Campo == "FREQ");
    }

    [Fact]
    public async Task Si_ni_por_mil_cae_en_la_banda_se_deja_como_venia()
    {
        var lectura = await Ayudas.LeerAsync(Registro("70cm", "7.1"));

        lectura.Qsos[0].Freq.Megahercios.Should().Be(7.1m);
    }
}
