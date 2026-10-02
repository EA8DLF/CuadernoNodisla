using System.Collections.Concurrent;
using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.CasosDeUso;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Integraciones.Cluster;

namespace Nodisla.Cuaderno.Integraciones.Pruebas.Cluster;

/// <summary>
/// Varios nodos a la vez, contra varios clusters de mentira en esta misma maquina. Ninguna
/// prueba sale a la red.
/// </summary>
public sealed class FuenteDeVariosNodosPruebas
{
    private static readonly TimeSpan Paciencia = TimeSpan.FromSeconds(10);

    private const string AnuncioDeOh2bh =
        "DX de OH2BH:     14025.0  3Y0J         Nice signal into EU            1432Z";

    private const string AnuncioDeF5xyz =
        "DX de F5XYZ:     14025.3  3Y0J         599 here                       1433Z";

    private static OpcionesCluster Nodo(string id, int puerto, string? contrasena = null, bool skimmer = false) => new()
    {
        Id = id,
        Nombre = id.ToUpperInvariant(),
        Servidor = "127.0.0.1",
        Puerto = puerto,
        Indicativo = Indicativo.Parse("EA8DLF"),
        Contrasena = contrasena,
        EsSkimmer = skimmer,
        GuionDeArranque = ["<CALLSIGN>", "<PASSWORD>", "SH/DX 30"],
        EsperaDelAviso = TimeSpan.FromMilliseconds(300),
        EsperaPrimerReintento = TimeSpan.FromMilliseconds(100),
        EsperaMaximaReintento = TimeSpan.FromMilliseconds(300),
        SilencioMaximo = TimeSpan.FromSeconds(30),
    };

    /// <summary>Un nodo que pide el indicativo (y la contrasena si se dice), anuncia y escucha.</summary>
    private static Func<SesionDeMentira, CancellationToken, Task> Guion(
        string anuncio,
        bool pideContrasena = false,
        bool cortaLaPrimera = false) => async (sesion, ct) =>
    {
        await sesion.EscribirAsync("login: ", ct);
        await sesion.LeerLineaONadaAsync(ct);
        if (pideContrasena)
        {
            await sesion.EscribirAsync("password: ", ct);
            await sesion.LeerLineaONadaAsync(ct);
        }

        if (cortaLaPrimera && sesion.Numero == 1)
        {
            await Task.Delay(150, ct);
            sesion.Cortar();
            return;
        }

        await sesion.EscribirLineaAsync(anuncio, ct);
        await sesion.EscucharHastaQueCierreAsync(ct);
    };

    private static FuenteDeVariosNodos Fuente(params OpcionesCluster[] nodos) =>
        new(opciones => new ClusterTelnet(opciones), nodos);

    [Fact]
    public async Task Se_conecta_a_varios_nodos_a_la_vez_y_cada_anuncio_dice_de_cual_viene()
    {
        await using var a = new ServidorDeMentira(Guion(AnuncioDeOh2bh));
        await using var b = new ServidorDeMentira(Guion(AnuncioDeF5xyz));
        await using var c = new ServidorDeMentira(Guion(AnuncioDeOh2bh));
        await using var fuente = Fuente(Nodo("a", a.Puerto), Nodo("b", b.Puerto), Nodo("c", c.Puerto));

        var llegados = new ConcurrentBag<Spot>();
        var tres = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fuente.SpotRecibido += (_, s) =>
        {
            llegados.Add(s);
            if (llegados.Count >= 3) tres.TrySetResult();
        };

        await fuente.ConectarAsync();
        await tres.Task.WaitAsync(Paciencia);

        llegados.Select(s => s.Fuente).Should().BeEquivalentTo(["A", "B", "C"]);
        fuente.Estado.Should().Be(EstadoDeConexion.Conectado);
        fuente.Nodos.Should().OnlyContain(n => n.Estado == EstadoDeConexion.Conectado);
        fuente.Nodos.Should().OnlyContain(n => n.SpotsPorMinuto == 1 && n.SpotsRecibidos == 1);
        a.Conexiones.Should().Be(1);
        b.Conexiones.Should().Be(1);
        c.Conexiones.Should().Be(1);
    }

