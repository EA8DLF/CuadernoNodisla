using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Ui.Desarrollo;
using Nodisla.Cuaderno.Ui.VistaModelos;
using static Nodisla.Cuaderno.Ui.Pruebas.MontajeDeLaPestanaDigital;

namespace Nodisla.Cuaderno.Ui.Pruebas;

/// <summary>
/// Cada orden de la pestaña Digital, ejecutada con modem, salida y reloj de mentira, y
/// comprobado su efecto. La hora la pone la prueba: ni un reloj de pared.
/// </summary>
/// <remarks>
/// <b>Aqui no se transmite.</b> El modem apunta lo que le piden y a que hora; la salida es la
/// simulada. Donde la prueba contesta «si» a la pregunta de transmitir es para comprobar que el
/// camino al aire sale en su ventana, y lo que «sale» es una linea en una lista.
/// </remarks>
public sealed class PestanaDigitalOrdenesPruebas
{
    private static readonly TimeSpan Paciencia = TimeSpan.FromSeconds(10);

    private static DateTimeOffset T(int segundos, int decimas = 0) =>
        Mediodia.AddSeconds(segundos).AddMilliseconds(decimas * 100);

    [Fact]
    public async Task UnContactoEnteroSaleSiempreEnLasVentanasPropias()
    {
        var modelo = await Listo(out var modem, out var reloj, out var cuaderno);

        // 12:00:00 (par) llama EA5XYZ; la decodificacion llega a las 12:00:16, ya en la impar.
        reloj.Ahora = T(16);
        await Ventana(modelo, () => modem.Soltar(T(0), Oido("CQ EA5XYZ IM98", -7, 800, T(0))));
        modelo.DecodificacionElegida = modelo.Decodificaciones.Single(f => f.Texto.StartsWith("CQ", StringComparison.Ordinal));
        await modelo.PrepararRespuestaCommand.ExecuteAsync(null);

        modelo.TxHabilitado.Should().BeTrue();
        modem.Emisiones.Should().ContainSingle();
        modem.Emisiones[0].Texto.Should().StartWith("EA5XYZ EA8DLF");
        modem.Emisiones[0].Cuando.Should().Be(T(16));

        // La ventana propia (12:00:15) no decide nada.
        reloj.Ahora = T(31);
        await Ventana(modelo, () => modem.Soltar(T(15)));
        modem.Emisiones.Should().HaveCount(1);

        // Contesta con informe: Tx3 (R-informe) en la siguiente propia.
        reloj.Ahora = T(46);
        await Ventana(modelo, () => modem.Soltar(T(30), Oido("EA8DLF EA5XYZ -12", -9, 800, T(30))));
        modem.Emisiones.Should().HaveCount(2);
        modem.Emisiones[1].Texto.Should().Contain("R-");
        modelo.InformeRecibido.Should().Be("-12");

        reloj.Ahora = T(61);
        await Ventana(modelo, () => modem.Soltar(T(45)));

        // RR73: Tx5 (73) y se acabo.
        reloj.Ahora = T(76);
        await Ventana(modelo, () => modem.Soltar(T(60), Oido("EA8DLF EA5XYZ RR73", -8, 800, T(60))));
        modem.Emisiones.Should().HaveCount(3);
        modem.Emisiones[2].Texto.Should().EndWith("73");
        modelo.ContactoCompleto.Should().BeTrue();
        modelo.TxHabilitado.Should().BeFalse("tras el 73 la secuencia se para");

        // Todas en ventana impar y dentro de los dos primeros segundos.
        modem.Emisiones.Should().OnlyContain(e => (e.Cuando - Mediodia).TotalSeconds % 30 >= 15 && (e.Cuando - Mediodia).TotalSeconds % 30 <= 17);

        // Sin dial el guardado solo no puede poner banda y falla; el botón sigue valiendo.
        await modelo.GuardadoEnCurso;
        modelo.PonerElDial(Frecuencia.DesdeMegahercios(14.074m));
        await modelo.RegistrarContactoCommand.ExecuteAsync(null);
        (await cuaderno.BuscarAsync(new CriterioQso(), 0, 10)).Elementos.Should().ContainSingle(modelo.Aviso);
    }

