using System.Xml.Linq;
using FluentAssertions;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Integraciones.Cluster;

namespace Nodisla.Cuaderno.Integraciones.Pruebas.Cluster;

/// <summary>Importacion de la lista de clusters de Log4OM.</summary>
public class ListaDeClustersLog4OmPruebas
{
    /// <summary>Tres nodos copiados del <c>cluster.xml</c> de verdad.</summary>
    private const string Xml = """
        <?xml version="1.0"?>
        <ArrayOfSingleCluster xmlns:xsd="http://www.w3.org/2001/XMLSchema">
          <SingleCluster>
            <ClusterName>4M5DX</ClusterName>
            <HostAddress>4m5dx.no-ip.org</HostAddress>
            <Port>7300</Port>
            <IsPrimary>false</IsPrimary>
            <Commands>
              <string>&lt;CALLSIGN&gt;</string>
              <string>&lt;PASSWORD&gt;</string>
              <string>SH/DX 30</string>
            </Commands>
            <Callsign />
            <Password />
            <Ssid />
          </SingleCluster>
          <SingleCluster>
            <ClusterName>6L0NJ</ClusterName>
            <HostAddress>dxc.dx.or.kr</HostAddress>
            <Port>23</Port>
            <Callsign>EA8DLF</Callsign>
            <Password>secreta</Password>
            <Ssid>2</Ssid>
          </SingleCluster>
          <SingleCluster>
            <ClusterName>Nodo roto</ClusterName>
            <HostAddress />
            <Port>no es un numero</Port>
          </SingleCluster>
        </ArrayOfSingleCluster>
        """;

    [Fact]
    public void Se_leen_los_nodos_con_su_puerto_y_su_guion()
    {
        var lista = ListaDeClustersLog4Om.Leer(XDocument.Parse(Xml), Indicativo.Parse("EA8DLF"));

        // El nodo sin direccion no cuenta: se salta sin tumbar la importacion.
        lista.Should().HaveCount(2);

        var primero = lista[0];
        primero.Nombre.Should().Be("4M5DX");
        primero.Servidor.Should().Be("4m5dx.no-ip.org");
        primero.Puerto.Should().Be(7300);
        primero.GuionDeArranque.Should().Equal("<CALLSIGN>", "<PASSWORD>", "SH/DX 30");
        primero.Contrasena.Should().BeNull();
        primero.IndicativoDeAcceso.Should().Be("EA8DLF");
    }

    [Fact]
    public void Un_nodo_puede_tener_su_propio_puerto_indicativo_y_sufijo()
    {
        var lista = ListaDeClustersLog4Om.Leer(XDocument.Parse(Xml), Indicativo.Parse("EA1ABC"));
        var coreano = lista[1];

        coreano.Puerto.Should().Be(23);
        coreano.Indicativo.Valor.Should().Be("EA8DLF");
        coreano.Contrasena.Should().Be("secreta");
        coreano.IndicativoDeAcceso.Should().Be("EA8DLF-2");
        // Sin guion propio, se usa el de siempre.
        coreano.GuionDeArranque.Should().BeEquivalentTo(OpcionesCluster.GuionPredeterminado);
    }

    [Fact]
    public void El_guion_predeterminado_es_el_de_log4om()
    {
        OpcionesCluster.GuionPredeterminado.Should().Equal("<CALLSIGN>", "<PASSWORD>", "SH/DX 30");
    }
}
