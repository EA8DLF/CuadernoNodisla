using FluentAssertions;
using Nodisla.Cuaderno.Ui.Digital;

namespace Nodisla.Cuaderno.Ui.Pruebas;

/// <summary>
/// El lado fox de fox/hound: varios cazadores a la vez, cada uno con su tono y en su propio paso,
/// sin radio, sin audio y sin reloj de pared.
/// </summary>
public sealed class SecuenciadorDeFoxPruebas
{
    private static readonly TimeSpan Periodo = TimeSpan.FromSeconds(15);
    private static readonly DateTimeOffset Par = DateTimeOffset.UnixEpoch;
    private static readonly DateTimeOffset Impar = Par + Periodo;

    private static SecuenciadorDeFox Nuevo(int cazadoresSimultaneos = 5) => new()
    {
        Periodo = Periodo,
        MiIndicativo = "EA8DLF",
        CazadoresSimultaneos = cazadoresSimultaneos,
        SeparacionMinimaDeTonosHz = 62.5,
        TonoBaseHz = 1000,
        TonoTopeHz = 3000,
    };

    private static MensajeOido Oido(string texto, int db = -10, int tono = 1500) =>
        new(InterpreteDeMensajes.Analizar(texto), db, tono);

    [Fact]
    public void SinEmpezarNoDecideNada()
    {
        var f = Nuevo();
        var decision = f.Procesar(Impar, [Oido("EA8DLF EA5XYZ IM98")]);
        decision.Transmisiones.Should().BeEmpty();
        f.Cazadores.Should().BeEmpty();
    }

    [Fact]
    public void EmpezarFijaLaParidadDeLaSiguienteVentana()
    {
        var f = Nuevo();
        f.Empezar(Par + TimeSpan.FromSeconds(1));
        f.Paridad.Should().Be(1);
        f.Activo.Should().BeTrue();
    }

    [Fact]
    public void LaVentanaPropiaNoDecideNada()
    {
        var f = Nuevo();
        f.Empezar(Par);
        f.Paridad.Should().Be(1);

        // La ventana que acaba de cerrar (Impar, paridad 1) es la propia: no se toca nada.
        var decision = f.Procesar(Impar, [Oido("EA8DLF EA5XYZ IM98")]);
        decision.Transmisiones.Should().BeEmpty();
        f.Cazadores.Should().BeEmpty();
    }

    [Fact]
    public void UnaLlamadaNuevaEntraYRecibeUnTonoPropio()
    {
        var f = Nuevo();
        f.Empezar(Par);

        var decision = f.Procesar(Par, [Oido("EA8DLF EA5XYZ IM98", db: -7, tono: 1200)]);

        f.Cazadores.Should().ContainKey("EA5XYZ");
        var cazador = f.Cazadores["EA5XYZ"];
        cazador.Paso.Should().Be(PasoDeCazador.EsperandoInforme);
        cazador.Grid.Should().Be("IM98");

        decision.Transmisiones.Should().ContainSingle();
        var tx = decision.Transmisiones[0];
        tx.Indicativo.Should().Be("EA5XYZ");
        tx.Texto.Should().Be("EA5XYZ EA8DLF -07");
        tx.TonoHz.Should().Be(cazador.TonoHz);

        // El tono en el que de verdad se le oyo es el suyo, no el que le toca en la mezcla.
        cazador.TonoRxHz.Should().Be(1200);
    }

    [Fact]
    public void ElPileupEnteroRecorreLosPasosHastaCompletarse()
    {
        var f = Nuevo();
        f.Empezar(Par);

        // 1) Llama por primera vez: se le manda el informe.
        var d1 = f.Procesar(Par, [Oido("EA8DLF EA5XYZ IM98", db: -7)]);
        d1.Transmisiones.Should().ContainSingle(t => t.Texto == "EA5XYZ EA8DLF -07");
        f.Cazadores["EA5XYZ"].Paso.Should().Be(PasoDeCazador.EsperandoInforme);

        // 2) Contesta con R+informe: toca RR73, y con eso se da por completo (el fox no espera
        // confirmacion).
        var d2 = f.Procesar(Par + (2 * Periodo), [Oido("EA8DLF EA5XYZ R-12")]);
        d2.Transmisiones.Should().ContainSingle(t => t.Texto == "EA5XYZ EA8DLF RR73");
        d2.ContactosCompletados.Should().ContainSingle(c => c.Indicativo == "EA5XYZ");
        d2.ContactosCompletados[0].InformeRecibido.Should().Be(-12);
        f.Cazadores.Should().NotContainKey("EA5XYZ", "el tono queda libre en cuanto se pone en cola el RR73");
    }

