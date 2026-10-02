using FluentAssertions;
using Nodisla.Cuaderno.Radio.Control;
using Nodisla.Cuaderno.Radio.Control.Ft710;
using Nodisla.Cuaderno.Radio.Control.Icom;
using Nodisla.Cuaderno.Radio.Cw;
using Nodisla.Cuaderno.Radio.Modelos;
using Nodisla.Cuaderno.Radio.Ptt;
using Nodisla.Cuaderno.Radio.Pruebas.Dobles;
using Nodisla.Cuaderno.Radio.Pruebas.Icom;

namespace Nodisla.Cuaderno.Radio.Pruebas.Cw;

/// <summary>
/// La telegrafia por el manipulador interno del equipo, contra canales CAT de mentira que
/// apuntan cada orden. Ninguna radio de verdad: el FT-710 es la captura real en memoria y el
/// ICOM es el de mentira de los manuales.
/// </summary>
public sealed class TransmisionCwPruebas
{
    private const string TextoLargo =
        "CQ CQ CQ DE EA8DLF EA8DLF EA8DLF PSE K CQ CQ CQ DE EA8DLF EA8DLF EA8DLF PSE K TNX FER CALL UR RST 599 599 NAME LUIS";

    private static OpcionesDelVigilante Vigilancia(int tiempoMaximoMs = 5000) => new()
    {
        TiempoMaximo = TimeSpan.FromMilliseconds(tiempoMaximoMs),
        TiempoSinLatido = TimeSpan.FromSeconds(5),
        PasoDeVigilancia = TimeSpan.FromMilliseconds(5),
        EsperaDeSuelta = TimeSpan.FromMilliseconds(500),
        EngancharseAlCierreDelProceso = false,

        // La captura del FT-710 esta en 27,555 MHz: fuera del plan. Aqui se prueba la
        // telegrafia, no el plan de banda (eso va en SeguridadDeTxPruebas).
        Seguridad = new OpcionesDeSeguridadDeTx { BloquearFueraDeBanda = false, VigilarRoe = false },
    };

    /// <summary>Un reloj de mentira: esperar adelanta el reloj y vuelve en el acto.</summary>
    private sealed class RelojDeMentira
    {
        private long _ticks;

        public TimeSpan Ahora => TimeSpan.FromTicks(Interlocked.Read(ref _ticks));

        public Func<TimeSpan, CancellationToken, Task>? AntesDeEsperar { get; set; }

        public OpcionesDelEmisorCw Opciones(TimeSpan? tope = null) => new()
        {
            TiempoMaximo = tope ?? TimeSpan.FromMinutes(2),
            Reloj = () => Ahora,
            Esperar = async (t, ct) =>
            {
                if (AntesDeEsperar is { } antes) await antes(t, ct);
                ct.ThrowIfCancellationRequested();
                Interlocked.Add(ref _ticks, t.Ticks);
            },
        };
    }

    private static async Task<(CanalCatDeCaptura Canal, ControlFt710 Control)> Ft710Async()
    {
        var canal = new CanalCatDeCaptura();
        var control = new ControlFt710(canal, new OpcionesFt710 { IntervaloDeSondeo = TimeSpan.FromHours(1) });
        await control.ConectarAsync();
        return (canal, control);
    }

    private static List<string> Desde(IReadOnlyList<string> todas, string primera) =>
        todas.Skip(todas.ToList().IndexOf(primera)).ToList();

    // ── El manual: ordenes y lista de prohibidas ─────────────────────────────────────────────

    [Fact]
    public void Las_ordenes_del_manipulador_son_las_del_manual()
    {
        OrdenesFt710.OrdenDeVelocidad(25).Should().Be("KS025;");
        OrdenesFt710.OrdenDeVelocidad(2).Should().Be("KS004;", "el manual va de 004 a 060");
        OrdenesFt710.OrdenDeVelocidad(99).Should().Be("KS060;");
        OrdenesFt710.OrdenDeEscribirTexto(5, "CQ TEST").Should().Be("KM5CQ TEST;");
        OrdenesFt710.OrdenDeReproducirTexto(5).Should().Be("KY05;");
        OrdenesFt710.PararElManipulador.Should().Be("KY00;");
        var largo = () => OrdenesFt710.OrdenDeEscribirTexto(5, new string('E', 51));
        largo.Should().Throw<ArgumentOutOfRangeException>("una memoria de texto lleva 50 como mucho");
    }

