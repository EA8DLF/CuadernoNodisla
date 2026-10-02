using System.Net;
using System.Net.Sockets;
using System.Text;
using FluentAssertions;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Servidores.Pruebas;

/// <summary>
/// El servidor compatible con rigctld, con un cliente que hace lo mismo que WSJT-X con la radio
/// «Hamlib NET rigctl»: <c>\chk_vfo</c>, <c>\dump_state</c>, frecuencia, modo y PTT. Todo en
/// 127.0.0.1 y con el equipo de prueba: nada sale al aire.
/// </summary>
public sealed class ServidorRigctldPruebas
{
    /// <summary>Un cliente rigctld minimo, linea a linea.</summary>
    private sealed class ClienteRigctld : IAsyncDisposable
    {
        private readonly TcpClient _tcp = new();
        private StreamReader _lector = null!;
        private NetworkStream _flujo = null!;

        public static async Task<ClienteRigctld> ConectarAsync(IPEndPoint punto)
        {
            var c = new ClienteRigctld();
            await c._tcp.ConnectAsync(IPAddress.Loopback, punto.Port);
            c._flujo = c._tcp.GetStream();
            c._lector = new StreamReader(c._flujo, Encoding.ASCII);
            return c;
        }

        public async Task<string[]> PedirAsync(string orden, int lineas)
        {
            await _flujo.WriteAsync(Encoding.ASCII.GetBytes(orden + "\n"));
            var salida = new string[lineas];
            for (var i = 0; i < lineas; i++)
            {
                salida[i] = await LeerAsync() ?? throw new IOException("Conexion cerrada.");
            }

            return salida;
        }

        public async Task<List<string>> PedirHastaAsync(string orden, string ultima)
        {
            await _flujo.WriteAsync(Encoding.ASCII.GetBytes(orden + "\n"));
            var salida = new List<string>();
            while (await LeerAsync() is { } linea)
            {
                salida.Add(linea);
                if (linea == ultima) break;
            }

            return salida;
        }

        public async Task EscribirAsync(string texto) => await _flujo.WriteAsync(Encoding.ASCII.GetBytes(texto));

        public async Task<string?> LeerAsync()
        {
            using var tope = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            return await _lector.ReadLineAsync(tope.Token);
        }