    [Fact]
    public async Task SiLaDecodificacionLlegaTardeSePierdeElTurnoEnVezDeSalirEncimaDeOtro()
    {
        var modelo = await Listo(out var modem, out var reloj, out _);
        reloj.Ahora = T(16);
        await Ventana(modelo, () => modem.Soltar(T(0), Oido("CQ EA5XYZ IM98", -7, 800, T(0))));
        modelo.DecodificacionElegida = modelo.Decodificaciones[0];
        await modelo.PrepararRespuestaCommand.ExecuteAsync(null);
        modem.Emisiones.Should().HaveCount(1);

        // Contesta, pero el decodificador tarda 3,5 s: fuera de tiempo, no se sale.
        reloj.Ahora = T(48, 5);
        await Ventana(modelo, () => modem.Soltar(T(30), Oido("EA8DLF EA5XYZ -12", -9, 800, T(30))));
        modem.Emisiones.Should().HaveCount(1);
    }

    [Fact]
    public async Task ContestarAMediaVentanaDelCorresponsalNoSaleEnElActo()
    {
        var modelo = await Listo(out var modem, out var reloj, out _);
        reloj.Ahora = T(16);
        await Ventana(modelo, () => modem.Soltar(T(0), Oido("CQ EA5XYZ IM98", -7, 800, T(0))));

        // El operador tarda y hace doble clic a las 12:00:35, en la ventana par: la del DX.
        reloj.Ahora = T(35);
        modelo.DecodificacionElegida = modelo.Decodificaciones[0];
        await modelo.PrepararRespuestaCommand.ExecuteAsync(null);
        modem.Emisiones.Should().BeEmpty("antes salia en el acto, encima del corresponsal");
        modelo.Aviso.Should().Contain("12:00:45");

        reloj.Ahora = T(46);
        await Ventana(modelo, () => modem.Soltar(T(30)));
        modem.Emisiones.Should().ContainSingle().Which.Cuando.Should().Be(T(46));
    }

    [Fact]
    public async Task LlamarCqEsperaAlPrincipioDeLaVentana()
    {
        var modelo = await Listo(out var modem, out var reloj, out _);
        reloj.Ahora = T(5);

        await modelo.LlamarCqCommand.ExecuteAsync(null);

        modem.Emisiones.Should().BeEmpty();
        modelo.Aviso.Should().Contain("12:00:15");
        modelo.Secuenciador.Activo.Should().BeTrue();

        reloj.Ahora = T(16);
        await Ventana(modelo, () => modem.Soltar(T(0)));
        modem.Emisiones.Should().ContainSingle().Which.Texto.Should().StartWith("CQ EA8DLF");
    }

    [Fact]
    public async Task TxHabilitadoPasaPorElPestilloYLaPreguntaYArrancaLaSecuencia()
    {
        var modelo = MontarSolo(out var modem, out var reloj);
        await modelo.EscucharCommand.ExecuteAsync(null);

        await modelo.AlternarTxHabilitadoCommand.ExecuteAsync(null);
        modelo.TxHabilitado.Should().BeFalse("con el pestillo cerrado no se enciende");

        modelo.PermitirTransmitir = true;
        await modelo.AlternarTxHabilitadoCommand.ExecuteAsync(null);
        modelo.TxHabilitado.Should().BeFalse("si se dice que no a la pregunta, no se enciende");

        modelo.ConfirmarQueVaATransmitir = _ => true;
        reloj.Ahora = T(7);
        await modelo.AlternarTxHabilitadoCommand.ExecuteAsync(null);
        modelo.TxHabilitado.Should().BeTrue();
        modelo.Secuenciador.Activo.Should().BeTrue("encenderla sin contacto pone en marcha el mensaje marcado (Tx6)");
        modem.Emisiones.Should().BeEmpty("a media ventana no se sale");

        await modelo.AlternarTxHabilitadoCommand.ExecuteAsync(null);
        modelo.TxHabilitado.Should().BeFalse();
    }

