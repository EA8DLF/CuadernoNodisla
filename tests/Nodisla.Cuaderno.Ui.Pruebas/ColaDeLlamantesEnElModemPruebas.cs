using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Ui.Desarrollo;
using Nodisla.Cuaderno.Ui.VistaModelos;
using static Nodisla.Cuaderno.Ui.Pruebas.MontajeDeLaPestanaDigital;

namespace Nodisla.Cuaderno.Ui.Pruebas;

/// <summary>
/// Llamar CQ con el modem propio, de principio a fin: la seleccion automatica de quien contesta,
/// la lista «En espera» y, sobre todo, que cada contacto quede apuntado en el cuaderno con los
/// datos de <b>su</b> corresponsal al pasar de uno al siguiente.
/// </summary>
/// <remarks>
/// <b>Aqui no se transmite.</b> Modem, salida y reloj de mentira: lo que «sale» es una linea en
/// una lista. Las ventanas propias son las impares (12:00:15, 12:00:45…); las de los demas, las
/// pares, y su decodificacion llega un segundo despues de cerrar.
/// </remarks>
public sealed class ColaDeLlamantesEnElModemPruebas
{
    private static readonly TimeSpan Paciencia = TimeSpan.FromSeconds(10);

    private static DateTimeOffset T(int segundos) => Mediodia.AddSeconds(segundos);

    // ── Varios llamantes: uno detras de otro, cada uno a su linea del cuaderno ──

    [Fact]
    public async Task ConDosLlamantesSeContestaAlPrimeroYAlAcabarAlSegundoSinVolverACq()
    {
        var modelo = await LlamandoCq(out var modem, out var reloj, out var cuaderno);

        // Contestan dos a la vez.
        reloj.Ahora = T(46);
        await Ventana(modelo, () => modem.Soltar(
            T(30),
            Oido("EA8DLF EA5XYZ IM98", -7, 800, T(30)),
            Oido("EA8DLF K1ABC FN42", -14, 1200, T(30))));

        modelo.Corresponsal.Should().Be("EA5XYZ", "se elige solo al primero que contesta");
        modem.Emisiones.Should().HaveCount(2);
        modem.Emisiones[1].Texto.Should().Be("EA5XYZ EA8DLF -07");
        modelo.HayEnEspera.Should().BeTrue();
        modelo.EnEspera.Should().ContainSingle().Which.Texto.Should().Be("K1ABC -14");

        // R+informe del primero; el segundo insiste, ahora con otra señal.
        reloj.Ahora = T(76);
        await Ventana(modelo, () => modem.Soltar(
            T(60),
            Oido("EA8DLF EA5XYZ R-05", -6, 800, T(60)),
            Oido("EA8DLF K1ABC FN42", -12, 1200, T(60))));
        await Hasta(() => modelo.Corresponsal == "K1ABC");

        modem.Emisiones.Should().HaveCount(3);
        modem.Emisiones[2].Texto.Should().Be("EA5XYZ EA8DLF RR73");

        // El contacto que acaba queda apuntado con SUS datos, no con los del que viene detras.
        var primero = (await Contactos(cuaderno)).Should().ContainSingle().Subject;
        primero.Call.Valor.Should().Be("EA5XYZ");
        primero.Gridsquare.ToString().Should().Be("IM98");
        primero.RstSent.ToString().Should().Be("-07");
        primero.RstRcvd.ToString().Should().Be("-05");
        primero.InicioUtc.Should().Be(T(30));
        primero.FinUtc.Should().Be(T(76));
        primero.Band.Nombre.Should().Be("20m");

        // Y la pantalla ya es la del siguiente, limpia de lo del anterior.
        modelo.TxHabilitado.Should().BeTrue("la cola no vacia manda sobre «1 QSO»: no se para");
        modelo.LocalizadorDelCorresponsal.Should().Be("FN42");
        modelo.InformeEnviado.Should().Be("-12");
        modelo.InformeRecibido.Should().BeEmpty();
        modelo.ContactoCompleto.Should().BeFalse();
        modelo.TxSiguiente.Should().Be(2);
        modelo.HayEnEspera.Should().BeFalse();

        // Al segundo se le contesta directamente con su informe: ni un CQ por medio.
        reloj.Ahora = T(106);
        await Ventana(modelo, () => modem.Soltar(T(90), Oido("EA8DLF K1ABC FN42", -11, 1200, T(90))));
        modem.Emisiones.Should().HaveCount(4);
        modem.Emisiones[3].Texto.Should().Be("K1ABC EA8DLF -12");

        reloj.Ahora = T(136);
        await Ventana(modelo, () => modem.Soltar(T(120), Oido("EA8DLF K1ABC R-09", -11, 1200, T(120))));
        await Hasta(() => !modelo.TxHabilitado);

        modem.Emisiones.Should().HaveCount(5);
        modem.Emisiones[4].Texto.Should().Be("K1ABC EA8DLF RR73");
        modelo.Aviso.Should().Be("Guardado: K1ABC 20m FT8");

        var contactos = await Contactos(cuaderno);
        contactos.Should().HaveCount(2, "ni se pierde ni se duplica ninguno");
        var segundo = contactos.Single(q => q.Call.Valor == "K1ABC");
        segundo.Gridsquare.ToString().Should().Be("FN42");
        segundo.RstSent.ToString().Should().Be("-12");
        segundo.RstRcvd.ToString().Should().Be("-09");
        segundo.InicioUtc.Should().Be(T(106), "su contacto empieza cuando se le contesta, no con el del anterior");
        segundo.FinUtc.Should().Be(T(136));

        // Cola vacia y «1 QSO» (de fabrica): se para y no sale nada mas.
        modelo.Secuenciador.Activo.Should().BeFalse();
        reloj.Ahora = T(166);
        await Ventana(modelo, () => modem.Soltar(T(150)));
        modem.Emisiones.Should().HaveCount(5);
        modem.Emisiones.Count(e => e.Texto.StartsWith("CQ", StringComparison.Ordinal)).Should().Be(1, "entre un contacto y el siguiente no se ha vuelto a llamar CQ");
    }

