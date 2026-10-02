using System.Net;
using System.Net.WebSockets;
using System.Text;
using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Servidores.Pruebas;

/// <summary>
/// El servidor TCI con un cliente WebSocket de prueba: saludo, lista blanca, PTT por el vigilante,
/// drive recortado, spots en los dos sentidos, Origin, contraseña y bajada al desconectar.
/// </summary>
public sealed class ServidorTciPruebas
{
    private sealed class ClienteTci : IAsyncDisposable
    {
        private readonly ClientWebSocket _ws = new();
        private readonly List<string> _recibidos = [];
        private Task? _lectura;

        public static async Task<ClienteTci> ConectarAsync(IPEndPoint punto, string? consulta = null, Action<ClientWebSocketOptions>? opciones = null)
        {
            var c = new ClienteTci();
            opciones?.Invoke(c._ws.Options);
            await c._ws.ConnectAsync(new Uri($"ws://127.0.0.1:{punto.Port}/{consulta}"), CancellationToken.None);
            c._lectura = Task.Run(c.LeerAsync);
            return c;
        }

        public WebSocketState Estado => _ws.State;

        public IReadOnlyList<string> Recibidos
        {
            get
            {
                lock (_recibidos) return [.. _recibidos];
            }
        }

        public Task MandarAsync(string texto) =>
            _ws.SendAsync(Encoding.UTF8.GetBytes(texto), WebSocketMessageType.Text, true, CancellationToken.None);

        public Task<bool> EsperarAsync(string mensaje) => Esperar.HastaAsync(() => Recibidos.Contains(mensaje));