    [Fact]
    public async Task DetenerCortaLaEmisionEnCursoYAnulaElMensajeLibrePendiente()
    {
        var modelo = await Listo(out var modem, out var reloj, out _);
        reloj.Ahora = T(7);
        modelo.MensajeAEmitir = "CQ TEST EA8DLF";
        await modelo.EmitirCommand.ExecuteAsync(null);
        modem.Emisiones.Should().BeEmpty("a media ventana se deja para la siguiente");
        modelo.Aviso.Should().Contain("12:00:15");

        modem.EstaEmitiendo = true;
        await modelo.DetenerTxCommand.ExecuteAsync(null);
        modem.Abortos.Should().Be(1);
        modelo.Secuenciador.Activo.Should().BeFalse();

        reloj.Ahora = T(16);
        await Ventana(modelo, () => modem.Soltar(T(0)));
        modem.Emisiones.Should().BeEmpty("el libre pendiente se anulo al detener");
    }

    [Fact]
    public async Task ElMensajeLibreSaleAlEmpezarLaVentanaSiguiente()
    {
        var modelo = await Listo(out var modem, out var reloj, out _);
        reloj.Ahora = T(7);
        modelo.MensajeAEmitir = "CQ TEST EA8DLF";
        await modelo.EmitirCommand.ExecuteAsync(null);

        reloj.Ahora = T(16);
        await Ventana(modelo, () => modem.Soltar(T(0)));
        modem.Emisiones.Should().ContainSingle().Which.Should().Be(("CQ TEST EA8DLF", T(16)));
    }

    [Fact]
    public async Task CortarYSoltarDiceLaVerdadYElAvisoSeCierra()
    {
        var modelo = await Listo(out var modem, out _, out _);

        await modelo.AbortarEmisionCommand.ExecuteAsync(null);
        modem.Abortos.Should().Be(1, "el PTT se suelta siempre, por si acaso");
        modelo.Aviso.Should().Contain("No había emisión");

        modem.EstaEmitiendo = true;
        await modelo.AbortarEmisionCommand.ExecuteAsync(null);
        modelo.Aviso.Should().Be("Emisión cortada y PTT soltado.");

        modelo.CerrarAvisoCommand.Execute(null);
        modelo.Aviso.Should().BeEmpty();
        modelo.HayAviso.Should().BeFalse();
    }

    [Fact]
    public async Task ElegirElSiguienteAMedioContactoMandaSobreLaSecuencia()
    {
        var modelo = await Listo(out var modem, out var reloj, out _);
        reloj.Ahora = T(16);
        await Ventana(modelo, () => modem.Soltar(T(0), Oido("CQ EA5XYZ IM98", -7, 800, T(0))));
        modelo.DecodificacionElegida = modelo.Decodificaciones[0];
        await modelo.PrepararRespuestaCommand.ExecuteAsync(null);

        modelo.ElegirTxCommand.Execute(modelo.Mensajes[1]);
        modelo.TxSiguiente.Should().Be(2);
        modelo.Mensajes.Single(m => m.EsElSiguiente).Numero.Should().Be(2);

        reloj.Ahora = T(46);
        await Ventana(modelo, () => modem.Soltar(T(30)));
        modem.Emisiones[^1].Texto.Should().Be(modelo.Mensajes[1].Texto);
    }

    [Fact]
    public async Task SinSecuenciaAutomaticaSeRepiteElMarcado()
    {
        var modelo = await Listo(out var modem, out var reloj, out _);
        modelo.SecuenciaAutomatica = false;
        reloj.Ahora = T(16);
        await Ventana(modelo, () => modem.Soltar(T(0), Oido("CQ EA5XYZ IM98", -7, 800, T(0))));
        modelo.DecodificacionElegida = modelo.Decodificaciones[0];
        await modelo.PrepararRespuestaCommand.ExecuteAsync(null);

        reloj.Ahora = T(46);
        await Ventana(modelo, () => modem.Soltar(T(30), Oido("EA8DLF EA5XYZ -12", -9, 800, T(30))));

        // Antes, con la automatica apagada, no salia NADA. Ahora sale el marcado, sin avanzar.
        modem.Emisiones.Should().HaveCount(2);
        modem.Emisiones[1].Texto.Should().Be(modem.Emisiones[0].Texto);
    }