    [Fact]
    public async Task SiElPrimeroAbrioConInformeYCierraConRr73ElSuyoSeApuntaAntesDePasarAlSiguiente()
    {
        var modelo = await LlamandoCq(out var modem, out var reloj, out var cuaderno);

        reloj.Ahora = T(46);
        await Ventana(modelo, () => modem.Soltar(
            T(30),
            Oido("EA8DLF EA5XYZ -03", -7, 800, T(30)),
            Oido("EA8DLF K1ABC FN42", -14, 1200, T(30))));
        modem.Emisiones[1].Texto.Should().Be("EA5XYZ EA8DLF R-07");

        // Su RR73: completo. Sale el 73 propio y, con el, se pasa a K1ABC.
        reloj.Ahora = T(76);
        await Ventana(modelo, () => modem.Soltar(
            T(60),
            Oido("EA8DLF EA5XYZ RR73", -6, 800, T(60)),
            Oido("EA8DLF K1ABC FN42", -12, 1200, T(60))));
        await Hasta(() => modelo.Corresponsal == "K1ABC");
        await modelo.GuardadoEnCurso;

        modem.Emisiones[2].Texto.Should().Be("EA5XYZ EA8DLF 73");
        var qso = (await Contactos(cuaderno)).Should().ContainSingle().Subject;
        qso.Call.Valor.Should().Be("EA5XYZ");
        qso.RstSent.ToString().Should().Be("-07");
        qso.RstRcvd.ToString().Should().Be("-03");
        modelo.TxSiguiente.Should().Be(2);
        modelo.InformeRecibido.Should().BeEmpty();
    }

    // ── El otro no manda 73 ─────────────────────────────────────────────────

    [Fact]
    public async Task Con1QsoYSinNadieEnEsperaSeParaAlSalirElRr73SinEsperarEl73NiRepetirlo()
    {
        var modelo = await LlamandoCq(out var modem, out var reloj, out var cuaderno);

        reloj.Ahora = T(46);
        await Ventana(modelo, () => modem.Soltar(T(30), Oido("EA8DLF EA5XYZ IM98", -7, 800, T(30))));
        reloj.Ahora = T(76);
        await Ventana(modelo, () => modem.Soltar(T(60), Oido("EA8DLF EA5XYZ R-05", -6, 800, T(60))));
        await Hasta(() => !modelo.TxHabilitado);

        modem.Emisiones.Should().HaveCount(3);
        modelo.Aviso.Should().Be("Guardado: EA5XYZ 20m FT8");
        modelo.Corresponsal.Should().Be("EA5XYZ", "el contacto hecho sigue a la vista");

        // No manda 73: no se repite el RR73 a ciegas.
        for (var n = 0; n < 3; n++)
        {
            reloj.Ahora = T(106 + (30 * n));
            await Ventana(modelo, () => modem.Soltar(T(90 + (30 * n))));
        }

        modem.Emisiones.Should().HaveCount(3);
        (await Contactos(cuaderno)).Should().ContainSingle();
    }

