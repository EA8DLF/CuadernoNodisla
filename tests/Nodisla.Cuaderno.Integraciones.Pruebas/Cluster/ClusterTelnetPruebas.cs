using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Integraciones.Cluster;

namespace Nodisla.Cuaderno.Integraciones.Pruebas.Cluster;

/// <summary>
/// Conexion con un cluster de DX, contra un servidor de mentira que escupe lineas reales.
/// </summary>
public class ClusterTelnetPruebas
{
    /// <summary>Tope de espera de cada prueba, para que un fallo no cuelgue la bateria.</summary>
    private static readonly TimeSpan Paciencia = TimeSpan.FromSeconds(10);

    private const string AnuncioClasico =
        "DX de OH2BH:     14025.0  EA8DLF       Nice signal into EU            1432Z";

    private static OpcionesCluster Opciones(int puerto, string? contrasena = null) => new()
    {
        Nombre = "Mentira",
        Servidor = "127.0.0.1",
        Puerto = puerto,
        Indicativo = Indicativo.Parse("EA8DLF"),
        Contrasena = contrasena,
        GuionDeArranque = ["<CALLSIGN>", "<PASSWORD>", "SH/DX 30"],
        EsperaDelAviso = TimeSpan.FromMilliseconds(300),
        EsperaPrimerReintento = TimeSpan.FromMilliseconds(100),
        EsperaMaximaReintento = TimeSpan.FromMilliseconds(300),
        SilencioMaximo = TimeSpan.FromSeconds(30),
    };

    [Fact]
    public async Task Se_identifica_y_manda_el_guion_de_arranque()
    {
        await using var servidor = new ServidorDeMentira(async (sesion, ct) =>
        {
            await sesion.EscribirAsync("Please enter your call: ", ct);
            await sesion.LeerLineaAsync(ct);
            await sesion.EscribirLineaAsync("Hello Luis, welcome to the cluster", ct);
            await sesion.LeerLineaAsync(ct);
            await sesion.EscribirLineaAsync(AnuncioClasico, ct);
            await Task.Delay(Paciencia, ct);
        });

        await using var cluster = new ClusterTelnet(Opciones(servidor.Puerto));
        var llegado = EsperarSpot(cluster);

        await cluster.ConectarAsync();
        var spot = await llegado.WaitAsync(Paciencia);

        spot.Indicativo.Valor.Should().Be("EA8DLF");
        spot.Frecuencia.Megahercios.Should().Be(14.025m);
        cluster.Estado.Should().Be(EstadoDeConexion.Conectado);
        servidor.Recibido.Should().Contain("EA8DLF");
        servidor.Recibido.Should().Contain("SH/DX 30");
    }

    [Fact]
    public async Task Se_manda_la_contrasenia_cuando_el_nodo_la_pide()
    {
        await using var servidor = new ServidorDeMentira(async (sesion, ct) =>
        {
            await sesion.EscribirAsync("login: ", ct);
            await sesion.LeerLineaAsync(ct);
            await sesion.EscribirAsync("password: ", ct);
            await sesion.LeerLineaAsync(ct);
            await sesion.EscribirLineaAsync(AnuncioClasico, ct);
            await Task.Delay(Paciencia, ct);
        });

        await using var cluster = new ClusterTelnet(Opciones(servidor.Puerto, "secreta"));
        var llegado = EsperarSpot(cluster);

        await cluster.ConectarAsync();
        await llegado.WaitAsync(Paciencia);

        servidor.Recibido.Should().ContainInOrder("EA8DLF", "secreta");
    }