        public ValueTask DisposeAsync()
        {
            _tcp.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task Hace_lo_que_WSJTX_dump_state_frecuencia_modo_y_PTT()
    {
        await using var banco = new Banco();
        await banco.ArrancarAsync();
        var punto = banco.Servidores.Rigctld.Punto!;
        punto.Address.Should().Be(IPAddress.Loopback, "de fabrica solo se escucha en 127.0.0.1");

        await using var wsjtx = await ClienteRigctld.ConectarAsync(punto);

        (await wsjtx.PedirAsync("\\chk_vfo", 1)).Should().Equal("0");
        var estado = await wsjtx.PedirHastaAsync("\\dump_state", "done");
        estado[0].Should().Be("1", "version 1 del protocolo");
        estado[1].Should().Be("2", "modelo NET rigctl");
        estado.Should().Contain("ptt_type=0x1");
        estado.Should().Contain("0 0 0 0 0 0 0");
        estado.Last().Should().Be("done");

        (await wsjtx.PedirAsync("f", 1)).Should().Equal("14074000");
        (await wsjtx.PedirAsync("m", 2)).Should().Equal("PKTUSB", "3000");
        (await wsjtx.PedirAsync("v", 1)).Should().Equal("VFOA");
        (await wsjtx.PedirAsync("s", 2)).Should().Equal("0", "VFOA");
        (await wsjtx.PedirAsync("\\get_powerstat", 1)).Should().Equal("1");

        (await wsjtx.PedirAsync("F 7074000", 1)).Should().Equal("RPRT 0");
        banco.Equipo.Estado.Frecuencia.Should().Be(Frecuencia.DesdeHercios(7_074_000));
        (await wsjtx.PedirAsync("\\get_freq", 1)).Should().Equal("7074000");

        // El mismo modo no se vuelve a mandar; uno distinto, si.
        var ordenes = banco.Equipo.Ordenes.Count;
        (await wsjtx.PedirAsync("M PKTUSB 0", 1)).Should().Equal("RPRT 0");
        banco.Equipo.Ordenes.Count.Should().Be(ordenes);
        (await wsjtx.PedirAsync("M LSB 0", 1)).Should().Equal("RPRT 0");
        banco.Equipo.Estado.Modo.Submodo.Should().Be("LSB");
        (await wsjtx.PedirAsync("M PKTUSB -1", 1)).Should().Equal("RPRT 0");

        // Split por el VFO B y su frecuencia.
        (await wsjtx.PedirAsync("S 1 VFOB", 1)).Should().Equal("RPRT 0");
        (await wsjtx.PedirAsync("I 7076000", 1)).Should().Equal("RPRT 0");
        (await wsjtx.PedirAsync("i", 1)).Should().Equal("7076000");
        (await wsjtx.PedirAsync("s", 2)).Should().Equal("1", "VFOB");
        (await wsjtx.PedirAsync("S 0 VFOA", 1)).Should().Equal("RPRT 0");

        // Potencia: fraccion de la maxima del equipo.
        (await wsjtx.PedirAsync("L RFPOWER 0.5", 1)).Should().Equal("RPRT 0");
        banco.Equipo.Potencia.Should().Be(50);
        (await wsjtx.PedirAsync("l RFPOWER", 1)).Should().Equal("0.500000");

        // PTT: sube por el vigilante con el nombre del cliente y baja.
        (await wsjtx.PedirAsync("t", 1)).Should().Equal("0");
        (await wsjtx.PedirAsync("T 1", 1)).Should().Equal("RPRT 0");
        banco.Equipo.EnAntena.Should().BeTrue();
        banco.Vigilante.FuenteEnAntena.Should().StartWith("rigctld 127.0.0.1:");
        (await wsjtx.PedirAsync("t", 1)).Should().Equal("1");
        banco.Vigilante.FuentesPulsadas().Should().Contain(f => f.Length > 0, "el cliente cuenta como fuente pulsada");

        // Con el equipo en el aire no se mueve la frecuencia.
        (await wsjtx.PedirAsync("F 7080000", 1)).Should().Equal("RPRT -9");

        (await wsjtx.PedirAsync("T 0", 1)).Should().Equal("RPRT 0");
        banco.Equipo.EnAntena.Should().BeFalse();
        banco.Vigilante.FuentesPulsadas().Should().BeEmpty();

        banco.Radio.Peticiones.Should().Contain(p => p.Concedida && p.Cliente.StartsWith("rigctld"));
    }

    [Fact]
    public async Task Respuesta_extendida_y_varias_ordenes_en_una_linea()
    {
        await using var banco = new Banco();
        await banco.ArrancarAsync();
        await using var cliente = await ClienteRigctld.ConectarAsync(banco.Servidores.Rigctld.Punto!);

        (await cliente.PedirAsync("+f", 3)).Should().Equal("get_freq:", "Frequency: 14074000", "RPRT 0");
        (await cliente.PedirAsync("+\\set_freq 14075000", 2)).Should().Equal("set_freq: 14075000", "RPRT 0");
        (await cliente.PedirAsync("f v", 2)).Should().Equal("14075000", "VFOA");
    }

    [Fact]
    public async Task El_PTT_se_rechaza_con_el_pestillo_cerrado_con_la_TX_externa_apagada_y_fuera_de_banda()
    {
        await using var banco = new Banco();

        // TX externa apagada (de fabrica).
        await banco.ArrancarAsync(tx: false);
        await using (var c = await ClienteRigctld.ConectarAsync(banco.Servidores.Rigctld.Punto!))
        {
            (await c.PedirAsync("T 1", 1)).Should().Equal("RPRT -9");
            banco.Equipo.Subidas.Should().Be(0);
        }

        // Pestillo «Permitir transmitir en esta sesion» cerrado.
        await banco.ArrancarAsync(tx: true);
        banco.Pestillo = false;
        await using (var c = await ClienteRigctld.ConectarAsync(banco.Servidores.Rigctld.Punto!))
        {
            (await c.PedirAsync("T 1", 1)).Should().Equal("RPRT -9");
            banco.Equipo.Subidas.Should().Be(0);

            // Fuera de banda: 14,400 MHz no es de aficionado.
            banco.Pestillo = true;
            (await c.PedirAsync("F 14400000", 1)).Should().Equal("RPRT 0");
            (await c.PedirAsync("T 1", 1)).Should().Equal("RPRT -9");
            banco.Equipo.Subidas.Should().Be(0, "el vigilante no sube el PTT fuera del plan de banda");
            (await c.PedirAsync("T 0", 1)).Should().Equal("RPRT 0");
        }

        banco.Radio.Peticiones.Should().HaveCount(3).And.OnlyContain(p => !p.Concedida);
    }

    [Fact]
    public async Task Si_el_cliente_se_desconecta_con_el_PTT_arriba_se_baja()
    {
        await using var banco = new Banco();
        await banco.ArrancarAsync();
        var c = await ClienteRigctld.ConectarAsync(banco.Servidores.Rigctld.Punto!);
        (await c.PedirAsync("T 1", 1)).Should().Equal("RPRT 0");
        banco.Equipo.EnAntena.Should().BeTrue();

        await c.DisposeAsync();
        (await Esperar.HastaAsync(() => !banco.Equipo.EnAntena, 2000)).Should().BeTrue("al irse el cliente se baja el PTT en el acto");
        (await Esperar.HastaAsync(() => banco.Radio.Clientes.Count == 0)).Should().BeTrue();
        banco.Vigilante.EnAntena.Should().BeFalse();
    }

    [Fact]
    public async Task Si_el_cliente_se_calla_con_el_PTT_arriba_se_baja()
    {
        await using var banco = new Banco();
        banco.Radio.VentanaDeSilencio = TimeSpan.FromMilliseconds(400);
        await banco.ArrancarAsync();
        await using var c = await ClienteRigctld.ConectarAsync(banco.Servidores.Rigctld.Punto!);
        (await c.PedirAsync("T 1", 1)).Should().Equal("RPRT 0");
        (await Esperar.HastaAsync(() => !banco.Equipo.EnAntena, 3000)).Should().BeTrue();
        banco.Radio.Peticiones.First().Concedida.Should().BeFalse("la bajada queda anotada");
    }

    [Fact]
    public async Task Cerrar_el_pestillo_o_quitar_la_TX_externa_baja_el_PTT_del_cliente()
    {
        await using var banco = new Banco();
        await banco.ArrancarAsync();
        await using var c = await ClienteRigctld.ConectarAsync(banco.Servidores.Rigctld.Punto!);
        (await c.PedirAsync("T 1", 1)).Should().Equal("RPRT 0");
        banco.Pestillo = false;
        (await Esperar.HastaAsync(() => !banco.Equipo.EnAntena)).Should().BeTrue();

        banco.Pestillo = true;
        (await c.PedirAsync("T 1", 1)).Should().Equal("RPRT 0");
        await banco.ArrancarAsync(tx: false);
        (await Esperar.HastaAsync(() => !banco.Equipo.EnAntena)).Should().BeTrue();
    }

    [Fact]
    public async Task Otra_fuente_en_el_aire_es_la_duena_y_el_cliente_no_la_suelta()
    {
        await using var banco = new Banco();
        await banco.ArrancarAsync();
        await using var propio = await banco.Vigilante.PedirAntenaAsync("MOX");
        await using var c = await ClienteRigctld.ConectarAsync(banco.Servidores.Rigctld.Punto!);

        (await c.PedirAsync("T 1", 1)).Should().Equal("RPRT -9");
        (await c.PedirAsync("T 0", 1)).Should().Equal("RPRT 0");
        banco.Equipo.EnAntena.Should().BeTrue("un cliente no suelta el PTT de otra fuente");
        banco.Vigilante.FuenteEnAntena.Should().Be("MOX");
    }

    [Fact]
    public async Task Ordenes_fuera_de_la_lista_se_rechazan_y_nada_de_CAT_en_crudo()
    {
        await using var banco = new Banco();
        await banco.ArrancarAsync();
        await using var c = await ClienteRigctld.ConectarAsync(banco.Servidores.Rigctld.Punto!);

        (await c.PedirAsync("w FA00014074000;", 1)).Should().Equal("RPRT -19");
        (await c.PedirAsync("\\send_cmd TX1;", 1)).Should().Equal("RPRT -19");
        (await c.PedirAsync("b CQ CQ", 1)).Should().Equal("RPRT -19");
        (await c.PedirAsync("\\set_powerstat 0", 1)).Should().Equal("RPRT -19");
        (await c.PedirAsync("\\inventada", 1)).Should().Equal("RPRT -4");
        (await c.PedirAsync("f", 1)).Should().Equal(new[] { "14074000" }, "la conexion sigue viva");

        banco.Equipo.Ordenes.Should().NotContain(o => o.StartsWith("CRUDO"));
        banco.Equipo.Subidas.Should().Be(0);
        banco.Radio.Clientes.Single().Rechazadas.Should().Be(5);
    }

    [Fact]
    public async Task Una_peticion_HTTP_de_un_navegador_se_corta_sin_hacer_nada()
    {
        await using var banco = new Banco();
        await banco.ArrancarAsync();
        await using var c = await ClienteRigctld.ConectarAsync(banco.Servidores.Rigctld.Punto!);
        await c.EscribirAsync("POST / HTTP/1.1\r\nHost: 127.0.0.1:4532\r\nContent-Type: text/plain\r\n\r\nT 1\nF 7000000\n");
        (await c.LeerAsync()).Should().BeNull("se corta la conexion");
        banco.Equipo.Subidas.Should().Be(0);
        banco.Equipo.Ordenes.Should().BeEmpty();
    }

    [Fact]
    public async Task Varias_conexiones_a_la_vez_y_desconectar_una_desde_la_pantalla()
    {
        await using var banco = new Banco();
        await banco.ArrancarAsync();
        var punto = banco.Servidores.Rigctld.Punto!;
        await using var a = await ClienteRigctld.ConectarAsync(punto);
        await using var b = await ClienteRigctld.ConectarAsync(punto);
        (await a.PedirAsync("f", 1)).Should().Equal("14074000");
        (await b.PedirAsync("f", 1)).Should().Equal("14074000");
        banco.Radio.Clientes.Should().HaveCount(2);

        (await a.PedirAsync("T 1", 1)).Should().Equal("RPRT 0");
        (await b.PedirAsync("T 1", 1)).Should().Equal(new[] { "RPRT -9" }, "el PTT ya es de otro cliente");

        var id = banco.Radio.Clientes.Single(x => x.Transmitiendo).Id;
        (await banco.Radio.DesconectarAsync(id)).Should().BeTrue();
        banco.Equipo.EnAntena.Should().BeFalse("primero se baja el PTT");
        (await a.LeerAsync()).Should().BeNull();
        (await b.PedirAsync("f", 1)).Should().Equal("14074000");
        (await Esperar.HastaAsync(() => banco.Radio.Clientes.Count == 1)).Should().BeTrue();
    }
}