    [Fact]
    public void El_texto_se_limpia_de_lo_que_el_manipulador_no_sabe_hacer()
    {
        OrdenesFt710.TextoParaElManipulador("cq  de ea8dlf <AR> #1;x <BT> é").Should().Be("CQ DE EA8DLF + 1X =");
        OrdenesIcom.TextoParaElManipulador("73 <SK> <AR>").Should().Be("73 ^SK +");
    }

    [Fact]
    public void KY_y_KM_siguen_prohibidos_fuera_del_ambito_y_parar_pasa_siempre()
    {
        var reproducir = () => OrdenesFt710.ComprobarQueEsSegura("KY05;");
        var escribir = () => OrdenesFt710.ComprobarQueEsSegura("KM5CQ CQ;");
        var mensaje = () => OrdenesFt710.ComprobarQueEsSegura("KY11;");
        reproducir.Should().Throw<OrdenPeligrosaException>();
        escribir.Should().Throw<OrdenPeligrosaException>();
        mensaje.Should().Throw<OrdenPeligrosaException>();

        var parar = () => OrdenesFt710.ComprobarQueEsSegura("KY00;");
        var leer = () => OrdenesFt710.ComprobarQueEsSegura("KM5;");
        parar.Should().NotThrow();
        leer.Should().NotThrow();

        var icom = () => OrdenesIcom.ComprobarQueEsSegura([0x17, (byte)'C', (byte)'Q']);
        icom.Should().Throw<OrdenPeligrosaException>();
        var pararIcom = () => OrdenesIcom.ComprobarQueEsSegura([0x17, 0xFF]);
        pararIcom.Should().NotThrow();
    }

    [Fact]
    public async Task Sin_PTT_pedido_al_vigilante_el_control_no_manipula_ni_en_crudo()
    {
        var (canal, control) = await Ft710Async();

        var manipular = () => control.ManipularAsync("CQ");
        await manipular.Should().ThrowAsync<InvalidOperationException>();
        var crudo = () => control.OrdenEnCrudoAsync("KY05;");
        await crudo.Should().ThrowAsync<OrdenPeligrosaException>();
        var crudoKm = () => control.OrdenEnCrudoAsync("KM5CQ;");
        await crudoKm.Should().ThrowAsync<OrdenPeligrosaException>();

        canal.Recibidas.Should().NotContain(o => o.StartsWith("KY0", StringComparison.Ordinal) && o != "KY00;");
        canal.Recibidas.Should().NotContain(o => o.StartsWith("KM5", StringComparison.Ordinal) && o.Length > 4);
    }

    [Fact]
    public void Un_equipo_sin_manipulador_por_CAT_dice_por_que()
    {
        var conmutable = new ControlEquipoConmutable();
        conmutable.PorQueNoManipula.Should().NotBeNullOrEmpty();
        var otro = new ControlFt710(new CanalCatDeCaptura(), perfil: Control.Yaesu.PerfilesYaesu.Ft991A);
        otro.PorQueNoManipula.Should().Contain("FT-991A", "su KY no se ha comprobado en el manual");
    }

    [Fact]
    public void PARIS_dura_50_unidades_con_su_hueco()
    {
        TiempoMorse.Unidades("PARIS").Should().Be(43);
        TiempoMorse.Duracion("PARIS PARIS", 20).Should().Be(TimeSpan.FromMilliseconds(60 * 43 + 60 * 7 + 60 * 43));
        TiempoMorse.Unidades("<SK>").Should().Be(15, "un prosigno va seguido: ...-.- sin el hueco de caracter");
        TiempoMorse.Unidades("SK").Should().Be(17);
    }

    // ── FT-710: trozos de 50, KS, parada ──────────────────────────────────────────────────────

