using FluentAssertions;
using Nodisla.Cuaderno.Ui.Digital;

namespace Nodisla.Cuaderno.Ui.Pruebas;

/// <summary>
/// Llamando CQ: la seleccion automatica de quien contesta y la cola de los que esperan su turno.
/// Sin radio, sin audio y sin reloj de pared: las ventanas son horas que se le pasan.
/// </summary>
public sealed class ColaDeLlamantesCqPruebas
{
    private static readonly TimeSpan Periodo = TimeSpan.FromSeconds(15);
    private static readonly DateTimeOffset Par = DateTimeOffset.UnixEpoch;

    /// <summary>La ventana numero <paramref name="n"/> de los otros (las pares; las propias son las impares).</summary>
    private static DateTimeOffset Suya(int n) => Par + (2 * n * Periodo);

    private static MensajeOido Oido(string texto, int db = -10, int tono = 1500, bool trabajado = false) =>
        new(InterpreteDeMensajes.Analizar(texto), db, tono, trabajado);

    /// <summary>Llamando CQ en las impares, con el CQ ya emitido una vez.</summary>
    private static SecuenciadorDeQso LlamandoCq(bool unSoloQso = true)
    {
        var s = new SecuenciadorDeQso { Periodo = Periodo, MiIndicativo = "EA8DLF", UnSoloQso = unSoloQso };
        s.LlamarCq(Par + TimeSpan.FromSeconds(1)).Tx.Should().Be(6);
        s.EmisionHecha(6);
        return s;
    }

    /// <summary>Lleva el contacto en curso hasta haber emitido el RR73 propio.</summary>
    private static DecisionDelSecuenciador HastaElRr73Propio(SecuenciadorDeQso s, int ventana, params MensajeOido[] ademas)
    {
        var dx = s.DxCall;
        s.EmisionHecha(s.TxActual);
        s.Procesar(Suya(ventana), [Oido($"EA8DLF {dx} R-05"), .. ademas]).Tx.Should().Be(4);
        return s.EmisionHecha(4);
    }

    // ── La seleccion automatica ─────────────────────────────────────────────

    [Fact]
    public void DeFabricaSeEligeSoloAQuienContestaAlCq()
    {
        new SecuenciadorDeQso().LlamarAlPrimero.Should().BeTrue();
        new Nodisla.Cuaderno.Ui.Ajustes.AjustesDelPrograma().Digital.LlamarAlPrimero.Should().BeTrue();

        var s = LlamandoCq();
        var decision = s.Procesar(Suya(1), [Oido("EA8DLF IZ2ABC JN45", db: -3)]);

        decision.Tx.Should().Be(2);
        s.DxCall.Should().Be("IZ2ABC");
        s.DxGrid.Should().Be("JN45");
        s.InformeEnviado.Should().Be(-3);
        s.Cola.EnEspera.Should().BeEmpty("el elegido no se queda en la lista de espera");
    }

    [Fact]
    public void ConVariosLlamantesSeContestaAlPrimeroYLosDemasEsperanSuTurno()
    {
        var s = LlamandoCq();

        s.Procesar(Suya(1),
        [
            Oido("EA8DLF IZ2ABC JN45", db: -3),
            Oido("EA8DLF K1ABC FN42", db: -14, tono: 900),
            Oido("EA8DLF PY2ZZZ -08", db: -11),
            Oido("CQ DL1XYZ JO31"),
        ]).Tx.Should().Be(2);

        s.DxCall.Should().Be("IZ2ABC");
        s.Cola.EnEspera.Select(l => l.Indicativo).Should().Equal("K1ABC", "PY2ZZZ");

        var k1 = s.Cola.EnEspera[0];
        k1.Localizador.Should().Be("FN42");
        k1.Decibelios.Should().Be(-14);
        k1.TonoHz.Should().Be(900);
        k1.OidoEn.Should().Be(Suya(1));
        k1.AbrioConInforme.Should().BeFalse();
        s.Cola.EnEspera[1].AbrioConInforme.Should().BeTrue();
    }

    [Fact]
    public void QuienLlamaEnMitadDeUnContactoNacidoDelCqTambienEntraEnLaCola()
    {
        var s = LlamandoCq();
        s.Procesar(Suya(1), [Oido("EA8DLF IZ2ABC JN45")]);
        s.EmisionHecha(2);

        s.Procesar(Suya(2), [Oido("EA8DLF IZ2ABC R-05"), Oido("EA8DLF K1ABC FN42", db: -9)]).Tx.Should().Be(4);

        s.DxCall.Should().Be("IZ2ABC");
        s.Cola.EnEspera.Should().ContainSingle().Which.Indicativo.Should().Be("K1ABC");
    }

