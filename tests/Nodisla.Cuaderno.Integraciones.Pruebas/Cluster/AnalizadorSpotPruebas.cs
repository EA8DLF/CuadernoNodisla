using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Integraciones.Cluster;

namespace Nodisla.Cuaderno.Integraciones.Pruebas.Cluster;

/// <summary>
/// Analisis de las lineas de un cluster de DX, con las variantes de formato que se ven de
/// verdad en los nodos.
/// </summary>
public class AnalizadorSpotPruebas
{
    private static readonly AnalizadorSpot Analizador = new("Pruebas");

    private static readonly DateTimeOffset Cuando = new(2026, 9, 21, 14, 30, 0, TimeSpan.Zero);

    [Fact]
    public void El_formato_clasico_de_columnas_fijas()
    {
        var anuncio = Analizador.Analizar(
            "DX de OH2BH:     14025.0  EA8DLF       Nice signal into EU            1432Z", Cuando);

        anuncio.Should().NotBeNull();
        anuncio!.Spot.Indicativo.Valor.Should().Be("EA8DLF");
        anuncio.Spot.Anunciante.Valor.Should().Be("OH2BH");
        anuncio.Spot.Frecuencia.Megahercios.Should().Be(14.025m);
        anuncio.Spot.Comentario.Should().Be("Nice signal into EU");
        anuncio.HoraDelCluster.Should().Be("1432Z");
        anuncio.Spot.EsDeEscuchaAutomatica.Should().BeFalse();
        anuncio.Spot.Fuente.Should().Be("Pruebas");
    }

    [Fact]
    public void El_continente_que_algunos_nodos_ponen_detras_de_la_hora_no_ensucia_el_comentario()
    {
        var anuncio = Analizador.Analizar(
            "DX de W1AW:       7005.0  EA8/DL1ABC   CW 599 tnx qso                 0301Z EU", Cuando);

        anuncio.Should().NotBeNull();
        anuncio!.Spot.Comentario.Should().Be("CW 599 tnx qso");
        anuncio.ContinenteDelNodo.Should().Be("EU");
        anuncio.Spot.ModoAnunciado.NombreUsual.Should().Be("CW");
    }

    [Fact]
    public void Una_linea_de_skimmer_da_decibelios_y_velocidad()
    {
        var anuncio = Analizador.Analizar(
            "DX de DL8LAS-#:  14012.5  UA3ABC       CW    12 dB  24 WPM  CQ      1503Z", Cuando);

        anuncio.Should().NotBeNull();
        anuncio!.Spot.EsDeEscuchaAutomatica.Should().BeTrue();
        anuncio.Spot.Anunciante.Valor.Should().Be("DL8LAS");
        anuncio.Spot.Decibelios.Should().Be(12);
        anuncio.Spot.PalabrasPorMinuto.Should().Be(24);
        anuncio.Spot.ModoAnunciado.NombreUsual.Should().Be("CW");
    }

    [Fact]
    public void Un_skimmer_de_ft8_da_los_decibelios_en_negativo()
    {
        var anuncio = Analizador.Analizar(
            "DX de EA5WA-#:   14074.0  VP8LP        FT8     -7 dB  15 WPM  CQ    1702Z", Cuando);

        anuncio.Should().NotBeNull();
        anuncio!.Spot.Decibelios.Should().Be(-7);
        anuncio.Spot.ModoAnunciado.NombreUsual.Should().Be("FT8");
        anuncio.Spot.Frecuencia.Kilohercios.Should().Be(14074m);
    }

    [Fact]
    public void Un_nodo_que_se_salta_los_dos_puntos_tambien_se_entiende()
    {
        var anuncio = Analizador.Analizar("DX de EA8DLF 21074.0 JA1XYZ FT8 tnx qso 1210Z", Cuando);

        anuncio.Should().NotBeNull();
        anuncio!.Spot.Indicativo.Valor.Should().Be("JA1XYZ");
        anuncio.Spot.Frecuencia.Megahercios.Should().Be(21.074m);
        anuncio.Spot.Comentario.Should().Be("FT8 tnx qso");
    }