    [Fact]
    public void VariosCazadoresALaVezRecibenTonosDistintosYSeparados()
    {
        var f = Nuevo();
        f.Empezar(Par);

        var decision = f.Procesar(Par,
        [
            Oido("EA8DLF EA1AAA IL18", db: -5),
            Oido("EA8DLF EA2BBB IL28", db: -8),
            Oido("EA8DLF EA3CCC IL38", db: -10),
        ]);

        decision.Transmisiones.Should().HaveCount(3);
        var tonos = f.Cazadores.Values.Select(c => c.TonoHz).OrderBy(t => t).ToList();
        tonos.Should().HaveCount(3);
        tonos.Distinct().Should().HaveCount(3, "cada cazador tiene que tener su propio tono");
        for (var i = 1; i < tonos.Count; i++)
            (tonos[i] - tonos[i - 1]).Should().BeGreaterThanOrEqualTo((int)Math.Ceiling(f.SeparacionMinimaDeTonosHz));
    }

    [Fact]
    public void NoSeAdmitenMasCazadoresDeLosConfigurados()
    {
        var f = Nuevo(cazadoresSimultaneos: 2);
        f.Empezar(Par);

        var oidos = new[]
        {
            Oido("EA8DLF EA1AAA IL18"),
            Oido("EA8DLF EA2BBB IL28"),
            Oido("EA8DLF EA3CCC IL38"),
        };
        f.Procesar(Par, oidos);

        f.Cazadores.Should().HaveCount(2, "el cupo es 2: el tercero se queda fuera por ahora");
    }

    [Fact]
    public void ElHuecoDeUnoQueSeVaLoOcupaElSiguienteDeLaCola()
    {
        var f = Nuevo(cazadoresSimultaneos: 1);
        f.Empezar(Par);

        // Solo cabe uno: EA1AAA entra, EA2BBB se queda fuera.
        f.Procesar(Par, [Oido("EA8DLF EA1AAA IL18"), Oido("EA8DLF EA2BBB IL28")]);
        f.Cazadores.Should().ContainKey("EA1AAA");
        f.Cazadores.Should().NotContainKey("EA2BBB");

        // EA1AAA completa su contacto: se libera el hueco.
        var completa = f.Procesar(Par + (2 * Periodo), [Oido("EA8DLF EA1AAA R-07")]);
        completa.ContactosCompletados.Should().Contain(c => c.Indicativo == "EA1AAA");
        f.Cazadores.Should().BeEmpty();

        // Ahora, con el hueco libre, EA2BBB por fin entra.
        f.Procesar(Par + (4 * Periodo), [Oido("EA8DLF EA2BBB IL28")]);
        f.Cazadores.Should().ContainKey("EA2BBB");
    }

    [Fact]
    public void UnCazadorQueNoContestaSeAbandonaTrasLosCiclosConfigurados()
    {
        var f = Nuevo();
        f.CiclosSinRespuesta = 2;
        f.Empezar(Par);

        f.Procesar(Par, [Oido("EA8DLF EA5XYZ IM98")]);
        f.Cazadores.Should().ContainKey("EA5XYZ");

        // Ventana 1 sin noticias suyas: un ciclo mas, todavia no se le abandona.
        var d1 = f.Procesar(Par + (2 * Periodo), []);
        d1.CazadoresAbandonados.Should().BeEmpty();
        f.Cazadores.Should().ContainKey("EA5XYZ");

        // Ventana 2 sin noticias: se cumple el limite y se abandona, liberando el tono.
        var d2 = f.Procesar(Par + (4 * Periodo), []);
        d2.CazadoresAbandonados.Should().ContainSingle("EA5XYZ");
        f.Cazadores.Should().BeEmpty();
    }

    [Fact]
    public void PararSeOlvidaDeTodosLosCazadores()
    {
        var f = Nuevo();
        f.Empezar(Par);
        f.Procesar(Par, [Oido("EA8DLF EA5XYZ IM98")]);
        f.Cazadores.Should().NotBeEmpty();

        f.Parar();

        f.Activo.Should().BeFalse();
        f.Cazadores.Should().BeEmpty();
        f.Procesar(Par + (2 * Periodo), [Oido("EA8DLF EA5XYZ IM98")]).Transmisiones.Should().BeEmpty();
    }

    [Fact]
    public void CazadoresSimultaneosSeAcotaAlMaximoDePrudencia()
    {
        var f = Nuevo();
        f.CazadoresSimultaneos = 999;
        f.CazadoresSimultaneos.Should().Be(SecuenciadorDeFox.MaximoDeCazadoresSimultaneos);

        f.CazadoresSimultaneos = 0;
        f.CazadoresSimultaneos.Should().Be(1);
    }

    [Fact]
    public void CambiarPeriodoParaYOlvidaLaParidad()
    {
        var f = Nuevo();
        f.Empezar(Par);
        f.Procesar(Par, [Oido("EA8DLF EA5XYZ IM98")]);

        f.CambiarPeriodo(TimeSpan.FromSeconds(7.5));

        f.Activo.Should().BeFalse();
        f.Paridad.Should().Be(-1);
        f.Cazadores.Should().BeEmpty();
        f.Periodo.Should().Be(TimeSpan.FromSeconds(7.5));
    }
}