    [Fact]
    public async Task El_mismo_anuncio_por_dos_nodos_es_una_fila_con_dos_origenes()
    {
        await using var a = new ServidorDeMentira(Guion(AnuncioDeOh2bh));
        await using var b = new ServidorDeMentira(Guion(AnuncioDeOh2bh));
        await using var fuente = Fuente(Nodo("a", a.Puerto), Nodo("b", b.Puerto));

        var junta = new JuntaDeRepetidos();
        var filas = new ConcurrentQueue<AnuncioDelCluster>();
        var dos = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fuente.SpotRecibido += (_, s) =>
        {
            filas.Enqueue(junta.Juntar(s));
            if (filas.Count >= 2) dos.TrySetResult();
        };

        await fuente.ConectarAsync();
        await dos.Task.WaitAsync(Paciencia);

        // Mismo anunciante por dos caminos: una fila, un solo «oyen» y dos nodos de origen.
        junta.Siguiendo.Should().Be(1);
        var ultima = filas.Last();
        ultima.Nodos.Should().BeEquivalentTo(["A", "B"]);
        ultima.Veces.Should().Be(1, "es el mismo anunciante llegado por dos nodos, no dos que lo oyen");
    }

    [Fact]
    public async Task Un_nodo_que_se_cae_reconecta_solo_sin_tocar_a_los_demas()
    {
        await using var cae = new ServidorDeMentira(Guion(AnuncioDeOh2bh, cortaLaPrimera: true));
        await using var firme = new ServidorDeMentira(Guion(AnuncioDeF5xyz));
        await using var fuente = Fuente(Nodo("cae", cae.Puerto), Nodo("firme", firme.Puerto));

        var estadosDelFirme = new ConcurrentQueue<EstadoDeConexion>();
        var vuelta = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var delFirme = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fuente.SpotRecibido += (_, s) =>
        {
            if (s.Fuente == "CAE") vuelta.TrySetResult();
            if (s.Fuente == "FIRME") delFirme.TrySetResult();
        };
        fuente.NodosCambiaron += (_, _) =>
        {
            var firme = fuente.Nodos.Single(n => n.Id == "firme");
            estadosDelFirme.Enqueue(firme.Estado);
        };

        await fuente.ConectarAsync();
        await delFirme.Task.WaitAsync(Paciencia);
        await vuelta.Task.WaitAsync(Paciencia);

        cae.Conexiones.Should().BeGreaterThanOrEqualTo(2, "el nodo caido ha vuelto a entrar");
        firme.Conexiones.Should().Be(1, "al otro nodo no se le ha cortado la sesion");
        estadosDelFirme.Should().NotContain(EstadoDeConexion.Reintentando);
        fuente.Nodos.Should().OnlyContain(n => n.Estado == EstadoDeConexion.Conectado);
    }

    [Fact]
    public async Task Entra_con_contrasena_en_el_nodo_que_la_pide_y_sin_ella_en_el_que_no()
    {
        await using var conClave = new ServidorDeMentira(Guion(AnuncioDeOh2bh, pideContrasena: true));
        await using var sinClave = new ServidorDeMentira(Guion(AnuncioDeF5xyz));
        await using var fuente = Fuente(
            Nodo("con", conClave.Puerto, contrasena: "secreta"),
            Nodo("sin", sinClave.Puerto));

        var dos = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cuantos = 0;
        fuente.SpotRecibido += (_, _) =>
        {
            if (Interlocked.Increment(ref cuantos) >= 2) dos.TrySetResult();
        };

        await fuente.ConectarAsync();
        await dos.Task.WaitAsync(Paciencia);

        conClave.Recibido.Should().ContainInOrder("EA8DLF", "secreta", "SH/DX 30");
        sinClave.Recibido.Should().ContainInOrder("EA8DLF", "SH/DX 30");
        sinClave.Recibido.Should().NotContain("secreta", "la contraseña de un nodo no viaja a otro");
    }