    [Fact]
    public void QuienVuelveALlamarNoSeDuplicaYConservaSuTurnoConLosDatosNuevos()
    {
        var s = LlamandoCq();
        s.Procesar(Suya(1), [Oido("EA8DLF IZ2ABC JN45"), Oido("EA8DLF K1ABC FN42", db: -14), Oido("EA8DLF PY2ZZZ GG66")]);
        s.EmisionHecha(2);

        s.Procesar(Suya(2), [Oido("EA8DLF PY2ZZZ GG66"), Oido("EA8DLF K1ABC FN42", db: -6, tono: 1111)]);

        s.Cola.EnEspera.Select(l => l.Indicativo).Should().Equal("K1ABC", "PY2ZZZ");
        s.Cola.EnEspera[0].Decibelios.Should().Be(-6);
        s.Cola.EnEspera[0].TonoHz.Should().Be(1111);
        s.Cola.EnEspera[0].OidoEn.Should().Be(Suya(2));
    }

    // ── Al acabar un contacto ───────────────────────────────────────────────

    [Fact]
    public void AlSalirElRr73PropioSePasaAlSiguienteDeLaColaSinVolverACqAunqueEste1Qso()
    {
        var s = LlamandoCq(unSoloQso: true);
        s.Procesar(Suya(1), [Oido("EA8DLF IZ2ABC JN45", db: -3), Oido("EA8DLF K1ABC FN42", db: -14)]);

        var fin = HastaElRr73Propio(s, 2);

        fin.Parada.Should().BeNull("la cola no vacia manda sobre «1 QSO»");
        fin.Tx.Should().Be(2, "al siguiente se le contesta con su informe, sin CQ por medio");
        fin.PasaA.Should().Be("K1ABC");
        fin.ContactoCompleto.Should().BeFalse("el de IZ2ABC ya se dio por completo al recibir su R+informe");
        s.Activo.Should().BeTrue();
        s.DxCall.Should().Be("K1ABC");
        s.DxGrid.Should().Be("FN42");
        s.InformeEnviado.Should().Be(-14);
        s.InformeRecibido.Should().BeNull("nada del contacto anterior se arrastra al siguiente");
        s.Cola.EnEspera.Should().BeEmpty();
    }

    [Fact]
    public void AlSiguienteQueAbrioConInformeSeLeContestaConTx3()
    {
        var s = LlamandoCq();
        s.Procesar(Suya(1), [Oido("EA8DLF IZ2ABC JN45"), Oido("EA8DLF PY2ZZZ -08", db: -11)]);

        var fin = HastaElRr73Propio(s, 2);

        fin.Tx.Should().Be(3);
        s.DxCall.Should().Be("PY2ZZZ");
        s.InformeRecibido.Should().Be(-8);
        s.InformeEnviado.Should().Be(-11);
    }

    [Fact]
    public void ElSegundoContactoDeLaColaSeCompletaYSeAnunciaComoCualquierOtro()
    {
        var s = LlamandoCq();
        s.Procesar(Suya(1), [Oido("EA8DLF IZ2ABC JN45"), Oido("EA8DLF K1ABC FN42")]);
        HastaElRr73Propio(s, 2);

        s.EmisionHecha(2);
        var completo = s.Procesar(Suya(3), [Oido("EA8DLF K1ABC R-12")]);
        completo.Tx.Should().Be(4);
        completo.ContactoCompleto.Should().BeTrue();
        s.InformeRecibido.Should().Be(-12);

        var fin = s.EmisionHecha(4);
        fin.Parada.Should().NotBeNull("cola vacia y «1 QSO»: se para");
        s.Activo.Should().BeFalse();
    }

    [Fact]
    public void SinNadieEnColaYCon1QsoSeParaAlSalirElRr73PropioSinEsperarEl73()
    {
        var s = LlamandoCq(unSoloQso: true);
        s.Procesar(Suya(1), [Oido("EA8DLF IZ2ABC JN45")]);

        var fin = HastaElRr73Propio(s, 2);

        fin.Parada.Should().Contain("terminado");
        fin.Tx.Should().BeNull("con RR73 no se espera el 73 del otro ni se repite el RR73 a ciegas");
        s.Activo.Should().BeFalse();
        s.Terminado.Should().BeTrue();
    }