    [Fact]
    public void Una_frecuencia_sin_decimales_y_una_hora_de_tres_cifras()
    {
        var anuncio = Analizador.Analizar("DX de VE3XYZ:     3573  W1ABC        FT8      930Z", Cuando);

        anuncio.Should().NotBeNull();
        anuncio!.Spot.Frecuencia.Megahercios.Should().Be(3.573m);
        anuncio.Spot.Banda.Nombre.Should().Be("80m");
        anuncio.HoraDelCluster.Should().Be("930Z");
    }

    [Fact]
    public void La_frecuencia_viene_en_kilohercios_no_en_megahercios()
    {
        var anuncio = Analizador.Analizar(
            "DX de EA8DLF:    14025.0  K1ABC        CQ DX                          1200Z", Cuando);

        anuncio.Should().NotBeNull();
        // 14025 kHz son 14,025 MHz: leerlo como megahercios se equivoca por mil.
        anuncio!.Spot.Frecuencia.Megahercios.Should().Be(14.025m);
        anuncio.Spot.Frecuencia.Kilohercios.Should().Be(14025m);
        anuncio.Spot.Banda.Nombre.Should().Be("20m");
    }

    [Fact]
    public void Una_pasarela_que_da_la_frecuencia_en_megahercios_tambien_se_entiende()
    {
        var anuncio = Analizador.Analizar("DX de EA8DLF:    14.025  K1ABC   CQ DX   1200Z", Cuando);

        anuncio.Should().NotBeNull();
        // 14,025 kHz no es ninguna banda; 14,025 MHz si, asi que se toma lo segundo.
        anuncio!.Spot.Frecuencia.Megahercios.Should().Be(14.025m);
    }

    [Theory]
    [InlineData("WWV de VE7CC <18Z> :   SFI=142, A=8, K=3, No Storms -> No Storms")]
    [InlineData("To ALL de EA8DLF: hola a todos, alguien oye la 40?")]
    [InlineData("Hello Jose, welcome to the CC Cluster")]
    [InlineData("login: ")]
    [InlineData("")]
    [InlineData("DX de EA8DLF:")]
    public void Lo_que_no_es_un_anuncio_no_se_analiza_como_tal(string linea)
    {
        Analizador.Analizar(linea, Cuando).Should().BeNull();
    }

    [Fact]
    public void Se_saca_la_referencia_de_pota_del_comentario()
    {
        var anuncio = Analizador.Analizar(
            "DX de K4SWL:     14061.0  K4SWL/P      POTA US-1234 tnx               1530Z", Cuando);

        anuncio.Should().NotBeNull();
        anuncio!.Spot.Referencias.Should().ContainSingle()
            .Which.Should().Be(new ReferenciaAnunciada(TipoDeReferencia.Pota, "US-1234"));
    }

    [Fact]
    public void Se_saca_la_referencia_de_sota_del_comentario()
    {
        var anuncio = Analizador.Analizar(
            "DX de EA8DLF:    14285.0  EA2IF/P      SOTA EA2/NV-093                1100Z", Cuando);

        anuncio.Should().NotBeNull();
        anuncio!.Spot.Referencias.Should().ContainSingle()
            .Which.Tipo.Should().Be(TipoDeReferencia.Sota);
        anuncio.Spot.Referencias[0].Codigo.Should().Be("EA2/NV-093");
    }

    [Fact]
    public void Se_saca_la_referencia_de_iota_del_comentario()
    {
        var anuncio = Analizador.Analizar(
            "DX de IK1ABC:    14040.0  IH9YMC       IOTA AF-018 up 2               0930Z", Cuando);

        anuncio.Should().NotBeNull();
        anuncio!.Spot.Referencias.Should().ContainSingle()
            .Which.Tipo.Should().Be(TipoDeReferencia.Iota);
    }

