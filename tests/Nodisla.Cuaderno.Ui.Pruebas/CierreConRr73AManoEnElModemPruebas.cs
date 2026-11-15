using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Ui.Desarrollo;
using Nodisla.Cuaderno.Ui.VistaModelos;
using static Nodisla.Cuaderno.Ui.Pruebas.MontajeDeLaPestanaDigital;

namespace Nodisla.Cuaderno.Ui.Pruebas;

/// <summary>
/// El contacto contestado a mano con el modem propio, de principio a fin: se cierra al salir el
/// RR73, queda apuntado una sola vez con los datos de su corresponsal, y a quien no copio el RR73
/// se le repite sin apuntarlo otra vez.
/// </summary>
/// <remarks>
/// <b>Aqui no se transmite.</b> Modem, salida y reloj de mentira. El corresponsal habla en las
/// ventanas pares (12:00:00, 12:00:30…) y su decodificacion llega un segundo despues de empezar
/// la propia (12:00:16, 12:00:46…), a tiempo de emitir en ella.
/// </remarks>
public sealed class CierreConRr73AManoEnElModemPruebas
{
    private static readonly TimeSpan Paciencia = TimeSpan.FromSeconds(10);

    private static DateTimeOffset T(int segundos) => Mediodia.AddSeconds(segundos);

    // ── El cierre ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ElContactoAManoSeCierraAlSalirElRr73SeGuardaUnaVezYNoSeRepiteACiegas()
    {
        var modelo = await ContestadoAManoHastaElRr73();
        var (modem, reloj, cuaderno) = Dobles(modelo);

        modem.Emisiones.Should().HaveCount(2);
        modem.Emisiones[1].Texto.Should().Be("EA5XYZ EA8DLF RR73");
        modelo.TxHabilitado.Should().BeFalse("el contacto a mano, al acabar, se para");
        modelo.Secuenciador.Activo.Should().BeFalse();
        modelo.Aviso.Should().Be("Guardado: EA5XYZ 20m FT8");
        modelo.Corresponsal.Should().Be("EA5XYZ", "el contacto hecho sigue a la vista");

        var qso = (await Contactos(cuaderno)).Should().ContainSingle().Subject;
        qso.Call.Valor.Should().Be("EA5XYZ");
        qso.Gridsquare.ToString().Should().Be("IM98");
        qso.RstSent.ToString().Should().Be("-07");
        qso.RstRcvd.ToString().Should().Be("-05");
        qso.InicioUtc.Should().Be(T(0));
        qso.FinUtc.Should().Be(T(46));
        qso.Band.Nombre.Should().Be("20m");

        // No manda 73 y no se le oye mas: ni RR73 a ciegas, ni «desaparecido», ni otro apunte.
        for (var n = 0; n < 8; n++)
        {
            reloj.Ahora = T(76 + (30 * n));
            await Ventana(modelo, () => modem.Soltar(T(60 + (30 * n))));
        }

        modem.Emisiones.Should().HaveCount(2);
        modelo.TxHabilitado.Should().BeFalse();
        modelo.Aviso.Should().NotContain("desaparecido");
        (await Contactos(cuaderno)).Should().ContainSingle();
    }

    [Fact]
    public async Task SiElOtroMandaSu73DespuesNoSaleNadaMasNiSeApuntaOtraVez()
    {
        var modelo = await ContestadoAManoHastaElRr73();
        var (modem, reloj, cuaderno) = Dobles(modelo);

        reloj.Ahora = T(76);
        await Ventana(modelo, () => modem.Soltar(T(60), Oido("EA8DLF EA5XYZ 73", -6, 800, T(60))));
        await modelo.GuardadoEnCurso;

        modem.Emisiones.Should().HaveCount(2);
        modelo.TxHabilitado.Should().BeFalse();
        (await Contactos(cuaderno)).Should().ContainSingle();
    }