    [Fact]
    public async Task Una_linea_partida_entre_dos_lecturas_no_se_pierde()
    {
        await using var servidor = new ServidorDeMentira(async (sesion, ct) =>
        {
            await sesion.EscribirAsync("login: ", ct);
            await sesion.LeerLineaAsync(ct);
            await sesion.EscribirAsync(AnuncioClasico[..30], ct);
            await Task.Delay(120, ct);
            await sesion.EscribirAsync(AnuncioClasico[30..] + "\r\n", ct);
            await Task.Delay(Paciencia, ct);
        });

        await using var cluster = new ClusterTelnet(Opciones(servidor.Puerto));
        var llegado = EsperarSpot(cluster);

        await cluster.ConectarAsync();
        var spot = await llegado.WaitAsync(Paciencia);

        spot.Indicativo.Valor.Should().Be("EA8DLF");
        spot.Comentario.Should().Be("Nice signal into EU");
    }

    [Fact]
    public async Task Las_secuencias_de_control_telnet_no_salen_como_texto()
    {
        // IAC DO ECHO, IAC WILL SUPPRESS-GO-AHEAD y una subnegociacion entera, metidas
        // antes, dentro y despues de la linea del anuncio.
        byte[] Control(params byte[] bytes) => bytes;

        await using var servidor = new ServidorDeMentira(async (sesion, ct) =>
        {
            await sesion.EscribirCrudoAsync(Control(255, 253, 1, 255, 251, 3), ct);
            await sesion.EscribirAsync("login: ", ct);
            await sesion.LeerLineaAsync(ct);
            await sesion.EscribirAsync(AnuncioClasico[..20], ct);
            await sesion.EscribirCrudoAsync(Control(255, 250, 24, 0, 68, 69, 67, 255, 240), ct);
            await sesion.EscribirAsync(AnuncioClasico[20..] + "\r\n", ct);
            await Task.Delay(Paciencia, ct);
        });

        var lineas = new List<string>();
        await using var cluster = new ClusterTelnet(Opciones(servidor.Puerto));
        cluster.LineaRecibida += (_, l) => { lock (lineas) { lineas.Add(l); } };
        var llegado = EsperarSpot(cluster);

        await cluster.ConectarAsync();
        var spot = await llegado.WaitAsync(Paciencia);

        spot.Comentario.Should().Be("Nice signal into EU");
        lock (lineas)
        {
            lineas.Should().NotContain(l => l.Contains((char)255));
        }
    }

    [Fact]
    public async Task Lo_que_no_es_un_anuncio_sale_por_linea_recibida()
    {
        const string Wwv = "WWV de VE7CC <18Z> :   SFI=142, A=8, K=3, No Storms -> No Storms";

        var lineas = new List<string>();
        var visto = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var servidor = new ServidorDeMentira(async (sesion, ct) =>
        {
            await sesion.EscribirAsync("login: ", ct);
            await sesion.LeerLineaAsync(ct);
            await sesion.EscribirLineaAsync(Wwv, ct);
            await Task.Delay(Paciencia, ct);
        });

        await using var cluster = new ClusterTelnet(Opciones(servidor.Puerto));
        cluster.LineaRecibida += (_, l) =>
        {
            lock (lineas) lineas.Add(l);
            if (l.StartsWith("WWV", StringComparison.Ordinal)) visto.TrySetResult(l);
        };

        await cluster.ConectarAsync();
        var linea = await visto.Task.WaitAsync(Paciencia);

        linea.Should().Be(Wwv);
    }

