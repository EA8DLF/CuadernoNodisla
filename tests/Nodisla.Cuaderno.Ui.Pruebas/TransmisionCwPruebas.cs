using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.CasosDeUso;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Idiomas;
using Nodisla.Cuaderno.Radio.Cw;
using Nodisla.Cuaderno.Ui.Ajustes;
using Nodisla.Cuaderno.Ui.Desarrollo;
using Nodisla.Cuaderno.Ui.Digital;
using Nodisla.Cuaderno.Ui.Telegrafia;
using Nodisla.Cuaderno.Ui.VistaModelos;
using Nodisla.Cuaderno.Ui.Vistas;

namespace Nodisla.Cuaderno.Ui.Pruebas;

/// <summary>
/// Transmitir en CW: macros, variables, la secuencia con texto decodificado simulado, las
/// puertas (pestillo, pregunta, modo) y la vista de verdad pintada fuera de la pantalla.
/// Sin radio: el equipo es el simulado, que solo apunta lo que «manipularia». Con
/// <c>CUADERNO_CAPTURAS_TXCW</c> (una carpeta) deja ademas las capturas de la ayuda.
/// </summary>
[Collection(nameof(ColeccionDeLaVentana))]
public sealed class TransmisionCwPruebas
{
    // ── Variables y macros ────────────────────────────────────────────────────────────────

    private static ContextoDeMacroCw Contexto(bool concurso = false, string call = "DL1ABC") =>
        new("EA8DLF", call, string.Empty, 7, "HANS", "BERLIN", "JO62", "JOSE", "TENERIFE", "IL18", concurso);

    [Fact]
    public void Las_variables_se_rellenan()
    {
        var r = ExpansorDeMacrosCw.Expandir("{CALL} DE {MICALL} UR {RST} NR {NR} {NOMBRE} {QTH} {LOC} OP {MINOMBRE} {MIQTH} {MILOC} {CQ} {TU} {73} {AGN} {?}", Contexto(), 22);
        r.Texto.Should().Be("DL1ABC DE EA8DLF UR 599 NR 007 HANS BERLIN JO62 OP JOSE TENERIFE IL18 CQ TU 73 AGN ?");
        r.FaltaIndicativo.Should().BeFalse();
        r.Trozos.Should().ContainSingle().Which.Wpm.Should().Be(22);
    }

    [Fact]
    public void En_concurso_el_RST_va_con_N_y_CQ_es_CQ_TEST()
    {
        var r = ExpansorDeMacrosCw.Expandir("{CQ} {MICALL} {RST} {NR}", Contexto(concurso: true), 28);
        r.Texto.Should().Be("CQ TEST EA8DLF 5NN 007");
    }

    [Fact]
    public void La_velocidad_cambia_dentro_de_la_macro()
    {
        var r = ExpansorDeMacrosCw.Expandir("CQ <+5>TEST<-10> EA8DLF <WPM 30>K", Contexto(), 20);
        r.Trozos.Select(t => (t.Texto, t.Wpm)).Should().Equal(("CQ", 20), ("TEST", 25), ("EA8DLF", 15), ("K", 30));
        ExpansorDeMacrosCw.Expandir("<WPM 99>CQ", Contexto(), 20, 4, 60).Trozos[0].Wpm.Should().Be(60, "se acota a lo que admite el equipo");
    }

    [Fact]
    public void Sin_indicativo_se_avisa_y_las_desconocidas_se_quitan()
    {
        var r = ExpansorDeMacrosCw.Expandir("{CALL} {QUE} 73", Contexto(call: string.Empty), 20);
        r.FaltaIndicativo.Should().BeTrue();
        r.Desconocidas.Should().Equal("{QUE}");
        r.Texto.Should().Be("73");
    }

