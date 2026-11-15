using FluentAssertions;
using Nodisla.Cuaderno.Ui.Digital;

namespace Nodisla.Cuaderno.Ui.Pruebas;

/// <summary>
/// El contacto contestado a mano (doble clic) se cierra al emitir el RR73 propio, igual que el
/// nacido del CQ; y, ya parada la secuencia, se le repite el RR73 a quien no lo copio, pero solo
/// durante unas ventanas y unas veces contadas. Sin radio ni reloj de pared.
/// </summary>
public sealed class CierreConRr73AManoPruebas
{
    private static readonly TimeSpan Periodo = TimeSpan.FromSeconds(15);
    private static readonly DateTimeOffset Par = DateTimeOffset.UnixEpoch;

    /// <summary>La ventana numero <paramref name="n"/> del corresponsal (las pares).</summary>
    private static DateTimeOffset Suya(int n) => Par + (2 * n * Periodo);

    /// <summary>La ventana propia que sigue a la suya numero <paramref name="n"/> (las impares).</summary>
    private static DateTimeOffset Mia(int n) => Suya(n) + Periodo;

    private static MensajeOido Oido(string texto, int db = -10, int tono = 1500) =>
        new(InterpreteDeMensajes.Analizar(texto), db, tono);

    /// <summary>
    /// PY2ZZZ me llama, se le contesta a mano con el doble clic (Tx2), manda su R+informe en su
    /// ventana 1 y toca el Tx4, todavia sin emitir.
    /// </summary>
    private static SecuenciadorDeQso AManoConElTx4Listo(bool rrr = false, TipoDeOperacion operacion = TipoDeOperacion.Normal)
    {
        var s = new SecuenciadorDeQso { Periodo = Periodo, MiIndicativo = "EA8DLF", Tx4ConRrr = rrr, Operacion = operacion, UnSoloQso = false };
        s.Iniciar(Oido("EA8DLF PY2ZZZ GG66", db: -3), Suya(0), Suya(0) + TimeSpan.FromSeconds(2)).Tx.Should().Be(2);
        s.EmisionHecha(2);

        var conR = s.Procesar(Suya(1), [Oido("EA8DLF PY2ZZZ R-05")]);
        conR.Tx.Should().Be(4);
        conR.ContactoCompleto.Should().BeTrue();
        return s;
    }

    /// <summary>Lo mismo, con el RR73 ya emitido: el contacto cerrado y la secuencia parada.</summary>
    private static SecuenciadorDeQso AManoCerradoConRr73()
    {
        var s = AManoConElTx4Listo();
        s.EmisionHecha(4).Parada.Should().NotBeNull();
        return s;
    }

    // ── El cierre ───────────────────────────────────────────────────────────

    [Fact]
    public void ElContactoContestadoAManoSeCierraAlEmitirElRr73YSePara()
    {
        var s = AManoConElTx4Listo();

        var fin = s.EmisionHecha(4);

        fin.Parada.Should().Contain("terminado");
        fin.Tx.Should().BeNull("a mano no se sigue solo: ni cola ni CQ, aunque «1 QSO» este quitado");
        fin.PasaA.Should().BeNull();
        fin.ContactoCompleto.Should().BeFalse("ya se anuncio al recibir su R+informe: no se apunta dos veces");
        s.Activo.Should().BeFalse();
        s.Terminado.Should().BeTrue();
        s.DxCall.Should().Be("PY2ZZZ", "el contacto hecho sigue a la vista");
    }

    [Fact]
    public void CerradoAManoNoSeRepiteElRr73ACiegasNiSeAcabaEnDesaparecido()
    {
        var s = AManoCerradoConRr73();

        for (var n = 2; n <= 12; n++)
        {
            s.Procesar(Suya(n), [Oido("CQ K1ABC FN42")]).Should().Be(DecisionDelSecuenciador.Nada);
            s.Procesar(Mia(n), []).Should().Be(DecisionDelSecuenciador.Nada);
        }

        s.Activo.Should().BeFalse();
    }

    [Fact]
    public void SiElOtroMandaSu73TrasElCierreNoPasaNada()
    {
        var s = AManoCerradoConRr73();

        s.Procesar(Suya(2), [Oido("EA8DLF PY2ZZZ 73")]).Should().Be(DecisionDelSecuenciador.Nada);
        s.Activo.Should().BeFalse();
    }

    [Fact]
    public void ConRrrElContactoAManoSigueEsperandoEl73()
    {
        var s = AManoConElTx4Listo(rrr: true);

        s.EmisionHecha(4).Should().Be(DecisionDelSecuenciador.Nada, "RRR pide confirmacion");
        s.Activo.Should().BeTrue();
        s.Procesar(Suya(2), []).Tx.Should().Be(4, "sin su 73 se repite el RRR");
        s.EmisionHecha(4);

        var fin = s.Procesar(Suya(3), [Oido("EA8DLF PY2ZZZ 73")]);
        fin.Parada.Should().Contain("terminado");
        fin.ContactoCompleto.Should().BeFalse();
        s.Activo.Should().BeFalse();
    }