    [Fact]
    public async Task Un_spot_propio_va_solo_por_el_nodo_elegido()
    {
        await using var a = new ServidorDeMentira(Guion(AnuncioDeOh2bh));
        await using var b = new ServidorDeMentira(Guion(AnuncioDeF5xyz));
        await using var fuente = Fuente(Nodo("a", a.Puerto), Nodo("b", b.Puerto));

        var dos = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cuantos = 0;
        fuente.SpotRecibido += (_, _) =>
        {
            if (Interlocked.Increment(ref cuantos) >= 2) dos.TrySetResult();
        };
        await fuente.ConectarAsync();
        await dos.Task.WaitAsync(Paciencia);

        const string spot = "DX 14025.0 3Y0J Bouvet loud";
        await fuente.EnviarANodoAsync("b", spot);
        await Esperar(() => b.Recibido.Contains(spot));
        await Task.Delay(300);

        b.Recibido.Count(l => l == spot).Should().Be(1, "una sola vez, por el nodo elegido");
        a.Recibido.Should().NotContain(spot, "nunca se manda por todos los nodos");
    }

    [Fact]
    public async Task Sin_elegir_nodo_la_orden_va_solo_al_primero_conectado()
    {
        await using var a = new ServidorDeMentira(Guion(AnuncioDeOh2bh));
        await using var b = new ServidorDeMentira(Guion(AnuncioDeF5xyz));
        await using var fuente = Fuente(Nodo("a", a.Puerto), Nodo("b", b.Puerto));

        await fuente.ConectarAsync();
        await Esperar(() => fuente.Nodos.All(n => n.Estado == EstadoDeConexion.Conectado));

        await fuente.EnviarAsync("SH/WWV");
        await Esperar(() => a.Recibido.Contains("SH/WWV"));
        await Task.Delay(300);

        b.Recibido.Should().NotContain("SH/WWV");
    }

    [Fact]
    public async Task Lo_del_nodo_de_escucha_automatica_sale_marcado_como_skimmer()
    {
        // La linea no lleva «-#»: lo que manda es que el nodo es de escucha automatica.
        await using var rbn = new ServidorDeMentira(Guion(AnuncioDeOh2bh));
        await using var fuente = Fuente(Nodo("rbn", rbn.Puerto, skimmer: true));

        var llegado = new TaskCompletionSource<Spot>(TaskCreationOptions.RunContinuationsAsynchronously);
        fuente.SpotRecibido += (_, s) => llegado.TrySetResult(s);

        await fuente.ConectarAsync();
        var spot = await llegado.Task.WaitAsync(Paciencia);

        spot.EsDeEscuchaAutomatica.Should().BeTrue();
        fuente.Nodos.Single().EsSkimmer.Should().BeTrue();
    }

    [Fact]
    public async Task Aplicar_una_lista_nueva_no_corta_los_nodos_que_no_cambian()
    {
        await using var a = new ServidorDeMentira(Guion(AnuncioDeOh2bh));
        await using var b = new ServidorDeMentira(Guion(AnuncioDeF5xyz));
        await using var c = new ServidorDeMentira(Guion(AnuncioDeOh2bh));
        await using var fuente = Fuente(Nodo("a", a.Puerto), Nodo("b", b.Puerto));

        await fuente.ConectarAsync();
        await Esperar(() => fuente.Nodos.All(n => n.Estado == EstadoDeConexion.Conectado));

        // Se quita «b», se anade «c» y «a» se queda igual.
        await fuente.AplicarAsync([Nodo("a", a.Puerto), Nodo("c", c.Puerto)]);
        await Esperar(() => fuente.Nodos.All(n => n.Estado == EstadoDeConexion.Conectado));

        fuente.Nodos.Select(n => n.Id).Should().Equal("a", "c");
        a.Conexiones.Should().Be(1, "el nodo que no ha cambiado sigue con su sesion");
        c.Conexiones.Should().Be(1, "el nodo nuevo entra solo porque el cluster estaba en marcha");
    }