    [Fact]
    public void El_localizador_que_manda_el_nodo_se_queda_en_el_anuncio()
    {
        var anuncio = Analizador.Analizar(
            "DX de OH2BH:     14025.0  EA8DLF       CQ DX                          1432Z IL18", Cuando);

        anuncio.Should().NotBeNull();
        anuncio!.Spot.Locator.Valor.Should().Be("IL18");
        // Lo que se leyo como localizador no se repite como continente del nodo.
        anuncio.ContinenteDelNodo.Should().BeNull();
    }

    [Fact]
    public void Un_continente_de_dos_letras_no_se_confunde_con_un_localizador()
    {
        // NA, AF y OC son localizadores validos de dos caracteres ademas de continentes.
        var anuncio = Analizador.Analizar(
            "DX de W1AW:       7005.0  K1ABC        CQ                             0301Z NA", Cuando);

        anuncio.Should().NotBeNull();
        anuncio!.Spot.Locator.EsVacio.Should().BeTrue();
        anuncio.ContinenteDelNodo.Should().Be("NA");
    }

    [Fact]
    public void El_anuncio_llega_con_la_entidad_dxcc_resuelta()
    {
        var anuncio = Analizador.Analizar(
            "DX de OH2BH:     14025.0  EA8DLF       CQ                             1432Z", Cuando);

        anuncio.Should().NotBeNull();
        anuncio!.Spot.Dxcc.Should().Be(29);
        anuncio.Spot.Pais.Should().Contain("Canaria");
        anuncio.Spot.Continente.Should().Be("AF");
    }

    [Fact]
    public void Lo_que_rellena_el_cuaderno_no_lo_rellena_la_fuente()
    {
        var anuncio = Analizador.Analizar(
            "DX de OH2BH:     14025.0  EA8DLF       CQ                             1432Z", Cuando);

        anuncio.Should().NotBeNull();
        anuncio!.Spot.EsNuevoEnBandaYModo.Should().BeFalse();
        anuncio.Spot.EsEntidadNueva.Should().BeFalse();
    }

    [Fact]
    public void Las_palabras_corrientes_no_se_confunden_con_un_modo()
    {
        VocabularioCluster.BuscarModo("TNX QSO 73 NEW ONE").EsVacio.Should().BeTrue();
        VocabularioCluster.BuscarModo("tnx for the ssb qso").Principal.Should().Be("SSB");
    }

    [Theory]
    // Coma decimal, que ponen algunos nodos europeos.
    [InlineData("DX de EA8DLF:    14025,0  K1ABC        CQ DX     1200Z")]
    // Todo en minusculas y sin hora al final.
    [InlineData("dx de ea8dlf: 14025.0 k1abc cq dx")]
    // Con el SSID del nodo detras del indicativo de quien anuncia.
    [InlineData("DX de EA8DLF-7:  14025.0  K1ABC        CQ DX     1200Z")]
    public void Las_manias_de_cada_nodo_no_impiden_leer_el_anuncio(string linea)
    {
        var anuncio = Analizador.Analizar(linea, Cuando);

        anuncio.Should().NotBeNull();
        anuncio!.Spot.Indicativo.Valor.Should().Be("K1ABC");
        anuncio.Spot.Anunciante.Valor.Should().Be("EA8DLF");
        anuncio.Spot.Frecuencia.Megahercios.Should().Be(14.025m);
    }

    [Fact]
    public void El_sufijo_del_nodo_no_es_parte_del_indicativo_de_quien_anuncia()
    {
        var (anunciante, automatica) = AnalizadorSpot.LeerAnunciante("W3LPL-#:");
        anunciante.Valor.Should().Be("W3LPL");
        automatica.Should().BeTrue();

        var (otro, tambien) = AnalizadorSpot.LeerAnunciante("EA8DLF-7");
        otro.Valor.Should().Be("EA8DLF");
        tambien.Should().BeFalse();
    }
}
