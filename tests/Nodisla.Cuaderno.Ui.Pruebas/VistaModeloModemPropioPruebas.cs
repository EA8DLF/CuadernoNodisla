using System.Collections.Specialized;
using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.CasosDeUso;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Ui.Ajustes;
using Nodisla.Cuaderno.Ui.Desarrollo;
using Nodisla.Cuaderno.Ui.VistaModelos;

namespace Nodisla.Cuaderno.Ui.Pruebas;

/// <summary>
/// Pruebas del panel del modem propio.
/// </summary>
/// <remarks>
/// <para>
/// No abren ninguna ventana ni ninguna tarjeta de sonido: se prueba lo que hay <b>detras</b>
/// de la pantalla, alimentando el modelo de vista con un modem de mentira.
/// </para>
/// <para>
/// <b>Aqui no se transmite.</b> El modem de mentira se niega a emitir, igual que el de verdad
/// cuando no tiene salida de audio, y ademas la confirmacion de transmision se sustituye por
/// una que siempre dice que no. Una suite que pudiera poner un equipo en antena seria una
/// suite que algun dia lo pone.
/// </para>
/// </remarks>
public sealed class VistaModeloModemPropioPruebas
{
    /// <summary>Lo que se espera como mucho a que llegue una señal. No es una medida de tiempo.</summary>
    private static readonly TimeSpan Paciencia = TimeSpan.FromSeconds(10);

    [Fact]
    public void SinModemNoSePuedeEscuchar()
    {
        var modelo = Montar(out _, conModem: false);

        modelo.HayModem.Should().BeFalse();
        modelo.EscucharCommand.CanExecute(null).Should().BeFalse();
        modelo.EstadoTexto.Should().Contain("no está montado");
    }

    [Fact]
    public void ElPestilloDeLaTransmisionEmpiezaCerrado()
    {
        var modelo = Montar(out _);

        modelo.PermitirTransmitir.Should().BeFalse();
        modelo.SePuedeEmitir.Should().BeFalse();
        modelo.EmitirCommand.CanExecute(null).Should().BeFalse();

        // Antes de escuchar, el motivo mas urgente es que la salida ni siquiera esta abierta
        // todavia; el pestillo se comprueba una vez que se puede escuchar.
        modelo.AvisoDeLaTransmision.Should().Contain("Escuchar");
    }

    [Fact]
    public async Task ConLaSalidaAbiertaElPestilloSigueCerradoPorDefecto()
    {
        var modelo = Montar(out _);
        await modelo.EscucharCommand.ExecuteAsync(null);

        modelo.PermitirTransmitir.Should().BeFalse();
        modelo.SePuedeEmitir.Should().BeFalse();
        modelo.EmitirCommand.CanExecute(null).Should().BeFalse();
        modelo.AvisoDeLaTransmision.Should().Contain("pestillo");
    }

    [Fact]
    public async Task EscucharAbreLaSalidaYSinEllaNoSePuedeEmitirAunqueElPestilloEsteAbierto()
    {
        var modelo = Montar(out _);
        modelo.PermitirTransmitir = true;

        // El fallo real: antes de arreglarlo, «Emitir» se dejaba pulsar con el pestillo abierto
        // aunque la salida de audio no se hubiera abierto nunca, y reventaba con «No hay ningún
        // dispositivo de salida abierto» al primer intento.
        modelo.SePuedeEmitir.Should().BeFalse("la salida todavía no se ha abierto");

        await modelo.EscucharCommand.ExecuteAsync(null);

        modelo.SePuedeEmitir.Should().BeTrue("escuchar abre también la salida, no solo la entrada");

        await modelo.PararCommand.ExecuteAsync(null);

        modelo.SePuedeEmitir.Should().BeFalse("al parar se suelta también la salida");
    }