    [Fact]
    public async Task Sin1QsoSeVuelveACqYSiElOtroNoCopioElRr73SeLeRepiteSinApuntarloDosVeces()
    {
        var modelo = await LlamandoCq(out var modem, out var reloj, out var cuaderno);
        modelo.UnSoloQso = false;

        reloj.Ahora = T(46);
        await Ventana(modelo, () => modem.Soltar(T(30), Oido("EA8DLF EA5XYZ IM98", -7, 800, T(30))));
        reloj.Ahora = T(76);
        await Ventana(modelo, () => modem.Soltar(T(60), Oido("EA8DLF EA5XYZ R-05", -6, 800, T(60))));
        await Hasta(() => modelo.TxSiguiente == 6);
        modelo.TxHabilitado.Should().BeTrue("sin «1 QSO» se sigue llamando");

        // No manda 73: en la siguiente ventana propia vuelve a salir el CQ.
        reloj.Ahora = T(106);
        await Ventana(modelo, () => modem.Soltar(T(90)));
        modem.Emisiones.Should().HaveCount(4);
        modem.Emisiones[3].Texto.Should().StartWith("CQ EA8DLF");

        // Repite su R+informe: no copio el RR73. Se le repite, y sigue habiendo un solo contacto.
        reloj.Ahora = T(136);
        await Ventana(modelo, () => modem.Soltar(T(120), Oido("EA8DLF EA5XYZ R-05", -6, 800, T(120))));
        await Hasta(() => modem.Emisiones.Count == 5 && modelo.TxSiguiente == 6);
        modem.Emisiones[4].Texto.Should().Be("EA5XYZ EA8DLF RR73");
        await modelo.GuardadoEnCurso;
        (await Contactos(cuaderno)).Should().ContainSingle();

        reloj.Ahora = T(166);
        await Ventana(modelo, () => modem.Soltar(T(150)));
        modem.Emisiones[5].Texto.Should().StartWith("CQ EA8DLF");
    }

    [Fact]
    public async Task ConRrrSeEsperaEl73DelOtroAntesDePasarAlSiguiente()
    {
        var modelo = await LlamandoCq(out var modem, out var reloj, out var cuaderno);
        modelo.Tx4ConRrr = true;

        reloj.Ahora = T(46);
        await Ventana(modelo, () => modem.Soltar(
            T(30),
            Oido("EA8DLF EA5XYZ IM98", -7, 800, T(30)),
            Oido("EA8DLF K1ABC FN42", -14, 1200, T(30))));
        reloj.Ahora = T(76);
        await Ventana(modelo, () => modem.Soltar(
            T(60),
            Oido("EA8DLF EA5XYZ R-05", -6, 800, T(60)),
            Oido("EA8DLF K1ABC FN42", -12, 1200, T(60))));
        await modelo.GuardadoEnCurso;

        modem.Emisiones[2].Texto.Should().Be("EA5XYZ EA8DLF RRR");
        modelo.Corresponsal.Should().Be("EA5XYZ", "RRR pide confirmacion: se sigue con el hasta su 73");

        reloj.Ahora = T(106);
        await Ventana(modelo, () => modem.Soltar(
            T(90),
            Oido("EA8DLF EA5XYZ 73", -6, 800, T(90)),
            Oido("EA8DLF K1ABC FN42", -12, 1200, T(90))));

        modelo.Corresponsal.Should().Be("K1ABC");
        modem.Emisiones[3].Texto.Should().Be("K1ABC EA8DLF -12");
        (await Contactos(cuaderno)).Should().ContainSingle().Which.Call.Valor.Should().Be("EA5XYZ");
    }

    // ── El operador al mando ────────────────────────────────────────────────