    [Fact]
    public void SinNadieEnColaYSin1QsoSeVuelveACqAlSalirElRr73Propio()
    {
        var s = LlamandoCq(unSoloQso: false);
        s.Procesar(Suya(1), [Oido("EA8DLF IZ2ABC JN45")]);

        var fin = HastaElRr73Propio(s, 2);

        fin.Tx.Should().Be(6);
        fin.Parada.Should().BeNull();
        fin.PasaA.Should().BeNull();
        s.Activo.Should().BeTrue();
        s.TxActual.Should().Be(6);
        s.DxCall.Should().BeEmpty();

        // Y sigue de verdad: el siguiente que llame se elige solo.
        s.EmisionHecha(6);
        s.Procesar(Suya(3), [Oido("EA8DLF K1ABC FN42")]).Tx.Should().Be(2);
        s.DxCall.Should().Be("K1ABC");
    }

    // ── Con RRR si hay que esperar el 73 ────────────────────────────────────

    [Fact]
    public void ConRrrSeEsperaEl73YAlLlegarSePasaAlSiguiente()
    {
        var s = LlamandoCq();
        s.Tx4ConRrr = true;
        s.Procesar(Suya(1), [Oido("EA8DLF IZ2ABC JN45"), Oido("EA8DLF K1ABC FN42")]);

        var trasRrr = HastaElRr73Propio(s, 2);
        trasRrr.Should().Be(DecisionDelSecuenciador.Nada, "RRR pide confirmacion: el contacto sigue abierto");
        s.DxCall.Should().Be("IZ2ABC");

        // Sin su 73 se repite el RRR.
        s.Procesar(Suya(3), [Oido("EA8DLF K1ABC FN42")]).Tx.Should().Be(4);
        s.EmisionHecha(4);

        var fin = s.Procesar(Suya(4), [Oido("EA8DLF IZ2ABC 73"), Oido("EA8DLF K1ABC FN42")]);
        fin.Tx.Should().Be(2);
        fin.PasaA.Should().Be("K1ABC");
        s.DxCall.Should().Be("K1ABC");
    }

    [Fact]
    public void ConRrrSinNadieEnColaYSin1QsoEl73DelOtroDevuelveACq()
    {
        // El fallo de antes: aqui se paraba aunque «1 QSO» estuviera quitado.
        var s = LlamandoCq(unSoloQso: false);
        s.Tx4ConRrr = true;
        s.Procesar(Suya(1), [Oido("EA8DLF IZ2ABC JN45")]);
        HastaElRr73Propio(s, 2);

        var fin = s.Procesar(Suya(3), [Oido("EA8DLF IZ2ABC 73")]);

        fin.Tx.Should().Be(6);
        fin.Parada.Should().BeNull();
        s.Activo.Should().BeTrue();
        s.DxCall.Should().BeEmpty();
    }

    [Fact]
    public void ConRrrSinNadieEnColaYCon1QsoEl73DelOtroPara()
    {
        var s = LlamandoCq(unSoloQso: true);
        s.Tx4ConRrr = true;
        s.Procesar(Suya(1), [Oido("EA8DLF IZ2ABC JN45")]);
        HastaElRr73Propio(s, 2);

        var fin = s.Procesar(Suya(3), [Oido("EA8DLF IZ2ABC 73")]);

        fin.Parada.Should().Contain("terminado");
        s.Activo.Should().BeFalse();
    }

    // ── El que no copio el RR73 ─────────────────────────────────────────────

    [Fact]
    public void SiElAnteriorRepiteSuRInformeMientrasSeLlamaCqSeLeRepiteElRr73SinApuntarloOtraVez()
    {
        var s = LlamandoCq(unSoloQso: false);
        s.Procesar(Suya(1), [Oido("EA8DLF IZ2ABC JN45", db: -3)]);
        HastaElRr73Propio(s, 2).Tx.Should().Be(6);
        s.EmisionHecha(6);

        var repite = s.Procesar(Suya(3), [Oido("EA8DLF IZ2ABC R-05")]);

        repite.Tx.Should().Be(4, "no copio el RR73: se le repite");
        repite.ContactoCompleto.Should().BeFalse("ese contacto ya se dio por completo: no se apunta dos veces");
        repite.PasaA.Should().Be("IZ2ABC");
        s.DxCall.Should().Be("IZ2ABC");
        s.InformeEnviado.Should().Be(-3);
        s.InformeRecibido.Should().Be(-5);

        s.EmisionHecha(4).Tx.Should().Be(6, "y de vuelta al CQ");
    }