    [Fact]
    public async Task AunqueEste1QsoQuitadoElContactoAManoNoPasaACq()
    {
        var modelo = await ContestadoAManoHastaElRr73(m => m.UnSoloQso = false);
        var (modem, reloj, _) = Dobles(modelo);

        reloj.Ahora = T(76);
        await Ventana(modelo, () => modem.Soltar(T(60), Oido("EA8DLF K1ABC FN42", -12, 1200, T(60))));

        modem.Emisiones.Should().HaveCount(2, "ni CQ ni contestar a otro: el doble clic no continua solo");
        modelo.TxHabilitado.Should().BeFalse();
        modelo.HayEnEspera.Should().BeFalse();
    }

    // ── El que no copio el RR73 ─────────────────────────────────────────────

    [Fact]
    public async Task SiElOtroRepiteSuRInformeSeLeRepiteElRr73YSigueHabiendoUnSoloContacto()
    {
        var modelo = await ContestadoAManoHastaElRr73();
        var (modem, reloj, cuaderno) = Dobles(modelo);

        // Una ventana suya en blanco y, a la siguiente, repite: no copio el RR73.
        reloj.Ahora = T(76);
        await Ventana(modelo, () => modem.Soltar(T(60)));
        reloj.Ahora = T(106);
        await Ventana(modelo, () => modem.Soltar(T(90), Oido("EA8DLF EA5XYZ R-05", -6, 800, T(90))));
        await Hasta(() => modem.Emisiones.Count == 3 && !modelo.TxHabilitado);

        modem.Emisiones[2].Texto.Should().Be("EA5XYZ EA8DLF RR73");
        modelo.Secuenciador.Activo.Should().BeFalse("sale el RR73 y se para otra vez");
        await modelo.GuardadoEnCurso;

        var qso = (await Contactos(cuaderno)).Should().ContainSingle("no se apunta dos veces").Subject;
        qso.FinUtc.Should().Be(T(46), "el apunte es el del primer RR73");
        modelo.Corresponsal.Should().Be("EA5XYZ");
        modelo.InformeRecibido.Should().Be("-05");
        modelo.LocalizadorDelCorresponsal.Should().Be("IM98");

        // Ahora si lo copia y se despide: nada mas.
        reloj.Ahora = T(136);
        await Ventana(modelo, () => modem.Soltar(T(120), Oido("EA8DLF EA5XYZ 73", -6, 800, T(120))));
        modem.Emisiones.Should().HaveCount(3);
        (await Contactos(cuaderno)).Should().ContainSingle();
    }

    [Fact]
    public async Task PasadasTresVentanasSuyasLaRepeticionYaNoArrancaLaTransmision()
    {
        var modelo = await ContestadoAManoHastaElRr73();
        var (modem, reloj, cuaderno) = Dobles(modelo);

        for (var n = 0; n < 3; n++)
        {
            reloj.Ahora = T(76 + (30 * n));
            await Ventana(modelo, () => modem.Soltar(T(60 + (30 * n))));
        }

        reloj.Ahora = T(166);
        await Ventana(modelo, () => modem.Soltar(T(150), Oido("EA8DLF EA5XYZ R-05", -6, 800, T(150))));

        modem.Emisiones.Should().HaveCount(2, "lleva 90 s parada: ya no emite sola");
        modelo.TxHabilitado.Should().BeFalse();
        (await Contactos(cuaderno)).Should().ContainSingle();
    }

    [Fact]
    public async Task TrasDetenerOConElPestilloCerradoLaRepeticionNoSacaNada()
    {
        // Detener: el operador ha dicho basta.
        var modelo = await ContestadoAManoHastaElRr73();
        var (modem, reloj, cuaderno) = Dobles(modelo);
        await modelo.DetenerTxCommand.ExecuteAsync(null);

        reloj.Ahora = T(76);
        await Ventana(modelo, () => modem.Soltar(T(60), Oido("EA8DLF EA5XYZ R-05", -6, 800, T(60))));
        modem.Emisiones.Should().HaveCount(2);
        modelo.TxHabilitado.Should().BeFalse();

        // Pestillo cerrado: no se habilita Tx por la puerta de atras.
        modelo = await ContestadoAManoHastaElRr73();
        (modem, reloj, cuaderno) = Dobles(modelo);
        modelo.PermitirTransmitir = false;

        reloj.Ahora = T(76);
        await Ventana(modelo, () => modem.Soltar(T(60), Oido("EA8DLF EA5XYZ R-05", -6, 800, T(60))));
        modem.Emisiones.Should().HaveCount(2);
        modelo.TxHabilitado.Should().BeFalse();
        modelo.Secuenciador.Activo.Should().BeFalse("sin poder emitir, la secuencia no se queda en marcha a medias");
        (await Contactos(cuaderno)).Should().ContainSingle();
    }