    [Fact]
    public async Task ConTxApagadoNoSaleNadaPeroLaListaSeSigueLlenando()
    {
        var modelo = Montar(out var modem, out var reloj, out _);
        await modelo.EscucharCommand.ExecuteAsync(null);
        modelo.PermitirTransmitir = true; // pero se dice que no a la pregunta: Tx no se habilita
        reloj.Ahora = T(5);
        await modelo.LlamarCqCommand.ExecuteAsync(null);
        modelo.TxHabilitado.Should().BeFalse();

        reloj.Ahora = T(46);
        await Ventana(modelo, () => modem.Soltar(
            T(30),
            Oido("EA8DLF EA5XYZ IM98", -7, 800, T(30)),
            Oido("EA8DLF K1ABC FN42", -14, 1200, T(30)),
            Oido("EA8DLF PY2ZZZ GG66", -3, 1500, T(30))));

        modem.Emisiones.Should().BeEmpty("sin «Tx habilitado» no se transmite nunca");
        modelo.EnEspera.Select(f => f.Indicativo).Should().Equal("K1ABC", "PY2ZZZ");
        modelo.EnEspera.Select(f => f.Turno).Should().Equal(1, 2);
    }

    [Fact]
    public async Task DetenerCambiarDeModoYCambiarDeBandaVacianLaLista()
    {
        var modelo = await LlamandoCq(out var modem, out var reloj, out _);

        async Task DosLlamantes(int ventana)
        {
            if (!modelo.Secuenciador.Activo)
            {
                reloj.Ahora = T(ventana - 25);
                await modelo.LlamarCqCommand.ExecuteAsync(null);
            }

            reloj.Ahora = T(ventana + 16);
            await Ventana(modelo, () => modem.Soltar(
                T(ventana),
                Oido("EA8DLF EA5XYZ IM98", -7, 800, T(ventana)),
                Oido("EA8DLF K1ABC FN42", -14, 1200, T(ventana))));
            modelo.HayEnEspera.Should().BeTrue();
        }

        await DosLlamantes(30);
        await modelo.DetenerTxCommand.ExecuteAsync(null);
        modelo.EnEspera.Should().BeEmpty("Detener vacia la cola");
        modelo.HayEnEspera.Should().BeFalse();

        await DosLlamantes(90);
        modelo.PonerElDial(Frecuencia.DesdeMegahercios(7.074m));
        modelo.EnEspera.Should().BeEmpty("en otra banda ya no estan los que esperaban");

        await DosLlamantes(150);
        modelo.PonerElDial(Frecuencia.DesdeMegahercios(7.0741m));
        modelo.HayEnEspera.Should().BeTrue("un retoque de 100 Hz no es irse a otra frecuencia");
        modelo.ModoElegido = modelo.Modos.Single(o => o.Valor == ModoDelModem.Ft4);
        modelo.EnEspera.Should().BeEmpty("cambiar de modo vacia la cola");
    }

    [Fact]
    public async Task ClicEnUnoDeLaListaLeContestaYaYLaEquisLoQuita()
    {
        var modelo = await LlamandoCq(out var modem, out var reloj, out var cuaderno);

        reloj.Ahora = T(46);
        await Ventana(modelo, () => modem.Soltar(
            T(30),
            Oido("EA8DLF EA5XYZ IM98", -7, 800, T(30)),
            Oido("EA8DLF K1ABC FN42", -14, 1200, T(30)),
            Oido("EA8DLF PY2ZZZ GG66", -3, 1500, T(30)),
            Oido("EA8DLF JA1XYZ PM95", -18, 1700, T(30))));
        modelo.EnEspera.Select(f => f.Indicativo).Should().Equal("K1ABC", "PY2ZZZ", "JA1XYZ");

        modelo.QuitarAlLlamanteCommand.Execute(modelo.EnEspera[0]);
        modelo.EnEspera.Select(f => f.Indicativo).Should().Equal("PY2ZZZ", "JA1XYZ");

        // A media ventana ajena: se elige a JA1XYZ, que iba el ultimo. Sale en la proxima propia.
        reloj.Ahora = T(65);
        await modelo.AtenderAlLlamanteCommand.ExecuteAsync(modelo.EnEspera[1]);
        modelo.Corresponsal.Should().Be("JA1XYZ");
        modelo.LocalizadorDelCorresponsal.Should().Be("PM95");
        modelo.InformeEnviado.Should().Be("-18");
        modelo.EnEspera.Select(f => f.Indicativo).Should().Equal("PY2ZZZ");
        modem.Emisiones.Should().HaveCount(2, "a media ventana no se sale");

        reloj.Ahora = T(76);
        await Ventana(modelo, () => modem.Soltar(T(60), Oido("EA8DLF PY2ZZZ GG66", -3, 1500, T(60))));
        modem.Emisiones[2].Texto.Should().Be("JA1XYZ EA8DLF -18");

        // Y al acabar con el, la sesion sigue siendo la del CQ: toca PY2ZZZ.
        reloj.Ahora = T(106);
        await Ventana(modelo, () => modem.Soltar(
            T(90),
            Oido("EA8DLF JA1XYZ R-10", -18, 1700, T(90)),
            Oido("EA8DLF PY2ZZZ GG66", -3, 1500, T(90))));
        await Hasta(() => modelo.Corresponsal == "PY2ZZZ");
        (await Contactos(cuaderno)).Should().ContainSingle().Which.Call.Valor.Should().Be("JA1XYZ");
    }