    // ── Quien sale de la cola ───────────────────────────────────────────────

    [Fact]
    public void SaleDeLaColaQuienLlamaOContestaAOtroOLlamaCq()
    {
        var s = LlamandoCq();
        s.Procesar(Suya(1),
        [
            Oido("EA8DLF IZ2ABC JN45"), Oido("EA8DLF K1ABC FN42"), Oido("EA8DLF PY2ZZZ GG66"), Oido("EA8DLF JA1XYZ PM95"),
        ]);
        s.EmisionHecha(2);

        s.Procesar(Suya(2), [Oido("DL1XYZ K1ABC FN42"), Oido("CQ PY2ZZZ GG66"), Oido("EA8DLF JA1XYZ PM95")]);

        s.Cola.EnEspera.Select(l => l.Indicativo).Should().Equal("JA1XYZ");
    }

    [Fact]
    public void SaleDeLaColaQuienLlevaTresDeSusVentanasSinOirse()
    {
        ColaDeLlamantes.VentanasSinOirloDeFabrica.Should().Be(3);

        var s = LlamandoCq();
        s.CiclosSinRespuesta = 50;
        s.Procesar(Suya(1), [Oido("EA8DLF IZ2ABC JN45"), Oido("EA8DLF K1ABC FN42")]);

        s.Procesar(Suya(2), [Oido("EA8DLF IZ2ABC JN45")]);
        s.Procesar(Suya(3), [Oido("EA8DLF IZ2ABC JN45")]);
        s.Cola.EnEspera.Should().ContainSingle("dos ventanas sin oirle todavia se le perdonan");

        s.Procesar(Suya(4), [Oido("EA8DLF IZ2ABC JN45")]);
        s.Cola.EnEspera.Should().BeEmpty("tres ventanas suyas sin oirle: ya no esta");
    }

    [Fact]
    public void ConQuienYaSeCompletoEnEstaSesionNiEntraEnLaColaNiSeEligeSolo()
    {
        var s = LlamandoCq(unSoloQso: false);
        s.Procesar(Suya(1), [Oido("EA8DLF IZ2ABC JN45")]);
        HastaElRr73Propio(s, 2).Tx.Should().Be(6);
        s.EmisionHecha(6);

        s.Procesar(Suya(3), [Oido("EA8DLF IZ2ABC JN45")]).Tx.Should().Be(6, "vuelve a llamar con el contacto ya hecho: no se le contesta solo");
        s.DxCall.Should().BeEmpty();
        s.Cola.EnEspera.Should().BeEmpty();
    }

    [Fact]
    public void AnsB4DejaAlYaTrabajadoEnLaListaPeroNoLoEligeSolo()
    {
        var s = LlamandoCq(unSoloQso: true);
        s.AnsB4 = true;
        s.Procesar(Suya(1),
        [
            Oido("EA8DLF IZ2ABC JN45"), Oido("EA8DLF K1ABC FN42", trabajado: true), Oido("EA8DLF PY2ZZZ GG66"),
        ]);
        s.Cola.EnEspera.Select(l => l.Indicativo).Should().Equal("K1ABC", "PY2ZZZ");
        s.Cola.EnEspera[0].TrabajadoAntes.Should().BeTrue();

        var fin = HastaElRr73Propio(s, 2);

        fin.PasaA.Should().Be("PY2ZZZ", "K1ABC ya esta trabajado: AnsB4 lo salta aunque vaya delante");
        s.Cola.EnEspera.Select(l => l.Indicativo).Should().Equal("K1ABC");

        // Solo queda el ya trabajado: a efectos de seguir solo, la cola esta vacia.
        s.EmisionHecha(2);
        s.Procesar(Suya(3), [Oido("EA8DLF PY2ZZZ R-01"), Oido("EA8DLF K1ABC FN42", trabajado: true)]);
        s.EmisionHecha(4).Parada.Should().NotBeNull();
    }

