using FluentAssertions;
using Nodisla.Cuaderno.Dominio.Entidades;

namespace Nodisla.Cuaderno.Adif.Pruebas;

/// <summary>
/// La fusion vista desde donde nace el problema: un fichero ADIF con dos registros que son el
/// mismo contacto.
/// </summary>
/// <remarks>
/// Las reglas de la fusion se prueban a fondo en las pruebas del dominio. Aqui solo esta el
/// caso que justifica que exista: un respaldo de Log4OM trae 43 pares con la misma clave
/// natural en los que cada copia sabe algo distinto, y en 41 de ellos una de las dos trae una
/// confirmacion que la otra no. Quedarse con una sola copia perderia diplomas.
/// </remarks>
public class FusionPruebas
{
    [Fact]
    public async Task Dos_registros_del_mismo_fichero_se_funden_sin_perder_la_confirmacion()
    {
        const string adif =
            "<ADIF_VER:5>3.1.5 <EOH>\n"
            + "<CALL:6>EA8DLF <QSO_DATE:8>20260101 <TIME_ON:6>120000 <BAND:3>20m <MODE:3>SSB "
            + "<LOTW_QSL_RCVD:1>N <RST_SENT:2>59 <SFI:3>124 <EOR>\n"
            + "<CALL:6>EA8DLF <QSO_DATE:8>20260101 <TIME_ON:6>120000 <BAND:3>20m <MODE:3>SSB "
            + "<LOTW_QSL_RCVD:1>Y <LOTW_QSLRDATE:8>20260210 <RST_RCVD:2>57 <SFI:1>0 <EOR>\n";

        var lectura = await Ayudas.LeerAsync(adif);
        lectura.Qsos.Should().HaveCount(2);
        lectura.Qsos[0].ClaveNatural.Should().Be(
            lectura.Qsos[1].ClaveNatural,
            "es el mismo contacto visto dos veces");

        var fusion = FusionDeQso.Fundir(lectura.Qsos[0], lectura.Qsos[1]);
        var fundido = lectura.Qsos[0];

        fundido.Confirmaciones.Single(c => c.Medio == MedioDeConfirmacion.Lotw)
            .EstaConfirmada.Should().BeTrue("saltarse la segunda copia habria perdido el diploma");
        fundido.RstSent.Texto.Should().Be("59");
        fundido.RstRcvd.Texto.Should().Be("57", "cada copia sabia un informe");
        fundido.Sfi.Should().Be(124, "el cero de flujo solar de la otra copia es un hueco");
        fusion.RecuperoConfirmacion.Should().BeTrue();

        // Y lo fundido se exporta entero, que es lo que acaba viendo el operador.
        var salida = await Ayudas.ExportarAsync([fundido]);
        salida.Should().Contain("<LOTW_QSL_RCVD:1>Y").And.Contain("<LOTW_QSLRDATE:8>20260210");
        salida.Should().Contain("<RST_SENT:2>59").And.Contain("<RST_RCVD:2>57");
        salida.Should().Contain("<SFI:3>124");
    }
}