    [Fact]
    public void Los_tres_juegos_de_fabrica_se_guardan_y_leen()
    {
        var carpeta = Path.Combine(Path.GetTempPath(), "cuaderno-txcw-" + Guid.NewGuid().ToString("N"));
        try
        {
            var ajustes = new AjustesDeTransmisionCw().Acotar();
            ajustes.Conversacion.Macros.Should().HaveCount(12, "el de charla trae los doce de siempre");
            ajustes.Contactos.Macros.Should().HaveCount(8, "sin concurso pero sin charla: menos que el de charla");
            ajustes.Concurso.Macros.Should().HaveCount(5, "reducido a las cinco teclas de toda la vida");
            ajustes.Conversacion.Macros[0] = new MacroGuardada("CQ", "CQ DE {MICALL} K");
            ajustes.Wpm = 99;
            ajustes.Acotar().Guardar(carpeta);

            var leidos = AjustesDeTransmisionCw.Leer(carpeta);
            leidos.Conversacion.Macros[0].Texto.Should().Be("CQ DE {MICALL} K");
            leidos.Wpm.Should().Be(60);
            File.ReadAllText(Path.Combine(carpeta, AjustesDeTransmisionCw.Fichero)).Should().NotContain("Permitir", "el pestillo no se guarda nunca");
        }
        finally
        {
            if (Directory.Exists(carpeta)) Directory.Delete(carpeta, true);
        }
    }

