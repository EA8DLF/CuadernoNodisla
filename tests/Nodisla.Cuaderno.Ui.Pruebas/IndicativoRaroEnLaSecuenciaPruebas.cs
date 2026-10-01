using FluentAssertions;
using Nodisla.Cuaderno.Modos.Ft8;
using Nodisla.Cuaderno.Ui.Digital;

namespace Nodisla.Cuaderno.Ui.Pruebas;

/// <summary>
/// Contactos con indicativos que no caben en el formato corriente (HB10GBT, PJ4/K1ABC): que
/// Tx1–Tx6 salgan en un formato que se pueda emitir y que la secuencia los siga.
/// </summary>
public sealed class IndicativoRaroEnLaSecuenciaPruebas
{
    private static readonly TimeSpan Periodo = TimeSpan.FromSeconds(15);
    private static readonly DateTimeOffset Par = DateTimeOffset.UnixEpoch;

    private static string[] Generar(string yo, string dx, int informe = -14, bool rrr = false) =>
        GramaticaDeMensajes.Generar(new DatosDeLosMensajes(yo, "IL18", dx, "", informe, "", "", rrr, TipoDeOperacion.Normal));

    [Fact]
    public void ConUnDxRaroLosMensajesLoLlevanResumido()
    {
        Generar("EA8DLF", "HB10GBT").Should().Equal(
            "<HB10GBT> EA8DLF IL18",
            "<HB10GBT> EA8DLF -14",
            "<HB10GBT> EA8DLF R-14",
            "<HB10GBT> EA8DLF RR73",
            "HB10GBT <EA8DLF> 73",
            "CQ EA8DLF IL18");
    }

    [Fact]
    public void ConElPropioRaroSeMandaEnteroDondeCabe()
    {
        Generar("PJ4/K1ABC", "W9XYZ", informe: 3).Should().Equal(
            "<W9XYZ> PJ4/K1ABC",
            "W9XYZ <PJ4/K1ABC> +03",
            "W9XYZ <PJ4/K1ABC> R+03",
            "<W9XYZ> PJ4/K1ABC RR73",
            "W9XYZ <PJ4/K1ABC> 73",
            "CQ PJ4/K1ABC");
    }

    [Theory]
    [InlineData("EA8DLF", "HB10GBT", false)]
    [InlineData("EA8DLF", "HB10GBT", true)]
    [InlineData("PJ4/K1ABC", "W9XYZ", false)]
    [InlineData("EA8DLF/P", "HB10GBT", false)]
    [InlineData("EA8DLF", "EA1ABC", false)]
    public void TodosLosMensajesGeneradosSePuedenEmitir(string yo, string dx, bool rrr)
    {
        foreach (var texto in Generar(yo, dx, rrr: rrr))
        {
            MensajeDe77Bits.TryEmpaquetar(texto, out var bits, out var motivo).Should().BeTrue($"«{texto}»: {motivo}");

            // Y lo que sale no es texto libre: es un mensaje de indicativos que se entiende.
            var catalogo = new CatalogoDeIndicativos();
            catalogo.Recordar(yo);
            catalogo.Recordar(dx);
            MensajeDe77Bits.TryDesempaquetar(bits, catalogo, out var m).Should().BeTrue();
            m.Tipo.Should().NotBe(TipoDeMensaje.TextoLibre, $"«{texto}» tiene que viajar como mensaje de indicativos");
        }
    }

    [Fact]
    public void ElSecuenciadorReconoceAlDxConYSinAngulos()
    {
        var s = new SecuenciadorDeQso { Periodo = Periodo, MiIndicativo = "EA8DLF", Operacion = TipoDeOperacion.Normal };

        // CQ de tipo 4, con el indicativo en claro.
        s.Iniciar(Oido("CQ HB10GBT"), Par, Par + TimeSpan.FromSeconds(2)).Tx.Should().Be(1);
        s.DxCall.Should().Be("HB10GBT");
        s.EmisionHecha(1);

        // Me contesta con su indicativo resumido: es el mismo corresponsal.
        var informe = s.Procesar(Par + (2 * Periodo), [Oido("EA8DLF <HB10GBT> -09")]);
        informe.Tx.Should().Be(3);
        s.InformeRecibido.Should().Be(-9);
        s.EmisionHecha(3);

        // El RRR de tipo 4 lleva mi indicativo resumido y el suyo en claro.
        var rrr = s.Procesar(Par + (4 * Periodo), [Oido("<EA8DLF> HB10GBT RRR")]);
        rrr.Tx.Should().Be(5);
        rrr.ContactoCompleto.Should().BeTrue();
    }

    [Fact]
    public void UnResumenSinResolverNoSeAtribuyeANadie()
    {
        var m = InterpreteDeMensajes.Analizar("EA8DLF <...> -09");
        m.Clase.Should().Be(ClaseDeMensaje.Otro, "no se sabe quien lo manda y no se inventa");
    }

    private static MensajeOido Oido(string texto, int db = -10) => new(InterpreteDeMensajes.Analizar(texto), db, 1500);
}
