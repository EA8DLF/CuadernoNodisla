using FluentAssertions;
using Nodisla.Cuaderno.Ui.Digital;

namespace Nodisla.Cuaderno.Ui.Pruebas;

/// <summary>
/// La secuencia automatica del contacto, sin radio, sin audio y sin reloj de pared: las
/// ventanas son horas que se le pasan.
/// </summary>
public sealed class SecuenciadorDeQsoPruebas
{
    private static readonly TimeSpan Periodo = TimeSpan.FromSeconds(15);
    private static readonly DateTimeOffset Par = DateTimeOffset.UnixEpoch;
    private static readonly DateTimeOffset Impar = Par + Periodo;

    private static SecuenciadorDeQso Nuevo(TipoDeOperacion operacion = TipoDeOperacion.Normal) => new()
    {
        Periodo = Periodo,
        MiIndicativo = "EA8DLF",
        Operacion = operacion,
    };

    private static MensajeOido Oido(string texto, int db = -10, int tono = 1500) =>
        new(InterpreteDeMensajes.Analizar(texto), db, tono);

    [Fact]
    public void LaParidadSaleDeLaHoraDeLaVentana()
    {
        SecuenciadorDeQso.ParidadDe(Par, Periodo).Should().Be(0);
        SecuenciadorDeQso.ParidadDe(Impar, Periodo).Should().Be(1);
        SecuenciadorDeQso.ParidadDe(Par + (2 * Periodo), Periodo).Should().Be(0);

        // A los 3 segundos de la ventana par, la siguiente es la impar.
        SecuenciadorDeQso.ParidadDeLaSiguiente(Par + TimeSpan.FromSeconds(3), Periodo).Should().Be(1);
    }

    [Fact]
    public void ContestarAUnCqRecorreLaSecuenciaEnteraHastaEl73()
    {
        var s = Nuevo();

        // Se oye el CQ en la ventana par; se contesta en la impar, que ya es la siguiente.
        var inicio = s.Iniciar(Oido("CQ EA5XYZ IM98", db: -7), Par, Par + TimeSpan.FromSeconds(2));
        inicio.Tx.Should().Be(1);
        s.Paridad.Should().Be(1);
        s.DxCall.Should().Be("EA5XYZ");
        s.DxGrid.Should().Be("IM98");
        s.InformeEnviado.Should().Be(-7);

        s.EmisionHecha(1);

        // Mi propia ventana (impar) no decide nada.
        s.Procesar(Impar, [Oido("CQ EA5XYZ IM98")]).Tx.Should().BeNull();

        // El DX contesta con el informe: toca R+informe.
        var conInforme = s.Procesar(Par + (2 * Periodo), [Oido("EA8DLF EA5XYZ -12")]);
        conInforme.Tx.Should().Be(3);
        conInforme.ContactoCompleto.Should().BeFalse();
        s.InformeRecibido.Should().Be(-12);
        s.EmisionHecha(3);

        // RR73: contacto hecho, se manda el 73 y con el 73 se para.
        var rr73 = s.Procesar(Par + (4 * Periodo), [Oido("EA8DLF EA5XYZ RR73")]);
        rr73.Tx.Should().Be(5);
        rr73.ContactoCompleto.Should().BeTrue();

        var tras73 = s.EmisionHecha(5);
        tras73.Parada.Should().NotBeNull();
        s.Activo.Should().BeFalse();
    }

    [Fact]
    public void LlamandoCqSeContestaAlPrimeroSoloSiSePide()
    {
        var s = Nuevo();
        s.LlamarCq(Par + TimeSpan.FromSeconds(1)).Tx.Should().Be(6);
        s.Paridad.Should().Be(1);
        s.EmisionHecha(6);

        // Sin «llamar al primero» se sigue llamando aunque alguien conteste.
        s.Procesar(Par + (2 * Periodo), [Oido("EA8DLF IZ2ABC JN45")]).Tx.Should().Be(6);

        s.LlamarAlPrimero = true;
        var decision = s.Procesar(Par + (4 * Periodo), [Oido("EA8DLF IZ2ABC JN45", db: -3), Oido("EA8DLF K1ABC FN42")]);
        decision.Tx.Should().Be(2);
        s.DxCall.Should().Be("IZ2ABC");
        s.InformeEnviado.Should().Be(-3);

        // Si me responde R+informe, RR73 y contacto completo.
        s.EmisionHecha(2);
        var r = s.Procesar(Par + (6 * Periodo), [Oido("EA8DLF IZ2ABC R-05")]);
        r.Tx.Should().Be(4);
        r.ContactoCompleto.Should().BeTrue();
        s.InformeRecibido.Should().Be(-5);

        // Y con su 73 se acaba sin mandar nada mas.
        s.EmisionHecha(4);
        var fin = s.Procesar(Par + (8 * Periodo), [Oido("EA8DLF IZ2ABC 73")]);
        fin.Tx.Should().BeNull();
        fin.Parada.Should().Contain("terminado");
        s.Activo.Should().BeFalse();
    }

