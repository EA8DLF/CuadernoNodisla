using System.IO;
using System.Text.Json;
using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Radio.Ptt;
using Nodisla.Cuaderno.Ui.Ajustes;

namespace Nodisla.Cuaderno.Ui.Pruebas;

/// <summary>
/// Los ajustes que el operador deja guardados: que se guarden, que se lean y —sobre todo— que
/// <b>la contrasena del cluster no acabe en el fichero</b>.
/// </summary>
public sealed class AjustesDelProgramaPruebas : IDisposable
{
    private readonly string _carpeta = Path.Combine(
        Path.GetTempPath(),
        "cuaderno-pruebas-" + Guid.NewGuid().ToString("N"));

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
    public void Sin_fichero_salen_los_valores_de_fabrica()
    {
        var ajustes = AjustesDelPrograma.Leer(_carpeta);

        ajustes.Equipo.Via.Should().Be(ViaDeControl.Ninguna);
        ajustes.Equipo.Baudios.Should().Be(38400, "es lo que trae el FT-710 de fábrica en su menú CAT RATE");
        ajustes.Equipo.DetectarElPuerto.Should().BeTrue();
        ajustes.Equipo.PttPor.Should().Be(ViaDePtt.Cat);
        var nodo = ajustes.Cluster.Nodos.Should().ContainSingle().Subject;
        nodo.Servidor.Should().Be("cluster.ea4rch.es");
        nodo.Puerto.Should().Be(7300);
        nodo.Id.Should().Be(ClavesDeCredencial.NodoDeClusterPrincipal);
        ajustes.Cluster.Indicativo.Should().BeNull("el indicativo sale del perfil de estación activo");
        ajustes.Cluster.ToleranciaDeRepetidosKhz.Should().Be(1.0m);
    }

    [Fact]
    public void Lo_guardado_es_lo_que_se_lee()
    {
        var ajustes = new AjustesDelPrograma
        {
            Equipo = new AjustesDeEquipo
            {
                Via = ViaDeControl.CatNativo,
                DetectarElPuerto = false,
                Puerto = "COM15",
                Baudios = 38400,
                SondeoMs = 250,
                EsperaDeOrdenMs = 400,
                PttPor = ViaDePtt.Rts,
                TiempoMaximoSegundos = 90,
            },
            Cluster = new AjustesDeCluster
            {
                Nombre = "Mi nodo",
                Servidor = "dxfun.com",
                Puerto = 8000,
                Indicativo = "EA8DLF",
                Sufijo = "1",
                GuionDeArranque = ["<CALLSIGN>", "SH/DX 50"],
            },
        };

        ajustes.Guardar(_carpeta);
        var leidos = AjustesDelPrograma.Leer(_carpeta);

        leidos.Equipo.Via.Should().Be(ViaDeControl.CatNativo);
        leidos.Equipo.Puerto.Should().Be("COM15");
        leidos.Equipo.Baudios.Should().Be(38400);
        leidos.Equipo.SondeoMs.Should().Be(250);
        leidos.Equipo.PttPor.Should().Be(ViaDePtt.Rts);
        leidos.Equipo.TiempoMaximoSegundos.Should().Be(90);
        var nodo = leidos.Cluster.Nodos.Should().ContainSingle().Subject;
        nodo.Nombre.Should().Be("Mi nodo");
        nodo.Servidor.Should().Be("dxfun.com");
        nodo.Puerto.Should().Be(8000);
        nodo.GuionDeArranque.Should().Equal("<CALLSIGN>", "SH/DX 50");
        leidos.Cluster.Sufijo.Should().Be("1");
    }

    [Fact]
    public void El_fichero_se_puede_leer_con_el_bloc_de_notas()
    {
        new AjustesDelPrograma().Guardar(_carpeta);

        var texto = File.ReadAllText(Path.Combine(_carpeta, AjustesDelPrograma.NombreDelFichero));

        // Las enumeraciones se escriben con su nombre, no con su numero: un ajuste que pone
        // «Via: 2» no lo entiende nadie, y estos ficheros se leen a mano cuando algo no conecta.
        texto.Should().Contain("\"Via\": \"Ninguna\"");
        texto.Should().Contain("\"PttPor\": \"Cat\"");
    }