    [Fact]
    public async Task ElDobleClicAManoSigueMandandoVaciaLaListaYNoContinuaSolo()
    {
        var modelo = await LlamandoCq(out var modem, out var reloj, out var cuaderno);
        modelo.UnSoloQso = false;

        reloj.Ahora = T(46);
        await Ventana(modelo, () => modem.Soltar(
            T(30),
            Oido("EA8DLF EA5XYZ IM98", -7, 800, T(30)),
            Oido("EA8DLF K1ABC FN42", -14, 1200, T(30)),
            Oido("EA8DLF PY2ZZZ GG66", -3, 1500, T(30))));
        modelo.HayEnEspera.Should().BeTrue();

        // El operador prefiere a PY2ZZZ y hace doble clic en su linea.
        reloj.Ahora = T(65);
        modelo.DecodificacionElegida = modelo.Decodificaciones.Single(f => f.Texto == "EA8DLF PY2ZZZ GG66");
        await modelo.PrepararRespuestaCommand.ExecuteAsync(null);
        modelo.Corresponsal.Should().Be("PY2ZZZ");
        modelo.EnEspera.Should().BeEmpty();

        reloj.Ahora = T(76);
        await Ventana(modelo, () => modem.Soltar(T(60), Oido("EA8DLF K1ABC FN42", -14, 1200, T(60))));
        modem.Emisiones[2].Texto.Should().Be("PY2ZZZ EA8DLF -03");
        modelo.EnEspera.Should().BeEmpty("el contacto elegido a mano no es del CQ: no hace cola");

        reloj.Ahora = T(106);
        await Ventana(modelo, () => modem.Soltar(T(90), Oido("EA8DLF PY2ZZZ R-01", -3, 1500, T(90))));
        reloj.Ahora = T(136);
        await Ventana(modelo, () => modem.Soltar(T(120), Oido("EA8DLF PY2ZZZ 73", -3, 1500, T(120))));
        await modelo.GuardadoEnCurso;

        modelo.TxHabilitado.Should().BeFalse("como siempre tras un contacto a mano: se para, aunque «1 QSO» este quitado");
        modelo.Corresponsal.Should().Be("PY2ZZZ");
        (await Contactos(cuaderno)).Should().ContainSingle().Which.Call.Valor.Should().Be("PY2ZZZ");
    }

    [Fact]
    public async Task SiLosContactosNoSeApuntanSolosNoSePasaAlSiguienteParaNoPerderElQueAcaba()
    {
        var modelo = await LlamandoCq(out var modem, out var reloj, out var cuaderno);
        modelo.RegistrarAlCompletar = false;

        reloj.Ahora = T(46);
        await Ventana(modelo, () => modem.Soltar(
            T(30),
            Oido("EA8DLF EA5XYZ IM98", -7, 800, T(30)),
            Oido("EA8DLF K1ABC FN42", -14, 1200, T(30))));
        reloj.Ahora = T(76);
        await Ventana(modelo, () => modem.Soltar(
            T(60),
            Oido("EA8DLF EA5XYZ R-05", -6, 800, T(60)),
            Oido("EA8DLF K1ABC FN42", -12, 1200, T(60))));
        await Hasta(() => !modelo.TxHabilitado);

        // El contacto sigue en pantalla, entero, esperando a que el operador lo guarde.
        modelo.Corresponsal.Should().Be("EA5XYZ");
        modelo.ContactoCompleto.Should().BeTrue();
        modelo.InformeRecibido.Should().Be("-05");
        modelo.EnEspera.Should().ContainSingle().Which.Indicativo.Should().Be("K1ABC");
        (await Contactos(cuaderno)).Should().BeEmpty();

        await modelo.RegistrarContactoCommand.ExecuteAsync(null);
        (await Contactos(cuaderno)).Should().ContainSingle().Which.Call.Valor.Should().Be("EA5XYZ");
    }