        private async Task LeerAsync()
        {
            var bufer = new byte[8192];
            try
            {
                while (_ws.State == WebSocketState.Open)
                {
                    var r = await _ws.ReceiveAsync(bufer, CancellationToken.None);
                    if (r.MessageType == WebSocketMessageType.Close) return;
                    lock (_recibidos) _recibidos.Add(Encoding.UTF8.GetString(bufer, 0, r.Count));
                }
            }
            catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException or InvalidOperationException)
            {
                // Cerrado.
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_ws.State == WebSocketState.Open)
            {
                try
                {
                    await _ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "fin", CancellationToken.None);
                }
                catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException or InvalidOperationException)
                {
                    // Ya cerrado.
                }
            }

            _ws.Dispose();
            if (_lectura is not null) await _lectura;
        }

        public void Abortar() => _ws.Abort();
    }

    [Fact]
    public async Task Saluda_y_atiende_vfo_modulacion_PTT_y_estado()
    {
        await using var banco = new Banco();
        await banco.ArrancarAsync(rigctld: false, tci: true);
        var punto = banco.Servidores.Tci.Punto!;
        punto.Address.Should().Be(IPAddress.Loopback);

        await using var c = await ClienteTci.ConectarAsync(punto);
        (await c.EsperarAsync("ready;")).Should().BeTrue();
        c.Recibidos.Should().Contain("protocol:ExpertSDR3,2.0;").And.Contain("trx_count:1;").And.Contain("dds:0,14074000;").And.Contain("modulation:0,digu;").And.Contain("trx:0,false;");

        await c.MandarAsync("vfo:0,0,7074000;");
        (await c.EsperarAsync("vfo:0,0,7074000;")).Should().BeTrue();
        banco.Equipo.Estado.Frecuencia.Should().Be(Frecuencia.DesdeHercios(7_074_000));

        await c.MandarAsync("modulation:0,lsb;");
        (await c.EsperarAsync("modulation:0,lsb;")).Should().BeTrue();
        await c.MandarAsync("modulation:0,digu;");
        (await c.EsperarAsync("modulation:0,digu;")).Should().BeTrue();

        await c.MandarAsync("trx:0,true;");
        (await c.EsperarAsync("trx:0,true;")).Should().BeTrue();
        banco.Equipo.EnAntena.Should().BeTrue();
        banco.Vigilante.FuenteEnAntena.Should().StartWith("TCI 127.0.0.1:");

        await c.MandarAsync("trx:0,false;");
        (await Esperar.HastaAsync(() => !banco.Equipo.EnAntena)).Should().BeTrue();

        await c.MandarAsync("rx_smeter:0,0;");
        (await c.EsperarAsync("rx_smeter:0,0,-73;")).Should().BeTrue();
    }

    [Fact]
    public async Task Fuera_de_la_lista_blanca_se_rechaza_sin_tocar_la_radio()
    {
        await using var banco = new Banco();
        await banco.ArrancarAsync(rigctld: false, tci: true);
        await using var c = await ClienteTci.ConectarAsync(banco.Servidores.Tci.Punto!);
        (await c.EsperarAsync("ready;")).Should().BeTrue();

        await c.MandarAsync("run_cat_ex:TX1;");
        await c.MandarAsync("tune:0,true;");
        await c.MandarAsync("cw_msg:0,CQ;");
        await c.MandarAsync("start;");
        await c.MandarAsync("trx:0,true,tci;");
        (await Esperar.HastaAsync(() => banco.Radio.Clientes.Single().Rechazadas == 4)).Should().BeTrue();
        await c.MandarAsync("dds:0;");
        (await c.EsperarAsync("dds:0,14074000;")).Should().BeTrue();

        banco.Equipo.Subidas.Should().Be(0, "ni tune, ni CAT, ni CW, ni audio TCI ponen el equipo en el aire");
        banco.Equipo.Ordenes.Should().BeEmpty();
    }

    [Fact]
    public async Task El_PTT_por_TCI_tiene_las_mismas_salvaguardas()
    {
        await using var banco = new Banco(mhz: 14.400m);
        banco.Pestillo = false;
        await banco.ArrancarAsync(rigctld: false, tci: true);
        await using var c = await ClienteTci.ConectarAsync(banco.Servidores.Tci.Punto!);
        (await c.EsperarAsync("ready;")).Should().BeTrue();

        await c.MandarAsync("trx:0,true;");
        (await Esperar.HastaAsync(() => banco.Radio.Peticiones.Count == 1)).Should().BeTrue();
        banco.Pestillo = true;
        await c.MandarAsync("trx:0,true;");
        (await Esperar.HastaAsync(() => banco.Radio.Peticiones.Count == 2)).Should().BeTrue();
        banco.Equipo.Subidas.Should().Be(0, "pestillo cerrado y luego fuera de banda");
        banco.Radio.Peticiones.Should().OnlyContain(p => !p.Concedida);
    }

    [Fact]
    public async Task Si_se_desconecta_con_el_PTT_arriba_se_baja()
    {
        await using var banco = new Banco();
        await banco.ArrancarAsync(rigctld: false, tci: true);
        var c = await ClienteTci.ConectarAsync(banco.Servidores.Tci.Punto!);
        (await c.EsperarAsync("ready;")).Should().BeTrue();
        await c.MandarAsync("trx:0,true;");
        (await c.EsperarAsync("trx:0,true;")).Should().BeTrue();
        banco.Equipo.EnAntena.Should().BeTrue();

        c.Abortar();
        (await Esperar.HastaAsync(() => !banco.Equipo.EnAntena)).Should().BeTrue();
        await c.DisposeAsync();
    }

    [Fact]
    public async Task El_drive_se_recorta_a_la_potencia_maxima_de_la_banda()
    {
        await using var banco = new Banco(potenciaMaximaDeBanda: 10);
        await banco.ArrancarAsync(rigctld: false, tci: true);
        await using var c = await ClienteTci.ConectarAsync(banco.Servidores.Tci.Punto!);
        (await c.EsperarAsync("ready;")).Should().BeTrue();

        await c.MandarAsync("drive:0,100;");
        (await c.EsperarAsync("drive:0,10;")).Should().BeTrue();
        banco.Equipo.Potencia.Should().Be(10);
    }

    [Fact]
    public async Task Spots_de_los_clientes_al_bandmap_y_del_Cuaderno_a_los_clientes()
    {
        var fuente = new FuenteDeSpotsDePrueba();
        await using var banco = new Banco();
        await using var servidores = new ServidoresParaOtrosProgramas(banco.Radio, fuente);
        await servidores.AplicarAsync(new OpcionesDeServidores { TciActivo = true, PuertoTci = 0 });
        var recibidos = new List<Spot>();
        servidores.SpotDeCliente += (_, s) => recibidos.Add(s);

        await using var c = await ClienteTci.ConectarAsync(servidores.Tci.Punto!);
        (await c.EsperarAsync("ready;")).Should().BeTrue();

        await c.MandarAsync("spot:EA8DLF,cw,7012000,4294967295,cq test;");
        (await Esperar.HastaAsync(() => recibidos.Count == 1)).Should().BeTrue();
        recibidos[0].Indicativo.Valor.Should().Be("EA8DLF");
        recibidos[0].Frecuencia.Hercios.Should().Be(7_012_000);
        recibidos[0].Fuente.Should().StartWith("TCI");

        fuente.Lanzar(new Spot(Indicativo.Crudo("EA8XYZ"), Frecuencia.DesdeHercios(14_025_000), Indicativo.Crudo("EA8DLF"), "tu 5nn", DateTimeOffset.UtcNow, "Cluster") { ModoAnunciado = Modo.Parse("CW") });
        (await c.EsperarAsync("spot:EA8XYZ,cw,14025000,4294967295,tu 5nn;")).Should().BeTrue();

        // Los que vienen de un cliente TCI no se devuelven (no hay eco).
        fuente.Lanzar(recibidos[0]);
        await c.MandarAsync("dds:0;");
        (await c.EsperarAsync("dds:0,14074000;")).Should().BeTrue();
        c.Recibidos.Should().NotContain(m => m.StartsWith("spot:EA8DLF", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Un_navegador_con_Origin_no_entra()
    {
        await using var banco = new Banco();
        await banco.ArrancarAsync(rigctld: false, tci: true);
        var conectar = () => ClienteTci.ConectarAsync(banco.Servidores.Tci.Punto!, opciones: o => o.SetRequestHeader("Origin", "http://pagina-cualquiera.example"));
        await conectar.Should().ThrowAsync<WebSocketException>();
        banco.Radio.Clientes.Should().BeEmpty();
    }

    [Fact]
    public async Task Con_contrasena_hay_que_darla()
    {
        await using var banco = new Banco();
        await banco.ArrancarAsync(rigctld: false, tci: true, token: "secreto-de-prueba");
        var punto = banco.Servidores.Tci.Punto!;

        await ((Func<Task>)(() => ClienteTci.ConectarAsync(punto))).Should().ThrowAsync<WebSocketException>();
        await ((Func<Task>)(() => ClienteTci.ConectarAsync(punto, "?token=mala"))).Should().ThrowAsync<WebSocketException>();

        await using var bueno = await ClienteTci.ConectarAsync(punto, "?token=secreto-de-prueba");
        (await bueno.EsperarAsync("ready;")).Should().BeTrue();

        await using var cabecera = await ClienteTci.ConectarAsync(punto, opciones: o => o.SetRequestHeader("Authorization", "Bearer secreto-de-prueba"));
        (await cabecera.EsperarAsync("ready;")).Should().BeTrue();
    }

    private sealed class FuenteDeSpotsDePrueba : IFuenteSpots
    {
        public string Nombre => "Prueba";

        public EstadoDeConexion Estado => EstadoDeConexion.Conectado;

        public event EventHandler<EstadoDeConexion>? EstadoCambiado;

        public event EventHandler<Spot>? SpotRecibido;

        public event EventHandler<string>? LineaRecibida;

        public void Lanzar(Spot spot) => SpotRecibido?.Invoke(this, spot);

        public Task ConectarAsync(CancellationToken ct = default)
        {
            EstadoCambiado?.Invoke(this, Estado);
            return Task.CompletedTask;
        }

        public Task DesconectarAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task EnviarAsync(string orden, CancellationToken ct = default)
        {
            LineaRecibida?.Invoke(this, orden);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