    [Fact]
    public void SaltarTx1EmpiezaPorElInforme()
    {
        var s = Nuevo();
        s.SaltarTx1 = true;

        s.Iniciar(Oido("CQ EA5XYZ IM98"), Par, Par + TimeSpan.FromSeconds(2)).Tx.Should().Be(2);
    }

    [Fact]
    public void SiElDobleClicLlegaTardeSeEsperaALaVentanaBuena()
    {
        var s = Nuevo();

        // Se oyo en la ventana par, pero ya estamos en la impar, casi acabando: la siguiente
        // ventana es par (la del DX) y no se emite hasta que cierre.
        var tarde = s.Iniciar(Oido("CQ EA5XYZ IM98"), Par, Impar + TimeSpan.FromSeconds(14));
        tarde.Tx.Should().BeNull();
        s.Activo.Should().BeTrue();
        s.TxActual.Should().Be(1);

        // Cierra la ventana par sin nada del DX: ahora si toca.
        s.Procesar(Par + (2 * Periodo), []).Tx.Should().Be(1);
    }

    [Fact]
    public void ElCorresponsalQueDesapareceParaLaSecuencia()
    {
        var s = Nuevo();
        s.CiclosSinRespuesta = 3;
        s.Iniciar(Oido("CQ EA5XYZ IM98"), Par, Par + TimeSpan.FromSeconds(2));

        for (var i = 0; i < 3; i++)
        {
            s.EmisionHecha(1);
        }

        var decision = s.Procesar(Par + (6 * Periodo), [Oido("CQ K1ABC FN42")]);
        decision.Parada.Should().Contain("EA5XYZ").And.Contain("desaparecido");
        s.Activo.Should().BeFalse();
    }

    [Fact]
    public void ElVigilanteParaElCqQueNadieContesta()
    {
        var s = Nuevo();
        s.CiclosMaximosLlamandoCq = 2;
        s.LlamarCq(Par);
        s.EmisionHecha(6);
        s.EmisionHecha(6);

        s.Procesar(Par + (4 * Periodo), []).Parada.Should().Contain("Vigilante");
    }

    [Fact]
    public void EnHoundSeSigueAlFoxYNoSeManda73()
    {
        var s = Nuevo(TipoDeOperacion.Hound);
        s.Iniciar(Oido("CQ KH1/KH7Z AJ10"), Par, Par + TimeSpan.FromSeconds(2)).Tx.Should().Be(1);
        s.EmisionHecha(1);

        // El fox contesta en un mensaje doble, en su tono: hay que ir a ese tono con Tx3.
        var decision = s.Procesar(Par + (2 * Periodo), [Oido("K1ABC RR73; EA8DLF <KH1/KH7Z> -10", tono: 480)]);
        decision.Tx.Should().Be(3);
        decision.TonoTx.Should().Be(480);
        s.InformeRecibido.Should().Be(-10);
        s.EmisionHecha(3);

        var fin = s.Procesar(Par + (4 * Periodo), [Oido("EA8DLF KH1/KH7Z RR73")]);
        fin.ContactoCompleto.Should().BeTrue();
        fin.Tx.Should().BeNull("al fox no se le manda 73");
        s.Activo.Should().BeFalse();
    }

    [Fact]
    public void EnConcursoVhfElLocalizadorHaceDeInforme()
    {
        var s = Nuevo(TipoDeOperacion.NaVhf);
        s.Iniciar(Oido("CQ TEST K1ABC FN42"), Par, Par + TimeSpan.FromSeconds(2)).Tx.Should().Be(1);
        s.EmisionHecha(1);

        // Su localizador sin R es su intercambio: se contesta con R y el mio.
        s.Procesar(Par + (2 * Periodo), [Oido("EA8DLF K1ABC FN42")]).Tx.Should().Be(3);
        s.EmisionHecha(3);

        var fin = s.Procesar(Par + (4 * Periodo), [Oido("EA8DLF K1ABC RR73")]);
        fin.ContactoCompleto.Should().BeTrue();
        fin.Tx.Should().Be(5);
    }

    [Fact]
    public void EnFieldDayElIntercambioSeGuarda()
    {
        var s = Nuevo(TipoDeOperacion.FieldDay);
        s.LlamarAlPrimero = true;
        s.LlamarCq(Par + TimeSpan.FromSeconds(1));
        s.EmisionHecha(6);

        var d = s.Procesar(Par + (2 * Periodo), [Oido("EA8DLF W9XYZ 2A EMA")]);
        d.Tx.Should().Be(3);
        s.IntercambioRecibido.Should().Be("2A EMA");
    }
}