    [Fact]
    public async Task ApagarElModoImpideEmitirYSueltaLaSalida()
    {
        var modelo = Montar(out _);
        modelo.PermitirTransmitir = true;
        await modelo.EscucharCommand.ExecuteAsync(null);
        modelo.SePuedeEmitir.Should().BeTrue("antes de apagar el modo, todo lo demas ya lo permite");

        modelo.ModoApagado = true;

        modelo.SePuedeEmitir.Should().BeFalse("el modo apagado tiene que ganar a todo lo demas");
        modelo.EmitirCommand.CanExecute(null).Should().BeFalse();
        modelo.EscucharCommand.CanExecute(null).Should().BeFalse("apagado no se puede volver a escuchar");
        modelo.AvisoDeLaTransmision.Should().Contain("apagado");

        // Apagar el modo no solo bloquea el boton: suelta de verdad la salida compartida, para
        // no disputarla con RTTY mientras esta "apagado" pero sigue escuchando por detras.
        // OnModoApagadoChanged dispara el parón en segundo plano (como EmitirSiEsSuVentanaAsync),
        // así que se espera un poco a que termine en vez de comprobarlo en el acto.
        for (var intentos = 0; modelo.Escuchando && intentos < 100; intentos++)
        {
            await Task.Delay(20);
        }

        modelo.Escuchando.Should().BeFalse("apagar el modo para de verdad el modem, no solo bloquea el boton");
    }

    [Fact]
    public void ElModoApagadoEmpiezaEncendido()
    {
        var modelo = Montar(out _);

        modelo.ModoApagado.Should().BeFalse("apagar un modo es una decision explicita del operador, nunca el valor de fabrica");
    }

    [Fact]
    public void SinLaTablaDelCorrectorSeDice()
    {
        var modelo = Montar(out _, corrector: new EstadoDelCorrector(false, "código de pruebas"));

        modelo.SinLaTablaDelCorrector.Should().BeTrue();
        modelo.ProcedenciaDeLasTablas.Should().Be("código de pruebas");
    }

    [Fact]
    public void ConLaTablaDeVerdadNoSeDiceNada()
    {
        var modelo = Montar(out _, corrector: new EstadoDelCorrector(true, "tablas-ft8.txt"));

        modelo.SinLaTablaDelCorrector.Should().BeFalse();
    }

    [Fact]
    public async Task LasDecodificacionesLleganALaListaYLasRescatadasVanMarcadas()
    {
        var modelo = Montar(out var modem);

        var llegaron = await EsperarADecodificaciones(modelo, esperadas: 2, () => modem.Soltar(
            Decodificacion("CQ EA5XYZ IM98", -7, 520, profunda: false),
            Decodificacion("EA8DLF PY2ZZZ -14", -23, 2310, profunda: true)));

        llegaron.Should().BeTrue("las decodificaciones tienen que llegar a la lista");
        modelo.Decodificaciones.Should().HaveCount(2);
        modelo.Decodificaciones.Should().ContainSingle(f => f.EsRescatada);
        modelo.Decodificaciones.Single(f => f.EsRescatada).Texto.Should().Be("EA8DLF PY2ZZZ -14");
    }

    [Fact]
    public async Task SoloSeResaltaLoQueVaDirigidoAUnoMismo()
    {
        var modelo = Montar(out var modem);
        modelo.MiIndicativo = Indicativo.Parse("EA8DLF");

        await EsperarADecodificaciones(modelo, esperadas: 2, () => modem.Soltar(

            // Dirigido a otro: en FT8 casi todo va dirigido a alguien, asi que esto NO puede
            // resaltarse o la lista entera quedaria resaltada, que es no resaltar nada.
            Decodificacion("K1ABC W9XYZ EN37", -9, 900, profunda: false, llamado: "K1ABC"),
            Decodificacion("EA8DLF PY2ZZZ -14", -15, 1400, profunda: false, llamado: "EA8DLF")));

        modelo.Decodificaciones.Should().ContainSingle(f => f.MeLlaman);
        modelo.Decodificaciones.Single(f => f.MeLlaman).Texto.Should().StartWith("EA8DLF");
        modelo.Decodificaciones.Should().OnlyContain(f => f.EsDirigido);
    }

