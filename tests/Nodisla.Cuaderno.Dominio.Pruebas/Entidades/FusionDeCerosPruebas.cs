using FluentAssertions;
using Nodisla.Cuaderno.Dominio.Entidades;

namespace Nodisla.Cuaderno.Dominio.Pruebas.Entidades;

/// <summary>
/// Cuando un cero es un hueco y cuando es un dato.
/// </summary>
/// <remarks>
/// Convertir el cero en hueco destruye informacion, asi que solo se hace donde el cero no
/// puede ser una medida de verdad: el flujo solar, que nunca llega a cero, y el par de
/// coordenadas propias (0, 0), que cae en el golfo de Guinea. En todos los demas campos el
/// cero vale y gana el destino con su choque, como cualquier otro dato en discordia.
/// Las dos ultimas secciones de este fichero son las que impiden que la excepcion se extienda
/// «por coherencia» a campos donde el cero es legitimo.
/// </remarks>
public sealed class FusionDeCerosPruebas
{
    // ── SFI: el cero es imposible, luego es un hueco ─────────────────────────

    [Fact]
    public void El_flujo_solar_a_cero_del_origen_no_pisa_al_del_destino_ni_deja_choque()
    {
        var destino = AyudaDeFusion.Qso();
        destino.Sfi = 124;
        var origen = AyudaDeFusion.Qso();
        origen.Sfi = 0;

        var resultado = FusionDeQso.Fundir(destino, origen);

        destino.Sfi.Should().Be(124);
        resultado.Choques.Should().NotContain(c => c.Campo == "SFI");
    }

    [Fact]
    public void El_flujo_solar_a_cero_del_destino_lo_rellena_el_origen()
    {
        var destino = AyudaDeFusion.Qso();
        destino.Sfi = 0;
        var origen = AyudaDeFusion.Qso();
        origen.Sfi = 192;

        var resultado = FusionDeQso.Fundir(destino, origen);

        destino.Sfi.Should().Be(192, "un cero de flujo solar es un hueco que se rellena");
        resultado.HuboCambios.Should().BeTrue();
        resultado.Choques.Should().NotContain(c => c.Campo == "SFI");
    }

    [Fact]
    public void Dos_flujos_solares_de_verdad_distintos_si_dejan_choque()
    {
        var destino = AyudaDeFusion.Qso();
        destino.Sfi = 124;
        var origen = AyudaDeFusion.Qso();
        origen.Sfi = 192;

        var resultado = FusionDeQso.Fundir(destino, origen);

        destino.Sfi.Should().Be(124);
        resultado.Choques.Should().ContainSingle(c => c.Campo == "SFI"
            && c.Conservado == "124" && c.Descartado == "192");
    }

    // ── Mi posicion: (0, 0) es el oceano, luego es un hueco ──────────────────

    [Fact]
    public void Mi_posicion_en_el_punto_nulo_del_origen_no_pisa_la_del_destino()
    {
        var destino = AyudaDeFusion.Qso();
        destino.MyLat = 40.02245;
        destino.MyLon = -3.70375;
        var origen = AyudaDeFusion.Qso();
        origen.MyLat = 0;
        origen.MyLon = 0;

        var resultado = FusionDeQso.Fundir(destino, origen);

        destino.MyLat.Should().Be(40.02245);
        destino.MyLon.Should().Be(-3.70375);
        resultado.Choques.Should().NotContain(c => c.Campo == "MY_LAT");
        resultado.Choques.Should().NotContain(c => c.Campo == "MY_LON");
    }

    [Fact]
    public void Mi_posicion_en_el_punto_nulo_del_destino_la_rellena_el_origen_entera()
    {
        var destino = AyudaDeFusion.Qso();
        destino.MyLat = 0;
        destino.MyLon = 0;
        var origen = AyudaDeFusion.Qso();
        origen.MyLat = 40.02245;
        origen.MyLon = -3.70375;

        var resultado = FusionDeQso.Fundir(destino, origen);

        destino.MyLat.Should().Be(40.02245);
        destino.MyLon.Should().Be(-3.70375);
        resultado.HuboCambios.Should().BeTrue();
        resultado.Choques.Should().BeEmpty();
    }

    [Fact]
    public void Mi_posicion_sin_declarar_cuenta_igual_que_el_punto_nulo()
    {
        var destino = AyudaDeFusion.Qso();
        var origen = AyudaDeFusion.Qso();
        origen.MyLat = 40.02245;
        origen.MyLon = -3.70375;

        FusionDeQso.Fundir(destino, origen);

        destino.MyLat.Should().Be(40.02245);
        destino.MyLon.Should().Be(-3.70375);
    }

