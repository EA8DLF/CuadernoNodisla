using System.IO;
using System.Text.Json;
using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Ui.Ajustes;

namespace Nodisla.Cuaderno.Ui.Pruebas;

/// <summary>
/// El paso de un solo nodo de cluster a varios: el nodo que el operador tenia configurado
/// pasa a ser el primero de la lista sin perder nada, tampoco la contrasena guardada.
/// </summary>
public sealed class MigracionDelClusterPruebas : IDisposable
{
    /// <summary>Un <c>ajustes.json</c> tal y como lo escribia la version de un solo nodo.</summary>
    private const string FicheroDeAntes = """
        {
          "Cluster": {
            "Nombre": "Mi nodo de casa",
            "Servidor": "dxc.nc7j.com",
            "Puerto": 7373,
            "Indicativo": "EA8DLF",
            "Sufijo": "3",
            "GuionDeArranque": [ "<CALLSIGN>", "<PASSWORD>", "set/dx/filter", "SH/DX 20" ],
            "ReconectarSolo": false,
            "EsperaDeConexionSegundos": 11,
            "PrimerReintentoSegundos": 7,
            "ReintentoMaximoSegundos": 120,
            "SilencioMaximoMinutos": 9
          }
        }
        """;

    private readonly string _carpeta = Path.Combine(
        Path.GetTempPath(),
        "cuaderno-migracion-" + Guid.NewGuid().ToString("N"));

    public MigracionDelClusterPruebas() => Directory.CreateDirectory(_carpeta);