    [Fact]
    public async Task ElDobleClicPreparaLaRespuestaYNoTransmiteNada()
    {
        var modelo = Montar(out var modem);

        await EsperarADecodificaciones(modelo, esperadas: 1, () => modem.Soltar(
            Decodificacion("CQ EA5XYZ IM98", -7, 520, profunda: false)));

        modelo.DecodificacionElegida = modelo.Decodificaciones[0];
        modelo.PrepararRespuestaCommand.Execute(null);

        modelo.Corresponsal.Should().Be("EA5XYZ");
        modelo.LocalizadorDelCorresponsal.Should().Be("IM98");
        modelo.InformeRecibido.Should().Be("-07");
        modelo.TonoDeTransmision.Should().Be(520);
        modelo.HayContactoEnCurso.Should().BeTrue();

        // Preparar no saca nada al aire, y el pestillo sigue cerrado.
        modem.Emisiones.Should().BeEmpty();
        modelo.PermitirTransmitir.Should().BeFalse();
    }

    [Fact]
    public async Task ElContactoEntraEnElCuadernoConSuFrecuenciaYSuModo()
    {
        var modelo = Montar(out _, out var cuaderno);
        modelo.PonerElDial(Frecuencia.DesdeMegahercios(14.074m));

        modelo.Corresponsal = "EA5XYZ";
        modelo.LocalizadorDelCorresponsal = "IM98";
        modelo.InformeEnviado = "-07";
        modelo.InformeRecibido = "-12";
        modelo.TonoDeTransmision = 1500;

        await modelo.RegistrarContactoCommand.ExecuteAsync(null);

        var guardados = await cuaderno.BuscarAsync(new CriterioQso(), 0, 10);
        guardados.Elementos.Should().ContainSingle();

        var qso = guardados.Elementos[0];
        qso.Call.Valor.Should().Be("EA5XYZ");
        qso.Gridsquare.Valor.Should().Be("IM98");

        // FT8 es modo PRINCIPAL en ADIF, no submodo de nada.
        qso.Mode.Principal.Should().Be("FT8");
        qso.Mode.Submodo.Should().BeNull();

        // El dial mas el tono de audio: si no, todos los contactos de la tarde quedarian en
        // la misma frecuencia y no se podria distinguir ninguno.
        qso.Freq.Hercios.Should().Be(14_075_500);
        qso.Band.EsVacia.Should().BeFalse();
        qso.Band.Nombre.Should().Be("20m");
        qso.RstSent.Texto.Should().Be("-07");
        qso.RstRcvd.Texto.Should().Be("-12");
        qso.Origen.Should().Be("módem propio");
    }

    [Fact]
    public async Task EnFt4ElModoEsSubmodoDeMfsk()
    {
        var modelo = Montar(out _, out var cuaderno);
        modelo.PonerElDial(Frecuencia.DesdeMegahercios(14.080m));
        modelo.EsFt4 = true;

        modelo.Corresponsal = "EA5XYZ";
        modelo.TonoDeTransmision = 1200;

        await modelo.RegistrarContactoCommand.ExecuteAsync(null);

        var guardados = await cuaderno.BuscarAsync(new CriterioQso(), 0, 10);
        guardados.Elementos.Should().ContainSingle();

        // FT4 NO es modo principal: en ADIF es submodo de MFSK. Ponerlo al reves deja el
        // contacto fuera de los diplomas que cuentan por modo.
        guardados.Elementos[0].Mode.Principal.Should().Be("MFSK");
        guardados.Elementos[0].Mode.Submodo.Should().Be("FT4");
    }