    [Fact]
    public async Task Un_nodo_caido_dice_por_que_y_los_demas_siguen()
    {
        // Un puerto que nadie escucha: se abre y se cierra un servidor para quedarse con uno libre.
        int libre;
        await using (var temporal = new ServidorDeMentira((_, _) => Task.CompletedTask))
        {
            libre = temporal.Puerto;
        }

        await using var bueno = new ServidorDeMentira(Guion(AnuncioDeOh2bh));
        await using var fuente = Fuente(Nodo("caido", libre), Nodo("bueno", bueno.Puerto));

        await fuente.ConectarAsync();
        await Esperar(() => fuente.Nodos.Single(n => n.Id == "bueno").Estado == EstadoDeConexion.Conectado
            && fuente.Nodos.Single(n => n.Id == "caido").Motivo is { Length: > 0 });

        var caido = fuente.Nodos.Single(n => n.Id == "caido");
        caido.Estado.Should().BeOneOf(EstadoDeConexion.Reintentando, EstadoDeConexion.Conectando);
        caido.Motivo.Should().NotBeNullOrWhiteSpace();
        fuente.Estado.Should().Be(EstadoDeConexion.Conectado, "con un nodo dentro ya llegan anuncios");
    }

    [Fact]
    public async Task Desconectar_un_nodo_deja_los_demas_conectados()
    {
        await using var a = new ServidorDeMentira(Guion(AnuncioDeOh2bh));
        await using var b = new ServidorDeMentira(Guion(AnuncioDeF5xyz));
        await using var fuente = Fuente(Nodo("a", a.Puerto), Nodo("b", b.Puerto));

        await fuente.ConectarAsync();
        await Esperar(() => fuente.Nodos.All(n => n.Estado == EstadoDeConexion.Conectado));

        await fuente.DesconectarNodoAsync("a");

        fuente.Nodos.Single(n => n.Id == "a").Estado.Should().Be(EstadoDeConexion.Desconectado);
        fuente.Nodos.Single(n => n.Id == "b").Estado.Should().Be(EstadoDeConexion.Conectado);
        fuente.Estado.Should().Be(EstadoDeConexion.Conectado);

        await fuente.ConectarNodoAsync("a");
        await Esperar(() => fuente.Nodos.All(n => n.Estado == EstadoDeConexion.Conectado));
        b.Conexiones.Should().Be(1);
    }

    [Fact]
    public async Task Probar_un_nodo_lee_el_saludo_y_no_manda_nada()
    {
        await using var nodo = new ServidorDeMentira(async (sesion, ct) =>
        {
            await sesion.EscribirLineaAsync("Welcome to the test cluster", ct);
            await sesion.EscribirAsync("login: ", ct);
            await sesion.EscucharHastaQueCierreAsync(ct);
        });

        var prueba = await ProbadorDeNodo.ProbarAsync("127.0.0.1", nodo.Puerto);
        await Task.Delay(200);

        prueba.Responde.Should().BeTrue();
        prueba.Saludo.Should().Be("Welcome to the test cluster");
        nodo.Recibido.Should().BeEmpty("probar no entra: no se manda ni el indicativo");
    }

    private static async Task Esperar(Func<bool> condicion)
    {
        var limite = DateTime.UtcNow + Paciencia;
        while (!condicion())
        {
            if (DateTime.UtcNow > limite) throw new TimeoutException("La condición no se ha cumplido a tiempo.");
            await Task.Delay(25);
        }
    }
}