    [Fact]
    public void Las_macros_se_exportan_y_se_importan()
    {
        var carpeta = Path.Combine(Path.GetTempPath(), "cuaderno-txcw-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(carpeta);
        try
        {
            var fichero = Path.Combine(carpeta, "mis-macros.json");
            var (vm, _, _, _) = Montar(carpeta);
            vm.Perfil = PerfilDeMacrosCw.Conversacion;
            vm.Macros[11].Texto = "QRL? DE {MICALL}";
            vm.ElegirFicheroParaExportar = () => fichero;
            vm.Exportar();
            File.Exists(fichero).Should().BeTrue();

            var (otro, _, _, _) = Montar(Path.Combine(carpeta, "otra"));
            otro.Perfil = PerfilDeMacrosCw.Conversacion;
            otro.Macros[11].Texto.Should().Be("QRL?");
            otro.ElegirFicheroParaImportar = () => fichero;
            otro.Importar();
            otro.Macros[11].Texto.Should().Be("QRL? DE {MICALL}");
            File.Exists(Path.Combine(carpeta, "otra", AjustesDeTransmisionCw.Fichero)).Should().BeTrue("lo importado se guarda");
        }
        finally
        {
            Directory.Delete(carpeta, true);
        }
    }

    // ── La secuencia sola ─────────────────────────────────────────────────────────────────

    private static void Oir(SecuenciadorCw s, string texto)
    {
        foreach (var p in texto.Split(' ')) s.Oir(p);
    }

    [Fact]
    public void Llamando_el_CQ_de_otro_no_es_respuesta_y_el_que_contesta_si()
    {
        var s = new SecuenciadorCw { MiIndicativo = "EA8DLF" };
        s.AlEnviar(PasoCw.Cq);
        Oir(s, "CQ CQ DE G4XYZ G4XYZ K");
        s.TerminarPasada().Should().BeFalse();
        Oir(s, "DL1ABC DE G4XYZ 599");
        s.TerminarPasada().Should().BeFalse("esta trabajando con otro");
        Oir(s, "EA8DLF DE DL1ABC DL1ABC K");
        s.TerminarPasada().Should().BeTrue();
        s.Corresponsal.Should().Be("DL1ABC");
        s.Siguiente.Should().Be(PasoCw.Informe);
    }

    [Fact]
    public void Un_interrogante_hace_repetir_y_el_silencio_tambien_hasta_rendirse()
    {
        var s = new SecuenciadorCw { MiIndicativo = "EA8DLF" };
        s.Empezar(PapelCw.Llamo);
        s.AlEnviar(PasoCw.Cq);
        Oir(s, "EA8DLF DE DL1ABC K");
        s.TerminarPasada();
        s.AlEnviar(PasoCw.Informe);
        Oir(s, "AGN?");
        s.TerminarPasada().Should().BeTrue();
        s.Siguiente.Should().Be(PasoCw.Informe);

        s.Silencio(2).Should().BeTrue();
        s.AlEnviar(PasoCw.Informe);
        s.AlEnviar(PasoCw.Informe);
        s.Silencio(2).Should().BeFalse("ya se ha repetido dos veces");
    }

    [Fact]
    public void Buscando_en_concurso_se_lee_el_numero_cortado_y_se_cierra_con_el_informe()
    {
        var s = new SecuenciadorCw { MiIndicativo = "EA8DLF", Concurso = true, CierraConElInforme = true };
        DatosDelQsoCw? completo = null;
        s.ContactoCompleto += (_, d) => completo = d;
        s.Empezar(PapelCw.Busco, "DL1ABC");
        s.AlEnviar(PasoCw.Respuesta);
        Oir(s, "EA8DLF 5NN 1T4");
        s.TerminarPasada().Should().BeTrue();
        s.RstRecibido.Should().Be("599");
        s.NumeroRecibido.Should().Be("104");
        s.AlEnviar(PasoCw.Informe);
        completo.Should().NotBeNull();
        completo!.Indicativo.Should().Be("DL1ABC");
    }

    [Fact]
    public void El_lector_pasa_las_palabras_terminadas_y_la_ultima_con_el_silencio()
    {
        var palabras = new ObservableCollection<PalabraCw>();
        using var lector = new LectorDePalabrasCw(palabras);
        var leidas = new List<string>();
        lector.PalabraLeida += (_, p) => leidas.Add(p);

        palabras.Add(new PalabraCw("E", TipoDePalabraCw.Normal));
        palabras[0] = new PalabraCw("EA8", TipoDePalabraCw.Normal);
        palabras[0] = new PalabraCw("EA8DLF", TipoDePalabraCw.Propio);
        leidas.Should().BeEmpty("la palabra aun se esta escribiendo");
        palabras.Add(new PalabraCw("D", TipoDePalabraCw.Normal));
        leidas.Should().Equal("EA8DLF");
        palabras[1] = new PalabraCw("DE", TipoDePalabraCw.Normal);
        lector.Cerrar();
        leidas.Should().Equal("EA8DLF", "DE");
        lector.Saltar();
        palabras.Add(new PalabraCw("X", TipoDePalabraCw.Normal));
        lector.Cerrar();
        leidas.Should().Equal("EA8DLF", "DE", "X");
    }

    // ── El modelo, con el equipo simulado ─────────────────────────────────────────────────

    /// <summary>
    /// Un equipo de mentira con manipulador: empieza conectado y en SSB, sube y baja el PTT
    /// cuando se lo pide el vigilante y apunta lo que «manipularia». No transmite nada.
    /// </summary>
    private sealed class EquipoCwDeMentira : IControlEquipo, IManipuladorCw
    {
        public List<string> Manipulado { get; } = [];

        public ViaDeControl Via => ViaDeControl.CatNativo;

        public EstadoDelEquipo Estado { get; private set; } = EstadoDelEquipo.Desconectado with
        {
            Conectado = true,
            Modo = Dominio.Valores.Modo.Parse("SSB"),
            Frecuencia = Dominio.Valores.Frecuencia.DesdeHercios(14_030_000),
        };

        public event EventHandler<EstadoDelEquipo>? EstadoCambiado;

        public string? PorQueNoManipula => null;

        public int LetrasPorOrden => 50;

        public int WpmMinima => 4;

        public int WpmMaxima => 60;

        public Task ConectarAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task DesconectarAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task PonerFrecuenciaAsync(Dominio.Valores.Frecuencia frecuencia, CancellationToken ct = default) => Task.CompletedTask;

        public Task PonerModoAsync(Dominio.Valores.Modo modo, CancellationToken ct = default)
        {
            Cambiar(Estado with { Modo = modo });
            return Task.CompletedTask;
        }

        public Task PonerPttAsync(bool transmitir, CancellationToken ct = default)
        {
            Cambiar(Estado with { Transmitiendo = transmitir });
            return Task.CompletedTask;
        }

        public Task PonerVelocidadAsync(int wpm, CancellationToken ct = default) => Task.CompletedTask;

        public Task ManipularAsync(string texto, CancellationToken ct = default)
        {
            if (!Estado.Transmitiendo) throw new InvalidOperationException("Sin PTT no se manipula.");
            lock (Manipulado) Manipulado.Add(texto);
            return Task.CompletedTask;
        }

        public Task PararManipuladorAsync(CancellationToken ct = default) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private void Cambiar(EstadoDelEquipo nuevo)
        {
            Estado = nuevo;
            EstadoCambiado?.Invoke(this, nuevo);
        }
    }

    private sealed class RelojDeMentira    {
        private long _ticks;

        public TimeSpan Ahora => TimeSpan.FromTicks(Interlocked.Read(ref _ticks));

        public Func<CancellationToken, Task>? AntesDeEsperar { get; set; }

        public OpcionesDelEmisorCw Opciones() => new()
        {
            Reloj = () => Ahora,
            Esperar = async (t, ct) =>
            {
                if (AntesDeEsperar is { } antes) await antes(ct);
                ct.ThrowIfCancellationRequested();
                Interlocked.Add(ref _ticks, t.Ticks);
            },
        };
    }

    private sealed record Montaje(EquipoCwDeMentira Equipo, VigilantePttDeDesarrollo Vigilante, RepositorioQsoEnMemoria Cuaderno, VistaModeloEntradaQso Entrada, RelojDeMentira Reloj);

    private static (VistaModeloTransmisionCw Modelo, Montaje Piezas, AjustesDelPrograma Ajustes, Func<DateTimeOffset, DateTimeOffset> Adelantar) Montar(string? carpeta = null)
    {
        var ajustes = new AjustesDelPrograma();
        ajustes.Digital.PedirConfirmacionAlTransmitir = true;
        ajustes.Digital.RegistrarAlCompletar = true;
        var equipo = new EquipoCwDeMentira();
        var vigilante = new VigilantePttDeDesarrollo(equipo);
        var reloj = new RelojDeMentira();
        var emisor = new EmisorCw(equipo, vigilante, reloj.Opciones());
        var cuaderno = new RepositorioQsoEnMemoria([]);
        var estaciones = new RepositorioEstacionEnMemoria();
        var entrada = new VistaModeloEntradaQso(
            new RegistrarQso(cuaderno, estaciones),
            new EditarQso(cuaderno, estaciones, new AvisosDeQsos()),
            new ConsultarTrabajadoAntes(cuaderno))
        {
            Esperar = (_, _) => Task.CompletedTask,
        };
        var ahora = new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
        var modelo = new VistaModeloTransmisionCw(ajustes, emisor, equipo, entrada, carpeta: carpeta, conReloj: false)
        {
            MiIndicativo = "EA8DLF",
            Ahora = () => ahora,
            ConfirmarQueVaATransmitir = _ => true,
            PreguntarSiCambiaACw = _ => true,
        };
        return (modelo, new Montaje(equipo, vigilante, cuaderno, entrada, reloj), ajustes, cuanto => ahora = cuanto);
    }

    private static async Task Esperar(Func<bool> condicion, string que)
    {
        var reloj = Stopwatch.StartNew();
        while (!condicion())
        {
            if (reloj.Elapsed > TimeSpan.FromSeconds(10)) throw new TimeoutException(que);
            await Task.Delay(10);
        }
    }

    private static List<string> Manipulado(EquipoCwDeMentira equipo)
    {
        lock (equipo.Manipulado) return [.. equipo.Manipulado];
    }

    [Fact]
    public async Task Con_el_pestillo_cerrado_no_sale_nada_ni_se_enciende_el_automatico()
    {
        var (vm, piezas, _, _) = Montar();
        vm.PermitirTransmitir.Should().BeFalse("cada arranque empieza cerrado");
        vm.SePuedeTransmitir.Should().BeFalse();

        await vm.EnviarMacroAsync(vm.Macros[0]);
        await vm.IntroAsync();
        vm.Automatico = true;

        Manipulado(piezas.Equipo).Should().BeEmpty();
        piezas.Equipo.Estado.Transmitiendo.Should().BeFalse();
        vm.Automatico.Should().BeFalse();
        vm.Aviso.Should().Be(Textos.T("Cabina.TxCw.PestilloCerrado"));
    }

    [Fact]
    public async Task Sin_la_pregunta_o_diciendo_que_no_no_se_transmite()
    {
        var (vm, piezas, ajustes, _) = Montar();
        vm.PermitirTransmitir = true;

        vm.ConfirmarQueVaATransmitir = null;
        await vm.EnviarMacroAsync(vm.Macros[0]);
        vm.ConfirmarQueVaATransmitir = _ => false;
        await vm.EnviarMacroAsync(vm.Macros[0]);
        Manipulado(piezas.Equipo).Should().BeEmpty();

        // Con «no volver a preguntar» (el mismo ajuste del modem) ya no se pregunta.
        ajustes.Digital.PedirConfirmacionAlTransmitir = false;
        await vm.EnviarMacroAsync(vm.Macros[0]);
        Manipulado(piezas.Equipo).Should().ContainSingle().Which.Should().StartWith("CQ DE EA8DLF");
    }

    [Fact]
    public async Task Con_el_equipo_en_otro_modo_se_pregunta_antes_de_pasarlo_a_CW()
    {
        var (vm, piezas, _, _) = Montar();
        vm.PermitirTransmitir = true;
        piezas.Equipo.Estado.Modo.NombreUsual.Should().Be("SSB");

        string? preguntado = null;
        vm.PreguntarSiCambiaACw = p => { preguntado = p; return false; };
        await vm.EnviarMacroAsync(vm.Macros[0]);
        preguntado.Should().Contain("SSB");
        Manipulado(piezas.Equipo).Should().BeEmpty();
        piezas.Equipo.Estado.Modo.NombreUsual.Should().Be("SSB");

        vm.PreguntarSiCambiaACw = _ => true;
        await vm.EnviarMacroAsync(vm.Macros[0]);
        piezas.Equipo.Estado.Modo.NombreUsual.Should().Be("CW");
        Manipulado(piezas.Equipo).Should().ContainSingle();
    }

    [Fact]
    public async Task El_tiempo_maximo_corta_y_avisa()
    {
        var (vm, piezas, _, _) = Montar();
        vm.PermitirTransmitir = true;
        vm.TiempoMaximoSegundos = 10;
        vm.Wpm = 5;
        vm.TextoLibre = "CQ CQ CQ DE EA8DLF EA8DLF EA8DLF CQ CQ CQ DE EA8DLF EA8DLF EA8DLF CQ CQ CQ DE EA8DLF EA8DLF K";
        await vm.EnviarTextoLibreAsync();

        vm.Aviso.Should().Be(Textos.F("Cabina.TxCw.TiempoAgotado", 10));
        Manipulado(piezas.Equipo).Should().ContainSingle("a 5 WPM el primer trozo ya pasa de 10 s");
        piezas.Equipo.Estado.Transmitiendo.Should().BeFalse();
    }

    [Fact]
    public async Task Un_QSO_completo_llamando_CQ_con_texto_decodificado_simulado()
    {
        var (vm, piezas, _, adelantar) = Montar();
        vm.PermitirTransmitir = true;
        vm.Perfil = PerfilDeMacrosCw.Conversacion;
        vm.MiNombre = "JOSE";
        vm.MiQth = "TENERIFE";
        vm.Automatico = true;

        // Intro: CQ.
        await vm.IntroAsync();
        vm.Secuencia.Paso.Should().Be(PasoCw.Cq);
        Manipulado(piezas.Equipo).Should().ContainSingle().Which.Should().StartWith("CQ CQ CQ DE EA8DLF");

        // Me contesta DL1ABC: la secuencia lo pone en el contacto nuevo y manda el informe sola.
        foreach (var p in "EA8DLF DE DL1ABC DL1ABC K".Split(' ')) vm.Oir(p);
        await Esperar(() => vm.Secuencia.Paso == PasoCw.Informe && !vm.Emisor.Enviando, "el informe");
        piezas.Entrada.Indicativo.Should().Be("DL1ABC");
        Manipulado(piezas.Equipo).Should().Contain(t => t.Contains("DL1ABC DE EA8DLF TNX FER CALL UR RST 599"));

        // Su informe, con nombre y QTH, y fin de pasada por silencio (sin K).
        foreach (var p in "EA8DLF DE DL1ABC R TNX UR RST 579 579 NAME HANS HANS QTH BERLIN HW?".Split(' ')) vm.Oir(p);
        adelantar(new DateTimeOffset(2026, 10, 2, 12, 0, 3, TimeSpan.Zero));
        vm.Latir();
        await Esperar(() => vm.Secuencia.Completo && !vm.Emisor.Enviando, "la confirmacion");
        Manipulado(piezas.Equipo).Should().Contain(t => t.Contains("R R TNX HANS UR 599"));

        // Completo: apuntado en el cuaderno con lo que se leyo.
        await Esperar(() => piezas.Cuaderno.BuscarAsync(new CriterioQso(), 0, 10).Result.Elementos.Count == 1, "el registro");
        var qso = (await piezas.Cuaderno.BuscarAsync(new CriterioQso(), 0, 10)).Elementos.Single();
        qso.Call.Valor.Should().Be("DL1ABC");
        qso.RstRcvd.ToString().Should().Be("579");
        qso.Name.Should().Be("HANS");
        qso.Qth.Should().Be("BERLIN");
        piezas.Equipo.Estado.Transmitiendo.Should().BeFalse();
    }

    [Fact]
    public async Task Buscando_en_concurso_Intro_manda_mi_indicativo_y_luego_el_intercambio_y_lo_apunta()
    {
        var (vm, piezas, _, adelantar) = Montar();
        vm.PermitirTransmitir = true;
        vm.Perfil = PerfilDeMacrosCw.Concurso;
        vm.Busco = true;
        vm.Numero = 41;
        vm.Automatico = true;
        piezas.Entrada.Indicativo = "DL1ABC";

        await vm.IntroAsync();
        Manipulado(piezas.Equipo).Should().Equal("EA8DLF");

        foreach (var p in "EA8DLF 5NN 1T4".Split(' ')) vm.Oir(p);
        adelantar(new DateTimeOffset(2026, 10, 2, 12, 0, 3, TimeSpan.Zero));
        vm.Latir();
        await Esperar(() => vm.Secuencia.Completo && !vm.Emisor.Enviando, "el intercambio");
        Manipulado(piezas.Equipo).Should().Equal("EA8DLF", "5NN 041");

        await Esperar(() => piezas.Cuaderno.BuscarAsync(new CriterioQso(), 0, 10).Result.Elementos.Count == 1, "el registro");
        var qso = (await piezas.Cuaderno.BuscarAsync(new CriterioQso(), 0, 10)).Elementos.Single();
        qso.Comentario.Should().Contain("041").And.Contain("104");
        vm.Numero.Should().Be(42);
    }

    // ── La vista de verdad ────────────────────────────────────────────────────────────────

    [Fact]
    public Task Esc_para_en_el_acto_y_la_vista_no_tiene_enlaces_rotos() => HiloDeVentana.Ejecutar(async () =>
    {
        var errores = new StringBuilder();
        using var oyente = new OyenteDeEnlacesTx(errores);
        var (vm, piezas, _, _) = Montar();
        vm.PermitirTransmitir = true;
        var esperando = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        piezas.Reloj.AntesDeEsperar = async ct =>
        {
            esperando.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
        };

        var (ventana, vista) = Pintar(vm, 1300);
        try
        {
            await Asentar();
            errores.ToString().Should().BeEmpty("cada enlace roto es un control o un dato muerto");
            Todos<Button>(vista).Where(b => b.Command is null && b.TemplatedParent is null).Should().BeEmpty("cada boton lleva su orden");

            // F3 desde el teclado, como en N1MM (con un indicativo puesto).
            piezas.Entrada.Indicativo = "DL1ABC";
            Teclear(ventana, Key.F3);
            await esperando.Task.WaitAsync(TimeSpan.FromSeconds(5));
            vm.Emisor.Enviando.Should().BeTrue();
            piezas.Equipo.Estado.Transmitiendo.Should().BeTrue();

            Teclear(ventana, Key.Escape);
            var reloj = Stopwatch.StartNew();
            while ((vm.Emisor.Enviando || vm.Aviso.Length == 0) && reloj.Elapsed < TimeSpan.FromSeconds(5)) await Asentar();
            vm.Emisor.Enviando.Should().BeFalse();
            piezas.Equipo.Estado.Transmitiendo.Should().BeFalse("Esc baja el PTT");
            vm.Aviso.Should().Be(Textos.T("Cabina.TxCw.Parado"));

            // El editor tampoco tiene enlaces rotos.
            vm.EditandoMacros = true;
            await Asentar();
            errores.ToString().Should().BeEmpty();
        }
        finally
        {
            ventana.Close();
        }
    });

    [Fact]
    public Task Capturas() => HiloDeVentana.Ejecutar(async () =>
    {
        if (Environment.GetEnvironmentVariable("CUADERNO_CAPTURAS_TXCW") is not { Length: > 0 } capturas) return;

        foreach (var (tema, sufijo) in new[] { ("Tema.Oscuro", string.Empty), ("Tema.Claro", "-claro") })
        {
            using var conTema = new ConTema(tema);

            // 1) En plena secuencia: el corresponsal ya contesto y sale el informe.
            var (vm, piezas, _, _) = Montar();
            vm.PermitirTransmitir = true;
            vm.MiNombre = "JOSE";
            vm.MiQth = "TENERIFE";
            var esperando = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await vm.IntroAsync();
            foreach (var p in "EA8DLF DE DL1ABC DL1ABC K".Split(' ')) vm.Oir(p);
            piezas.Reloj.AntesDeEsperar = async ct =>
            {
                esperando.TrySetResult();
                await Task.Delay(Timeout.Infinite, ct);
            };
            var envio = vm.IntroAsync();
            await esperando.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var (ventana, vista) = Pintar(vm, 1340);
            try
            {
                await Asentar();
                await Asentar();
                Retratar(vista, Path.Combine(capturas, $"txcw-secuencia{sufijo}.png"));
            }
            finally
            {
                await vm.PararAsync();
                await envio;
                ventana.Close();
            }

            // 2) El editor de macros, con el pestillo cerrado.
            var (vm2, _, _, _) = Montar();
            vm2.EditandoMacros = true;
            var (ventana2, vista2) = Pintar(vm2, 1340);
            try
            {
                await Asentar();
                Retratar(vista2, Path.Combine(capturas, $"txcw-editor{sufijo}.png"));
            }
            finally
            {
                ventana2.Close();
            }
        }
    });

    // ── Utilidades ────────────────────────────────────────────────────────────────────────

    private static void Teclear(Window ventana, Key tecla)
    {
        var fuente = PresentationSource.FromVisual(ventana)!;
        ventana.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, fuente, 0, tecla) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
    }