    [Fact]
    public async Task EnviarUnMensajeLoDejaParaSuVentana()
    {
        var modelo = await Listo(out var modem, out var reloj, out _);
        reloj.Ahora = T(1);

        await modelo.EnviarTxCommand.ExecuteAsync(modelo.Mensajes[5]);

        modelo.TxHabilitado.Should().BeTrue();
        modelo.TxSiguiente.Should().Be(6);
        modem.Emisiones.Should().BeEmpty("la secuencia sale en la ventana siguiente, la impar");

        reloj.Ahora = T(16);
        await Ventana(modelo, () => modem.Soltar(T(0)));
        modem.Emisiones.Should().ContainSingle();
    }

    [Fact]
    public async Task LosFiltrosVaciarYElWav()
    {
        var modelo = await Listo(out var modem, out var reloj, out _);
        reloj.Ahora = T(16);
        await Ventana(modelo, () => modem.Soltar(
            T(0),
            Oido("CQ EA5XYZ IM98", -7, 800, T(0)),
            Oido("EA8DLF PY2ZZZ GG66", -12, 1200, T(0)),
            Oido("K1ABC W9XYZ EN37", -3, 1500, T(0))));
        modelo.Decodificaciones.Should().HaveCount(3);

        modelo.SoloCq = true;
        modelo.Decodificaciones.Should().ContainSingle().Which.EsCq.Should().BeTrue();
        modelo.SoloCq = false;

        modelo.SoloMeLlaman = true;
        modelo.Decodificaciones.Should().ContainSingle().Which.MeLlaman.Should().BeTrue();
        modelo.SoloMeLlaman = false;
        modelo.Decodificaciones.Should().HaveCount(3);

        modelo.LimpiarCommand.Execute(null);
        modelo.Decodificaciones.Should().BeEmpty();

        modelo.ElegirFicheroWav = () => @"C:\no\existe.wav";
        await modelo.DecodificarFicheroCommand.ExecuteAsync(null);
        await EsperaALaVentana.DrenarAsync();
        modem.FicherosDecodificados.Should().ContainSingle();
        modelo.Decodificaciones.Should().ContainSingle();
    }

    [Fact]
    public void ElContactoSeOlvidaYLosMensajesSeGeneran()
    {
        var modelo = MontarSolo(out _, out _);

        modelo.OlvidarContactoCommand.CanExecute(null).Should().BeFalse();
        modelo.Corresponsal = "EA5XYZ";
        modelo.LocalizadorDelCorresponsal = "IM98";
        modelo.Mensajes[0].Texto.Should().Be("EA5XYZ EA8DLF IL18");
        modelo.DistanciaYAcimut.Should().Contain("km");

        modelo.Mensajes[0].Texto = "a mano";
        modelo.GenerarMensajesCommand.Execute(null);
        modelo.Mensajes[0].Texto.Should().Be("EA5XYZ EA8DLF IL18");

        modelo.OlvidarContactoCommand.CanExecute(null).Should().BeTrue();
        modelo.OlvidarContactoCommand.Execute(null);
        modelo.Corresponsal.Should().BeEmpty();
        modelo.HayContactoEnCurso.Should().BeFalse();
    }