    [Fact]
    public async Task ConElPestilloAbiertoSiguePreguntandoseAntesDeTransmitir()
    {
        var modelo = Montar(out var modem);

        // Hace falta escuchar antes de poder emitir: es «Escuchar» quien abre la salida de
        // audio (con la de mentira del montaje, sin tocar ninguna tarjeta), y sin salida
        // abierta el pestillo no basta para que el botón de emitir se habilite.
        await modelo.EscucharCommand.ExecuteAsync(null);

        modelo.MensajeAEmitir = "EA5XYZ EA8DLF -07";
        modelo.PermitirTransmitir = true;

        // La confirmacion dice que no, que es lo que tiene que decir en pruebas.
        var preguntado = false;
        modelo.ConfirmarQueVaATransmitir = _ =>
        {
            preguntado = true;
            return false;
        };

        modelo.EmitirCommand.CanExecute(null).Should().BeTrue();
        modelo.EmitirCommand.Execute(null);

        preguntado.Should().BeTrue("antes de poner el equipo en antena hay que preguntar");
        modem.Emisiones.Should().BeEmpty("si se dice que no, no se transmite");
    }

    [Fact]
    public async Task ConElRelojFueraDeVentanaNoSeTransmite()
    {
        var reloj = new RelojSimulado();
        var modelo = Montar(out var modem, out _, reloj: reloj);

        // Con la salida abierta (la de mentira): así lo que frena es el reloj, no la salida.
        await modelo.EscucharCommand.ExecuteAsync(null);

        modelo.MensajeAEmitir = "EA5XYZ EA8DLF -07";
        modelo.PermitirTransmitir = true;
        modelo.ConfirmarQueVaATransmitir = _ => true;

        // El reloj simulado arranca con el desvio de verdad de esta maquina: -1,29 s.
        modelo.Reloj.RelojFueraDeVentana.Should().BeTrue();

        modelo.EmitirCommand.Execute(null);

        modem.Emisiones.Should().BeEmpty("con el reloj asi se transmitiria fuera de ventana");
        modelo.Aviso.Should().Contain("fuera de ventana");
    }

    private static DecodificacionPropia Decodificacion(
        string texto, int db, int tono, bool profunda, string? llamado = null)
    {
        var partes = texto.Split(' ');
        var esCq = partes[0] == "CQ";
        var llamante = Indicativo.TryParse(partes[1], out var quien) ? quien : Indicativo.Vacio;

        return new DecodificacionPropia(texto, db, 0.2, tono, ModoDelModem.Ft8, DateTimeOffset.UtcNow)
        {
            Llamante = llamante,
            Llamado = Indicativo.TryParse(llamado, out var aQuien) ? aQuien : Indicativo.Vacio,
            Locator = Locator.TryParse(partes[^1], out var rejilla) ? rejilla : Locator.Vacio,
            EsCq = esCq,
            EsRecuperacionProfunda = profunda,
        };
    }