    [Fact]
    public async Task SinGuardadoSoloElContactoAManoSeCierraIgualYSeGuardaConElBotonUnaVez()
    {
        var modelo = await ContestadoAManoHastaElRr73(m => m.RegistrarAlCompletar = false);
        var (modem, reloj, cuaderno) = Dobles(modelo);

        modem.Emisiones.Should().HaveCount(2);
        modelo.TxHabilitado.Should().BeFalse();
        modelo.ContactoCompleto.Should().BeTrue();
        (await Contactos(cuaderno)).Should().BeEmpty();

        // Repite su R+informe: se le repite el RR73 y el contacto sigue entero en pantalla.
        reloj.Ahora = T(76);
        await Ventana(modelo, () => modem.Soltar(T(60), Oido("EA8DLF EA5XYZ R-05", -6, 800, T(60))));
        await Hasta(() => modem.Emisiones.Count == 3 && !modelo.TxHabilitado);
        modelo.ContactoCompleto.Should().BeTrue();
        modelo.Corresponsal.Should().Be("EA5XYZ");
        modelo.InformeRecibido.Should().Be("-05");
        (await Contactos(cuaderno)).Should().BeEmpty();

        await modelo.RegistrarContactoCommand.ExecuteAsync(null);
        var qso = (await Contactos(cuaderno)).Should().ContainSingle().Subject;
        qso.Call.Valor.Should().Be("EA5XYZ");
        qso.RstRcvd.ToString().Should().Be("-05");
    }

    // ── Con RRR ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task ConRrrSeEsperaEl73YAlLlegarSeParaConUnSoloContacto()
    {
        var modelo = await ContestadoAManoHastaElRr73(m => m.Tx4ConRrr = true);
        var (modem, reloj, cuaderno) = Dobles(modelo);

        modem.Emisiones[1].Texto.Should().Be("EA5XYZ EA8DLF RRR");
        modelo.TxHabilitado.Should().BeTrue("RRR pide confirmacion: se espera su 73");
        (await Contactos(cuaderno)).Should().ContainSingle("se apunta al salir el RRR");

        // Sin su 73 se repite el RRR; con el, se para.
        reloj.Ahora = T(76);
        await Ventana(modelo, () => modem.Soltar(T(60)));
        modem.Emisiones.Should().HaveCount(3);
        modem.Emisiones[2].Texto.Should().Be("EA5XYZ EA8DLF RRR");

        reloj.Ahora = T(106);
        await Ventana(modelo, () => modem.Soltar(T(90), Oido("EA8DLF EA5XYZ 73", -6, 800, T(90))));
        await modelo.GuardadoEnCurso;

        modem.Emisiones.Should().HaveCount(3);
        modelo.TxHabilitado.Should().BeFalse();
        (await Contactos(cuaderno)).Should().ContainSingle();
    }

    [Fact]
    public async Task ConRrrSiElOtroNoMandaEl73ElAvisoDiceQueElContactoEstaGuardadoYNoDesaparecido()
    {
        var modelo = await ContestadoAManoHastaElRr73(m => m.Tx4ConRrr = true);
        var (modem, reloj, cuaderno) = Dobles(modelo);

        for (var n = 0; n < 12 && modelo.TxHabilitado; n++)
        {
            reloj.Ahora = T(76 + (30 * n));
            await Ventana(modelo, () => modem.Soltar(T(60 + (30 * n))));
        }

        modelo.TxHabilitado.Should().BeFalse("el vigilante la ha parado");
        modem.Emisiones.Skip(1).Should().OnlyContain(e => e.Texto == "EA5XYZ EA8DLF RRR");
        modelo.Aviso.Should().NotContain("desaparecido");
        modelo.Aviso.Should().Contain("EA5XYZ").And.Contain("guardado").And.Contain("73");
        (await Contactos(cuaderno)).Should().ContainSingle();
    }

