using System.Diagnostics;
using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Radio.Control.Ft710;
using Nodisla.Cuaderno.Radio.Ptt;
using Nodisla.Cuaderno.Radio.Pruebas.Dobles;

namespace Nodisla.Cuaderno.Radio.Pruebas;

/// <summary>
/// Las salvaguardas de transmision del vigilante: plan de banda con los bordes del filtro,
/// corte por ROE, no rearmar tras un corte hasta soltar todas las fuentes, la bajada normal
/// solo por la fuente dueña y la potencia maxima por banda. Todo con dobles: nada sale al aire.
/// </summary>
public sealed class SeguridadDeTxPruebas
{
    private static OpcionesDelVigilante Opciones(OpcionesDeSeguridadDeTx? seguridad = null, int tiempoMaximoMs = 5000) => new()
    {
        TiempoMaximo = TimeSpan.FromMilliseconds(tiempoMaximoMs),
        TiempoSinLatido = TimeSpan.FromSeconds(5),
        PasoDeVigilancia = TimeSpan.FromMilliseconds(5),
        EsperaDeSuelta = TimeSpan.FromMilliseconds(500),
        EngancharseAlCierreDelProceso = false,
        Seguridad = seguridad ?? new OpcionesDeSeguridadDeTx { PasoDeRoe = TimeSpan.FromMilliseconds(10), PausaTrasUnCorte = TimeSpan.FromMilliseconds(50) },
    };

    /// <summary>El 40 m de la region 1, como el plan del programa: 7.000 a 7.200.</summary>
    private sealed class PlanDe40m : IBandplan
    {
        public RegionIaru Region => RegionIaru.Region1;

        public IReadOnlyList<TramoDeBanda> TramosDe(Banda banda) => [];

        public ConsultaDeBandplan Consultar(Frecuencia frecuencia, Modo modo = default)
        {
            var dentro = frecuencia.Megahercios is >= 7.000m and <= 7.200m;
            var tramo = dentro ? new TramoDeBanda(Banda.Parse("40m"), Frecuencia.DesdeMegahercios(7.0m), Frecuencia.DesdeMegahercios(7.2m), UsoDelTramo.Todos, "40m", null) : null;
            return new ConsultaDeBandplan(frecuencia, tramo, dentro, null, null);
        }

        public IReadOnlyList<(Frecuencia Frecuencia, string Descripcion)> FrecuenciasSenaladas(Banda banda) => [];
    }

    /// <summary>Un equipo de mentira con PTT y ROE por guion.</summary>
    private sealed class EquipoConRoe : IControlEquipo, IMedidorDeRoe
    {
        private readonly object _candado = new();
        private readonly Queue<LecturaDeRoe> _guion = new();

        public EquipoConRoe(decimal mhz = 7.100m, string modo = "LSB") =>
            Estado = EstadoDelEquipo.Desconectado with { Conectado = true, Frecuencia = Frecuencia.DesdeMegahercios(mhz), Modo = Modo.Parse(modo) };

        public LecturaDeRoe Siempre { get; set; } = new(1.1, true, false);

        public ViaDeControl Via => ViaDeControl.CatNativo;

        public EstadoDelEquipo Estado { get; set; }

        public bool EnAntena { get; private set; }

        public int Subidas { get; private set; }

        public event EventHandler<EstadoDelEquipo>? EstadoCambiado;

        public void Guion(params LecturaDeRoe[] lecturas)
        {
            lock (_candado) foreach (var l in lecturas) _guion.Enqueue(l);
        }

        public Task<LecturaDeRoe?> LeerRoeAsync(CancellationToken ct = default)
        {
            lock (_candado) return Task.FromResult<LecturaDeRoe?>(_guion.Count > 0 ? _guion.Dequeue() : Siempre);
        }

        public Task ConectarAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task DesconectarAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task PonerFrecuenciaAsync(Frecuencia frecuencia, CancellationToken ct = default) => Task.CompletedTask;