    [Fact]
    public async Task LasFrecuenciasDeTrabajo()
    {
        var modelo = MontarSolo(out _, out _);
        var cuantas = modelo.FrecuenciasDeTrabajo.Count;
        modelo.PonerElDial(Frecuencia.DesdeMegahercios(14.074m));

        modelo.AnadirFrecuenciaCommand.Execute(null);
        modelo.FrecuenciasDeTrabajo.Should().HaveCount(cuantas + 1);
        modelo.QuitarFrecuenciaCommand.CanExecute(null).Should().BeTrue();
        modelo.QuitarFrecuenciaCommand.Execute(null);
        modelo.FrecuenciasDeTrabajo.Should().HaveCount(cuantas);

        modelo.FrecuenciasDeTrabajo.Clear();
        modelo.RestaurarFrecuenciasCommand.Execute(null);
        modelo.FrecuenciasDeTrabajo.Should().HaveCount(cuantas);
        modelo.FrecuenciaDelModoTexto.Should().Contain("20m");

        // Sin equipo, lo dice en vez de callarse.
        await modelo.IrALaFrecuenciaDelModoCommand.ExecuteAsync(null);
        modelo.Aviso.Should().Contain("No hay equipo");
    }

    [Fact]
    public void LaCascadaElModoYLosTonos()
    {
        var modelo = MontarSolo(out _, out _);

        modelo.Modos.Should().HaveCount(9);
        modelo.ModoElegido = modelo.Modos.Single(o => o.Valor == ModoDelModem.Ft4);
        modelo.Modo.Should().Be(ModoDelModem.Ft4);

        modelo.GananciaDeLaCascada = 12;
        modelo.Pintor.GananciaDb.Should().Be(12);
        modelo.AnchoVisibleHz = 9000;
        modelo.Pintor.AnchoVisibleHz.Should().Be(5000);
        modelo.PaletaElegida = modelo.Paletas[^1];
        modelo.Pintor.Paleta.Should().Be(modelo.Paletas[^1].Valor);

        modelo.MantenerTx = true;
        modelo.TonoDeTransmision = 1500;
        modelo.SintonizarEn(900);
        modelo.TonoDeRecepcion.Should().Be(900);
        modelo.TonoDeTransmision.Should().Be(1500, "con «Mantener Tx» la transmisión no sigue al clic");
    }

    [Fact]
    public async Task LosBotonesDelReloj()
    {
        var sincronizador = new SincronizadorDeMentira();
        var modelo = Montar(out _, out var reloj, out _, sincronizador);

        await modelo.Reloj.MedirCommand.ExecuteAsync(null);
        reloj.Mediciones.Should().Be(1);

        modelo.Reloj.PonerEnHoraCommand.CanExecute(null).Should().BeTrue();
        await modelo.Reloj.PonerEnHoraCommand.ExecuteAsync(null);
        sincronizador.PuestasEnHora.Should().Be(1);
        modelo.Reloj.InstruccionesParaHacerloAMano.Should().BeEmpty("salió bien: sobran las órdenes de w32tm");

        sincronizador.Contestar = false;
        await modelo.Reloj.ConfigurarElServicioCommand.ExecuteAsync(null);
        sincronizador.Configuraciones.Should().Be(1);
        modelo.Reloj.InstruccionesParaHacerloAMano.Should().NotBeEmpty("si no se pudo, se dice qué teclear");
    }

    // ── Montaje ───────────────────────────────────────────────────────────

    private static VistaModeloModemPropio MontarSolo(out ModemApuntador modem, out RelojManual reloj) =>
        Montar(out modem, out reloj, out _);

    /// <summary>Escuchando, con el pestillo abierto y diciendo que si a la pregunta.</summary>
    private static async Task<VistaModeloModemPropio> ListoAsync(VistaModeloModemPropio modelo)
    {
        await modelo.EscucharCommand.ExecuteAsync(null);
        modelo.PermitirTransmitir = true;
        modelo.ConfirmarQueVaATransmitir = _ => true;
        return modelo;
    }

    private static Task<VistaModeloModemPropio> Listo(out ModemApuntador modem, out RelojManual reloj, out RepositorioQsoEnMemoria cuaderno)
    {
        var modelo = Montar(out modem, out reloj, out var repo);
        cuaderno = repo;
        return ListoAsync(modelo);
    }

    private static async Task Ventana(VistaModeloModemPropio modelo, Action soltar)
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
}