    /// <summary>
    /// Espera a que la lista se mueva, sobre una señal y no sobre un reloj de pared.
    /// </summary>
    private static async Task<bool> EsperarADecodificaciones(
        VistaModeloModemPropio modelo, int esperadas, Action soltar)
    {
        var aviso = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        void AlCambiar(object? _, NotifyCollectionChangedEventArgs __)
        {
            if (modelo.Decodificaciones.Count >= esperadas) aviso.TrySetResult();
        }

        modelo.Decodificaciones.CollectionChanged += AlCambiar;
        try
        {
            soltar();
            await aviso.Task.WaitAsync(Paciencia);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
        finally
        {
            modelo.Decodificaciones.CollectionChanged -= AlCambiar;
        }
    }

    /// <summary>Espera a que una ventana pase entera por el modelo, sobre su propio aviso.</summary>
    private static async Task EsperarAVentana(VistaModeloModemPropio modelo, Action soltar)
    {
        var aviso = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void AlProcesar(object? _, DateTimeOffset __) => aviso.TrySetResult();

        modelo.VentanaProcesada += AlProcesar;
        try
        {
            soltar();
            await aviso.Task.WaitAsync(Paciencia);
        }
        finally
        {
            modelo.VentanaProcesada -= AlProcesar;
        }
    }

    private static VistaModeloModemPropio Montar(
        out ModemDeMentira modem,
        bool conModem = true,
        EstadoDelCorrector? corrector = null) =>
        Montar(out modem, out _, conModem: conModem, corrector: corrector);

    private static VistaModeloModemPropio Montar(
        out ModemDeMentira modem,
        out RepositorioQsoEnMemoria cuaderno,
        bool conModem = true,
        EstadoDelCorrector? corrector = null,
        IRelojDelModem? reloj = null)
    {
        modem = new ModemDeMentira();
        cuaderno = new RepositorioQsoEnMemoria([]);

        var modelo = new VistaModeloModemPropio(
            new VistaModeloRelojDigital(reloj ?? new RelojParado()),
            new AjustesDelPrograma(),
            new ConsultarTrabajadoAntes(cuaderno),
            new RegistrarQso(cuaderno, new RepositorioEstacionEnMemoria()),
            corrector ?? EstadoDelCorrector.NoProcede,
            conModem ? modem : null,

            // Con salida de audio de mentira: es lo que permite comprobar que el camino de la
            // transmision esta cerrado por el pestillo y por la confirmacion, y no por un
            // descuido de montaje.
            entrada: null,
            salida: new SalidaDeAudioSimulada())
        {
            // Que la suite no pueda transmitir ni diciendo que si.
            ConfirmarQueVaATransmitir = _ => false,
        };

        return modelo;
    }

    /// <summary>Un reloj que siempre dice que esta en hora y nunca sale a la red.</summary>
    private sealed class RelojParado : IRelojDelModem
    {
        private readonly DesvioDelReloj _desvio = new(5, "pruebas", DateTimeOffset.UnixEpoch, EsFiable: true);

        public DateTimeOffset Ahora => DateTimeOffset.UnixEpoch;

        public DesvioDelReloj Desvio => _desvio;

        public EstadoDelReloj Estado => new(_desvio, CalidadDelReloj.Bien, "En hora.", string.Empty, "+5 ms");

        public event EventHandler<EstadoDelReloj>? DesvioMedido;

        public Task<DesvioDelReloj> MedirAsync(bool forzar = false, CancellationToken ct = default)
        {
            DesvioMedido?.Invoke(this, Estado);
            return Task.FromResult(_desvio);
        }

        public DateTimeOffset ProximaVentana(TimeSpan periodo) => Ahora + periodo;
    }

    /// <summary>
    /// Un modem que apunta lo que le piden emitir y <b>no emite nada</b>.
    /// </summary>
    /// <remarks>
    /// Que apunte en vez de transmitir es justo lo que permite comprobar en la suite que el
    /// camino de la transmision esta bien cerrado sin poner ningun equipo en antena.
    /// </remarks>
    private sealed class ModemDeMentira : IModemPropio
    {
        public List<string> Emisiones { get; } = [];

        public ModoDelModem Modo { get; private set; } = ModoDelModem.Ft8;

        public bool EstaEscuchando { get; private set; }

        public bool EstaEmitiendo => false;

        public Frecuencia FrecuenciaDelDial { get; set; }

        public event EventHandler<ColumnaDeCascada>? CascadaActualizada;

        public event EventHandler<VentanaDecodificada>? VentanaLista;

        public void Soltar(params DecodificacionPropia[] decodificaciones) =>
            VentanaLista?.Invoke(this, new VentanaDecodificada(
                DateTimeOffset.UtcNow, decodificaciones, TimeSpan.FromSeconds(1), -118));

        public void SoltarColumna(ColumnaDeCascada columna) => CascadaActualizada?.Invoke(this, columna);

        public Task EscucharAsync(ModoDelModem modo, CancellationToken ct = default)
        {
            Modo = modo;
            EstaEscuchando = true;
            return Task.CompletedTask;
        }

        public Task PararAsync(CancellationToken ct = default)
        {
            EstaEscuchando = false;
            return Task.CompletedTask;
        }

        public Task EmitirAsync(string texto, int tonoHz, CancellationToken ct = default)
        {
            Emisiones.Add(texto);
            return Task.CompletedTask;
        }

        public Task AbortarEmisionAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<IReadOnlyList<DecodificacionPropia>> DecodificarFicheroAsync(
            string rutaWav, ModoDelModem modo, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<DecodificacionPropia>>([]);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