    [Fact]
    public void Dos_posiciones_de_verdad_distintas_si_dejan_choque()
    {
        var destino = AyudaDeFusion.Qso();
        destino.MyLat = 40.02245;
        destino.MyLon = -3.70375;
        var origen = AyudaDeFusion.Qso();
        origen.MyLat = 28.5;
        origen.MyLon = -16.25;

        var resultado = FusionDeQso.Fundir(destino, origen);

        destino.MyLat.Should().Be(40.02245);
        resultado.Choques.Should().Contain(c => c.Campo == "MY_LAT");
        resultado.Choques.Should().Contain(c => c.Campo == "MY_LON");
    }

    // ── Lo que NO cambia: el cero como dato legitimo ─────────────────────────

    [Fact]
    public void La_longitud_cero_a_solas_es_greenwich_y_cuenta_como_valor()
    {
        // La condicion del punto nulo es conjunta a proposito: con una latitud de verdad, una
        // longitud de exactamente cero es el meridiano de Greenwich y hay estaciones ahi.
        var destino = AyudaDeFusion.Qso();
        destino.MyLat = 51.4779;
        destino.MyLon = 0;
        var origen = AyudaDeFusion.Qso();
        origen.MyLat = 51.4779;
        origen.MyLon = -3.70375;

        var resultado = FusionDeQso.Fundir(destino, origen);

        destino.MyLon.Should().Be(0, "el cero de Greenwich es un dato y gana el destino");
        resultado.Choques.Should().ContainSingle(c => c.Campo == "MY_LON"
            && c.Conservado == "0" && c.Descartado == "-3.70375");
    }

    [Fact]
    public void La_latitud_cero_a_solas_es_el_ecuador_y_cuenta_como_valor()
    {
        var destino = AyudaDeFusion.Qso();
        destino.MyLat = 0;
        destino.MyLon = -78.5;
        var origen = AyudaDeFusion.Qso();
        origen.MyLat = 40.02245;
        origen.MyLon = -78.5;

        var resultado = FusionDeQso.Fundir(destino, origen);

        destino.MyLat.Should().Be(0, "el ecuador es un paralelo como cualquier otro");
        resultado.Choques.Should().ContainSingle(c => c.Campo == "MY_LAT");
    }

    [Fact]
    public void El_rumbo_de_antena_a_cero_es_el_norte_y_cuenta_como_valor()
    {
        var destino = AyudaDeFusion.Qso();
        destino.AntAz = 0;
        var origen = AyudaDeFusion.Qso();
        origen.AntAz = 197.449;

        var resultado = FusionDeQso.Fundir(destino, origen);

        destino.AntAz.Should().Be(0, "cero grados es el norte, no un hueco");
        resultado.Choques.Should().ContainSingle(c => c.Campo == "ANT_AZ"
            && c.Conservado == "0" && c.Descartado == "197.449");
    }

    [Theory]
    [InlineData("A_INDEX")]
    [InlineData("K_INDEX")]
    [InlineData("ANT_EL")]
    [InlineData("TX_PWR")]
    [InlineData("RX_PWR")]
    [InlineData("DISTANCE")]
    [InlineData("ALTITUDE")]
    public void El_cero_sigue_siendo_un_valor_en_los_demas_campos(string campo)
    {
        var destino = AyudaDeFusion.Qso();
        var origen = AyudaDeFusion.Qso();
        Poner(destino, campo, 0);
        Poner(origen, campo, 42);

        var resultado = FusionDeQso.Fundir(destino, origen);

        resultado.Choques.Should().ContainSingle(c => c.Campo == campo
            && c.Conservado == "0" && c.Descartado == "42");
    }

    private static void Poner(Qso qso, string campo, double valor)
    {
        switch (campo)
        {
            case "A_INDEX": qso.AIndex = valor; break;
            case "K_INDEX": qso.KIndex = valor; break;
            case "ANT_EL": qso.AntEl = valor; break;
            case "TX_PWR": qso.TxPwr = valor; break;
            case "RX_PWR": qso.RxPwr = valor; break;
            case "DISTANCE": qso.Distance = valor; break;
            case "ALTITUDE": qso.Altitude = valor; break;
            default: throw new ArgumentOutOfRangeException(nameof(campo), campo, "Campo no contemplado.");
        }
    }
}