    [Fact]
    public void SiElCorresponsalDesapareceSePasaAlSiguienteDeLaCola()
    {
        var s = LlamandoCq();
        s.CiclosSinRespuesta = 2;
        s.Procesar(Suya(1), [Oido("EA8DLF IZ2ABC JN45"), Oido("EA8DLF K1ABC FN42")]);
        s.EmisionHecha(2);
        s.Procesar(Suya(2), [Oido("EA8DLF K1ABC FN42")]).Tx.Should().Be(2);
        s.EmisionHecha(2);

        var decision = s.Procesar(Suya(3), [Oido("EA8DLF K1ABC FN42")]);

        decision.Parada.Should().BeNull();
        decision.PasaA.Should().Be("K1ABC");
        decision.Tx.Should().Be(2);
        s.DxCall.Should().Be("K1ABC");
    }

    // ── El operador manda ───────────────────────────────────────────────────

    [Fact]
    public void SinLaSeleccionAutomaticaLaColaSeLlenaPeroNoSeContestaANadieSolo()
    {
        var s = LlamandoCq();
        s.LlamarAlPrimero = false;

        s.Procesar(Suya(1), [Oido("EA8DLF IZ2ABC JN45"), Oido("EA8DLF K1ABC FN42")]).Tx.Should().Be(6);

        s.DxCall.Should().BeEmpty();
        s.Cola.EnEspera.Select(l => l.Indicativo).Should().Equal("IZ2ABC", "K1ABC");
    }

    [Fact]
    public void ElegirAUnoDeLaListaLeContestaYaYLaSesionSigueSiendoLaDelCq()
    {
        var s = LlamandoCq();
        s.Procesar(Suya(1), [Oido("EA8DLF IZ2ABC JN45"), Oido("EA8DLF K1ABC FN42"), Oido("EA8DLF PY2ZZZ GG66", db: -1)]);

        // A los 2 s de una ventana suya: la siguiente es la propia.
        var decision = s.AtenderDeLaCola("PY2ZZZ", Suya(2) + TimeSpan.FromSeconds(2));

        decision.Tx.Should().Be(2);
        decision.PasaA.Should().Be("PY2ZZZ");
        s.DxCall.Should().Be("PY2ZZZ");
        s.InformeEnviado.Should().Be(-1);
        s.Cola.EnEspera.Select(l => l.Indicativo).Should().Equal("K1ABC");

        // Al acabar con el, sigue con el que quedaba.
        HastaElRr73Propio(s, 3).PasaA.Should().Be("K1ABC");

        s.AtenderDeLaCola("NADIE1", Suya(4)).Should().Be(DecisionDelSecuenciador.Nada);
    }

    [Fact]
    public void QuitarAUnoDeLaListaLoSaltaAlAcabar()
    {
        var s = LlamandoCq();
        s.Procesar(Suya(1), [Oido("EA8DLF IZ2ABC JN45"), Oido("EA8DLF K1ABC FN42"), Oido("EA8DLF PY2ZZZ GG66")]);

        s.Cola.Quitar("K1ABC").Should().BeTrue();

        HastaElRr73Propio(s, 2).PasaA.Should().Be("PY2ZZZ");
    }

    [Fact]
    public void ElDobleClicAManoVaciaLaColaYNoSigueSolo()
    {
        var s = LlamandoCq(unSoloQso: false);
        s.Procesar(Suya(1), [Oido("EA8DLF IZ2ABC JN45"), Oido("EA8DLF K1ABC FN42")]);

        s.Iniciar(Oido("EA8DLF PY2ZZZ GG66"), Suya(2), Suya(2) + TimeSpan.FromSeconds(2)).Tx.Should().Be(2);
        s.Cola.EnEspera.Should().BeEmpty();

        s.EmisionHecha(2);
        s.Procesar(Suya(3), [Oido("EA8DLF PY2ZZZ R-05"), Oido("EA8DLF K1ABC FN42")]).Tx.Should().Be(4);
        s.Cola.EnEspera.Should().BeEmpty("un contacto elegido a mano no es del CQ: no hace cola");

        // Y como siempre en un contacto a mano: se espera su 73 y con el se para.
        s.EmisionHecha(4).Should().Be(DecisionDelSecuenciador.Nada);
        s.Procesar(Suya(4), [Oido("EA8DLF PY2ZZZ 73")]).Parada.Should().NotBeNull();
        s.Activo.Should().BeFalse();
    }