    [Fact]
    public async Task Una_linea_a_medias_al_cortarse_la_conexion_no_inventa_un_anuncio()
    {
        var anuncios = new List<Spot>();
        var segundo = new TaskCompletionSource<Spot>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var servidor = new ServidorDeMentira(async (sesion, ct) =>
        {
            await sesion.EscribirAsync("login: ", ct);
            await sesion.LeerLineaAsync(ct);

            if (sesion.Numero == 1)
            {
                // Se corta a mitad de anuncio: lo que quedo a medias no vale.
                await sesion.EscribirAsync("DX de OH2BH:     14025.0  EA8D", ct);
                await Task.Delay(50, ct);
                sesion.Cortar();
                return;
            }

            await sesion.EscribirLineaAsync(AnuncioClasico, ct);
            await Task.Delay(Paciencia, ct);
        });

        await using var cluster = new ClusterTelnet(Opciones(servidor.Puerto));
        cluster.SpotRecibido += (_, s) =>
        {
            lock (anuncios) anuncios.Add(s);
            segundo.TrySetResult(s);
        };

        await cluster.ConectarAsync();
        var spot = await segundo.Task.WaitAsync(Paciencia);

        // El unico anuncio es el de la segunda conexion, entero.
        spot.Indicativo.Valor.Should().Be("EA8DLF");
        lock (anuncios) anuncios.Should().ContainSingle();
        servidor.Conexiones.Should().BeGreaterThanOrEqualTo(2);
        cluster.Reconexiones.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task Se_reconecta_cuando_el_nodo_cierra_la_conexion()
    {
        var estados = new List<EstadoDeConexion>();
        var recuperado = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var servidor = new ServidorDeMentira(async (sesion, ct) =>
        {
            await sesion.EscribirAsync("login: ", ct);
            await sesion.LeerLineaAsync(ct);
            if (sesion.Numero == 1)
            {
                sesion.Cortar();
                return;
            }
            await sesion.EscribirLineaAsync(AnuncioClasico, ct);
            await Task.Delay(Paciencia, ct);
        });

        await using var cluster = new ClusterTelnet(Opciones(servidor.Puerto));
        cluster.EstadoCambiado += (_, e) => { lock (estados) estados.Add(e); };
        cluster.SpotRecibido += (_, _) => recuperado.TrySetResult();

        await cluster.ConectarAsync();
        await recuperado.Task.WaitAsync(Paciencia);

        lock (estados)
        {
            estados.Should().Contain(EstadoDeConexion.Reintentando);
        }
        cluster.Estado.Should().Be(EstadoDeConexion.Conectado);
    }

    [Fact]
    public async Task Desconectar_deja_el_cluster_parado()
    {
        await using var servidor = new ServidorDeMentira(async (sesion, ct) =>
        {
            await sesion.EscribirAsync("login: ", ct);
            await sesion.LeerLineaAsync(ct);
            await sesion.EscribirLineaAsync(AnuncioClasico, ct);
            await Task.Delay(Paciencia, ct);
        });

        await using var cluster = new ClusterTelnet(Opciones(servidor.Puerto));
        var llegado = EsperarSpot(cluster);
        await cluster.ConectarAsync();
        await llegado.WaitAsync(Paciencia);

        await cluster.DesconectarAsync();

        cluster.Estado.Should().Be(EstadoDeConexion.Desconectado);
        var enviar = async () => await cluster.EnviarAsync("SH/DX");
        await enviar.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Un_nodo_que_rechaza_el_indicativo_no_se_reintenta()
    {
        await using var servidor = new ServidorDeMentira(async (sesion, ct) =>
        {
            await sesion.EscribirAsync("login: ", ct);
            await sesion.LeerLineaAsync(ct);
            await sesion.EscribirLineaAsync("Invalid callsign, connection closed", ct);
            await Task.Delay(200, ct);
            sesion.Cortar();
        });

        await using var cluster = new ClusterTelnet(Opciones(servidor.Puerto));
        var fallo = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        cluster.EstadoCambiado += (_, e) =>
        {
            if (e == EstadoDeConexion.Fallido) fallo.TrySetResult();
        };

        await cluster.ConectarAsync();
        await fallo.Task.WaitAsync(Paciencia);

        cluster.Estado.Should().Be(EstadoDeConexion.Fallido);
        await Task.Delay(500);
        servidor.Conexiones.Should().Be(1);
    }

    private static Task<Spot> EsperarSpot(ClusterTelnet cluster)
    {
        var espera = new TaskCompletionSource<Spot>(TaskCreationOptions.RunContinuationsAsynchronously);
        cluster.SpotRecibido += (_, s) => espera.TrySetResult(s);
        return espera.Task;
    }
}