    [Fact]
    public void EnConcursoElRr73PropioDeUnContactoAManoNoLoCierra()
    {
        var s = new SecuenciadorDeQso { Periodo = Periodo, MiIndicativo = "EA8DLF", Operacion = TipoDeOperacion.FieldDay };
        s.Iniciar(Oido("EA8DLF W9XYZ 2A EMA"), Suya(0), Suya(0) + TimeSpan.FromSeconds(2));
        s.EmisionHecha(s.TxActual);
        s.Procesar(Suya(1), [Oido("EA8DLF W9XYZ R 2A EMA")]).Tx.Should().Be(4);

        s.EmisionHecha(4).Should().Be(DecisionDelSecuenciador.Nada);
        s.Activo.Should().BeTrue();
    }

    // ── El que no copio el RR73, con la secuencia ya parada ─────────────────

    [Fact]
    public void LosValoresDeLaRepeticionSonTresVentanasYTresReenvios()
    {
        SecuenciadorDeQso.VentanasAtentoALaRepeticion.Should().Be(3);
        SecuenciadorDeQso.ReenviosMaximosDelRr73.Should().Be(3);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void SiRepiteSuRInformeEnLasTresVentanasSiguientesSeLeRepiteElRr73SinApuntarloOtraVez(int ventana)
    {
        var s = AManoCerradoConRr73();
        for (var n = 2; n < ventana; n++) s.Procesar(Suya(n), []);

        var repite = s.Procesar(Suya(ventana), [Oido("EA8DLF PY2ZZZ R-05")]);

        repite.Tx.Should().Be(4, "no copio el RR73: se le repite");
        repite.ReanudaTx.Should().BeTrue("la secuencia estaba parada: hay que volver a habilitar Tx");
        repite.PasaA.Should().Be("PY2ZZZ");
        repite.ContactoCompleto.Should().BeFalse("ese contacto ya se dio por completo: no se apunta dos veces");
        repite.Parada.Should().BeNull();
        s.Activo.Should().BeTrue();
        s.TxActual.Should().Be(4);
        s.DxCall.Should().Be("PY2ZZZ");
        s.DxGrid.Should().Be("GG66");
        s.InformeEnviado.Should().Be(-3);
        s.InformeRecibido.Should().Be(-5);

        var fin = s.EmisionHecha(4);
        fin.Parada.Should().Contain("terminado", "sale el RR73 y se para otra vez");
        fin.ContactoCompleto.Should().BeFalse();
        fin.Tx.Should().BeNull();
        s.Activo.Should().BeFalse();
    }

    [Fact]
    public void PasadasTresVentanasSuyasLaRepeticionYaNoSeAtiende()
    {
        var s = AManoCerradoConRr73();
        s.Procesar(Suya(2), []);
        s.Procesar(Suya(3), []);
        s.Procesar(Suya(4), []);

        s.Procesar(Suya(5), [Oido("EA8DLF PY2ZZZ R-05")]).Should().Be(DecisionDelSecuenciador.Nada);
        s.Activo.Should().BeFalse("parada desde hace 90 s: ya no arranca sola");
    }

    [Fact]
    public void LasVentanasPropiasNoCuentanParaElPlazoNiDisparanNada()
    {
        var s = AManoCerradoConRr73();
        for (var n = 1; n <= 3; n++) s.Procesar(Mia(n), [Oido("EA8DLF PY2ZZZ R-05")]).Should().Be(DecisionDelSecuenciador.Nada);

        s.Procesar(Suya(2), [Oido("EA8DLF PY2ZZZ R-05")]).Tx.Should().Be(4);
    }

    [Fact]
    public void CadaRepeticionReabreElPlazoPeroComoMuchoSeRepiteTresVeces()
    {
        var s = AManoCerradoConRr73();
        var ventana = 2;

        for (var vez = 1; vez <= SecuenciadorDeQso.ReenviosMaximosDelRr73; vez++)
        {
            // Deja pasar dos ventanas y repite en la tercera: el plazo se reabre con cada RR73.
            s.Procesar(Suya(ventana++), []);
            s.Procesar(Suya(ventana++), []);
            s.Procesar(Suya(ventana++), [Oido("EA8DLF PY2ZZZ R-05")]).Tx.Should().Be(4, $"repeticion numero {vez}");
            s.EmisionHecha(4).Parada.Should().NotBeNull();
        }

        s.Procesar(Suya(ventana), [Oido("EA8DLF PY2ZZZ R-05")]).Should().Be(DecisionDelSecuenciador.Nada, "cuatro RR73 ya son bastantes");
        s.Activo.Should().BeFalse();
    }

    [Fact]
    public void SoloVuelveAArrancarElRInformeDelMismoCorresponsalYDirigidoAMi()
    {
        var s = AManoCerradoConRr73();

        s.Procesar(Suya(2),
        [
            Oido("EA8DLF K1ABC R-05"),      // otro
            Oido("DL1XYZ PY2ZZZ R-05"),     // el mismo, pero a otro
            Oido("EA8DLF PY2ZZZ GG66"),     // el mismo, llamando de nuevo
            Oido("EA8DLF PY2ZZZ -05"),      // el mismo, informe sin R
        ]).Should().Be(DecisionDelSecuenciador.Nada);
        s.Activo.Should().BeFalse();
    }

    [Fact]
    public void SiElOperadorParaAManoOEligeAOtroLaRepeticionYaNoSeAtiende()
    {
        var s = AManoCerradoConRr73();
        s.Parar();
        s.Procesar(Suya(2), [Oido("EA8DLF PY2ZZZ R-05")]).Should().Be(DecisionDelSecuenciador.Nada);

        s = AManoCerradoConRr73();
        s.OlvidarLaSesion(); // cambio de banda
        s.Procesar(Suya(2), [Oido("EA8DLF PY2ZZZ R-05")]).Should().Be(DecisionDelSecuenciador.Nada);

        s = AManoCerradoConRr73();
        s.Iniciar(Oido("CQ K1ABC FN42"), Suya(2), Suya(2) + TimeSpan.FromSeconds(2)).Tx.Should().Be(1);
        s.EmisionHecha(1);
        s.Procesar(Suya(3), [Oido("EA8DLF PY2ZZZ R-05")]).Tx.Should().Be(1, "ahora se esta con K1ABC");
        s.DxCall.Should().Be("K1ABC");
    }

    [Fact]
    public void SiTrasElCierreSePasaARrrLaRepeticionNoSeAtiende()
    {
        var s = AManoCerradoConRr73();
        s.Tx4ConRrr = true;

        s.Procesar(Suya(2), [Oido("EA8DLF PY2ZZZ R-05")]).Should().Be(DecisionDelSecuenciador.Nada);
    }

    [Fact]
    public void TambienSeAtiendeTrasPararseUnContactoDelCqCon1Qso()
    {
        var s = new SecuenciadorDeQso { Periodo = Periodo, MiIndicativo = "EA8DLF", UnSoloQso = true };
        s.LlamarCq(Par + TimeSpan.FromSeconds(1));
        s.EmisionHecha(6);
        s.Procesar(Suya(1), [Oido("EA8DLF IZ2ABC JN45", db: -3)]).Tx.Should().Be(2);
        s.EmisionHecha(2);
        s.Procesar(Suya(2), [Oido("EA8DLF IZ2ABC R-05")]).Tx.Should().Be(4);
        s.EmisionHecha(4).Parada.Should().NotBeNull("«1 QSO» y nadie en espera: se para");

        var repite = s.Procesar(Suya(3), [Oido("EA8DLF IZ2ABC R-05")]);

        repite.Tx.Should().Be(4);
        repite.ReanudaTx.Should().BeTrue();
        repite.ContactoCompleto.Should().BeFalse();

        var fin = s.EmisionHecha(4);
        fin.Parada.Should().NotBeNull("parada estaba y parada se queda: no vuelve a llamar CQ");
        fin.Tx.Should().BeNull();
    }

    // ── Con RRR y sin su 73: completo, no «desaparecido» ────────────────────

    [Fact]
    public void ConRrrSiNoLlegaSu73ElMotivoDiceQueElContactoEstaCompletoYNoDesaparecido()
    {
        var s = AManoConElTx4Listo(rrr: true);
        s.CiclosSinRespuesta = 3;
        for (var n = 0; n < 3; n++) s.EmisionHecha(4);

        var decision = s.Procesar(Suya(4), []);

        decision.Parada.Should().Contain("PY2ZZZ").And.Contain("completo").And.Contain("73");
        decision.Parada.Should().NotContain("desaparecido");
        decision.CompletoSinSu73.Should().BeTrue();
        decision.ContactoCompleto.Should().BeFalse("ya se anuncio: no se apunta dos veces");
        s.Activo.Should().BeFalse();
        s.Terminado.Should().BeTrue("el contacto esta hecho: lo que falta es la despedida");
    }

    [Fact]
    public void ElContactoQueSeQuedaAMediasSigueDiciendoDesaparecido()
    {
        var s = new SecuenciadorDeQso { Periodo = Periodo, MiIndicativo = "EA8DLF", CiclosSinRespuesta = 3 };
        s.Iniciar(Oido("EA8DLF PY2ZZZ GG66"), Suya(0), Suya(0) + TimeSpan.FromSeconds(2));
        for (var n = 0; n < 3; n++) s.EmisionHecha(2);

        var decision = s.Procesar(Suya(4), []);

        decision.Parada.Should().Contain("desaparecido");
        decision.CompletoSinSu73.Should().BeFalse();
        s.Terminado.Should().BeFalse();
    }
}