    [Fact]
    public async Task FT710_trocea_en_memorias_de_50_por_palabras_y_baja_el_PTT_parando_el_manipulador()
    {
        var (canal, control) = await Ft710Async();
        canal.Responder("KM5;", "KM5MI TEXTO DE SIEMPRE;");
        await using var vigilante = new VigilantePtt(control, Vigilancia(tiempoMaximoMs: 60_000));
        var reloj = new RelojDeMentira();
        var emisor = new EmisorCw(control, vigilante, reloj.Opciones());

        var fin = await emisor.TransmitirAsync([new TrozoCw(TextoLargo, 25)], "prueba");

        fin.Should().Be(FinDelEnvioCw.Completo);
        var ordenes = Desde(canal.Recibidas, "TX1;");
        ordenes[0].Should().Be("TX1;", "primero la antena, por el vigilante");
        ordenes.Should().Contain("KS025;");
        var escrituras = ordenes.Where(o => o.StartsWith("KM5", StringComparison.Ordinal) && o.Length > 4).ToList();
        var trozos = escrituras.Take(escrituras.Count - 1).Select(o => o[3..^1]).ToList();
        trozos.Should().HaveCountGreaterThan(2);
        trozos.Should().OnlyContain(t => t.Length <= 50);
        string.Join(' ', trozos).Should().Be(TextoLargo, "se corta por palabras, sin perder ni partir ninguna");
        ordenes.Count(o => o == "KY05;").Should().Be(trozos.Count, "cada trozo se reproduce una vez");

        // Al terminar: parar el manipulador ANTES de bajar el PTT, y devolver la memoria.
        var tx0 = ordenes.IndexOf("TX0;");
        tx0.Should().BePositive();
        ordenes.IndexOf("KY00;").Should().BeInRange(0, tx0 - 1);
        ordenes.Last().Should().Be("KM5MI TEXTO DE SIEMPRE;", "la memoria 5 vuelve a tener lo que tenia");
        vigilante.EnAntena.Should().BeFalse();
    }

    [Fact]
    public async Task FT710_parar_a_mitad_manda_KY00_y_TX0_y_no_sale_nada_mas()
    {
        var (canal, control) = await Ft710Async();
        await using var vigilante = new VigilantePtt(control, Vigilancia());
        var reloj = new RelojDeMentira();
        var primeraEspera = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var soltar = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        reloj.AntesDeEsperar = async (_, ct) =>
        {
            primeraEspera.TrySetResult();
            await soltar.Task.WaitAsync(ct);
        };
        var emisor = new EmisorCw(control, vigilante, reloj.Opciones());

        var transmision = emisor.TransmitirAsync([new TrozoCw(TextoLargo, 25)], "prueba");
        await primeraEspera.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await emisor.PararAsync();
        var fin = await transmision.WaitAsync(TimeSpan.FromSeconds(5));

        fin.Should().Be(FinDelEnvioCw.Parado);
        var ordenes = Desde(canal.Recibidas, "TX1;");
        ordenes.Count(o => o == "KY05;").Should().Be(1, "solo salio el primer trozo");
        var parar = ordenes.IndexOf("KY00;");
        parar.Should().BePositive();
        ordenes.IndexOf("TX0;").Should().BeGreaterThan(parar);
        vigilante.EnAntena.Should().BeFalse();
        emisor.Enviando.Should().BeFalse();
    }

    [Fact]
    public async Task FT710_el_tiempo_maximo_corta_la_transmision()
    {
        var (canal, control) = await Ft710Async();
        await using var vigilante = new VigilantePtt(control, Vigilancia());
        var reloj = new RelojDeMentira();
        var emisor = new EmisorCw(control, vigilante, reloj.Opciones(TimeSpan.FromSeconds(5)));

        // A 10 WPM cada trozo de 50 tarda bastante mas de 5 s.
        var fin = await emisor.TransmitirAsync([new TrozoCw(TextoLargo, 10)], "prueba");

        fin.Should().Be(FinDelEnvioCw.TiempoAgotado);
        var ordenes = Desde(canal.Recibidas, "TX1;");
        ordenes.Count(o => o == "KY05;").Should().Be(1);
        ordenes.IndexOf("TX0;").Should().BeGreaterThan(ordenes.IndexOf("KY00;"));
        vigilante.EnAntena.Should().BeFalse();
    }

