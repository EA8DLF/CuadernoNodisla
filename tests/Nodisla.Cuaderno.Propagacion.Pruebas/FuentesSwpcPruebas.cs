using System.Globalization;
using FluentAssertions;
using Nodisla.Cuaderno.Propagacion.Solar;

namespace Nodisla.Cuaderno.Propagacion.Pruebas;

/// <summary>
/// Lectura de los boletines del SWPC. Todos los textos son copias literales de lo que sirvio el
/// NOAA el 22 de septiembre de 2026, incluidos los huecos y las columnas pegadas.
/// </summary>
public class FuentesSwpcPruebas
{
    private const string BoletinGeofisico = """
        :Product: Geophysical Alert Message wwv.txt
        :Issued: 2026 Sep 22 0615 UTC
        # Prepared by the US Dept. of Commerce, NOAA, Space Weather Prediction Center
        #
        #          Geophysical Alert Message
        #
        Solar-terrestrial indices for 21 September follow.
        Solar flux 104 and estimated planetary A-index 3.
        The estimated planetary K-index at 0600 UTC on 22 September was 0.67.

        No space weather storms were observed for the past 24 hours.

        No space weather storms are predicted for the next 24 hours.
        """;

    private const string BoletinConTormenta = """
        :Product: Geophysical Alert Message wwv.txt
        :Issued: 2026 Sep 22 0615 UTC
        Solar-terrestrial indices for 21 September follow.
        Solar flux 187 and estimated planetary A-index 48.
        The estimated planetary K-index at 0600 UTC on 22 September was 6.33.

        Geomagnetic storms reaching the G2 level were observed for the past 24 hours.
        """;

    private const string IndicesSolaresDiarios = """
        :Product: Daily Solar Data            DSD.txt
        :Issued: 0825 UT 22 Sep 2026
        #
        #                         Sunspot       Stanford GOES15
        #           Radio  SESC     Area          Solar  X-Ray  ------ Flares ------
        #           Flux  Sunspot  10E-6   New     Mean  Bkgd    X-Ray      Optical
        #  Date     10.7cm Number  Hemis. Regions Field  Flux   C  M  X  S  1  2  3
        #---------------------------------------------------------------------------
        2026 09 19   97     11       10      1    -999      *   1  0  0  0  0  0  0
        2026 09 20  101     54       90      3    -999      *   2  0  0  4  1  0  0
        2026 09 21  104     85      140      3    -999      *   2  0  0  1  0  0  0
        """;

    private const string IndicesGeomagneticosDiarios = """
        :Product: Daily Geomagnetic Data          DGD.txt
        :Issued: 1230 UT 22 Sep 2026
        #
        #                Middle Latitude        High Latitude            Estimated
        #              - Fredericksburg -     ---- College ----      --- Planetary ---
        #  Date        A     K-indices        A     K-indices        A     K-indices
        2026 09 20     6  2 3 2 1 2 1 1 0     4  1 3 3 0 0 0 0 0     6   2.33  3.00  2.00  0.67  1.00  0.33  0.33  1.00
        2026 09 21     2  1 1 0 1 2 1 0 0     0  1 0 0 0 0 0 0 0     3   1.67  1.00  0.33  0.67  0.67  0.33  0.00  1.33
        2026 09 22    -1  1 0-1-1-1-1-1-1    -1  1 0-1-1-1-1-1-1     7   2.00  0.67 -1.00 -1.00 -1.00 -1.00 -1.00 -1.00
        """;

    private const string ResumenVientoSolar =
        """[{"proton_speed": 309, "time_tag": "2026-09-22T07:17:00Z"}]""";

    private const string ResumenCampoMagnetico =
        """[{"bt": 4, "bz_gsm": -3.2, "time_tag": "2026-09-22T07:17:00Z"}]""";

    [Fact]
    public void Se_lee_el_boletin_geofisico_entero()
    {
        var lectura = FuentesSwpc.LeerBoletinGeofisico(BoletinGeofisico);

        lectura.Should().NotBeNull();
        lectura!.FlujoSolar.Should().Be(104.0);
        lectura.IndiceA.Should().Be(3.0);
        lectura.IndiceK.Should().Be(0.67);
        lectura.Tormenta.Should().BeFalse();
        lectura.EmitidoUtc.Should().Be(Momento("2026-09-22T06:15:00Z"));
        lectura.FlujoMedidoUtc.Should().Be(Momento("2026-09-21T20:00:00Z"));
        lectura.KMedidoUtc.Should().Be(Momento("2026-09-22T06:00:00Z"));
    }

    [Fact]
    public void Un_boletin_con_tormenta_la_marca()
    {
        var lectura = FuentesSwpc.LeerBoletinGeofisico(BoletinConTormenta);

        lectura!.Tormenta.Should().BeTrue();
        lectura.IndiceK.Should().Be(6.33);
    }

    [Fact]
    public void Un_texto_que_no_es_un_boletin_devuelve_nulo()
    {
        FuentesSwpc.LeerBoletinGeofisico(null).Should().BeNull();
        FuentesSwpc.LeerBoletinGeofisico(string.Empty).Should().BeNull();
        FuentesSwpc.LeerBoletinGeofisico("<html>404 Not Found</html>").Should().BeNull();
    }

    [Fact]
    public void Se_lee_la_ultima_fila_de_los_indices_solares_diarios()
    {
        var lectura = FuentesSwpc.LeerIndicesSolaresDiarios(IndicesSolaresDiarios);

        lectura.Should().NotBeNull();
        lectura!.DiaUtc.Should().Be(Momento("2026-09-21T00:00:00Z"));
        lectura.FlujoSolar.Should().Be(104.0);
        lectura.Manchas.Should().Be(85.0);
    }

    [Fact]
    public void El_indice_a_planetario_se_lee_aunque_las_columnas_de_k_vengan_pegadas()
    {
        // La ultima fila trae "0-1-1-1-1-1-1" de una pieza: partir por espacios desordena las
        // columnas y hay que apoyarse en el bloque de Kp con decimales.
        var lectura = FuentesSwpc.LeerIndiceAPlanetario(IndicesGeomagneticosDiarios);

        lectura.Should().NotBeNull();
        lectura!.Valor.Should().Be(7.0);
    }

    [Fact]
    public void Se_leen_los_resumenes_de_viento_solar_y_campo_magnetico()
    {
        var viento = FuentesSwpc.LeerVientoSolar(ResumenVientoSolar);
        var campo = FuentesSwpc.LeerCampoBz(ResumenCampoMagnetico);

        viento!.Valor.Should().Be(309.0);
        viento.MedidoUtc.Should().Be(Momento("2026-09-22T07:17:00Z"));
        campo!.Valor.Should().Be(-3.2);
    }

    [Fact]
    public void Un_json_roto_no_revienta()
    {
        FuentesSwpc.LeerVientoSolar("{no es json").Should().BeNull();
        FuentesSwpc.LeerVientoSolar("[]").Should().BeNull();
        FuentesSwpc.LeerCampoBz(null).Should().BeNull();
    }

    private static DateTimeOffset Momento(string texto) =>
        DateTimeOffset.Parse(texto, CultureInfo.InvariantCulture);
}