    private static (Window Ventana, MacrosCw Vista) Pintar(VistaModeloTransmisionCw modelo, double ancho)
    {
        var vista = new MacrosCw { DataContext = modelo, Margin = new Thickness(8) };
        var borde = new Border { Child = vista, Padding = new Thickness(4), VerticalAlignment = VerticalAlignment.Top };
        borde.SetResourceReference(Border.BackgroundProperty, "FondoPanel");
        var ventana = new Window
        {
            Content = borde,
            Width = ancho,
            SizeToContent = SizeToContent.Height,
            WindowStyle = WindowStyle.None,
            ShowActivated = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -10000,
            Top = 0,
        };
        ventana.SetResourceReference(TextElement.ForegroundProperty, "Texto");
        ventana.Show();
        ventana.UpdateLayout();
        return (ventana, vista);
    }

    private static async Task Asentar()
    {
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
    }

    private static IEnumerable<T> Todos<T>(DependencyObject raiz) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(raiz); i++)
        {
            var hijo = VisualTreeHelper.GetChild(raiz, i);
            if (hijo is T t) yield return t;
            foreach (var nieto in Todos<T>(hijo)) yield return nieto;
        }
    }

    private static void Retratar(FrameworkElement elemento, string ruta)
    {
        var raiz = (FrameworkElement)VisualTreeHelper.GetParent(elemento);
        var ancho = (int)Math.Ceiling(raiz.ActualWidth);
        var alto = (int)Math.Ceiling(raiz.ActualHeight);
        var dibujo = new DrawingVisual();
        using (var lienzo = dibujo.RenderOpen())
        {
            lienzo.DrawRectangle((Brush)Application.Current.Resources["FondoPanel"], null, new Rect(0, 0, ancho, alto));
            lienzo.DrawRectangle(new VisualBrush(raiz) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top }, null, new Rect(0, 0, ancho, alto));
        }

        var imagen = new RenderTargetBitmap(ancho, alto, 96, 96, PixelFormats.Pbgra32);
        imagen.Render(dibujo);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(imagen));
        Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);
        using var fichero = File.Create(ruta);
        png.Save(fichero);
    }

    private sealed class ConTema : IDisposable
    {
        private readonly int _indice = -1;
        private readonly ResourceDictionary? _antes;

        public ConTema(string tema)
        {
            var diccionarios = Application.Current.Resources.MergedDictionaries;
            for (var i = 0; i < diccionarios.Count; i++)
            {
                if (!diccionarios[i].Contains("FondoVentana") || !diccionarios[i].Contains("Acento")) continue;
                _indice = i;
                _antes = diccionarios[i];
                diccionarios[i] = (ResourceDictionary)Application.LoadComponent(new Uri($"/Nodisla.Cuaderno.Ui;component/Recursos/{tema}.xaml", UriKind.Relative));
                break;
            }
        }

        public void Dispose()
        {
            if (_indice >= 0 && _antes is not null) Application.Current.Resources.MergedDictionaries[_indice] = _antes;
        }
    }

    private sealed class OyenteDeEnlacesTx : TraceListener
    {
        private readonly StringBuilder _errores;

        public OyenteDeEnlacesTx(StringBuilder errores)
        {
            _errores = errores;
            PresentationTraceSources.Refresh();
            PresentationTraceSources.DataBindingSource.Listeners.Add(this);
            PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
        }

        public override void Write(string? message) => _errores.Append(message);

        public override void WriteLine(string? message) => _errores.AppendLine(message);

        protected override void Dispose(bool disposing)
        {
            PresentationTraceSources.DataBindingSource.Listeners.Remove(this);
            base.Dispose(disposing);
        }
    }
}