        public Task PonerModoAsync(Modo modo, CancellationToken ct = default) => Task.CompletedTask;

        public Task PonerPttAsync(bool transmitir, CancellationToken ct = default)
        {
            EnAntena = transmitir;
            if (transmitir) Subidas++;
            EstadoCambiado?.Invoke(this, Estado);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static async Task Esperar(Func<bool> condicion, string que)
    {
        var reloj = Stopwatch.StartNew();
        while (!condicion())
        {
            if (reloj.Elapsed > TimeSpan.FromSeconds(5)) throw new TimeoutException(que);
            await Task.Delay(10);
        }
    }

    // ── 1. Plan de banda, con los bordes del filtro ────────────────────────────────────────────

    [Theory]
    [InlineData(7.0003, "CW", null, true)]   // 7.00005-7.00055: dentro
    [InlineData(7.0002, "CW", null, false)]  // el medio filtro baja de 7.000
    [InlineData(7.0002, "CW", 300, true)]    // con un filtro de 300 Hz ya cabe
    [InlineData(7.002, "LSB", null, false)]  // LSB: 3 kHz hacia abajo
    [InlineData(7.197, "USB", null, true)]   // USB: 7.197-7.200
    [InlineData(7.198, "USB", null, false)]  // se sale por arriba
    [InlineData(7.199, "LSB", null, true)]
    [InlineData(27.555, "USB", null, false)] // ni siquiera es de aficionado
    public void El_canal_entero_tiene_que_caber_en_el_plan(double mhz, string modo, int? ancho, bool cabe)
    {
        var motivo = PlanDeBandaDeTransmision.MotivoParaNoTransmitir(
            Frecuencia.DesdeMegahercios((decimal)mhz), Modo.Parse(modo), ancho, new PlanDe40m(), new OpcionesDeSeguridadDeTx());
        (motivo is null).Should().Be(cabe, motivo);
    }

    [Fact]
    public void Una_banda_liberada_o_el_bloqueo_quitado_dejan_transmitir()
    {
        var f = Frecuencia.DesdeMegahercios(27.555m);
        PlanDeBandaDeTransmision.MotivoParaNoTransmitir(f, Modo.Parse("USB"), null, new PlanDe40m(),
            new OpcionesDeSeguridadDeTx { BandasLiberadas = new HashSet<string> { "27 MHz" } }).Should().BeNull();
        PlanDeBandaDeTransmision.MotivoParaNoTransmitir(f, Modo.Parse("USB"), null, new PlanDe40m(),
            new OpcionesDeSeguridadDeTx { BloquearFueraDeBanda = false }).Should().BeNull();
        OpcionesDeSeguridadDeTx.ClaveDeBanda(Frecuencia.DesdeMegahercios(7.1m)).Should().Be("40m");
    }

    [Fact]
    public async Task Fuera_del_plan_el_vigilante_no_sube_el_PTT_y_dice_por_que()
    {
        var equipo = new EquipoConRoe(7.002m, "LSB");
        await using var vigilante = new VigilantePtt(equipo, Opciones(), bandplan: new PlanDe40m());

        var pedir = () => vigilante.PedirAntenaAsync("prueba");
        (await pedir.Should().ThrowAsync<TransmisionBloqueadaException>()).Which.Message.Should().Contain("7.002");
        equipo.Subidas.Should().Be(0);
        vigilante.EnAntena.Should().BeFalse();

        equipo.Estado = equipo.Estado with { Frecuencia = Frecuencia.DesdeMegahercios(7.150m) };
        await using (await vigilante.PedirAntenaAsync("prueba"))
        {
            equipo.EnAntena.Should().BeTrue();
        }
    }

    [Fact]
    public async Task FT710_en_27_MHz_de_la_captura_no_transmite_y_no_manda_TX1()
    {
        var canal = new CanalCatDeCaptura();
        var control = new ControlFt710(canal, new OpcionesFt710 { IntervaloDeSondeo = TimeSpan.FromHours(1) });
        await control.ConectarAsync();
        await using var vigilante = new VigilantePtt(control, Opciones());

        var pedir = () => vigilante.PedirAntenaAsync("prueba");
        await pedir.Should().ThrowAsync<TransmisionBloqueadaException>();
        canal.Recibidas.Should().NotContain("TX1;");
    }

    // ── 5. Potencia maxima por banda ───────────────────────────────────────────────────────────

    [Fact]
    public async Task FT710_baja_la_potencia_al_maximo_de_la_banda_antes_de_subir_el_PTT()
    {
        var canal = new CanalCatDeCaptura();
        canal.Responder("PC;", "PC100;");
        var control = new ControlFt710(canal, new OpcionesFt710 { IntervaloDeSondeo = TimeSpan.FromHours(1) });
        await control.ConectarAsync();
        var seguridad = new OpcionesDeSeguridadDeTx
        {
            BandasLiberadas = new HashSet<string> { "27 MHz" },
            PotenciaMaximaPorBanda = new Dictionary<string, int> { ["27 MHz"] = 10 },
            VigilarRoe = false,
        };
        await using var vigilante = new VigilantePtt(control, Opciones(seguridad));

        await using (await vigilante.PedirAntenaAsync("prueba"))
        {
        }

        var ordenes = canal.Recibidas.ToList();
        var potencia = ordenes.IndexOf("PC010;");
        potencia.Should().BeGreaterThanOrEqualTo(0, "se pone el maximo de la banda");
        ordenes.IndexOf("TX1;").Should().BeGreaterThan(potencia, "antes de salir al aire");
    }

    [Fact]
    public async Task Sin_poder_aplicar_el_maximo_de_potencia_no_se_transmite()
    {
        var equipo = new EquipoConRoe();
        var seguridad = new OpcionesDeSeguridadDeTx { PotenciaMaximaPorBanda = new Dictionary<string, int> { ["40m"] = 50 } };
        await using var vigilante = new VigilantePtt(equipo, Opciones(seguridad));

        var pedir = () => vigilante.PedirAntenaAsync("prueba");
        await pedir.Should().ThrowAsync<TransmisionBloqueadaException>("este equipo no deja leer ni poner la potencia");
        equipo.Subidas.Should().Be(0);
    }

    // ── 2. ROE ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ROE_alta_varias_lecturas_seguidas_corta_y_bloquea()
    {
        var equipo = new EquipoConRoe();
        await using var vigilante = new VigilantePtt(equipo, Opciones());
        var motivos = new List<MotivoDeSuelta>();
        vigilante.PttSoltado += (_, m) => motivos.Add(m);
        equipo.Guion(new(2.8, true, false), new(1.2, true, false), new(2.8, true, false), new(2.9, true, false));
        equipo.Siempre = new(3.0 - 0.1, true, false);

        var antena = await vigilante.PedirAntenaAsync("prueba");
        await Esperar(() => !equipo.EnAntena, "el corte por ROE");

        motivos.Should().Contain(MotivoDeSuelta.RoeAlta);
        antena.EnAntena.Should().BeFalse();
        vigilante.MotivoDeBloqueo.Should().Contain("2.9");
        await antena.DisposeAsync();
    }

    [Fact]
    public async Task Sin_potencia_la_ROE_no_corta_y_la_alarma_del_equipo_corta_en_el_acto()
    {
        var equipo = new EquipoConRoe { Siempre = new(9.9, false, false) };
        await using var vigilante = new VigilantePtt(equipo, Opciones());

        await using (var antena = await vigilante.PedirAntenaAsync("prueba"))
        {
            await Task.Delay(150);
            antena.Latir();
            equipo.EnAntena.Should().BeTrue("con la llave arriba la ROE no dice nada");
        }

        await Esperar(() => vigilante.MotivoDeBloqueo is null && !vigilante.EnAntena, "nada bloqueado");
        equipo.Siempre = new(1.1, true, AlarmaDelEquipo: true);
        var motivos = new List<MotivoDeSuelta>();
        vigilante.PttSoltado += (_, m) => motivos.Add(m);
        var otra = await vigilante.PedirAntenaAsync("prueba");
        await Esperar(() => !equipo.EnAntena, "el corte por la alarma");
        motivos.Should().Contain(MotivoDeSuelta.AntenaAbierta);
        await otra.DisposeAsync();
    }

    // ── 3. No rearmar hasta soltar todas las fuentes ────────────────────────────────────────

    [Fact]
    public async Task Tras_un_corte_no_se_transmite_hasta_que_se_sueltan_todas_las_fuentes()
    {
        var equipo = new EquipoConRoe();
        await using var vigilante = new VigilantePtt(equipo, Opciones(tiempoMaximoMs: 150));
        var modemPulsado = true;
        var moxPulsado = false;
        using var modem = vigilante.RegistrarFuente("Módem", () => modemPulsado);
        using var mox = vigilante.RegistrarFuente("MOX", () => moxPulsado);

        // El modem transmite y se pasa del tope: corte de seguridad.
        var antena = await vigilante.PedirAntenaAsync("Módem");
        await Esperar(() => !equipo.EnAntena, "el corte por tiempo");
        vigilante.MotivoDeBloqueo.Should().NotBeNull();
        await antena.DisposeAsync();

        // El modem sigue con el «Tx habilitado»: ni el ni el MOX pueden volver al aire.
        moxPulsado = true;
        await Task.Delay(400);
        var otra = () => vigilante.PedirAntenaAsync("MOX");
        await otra.Should().ThrowAsync<TransmisionBloqueadaException>();
        equipo.Subidas.Should().Be(1);

        // Se suelta el modem, pero el MOX sigue abajo: sigue bloqueado.
        modemPulsado = false;
        await Task.Delay(400);
        vigilante.MotivoDeBloqueo.Should().NotBeNull("el MOX sigue pulsado");

        // Todas sueltas: se levanta solo.
        moxPulsado = false;
        await Esperar(() => vigilante.MotivoDeBloqueo is null, "el rearme");
        await using (await vigilante.PedirAntenaAsync("MOX"))
        {
            equipo.EnAntena.Should().BeTrue();
        }
    }

    // ── 4. Solo la fuente dueña baja el PTT en el uso normal ────────────────────────────────

    [Fact]
    public async Task Solo_la_fuente_que_subio_el_PTT_lo_baja_y_la_emergencia_siempre_puede()
    {
        var equipo = new EquipoConRoe();
        await using var vigilante = new VigilantePtt(equipo, Opciones());

        var primera = await vigilante.PedirAntenaAsync("Telegrafía");
        vigilante.FuenteEnAntena.Should().Be("Telegrafía");

        // Otra fuente no puede ni subirlo encima ni bajarlo «normal».
        var otra = () => vigilante.PedirAntenaAsync("Fonía");
        await otra.Should().ThrowAsync<InvalidOperationException>();
        var normal = () => vigilante.SoltarYaAsync(MotivoDeSuelta.Normal);
        await normal.Should().ThrowAsync<ArgumentException>();
        equipo.EnAntena.Should().BeTrue();

        // La dueña lo baja; un testigo viejo ya no baja la transmision siguiente.
        await primera.DisposeAsync();
        equipo.EnAntena.Should().BeFalse();
        var segunda = await vigilante.PedirAntenaAsync("Fonía");
        await primera.DisposeAsync();
        equipo.EnAntena.Should().BeTrue("el testigo de la telegrafia no es dueño de la de fonia");

        // La emergencia baja siempre.
        await vigilante.SoltarYaAsync(MotivoDeSuelta.Panico);
        equipo.EnAntena.Should().BeFalse();
        await segunda.DisposeAsync();
    }
}