    private string Ruta => Path.Combine(_carpeta, AjustesDelPrograma.NombreDelFichero);

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_carpeta)) Directory.Delete(_carpeta, recursive: true);
        }
        catch (IOException)
        {
            // Una carpeta temporal que no se deja borrar no invalida la prueba.
        }
    }

    [Fact]
    public void El_nodo_de_antes_pasa_a_ser_el_primero_de_la_lista_sin_perder_nada()
    {
        File.WriteAllText(Ruta, FicheroDeAntes);

        var cluster = AjustesDelPrograma.Leer(_carpeta).Cluster;

        var nodo = cluster.Nodos.Should().ContainSingle().Subject;
        nodo.Id.Should().Be(ClavesDeCredencial.NodoDeClusterPrincipal);
        nodo.Nombre.Should().Be("Mi nodo de casa");
        nodo.Servidor.Should().Be("dxc.nc7j.com");
        nodo.Puerto.Should().Be(7373);
        nodo.Activo.Should().BeTrue();
        nodo.ReconectarSolo.Should().BeFalse();
        nodo.GuionDeArranque.Should().Equal("<CALLSIGN>", "<PASSWORD>", "set/dx/filter", "SH/DX 20");

        // Lo comun se queda donde estaba.
        cluster.Indicativo.Should().Be("EA8DLF");
        cluster.Sufijo.Should().Be("3");
        cluster.EsperaDeConexionSegundos.Should().Be(11);
        cluster.PrimerReintentoSegundos.Should().Be(7);
        cluster.ReintentoMaximoSegundos.Should().Be(120);
        cluster.SilencioMaximoMinutos.Should().Be(9);
    }

    [Fact]
    public void La_contrasena_que_ya_estaba_guardada_sigue_valiendo()
    {
        File.WriteAllText(Ruta, FicheroDeAntes);

        var nodo = AjustesDelPrograma.Leer(_carpeta).Cluster.Nodos[0];

        // Misma clave del almacen que antes: no hay que mover ni volver a teclear nada.
        nodo.ClaveDeContrasena.Should().Be(ClavesDeCredencial.ClusterContrasena);
        new AjustesDeNodoDeCluster { Id = "otro1234" }.ClaveDeContrasena
            .Should().Be(ClavesDeCredencial.ClusterContrasena + ".otro1234", "cada nodo nuevo tiene la suya");
    }

    [Fact]
    public void Las_opciones_del_nodo_migrado_son_las_mismas_que_antes()
    {
        File.WriteAllText(Ruta, FicheroDeAntes);
        var cluster = AjustesDelPrograma.Leer(_carpeta).Cluster;

        var opciones = cluster.AOpcionesDeNodo(cluster.Nodos[0], Indicativo.Parse("EA8DLF"), "secreta");

        opciones.Servidor.Should().Be("dxc.nc7j.com");
        opciones.Puerto.Should().Be(7373);
        opciones.IndicativoDeAcceso.Should().Be("EA8DLF-3");
        opciones.Contrasena.Should().Be("secreta");
        opciones.ReconectarSolo.Should().BeFalse();
        opciones.EsperaDeConexion.Should().Be(TimeSpan.FromSeconds(11));
        opciones.EsperaPrimerReintento.Should().Be(TimeSpan.FromSeconds(7));
        opciones.EsperaMaximaReintento.Should().Be(TimeSpan.FromSeconds(120));
        opciones.SilencioMaximo.Should().Be(TimeSpan.FromMinutes(9));
        opciones.Clave.Should().Be(ClavesDeCredencial.NodoDeClusterPrincipal);
    }

    [Fact]
    public void Al_guardar_se_escribe_la_lista_y_no_los_campos_sueltos_de_antes()
    {
        File.WriteAllText(Ruta, FicheroDeAntes);

        AjustesDelPrograma.Leer(_carpeta).Guardar(_carpeta);

        using var documento = JsonDocument.Parse(File.ReadAllText(Ruta));
        var cluster = documento.RootElement.GetProperty("Cluster");
        cluster.TryGetProperty("Servidor", out _).Should().BeFalse("el servidor va ahora dentro de cada nodo");
        cluster.TryGetProperty("GuionDeArranque", out _).Should().BeFalse();
        cluster.GetProperty("Nodos")[0].GetProperty("Servidor").GetString().Should().Be("dxc.nc7j.com");

        // Y leer lo guardado da lo mismo: la migracion no se repite ni duplica el nodo.
        var otraVez = AjustesDelPrograma.Leer(_carpeta).Cluster;
        otraVez.Nodos.Should().ContainSingle().Which.Servidor.Should().Be("dxc.nc7j.com");
    }

    [Fact]
    public void Si_ya_hay_lista_manda_la_lista_aunque_queden_campos_de_antes()
    {
        File.WriteAllText(Ruta, """
            {
              "Cluster": {
                "Servidor": "viejo.ejemplo",
                "Nodos": [
                  { "Id": "a1", "Nombre": "Uno", "Servidor": "uno.ejemplo", "Puerto": 7300 },
                  { "Id": "b2", "Nombre": "Dos", "Servidor": "dos.ejemplo", "Puerto": 8000, "Activo": false, "EsSkimmer": true }
                ]
              }
            }
            """);

        var cluster = AjustesDelPrograma.Leer(_carpeta).Cluster;

        cluster.Nodos.Select(n => n.Servidor).Should().Equal("uno.ejemplo", "dos.ejemplo");
        cluster.Nodos[1].Activo.Should().BeFalse();
        cluster.Nodos[1].EsSkimmer.Should().BeTrue();
        cluster.Servidor.Should().BeNull("lo de antes se tira cuando ya hay lista");
    }

    [Fact]
    public void Un_nodo_puede_entrar_con_otro_indicativo_y_otro_sufijo()
    {
        var cluster = new AjustesDeCluster { Sufijo = "1" };
        cluster.Nodos.Add(new AjustesDeNodoDeCluster
        {
            Nombre = "RBN",
            Servidor = "telnet.reversebeacon.net",
            Puerto = 7000,
            Indicativo = "EA8URL",
            Sufijo = "2",
            EsSkimmer = true,
        });

        var comun = cluster.AOpcionesDeNodo(cluster.Nodos[0], Indicativo.Parse("EA8DLF"), null);
        var propio = cluster.AOpcionesDeNodo(cluster.Nodos[1], Indicativo.Parse("EA8DLF"), null);

        comun.IndicativoDeAcceso.Should().Be("EA8DLF-1");
        propio.IndicativoDeAcceso.Should().Be("EA8URL-2");
        propio.EsSkimmer.Should().BeTrue();
    }
}