    [Fact]
    public async Task ConRrrSinGuardadoSoloElAvisoDiceCompletoPeroNoGuardado()
    {
        var modelo = await ContestadoAManoHastaElRr73(m =>
        {
            m.Tx4ConRrr = true;
            m.RegistrarAlCompletar = false;
        });
        var (modem, reloj, cuaderno) = Dobles(modelo);

        for (var n = 0; n < 12 && modelo.TxHabilitado; n++)
        {
            reloj.Ahora = T(76 + (30 * n));
            await Ventana(modelo, () => modem.Soltar(T(60 + (30 * n))));
        }

        modelo.TxHabilitado.Should().BeFalse();
        modelo.Aviso.Should().NotContain("desaparecido").And.NotContain("guardado");
        modelo.Aviso.Should().Contain("EA5XYZ").And.Contain("completo");
        modelo.ContactoCompleto.Should().BeTrue();
        (await Contactos(cuaderno)).Should().BeEmpty();
    }

    // ── Montaje ───────────────────────────────────────────────────────────

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<VistaModeloModemPropio, object> DoblesDe = new();

    private static (ModemApuntador Modem, RelojManual Reloj, RepositorioQsoEnMemoria Cuaderno) Dobles(VistaModeloModemPropio modelo) =>
        DoblesDe.TryGetValue(modelo, out var dobles)
            ? ((ModemApuntador, RelojManual, RepositorioQsoEnMemoria))dobles
            : throw new InvalidOperationException("Modelo sin montar.");

    /// <summary>
    /// EA5XYZ me llama (12:00:00), se le contesta a mano con el doble clic y sale el informe
    /// (12:00:15); manda su R+informe (12:00:30) y sale el Tx4 (12:00:45): RR73, o RRR si se pide.
    /// </summary>
    private static async Task<VistaModeloModemPropio> ContestadoAManoHastaElRr73(Action<VistaModeloModemPropio>? ajustar = null)
    {
        var modelo = Montar(out var modem, out var reloj, out var cuaderno);
        DoblesDe.Add(modelo, (modem, reloj, cuaderno));

        await modelo.EscucharCommand.ExecuteAsync(null);
        modelo.PermitirTransmitir = true;
        modelo.ConfirmarQueVaATransmitir = _ => true;
        modelo.PonerElDial(Frecuencia.DesdeMegahercios(14.074m));
        ajustar?.Invoke(modelo);

        reloj.Ahora = T(16);
        await Ventana(modelo, () => modem.Soltar(T(0), Oido("EA8DLF EA5XYZ IM98", -7, 800, T(0))));
        modelo.DecodificacionElegida = modelo.Decodificaciones.Single();
        await modelo.PrepararRespuestaCommand.ExecuteAsync(null);
        modem.Emisiones.Should().ContainSingle().Which.Texto.Should().Be("EA5XYZ EA8DLF -07");

        reloj.Ahora = T(46);
        await Ventana(modelo, () => modem.Soltar(T(30), Oido("EA8DLF EA5XYZ R-05", -6, 800, T(30))));
        await Hasta(() => modem.Emisiones.Count == 2);
        await modelo.GuardadoEnCurso;
        return modelo;
    }

    private static async Task<IReadOnlyList<Qso>> Contactos(RepositorioQsoEnMemoria cuaderno) =>
        (await cuaderno.BuscarAsync(new CriterioQso(), 0, 50)).Elementos;

    /// <summary>Espera a que se cumpla algo que ocurre al terminar una emision (el guardado va con espera).</summary>
    private static async Task Hasta(Func<bool> condicion)
    {
        var limite = DateTime.UtcNow + Paciencia;
        while (!condicion())
        {
            if (DateTime.UtcNow > limite) throw new TimeoutException("No se ha cumplido la condición esperada.");
            await Task.Delay(5);
        }

        // Un respiro mas, para que termine lo que venga pegado a la condicion.
        await Task.Delay(20);
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