    [Fact]
    public async Task AnsB4NoEligeSoloAlYaTrabajadoPeroLoDejaEnLaListaParaContestarleAMano()
    {
        var modelo = Montar(out var modem, out var reloj, out var cuaderno);
        await modelo.EscucharCommand.ExecuteAsync(null);
        modelo.PermitirTransmitir = true;
        modelo.ConfirmarQueVaATransmitir = _ => true;
        modelo.PonerElDial(Frecuencia.DesdeMegahercios(14.074m));
        modelo.AnsB4 = true;

        // K1ABC ya esta en el cuaderno, de un contacto anterior guardado a mano.
        modelo.Corresponsal = "K1ABC";
        modelo.InformeEnviado = "-10";
        modelo.InformeRecibido = "-10";
        await modelo.RegistrarContactoCommand.ExecuteAsync(null);
        (await Contactos(cuaderno)).Should().ContainSingle();
        modelo.Corresponsal.Should().BeEmpty();

        reloj.Ahora = T(5);
        await modelo.LlamarCqCommand.ExecuteAsync(null);
        reloj.Ahora = T(16);
        await Ventana(modelo, () => modem.Soltar(T(0)));

        // Solo llama uno, y ya esta trabajado: se sigue llamando CQ y el queda a la vista.
        reloj.Ahora = T(46);
        await Ventana(modelo, () => modem.Soltar(T(30), Oido("EA8DLF K1ABC FN42", -14, 1200, T(30))));

        modelo.Corresponsal.Should().BeEmpty();
        modem.Emisiones.Should().HaveCount(2);
        modem.Emisiones[1].Texto.Should().StartWith("CQ EA8DLF");
        var enEspera = modelo.EnEspera.Should().ContainSingle().Subject;
        enEspera.Indicativo.Should().Be("K1ABC");
        enEspera.TrabajadoAntes.Should().BeTrue();

        // A mano si: un clic en la lista y se le contesta.
        reloj.Ahora = T(65);
        await modelo.AtenderAlLlamanteCommand.ExecuteAsync(enEspera);
        modelo.Corresponsal.Should().Be("K1ABC");
    }

    // ── Montaje ───────────────────────────────────────────────────────────

    /// <summary>
    /// Escuchando, con Tx habilitado, en 20 m y con el primer CQ ya emitido en 12:00:15: las
    /// ventanas propias son las impares.
    /// </summary>
    private static async Task<VistaModeloModemPropio> LlamandoCqAsync(VistaModeloModemPropio modelo, ModemApuntador modem, RelojManual reloj)
    {
        await modelo.EscucharCommand.ExecuteAsync(null);
        modelo.PermitirTransmitir = true;
        modelo.ConfirmarQueVaATransmitir = _ => true;
        modelo.PonerElDial(Frecuencia.DesdeMegahercios(14.074m));

        reloj.Ahora = T(5);
        await modelo.LlamarCqCommand.ExecuteAsync(null);
        reloj.Ahora = T(16);
        await Ventana(modelo, () => modem.Soltar(T(0)));
        modem.Emisiones.Should().ContainSingle().Which.Texto.Should().StartWith("CQ EA8DLF");
        return modelo;
    }

    private static Task<VistaModeloModemPropio> LlamandoCq(out ModemApuntador modem, out RelojManual reloj, out RepositorioQsoEnMemoria cuaderno)
    {
        var modelo = Montar(out modem, out reloj, out cuaderno);
        modelo.LlamarAlPrimero.Should().BeTrue("de fabrica se elige solo a quien contesta");
        modelo.UnSoloQso.Should().BeTrue();
        return LlamandoCqAsync(modelo, modem, reloj);
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