    [Fact]
    public void La_contrasena_del_cluster_no_cabe_en_los_ajustes()
    {
        // Esta prueba no comprueba un comportamiento: comprueba que NO EXISTE un sitio donde
        // escribir la contrasena en claro. Si alguien añade la propiedad, esto se cae.
        var propiedades = typeof(AjustesDeCluster).GetProperties().Select(p => p.Name).ToList();

        propiedades.Should().NotContain(nombre =>
            nombre.Contains("Contrasen", StringComparison.OrdinalIgnoreCase)
            || nombre.Contains("Password", StringComparison.OrdinalIgnoreCase)
            || nombre.Contains("Secreto", StringComparison.OrdinalIgnoreCase));

        // Y en cada nodo, lo unico con ese nombre es la CLAVE del almacen, que no se escribe.
        var delNodo = typeof(AjustesDeNodoDeCluster).GetProperties()
            .Where(p => !p.IsDefined(typeof(System.Text.Json.Serialization.JsonIgnoreAttribute), false))
            .Select(p => p.Name)
            .ToList();
        delNodo.Should().NotContain(nombre =>
            nombre.Contains("Contrasen", StringComparison.OrdinalIgnoreCase)
            || nombre.Contains("Password", StringComparison.OrdinalIgnoreCase)
            || nombre.Contains("Secreto", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void La_contrasena_no_aparece_en_el_fichero_ni_por_el_guion_de_arranque()
    {
        const string secreto = "loquesea-1234";

        var ajustes = new AjustesDelPrograma();
        ajustes.Cluster.Nodos[0].Servidor = "cluster.ea4rch.es";
        ajustes.Guardar(_carpeta);

        var texto = File.ReadAllText(Path.Combine(_carpeta, AjustesDelPrograma.NombreDelFichero));
        texto.Should().NotContain(secreto);

        // Y en el guion va la MARCA, nunca la contrasena: <PASSWORD> se sustituye al conectar.
        // El serializador escapa los angulos, asi que se busca la marca sin ellos.
        texto.Should().Contain("PASSWORD");

        // La contrasena solo entra al construir las opciones, y ahi viene del almacen cifrado.
        var opciones = ajustes.Cluster.AOpcionesDeNodo(ajustes.Cluster.Nodos[0], Indicativo.Parse("EA8DLF"), secreto);
        opciones.Contrasena.Should().Be(secreto);
        opciones.IndicativoDeAcceso.Should().Be("EA8DLF");
    }

    [Fact]
    public void Un_fichero_a_medias_no_tumba_el_arranque()
    {
        Directory.CreateDirectory(_carpeta);
        File.WriteAllText(
            Path.Combine(_carpeta, AjustesDelPrograma.NombreDelFichero),
            "{ \"Equipo\": null, \"Cluster\": null }");

        var leidos = AjustesDelPrograma.Leer(_carpeta);

        leidos.Equipo.Should().NotBeNull();
        leidos.Cluster.Should().NotBeNull();
        leidos.Equipo.Via.Should().Be(ViaDeControl.Ninguna);
    }

    [Fact]
    public void Un_fichero_ilegible_devuelve_lo_de_fabrica_en_vez_de_reventar()
    {
        Directory.CreateDirectory(_carpeta);
        File.WriteAllText(Path.Combine(_carpeta, AjustesDelPrograma.NombreDelFichero), "esto no es json");

        var leidos = AjustesDelPrograma.Leer(_carpeta);

        leidos.Equipo.Via.Should().Be(ViaDeControl.Ninguna);
    }

    [Fact]
    public void Los_ajustes_del_equipo_se_pasan_enteros_al_modulo_de_radio()
    {
        var equipo = new AjustesDeEquipo
        {
            Via = ViaDeControl.CatNativo,
            Puerto = "COM3",
            Baudios = 38400,
            SondeoMs = 300,
            EsperaDeOrdenMs = 450,
            PttPor = ViaDePtt.Dtr,
            MaquinaDeRigctld = "192.168.1.10",
            PuertoDeRigctld = 4533,
            EquipoDeOmniRig = 2,
            TiempoMaximoSegundos = 120,
            TiempoSinLatidoSegundos = 20,
        };

        var opciones = equipo.AOpcionesDeRadio();

        opciones.Via.Should().Be(ViaDeControl.CatNativo);
        opciones.Ft710.Puerto.Should().Be("COM3", "el puerto guardado es la pista por donde se prueba primero");
        opciones.Ft710.Baudios.Should().Be(38400);
        opciones.Ft710.IntervaloDeSondeo.Should().Be(TimeSpan.FromMilliseconds(300));
        opciones.Ft710.EsperaDeOrden.Should().Be(TimeSpan.FromMilliseconds(450));
        opciones.Ft710.ViaDePtt.Should().Be(ViaDePtt.Dtr);
        opciones.Rigctld.Maquina.Should().Be("192.168.1.10");
        opciones.Rigctld.Puerto.Should().Be(4533);
        opciones.OmniRig.NumeroDeEquipo.Should().Be(2);
        opciones.Vigilante.TiempoMaximo.Should().Be(TimeSpan.FromMinutes(2));
        opciones.Vigilante.TiempoSinLatido.Should().Be(TimeSpan.FromSeconds(20));
    }

    [Fact]
    public void Un_tiempo_maximo_disparatado_se_recorta_al_tope_del_vigilante()
    {
        // El vigilante revienta si se le pide mas del tope duro, y un ajuste no puede tumbar el
        // programa: se recorta.
        var opciones = new AjustesDeEquipo { TiempoMaximoSegundos = 99_999 }.AOpcionesDeRadio();

        opciones.Vigilante.TiempoMaximo.Should().Be(OpcionesDelVigilante.TiempoMaximoPermitido);
    }

    [Fact]
    public void Un_numero_de_equipo_de_OmniRig_que_no_existe_se_queda_en_el_primero()
    {
        var opciones = new AjustesDeEquipo { EquipoDeOmniRig = 7 }.AOpcionesDeRadio();

        opciones.OmniRig.NumeroDeEquipo.Should().Be(1);
    }

    [Fact]
    public void El_sufijo_del_indicativo_se_escribe_con_el_guion_una_sola_vez()
    {
        var cluster = new AjustesDeCluster { Sufijo = "-2" };

        var opciones = cluster.AOpcionesDeNodo(cluster.Nodos[0], Indicativo.Parse("EA8DLF"), null);

        opciones.IndicativoDeAcceso.Should().Be("EA8DLF-2");
    }

    [Fact]
    public void Guardar_dos_veces_deja_un_solo_fichero_bueno()
    {
        var ajustes = new AjustesDelPrograma();
        ajustes.Guardar(_carpeta);

        ajustes.Cluster.Nodos[0].Servidor = "dxfun.com";
        ajustes.Guardar(_carpeta);

        var ruta = Path.Combine(_carpeta, AjustesDelPrograma.NombreDelFichero);
        Directory.GetFiles(_carpeta).Should().ContainSingle().Which.Should().Be(ruta);

        using var documento = JsonDocument.Parse(File.ReadAllText(ruta));
        documento.RootElement.GetProperty("Cluster").GetProperty("Nodos")[0].GetProperty("Servidor").GetString()
            .Should().Be("dxfun.com");
    }
}