    [Fact]
    public async Task FT710_si_el_vigilante_suelta_la_antena_se_para_el_manipulador()
    {
        var (canal, control) = await Ft710Async();
        await using var vigilante = new VigilantePtt(control, Vigilancia());
        var reloj = new RelojDeMentira();
        var primeraEspera = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var soltar = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        reloj.AntesDeEsperar = async (_, ct) =>
        {
            primeraEspera.TrySetResult();
            await soltar.Task.WaitAsync(ct);
        };
        var emisor = new EmisorCw(control, vigilante, reloj.Opciones());

        var transmision = emisor.TransmitirAsync([new TrozoCw(TextoLargo, 5)], "prueba");
        await primeraEspera.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // El vigilante suelta por su cuenta (tope, latido, panico, equipo perdido…).
        await vigilante.SoltarYaAsync(Aplicacion.Puertos.MotivoDeSuelta.TiempoAgotado);
        soltar.TrySetResult();
        var fin = await transmision.WaitAsync(TimeSpan.FromSeconds(10));
        fin.Should().Be(FinDelEnvioCw.Parado);
        var ordenes = Desde(canal.Recibidas, "TX1;");
        var tx0 = ordenes.IndexOf("TX0;");
        tx0.Should().BePositive();
        ordenes.IndexOf("KY00;").Should().BeInRange(0, tx0 - 1, "el control para el manipulador antes de bajar el PTT, venga de donde venga");
        ordenes.Count(o => o == "KY05;").Should().Be(1);
        vigilante.EnAntena.Should().BeFalse();
    }

    // ── ICOM: orden 17 ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ICOM_manda_17_en_trozos_de_30_y_para_con_17_FF_antes_de_bajar_el_PTT()
    {
        var radio = new IcomDeMentira(ModelosIcom.Todos.Single(p => p.Modelo.Clave == "icom-ic7300"));
        var canal = new CanalCivDeMentira(radio);
        var control = new ControlIcom(canal, radio.Perfil, new OpcionesFt710
        {
            IntervaloDeSondeo = TimeSpan.FromHours(1),
            EsperaDeOrden = TimeSpan.FromMilliseconds(300),
            Esperar = (_, _) => Task.CompletedTask,
        });
        await control.ConectarAsync();
        radio.Olvidar();
        await using var vigilante = new VigilantePtt(control, Vigilancia());
        var reloj = new RelojDeMentira();
        var primeraEspera = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        reloj.AntesDeEsperar = async (_, ct) =>
        {
            primeraEspera.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
        };
        var emisor = new EmisorCw(control, vigilante, reloj.Opciones());

        var transmision = emisor.TransmitirAsync([new TrozoCw("CQ CQ DE EA8DLF EA8DLF EA8DLF TEST <SK>", 22)], "prueba");
        await primeraEspera.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var enHex = radio.RecibidasEnHex;
        enHex.Should().Contain("1C 00 01");
        var primera = radio.Recibidas.First(c => c[0] == 0x17);
        primera.Length.Should().BeLessThanOrEqualTo(31, "17 mas 30 caracteres como mucho");
        System.Text.Encoding.ASCII.GetString(primera[1..]).Should().Be("CQ CQ DE EA8DLF EA8DLF EA8DLF");

        await emisor.PararAsync();
        (await transmision.WaitAsync(TimeSpan.FromSeconds(5))).Should().Be(FinDelEnvioCw.Parado);
        enHex = radio.RecibidasEnHex;
        var parar = enHex.ToList().IndexOf("17 FF");
        parar.Should().BePositive();
        enHex.ToList().LastIndexOf("1C 00 00").Should().BeGreaterThan(parar);
        radio.EnAntena.Should().BeFalse();
    }
}