    [Fact]
    public void PararYCambiarDeModoVacianLaCola()
    {
        var s = LlamandoCq();
        s.Procesar(Suya(1), [Oido("EA8DLF IZ2ABC JN45"), Oido("EA8DLF K1ABC FN42")]);
        s.Parar();
        s.Cola.EnEspera.Should().BeEmpty();

        s = LlamandoCq();
        s.Procesar(Suya(1), [Oido("EA8DLF IZ2ABC JN45"), Oido("EA8DLF K1ABC FN42")]);
        s.CambiarPeriodo(TimeSpan.FromSeconds(7.5));
        s.Cola.EnEspera.Should().BeEmpty();

        s = LlamandoCq();
        s.Procesar(Suya(1), [Oido("EA8DLF IZ2ABC JN45"), Oido("EA8DLF K1ABC FN42")]);
        s.OlvidarLaSesion();
        s.Cola.EnEspera.Should().BeEmpty();
        s.Activo.Should().BeTrue("cambiar de banda vacia la cola, pero no para la secuencia por su cuenta");
    }

    [Fact]
    public void MandarElTx6AManoTambienEsLlamarCq()
    {
        var s = new SecuenciadorDeQso { Periodo = Periodo, MiIndicativo = "EA8DLF" };
        s.Enviar(6, Par + TimeSpan.FromSeconds(1));
        s.EmisionHecha(6);

        s.Procesar(Suya(1), [Oido("EA8DLF IZ2ABC JN45"), Oido("EA8DLF K1ABC FN42")]).Tx.Should().Be(2);
        s.Cola.EnEspera.Should().ContainSingle();
        HastaElRr73Propio(s, 2).PasaA.Should().Be("K1ABC");
    }

    [Fact]
    public void SinPermisoParaPasarSoloAlSiguienteSeHaceLoDeSiempreYLaColaSeConserva()
    {
        // Es lo que pide la pantalla cuando el contacto no se apunta solo: pasar al siguiente
        // borraria de la vista un contacto que el operador aun tiene que guardar a mano.
        var s = LlamandoCq(unSoloQso: true);
        s.PasarSoloAlSiguiente = false;
        s.Procesar(Suya(1), [Oido("EA8DLF IZ2ABC JN45"), Oido("EA8DLF K1ABC FN42")]);

        HastaElRr73Propio(s, 2).Parada.Should().NotBeNull();
        s.Cola.EnEspera.Should().ContainSingle();
    }

    // ── Lo que no cambia ────────────────────────────────────────────────────

    [Fact]
    public void EnConcursoNoHayColaYElRr73PropioNoCierraElContacto()
    {
        var s = new SecuenciadorDeQso { Periodo = Periodo, MiIndicativo = "EA8DLF", Operacion = TipoDeOperacion.FieldDay };
        s.LlamarCq(Par + TimeSpan.FromSeconds(1));
        s.EmisionHecha(6);

        s.Procesar(Suya(1), [Oido("EA8DLF W9XYZ 2A EMA"), Oido("EA8DLF K1ABC 1D ME")]).Tx.Should().Be(3);
        s.Cola.EnEspera.Should().BeEmpty();
        s.EmisionHecha(3);
        s.Procesar(Suya(2), [Oido("EA8DLF W9XYZ R 2A EMA")]).Tx.Should().Be(4);

        s.EmisionHecha(4).Should().Be(DecisionDelSecuenciador.Nada);
        s.Activo.Should().BeTrue();
    }

    // ── Los mensajes propios no son de nadie ────────────────────────────────

    [Fact]
    public void LoQueSeOyeDeUnoMismoNoSeEligeNiHaceCola()
    {
        var s = LlamandoCq();
        s.MiIndicativo = "EA8DLF";

        // Un eco raro: uno mismo «llamandose», con y sin sufijo de operacion.
        var decision = s.Procesar(Suya(1), [Oido("EA8DLF EA8DLF IL27"), Oido("EA8DLF EA8DLF/P IL27"), Oido("CQ EA8DLF IL27")]);

        decision.Tx.Should().Be(6);
        s.DxCall.Should().BeEmpty();
        s.Cola.EnEspera.Should().BeEmpty();

        s.Iniciar(Oido("CQ EA8DLF IL27"), Suya(1), Suya(1) + TimeSpan.FromSeconds(16)).Should().Be(DecisionDelSecuenciador.Nada);
        s.DxCall.Should().BeEmpty("a uno mismo no se le puede elegir de corresponsal");
    }
}
