using System.Globalization;
using FluentAssertions;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Propagacion;
using Nodisla.Cuaderno.Propagacion.Prediccion;

namespace Nodisla.Cuaderno.Propagacion.Pruebas;

/// <summary>
/// El fichero de entrada de ITURHFProp y la lectura de su informe. Sin lanzar el proceso: lo que
/// se fija aqui son las tres trampas del formato, que no avisan cuando se cae en ellas.
/// </summary>
public class IturHfPropFormatoPruebas
{
    private static readonly Coordenada Tenerife = new(28.4636, -16.2518);
    private static readonly Coordenada Madrid = new(40.4168, -3.7038);

    [Fact]
    public void La_medianoche_se_escribe_como_hora_veinticuatro()
    {
        // Trampa numero uno, y es la peor: con Path.hour 0 el programa no protesta, se cae con
        // una violacion de segmento y no deja informe. La medianoche es la hora 24.
        EntradaIturHfProp.HoraDelModelo(Momento("2026-09-22T00:30:00Z")).Should().Be(24);
        EntradaIturHfProp.HoraDelModelo(Momento("2026-09-22T00:00:00Z")).Should().Be(24);
        EntradaIturHfProp.HoraDelModelo(Momento("2026-09-22T13:45:00Z")).Should().Be(13);
        EntradaIturHfProp.HoraDelModelo(Momento("2026-09-22T23:59:00Z")).Should().Be(23);

        Componer(Momento("2026-09-22T00:10:00Z")).Should().Contain("Path.hour 24");
    }

    [Fact]
    public void El_informe_se_pide_entero_y_no_por_lista_de_opciones()
    {
        // Trampa numero dos: el analizador de opciones de la UIT lee el resto de la linea con un
        // patron que no admite digitos, asi que una opcion como RPT_N0_F2 se lleva por delante a
        // todas las que vengan detras y el informe sale con menos columnas sin avisar.
        EntradaIturHfProp.FormatoDelInforme.Should().Be("RPT_ALL");
        Componer().Should().Contain("RptFileFormat \"RPT_ALL\"");
    }

    [Fact]
    public void La_ruta_de_los_datos_lleva_barra_final()
    {
        // Trampa numero tres: el programa pega el nombre del fichero detras de esta ruta sin
        // poner separador. Sin la barra busca "...datosionos09.bin" y no lo encuentra.
        var entrada = EntradaIturHfProp.Componer(
            Tenerife,
            Madrid,
            Momento("2026-09-22T13:00:00Z"),
            [14.15],
            52,
            100,
            new OpcionesPropagacion(),
            @"C:\ruta\sin\barra");

        entrada.Should().Contain("DataFilePath \"C:/ruta/sin/barra/\"");
    }

    [Fact]
    public void La_potencia_va_en_decibelios_sobre_un_kilovatio()
    {
        EntradaIturHfProp.PotenciaDbKw(1000).Should().BeApproximately(0.0, 1e-9);
        EntradaIturHfProp.PotenciaDbKw(100).Should().BeApproximately(-10.0, 1e-9);
        EntradaIturHfProp.PotenciaDbKw(5).Should().BeApproximately(-23.0, 0.02);
    }

    [Fact]
    public void Las_manchas_se_recortan_al_rango_que_acepta_el_modelo()
    {
        EntradaIturHfProp.Manchas(0).Should().Be(1);
        EntradaIturHfProp.Manchas(-40).Should().Be(1);
        EntradaIturHfProp.Manchas(52.4).Should().Be(52);
        EntradaIturHfProp.Manchas(900).Should().Be(311);
    }

    [Fact]
    public void Seis_metros_se_sale_del_rango_del_modelo()
    {
        EntradaIturHfProp.EstaEnRango(1.840).Should().BeTrue();
        EntradaIturHfProp.EstaEnRango(28.400).Should().BeTrue();
        EntradaIturHfProp.EstaEnRango(50.150).Should().BeFalse();
        EntradaIturHfProp.EstaEnRango(1.5).Should().BeFalse();
    }

    [Fact]
    public void Las_frecuencias_van_en_una_sola_lista_para_no_lanzar_once_procesos()
    {
        var entrada = EntradaIturHfProp.Componer(
            Tenerife,
            Madrid,
            Momento("2026-09-22T13:00:00Z"),
            [1.84, 14.15, 28.4],
            52,
            100,
            new OpcionesPropagacion(),
            "/datos/");

        entrada.Should().Contain("Path.frequency 1.84, 14.15, 28.4");
    }

    [Fact]
    public void El_ambiente_de_ruido_se_traduce_a_los_nombres_de_la_uit()
    {
        EntradaIturHfProp.NombreDelRuido(AmbienteDeRuido.Industrial).Should().Be("CITY");
        EntradaIturHfProp.NombreDelRuido(AmbienteDeRuido.Residencial).Should().Be("RESIDENTIAL");
        EntradaIturHfProp.NombreDelRuido(AmbienteDeRuido.Rural).Should().Be("RURAL");
        EntradaIturHfProp.NombreDelRuido(AmbienteDeRuido.RuralTranquilo).Should().Be("QUIETRURAL");
    }

    [Fact]
    public void Los_numeros_se_escriben_con_punto_aunque_el_equipo_este_en_espanol()
    {
        var antes = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("es-ES");
            Componer().Should().Contain("Path.L_tx.lat 28.4636").And.NotContain("28,4636");
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = antes;
        }
    }

    [Fact]
    public void Se_lee_el_informe_buscando_las_columnas_por_su_nombre()
    {
        var filas = InformeIturHfProp.Leer(InformeDeEjemplo);

        filas.Should().HaveCount(2);
        filas[0].FrecuenciaMhz.Should().Be(14.150);
        filas[0].FiabilidadBasica.Should().BeApproximately(0.9608, 1e-4);
        filas[0].PotenciaRecibidaDbw.Should().Be(-104.86);
        filas[0].RelacionSenalRuidoDb.Should().Be(23.62);
        filas[0].MufBasicaMhz.Should().Be(20.22);
        filas[0].Saltos.Should().Be(1);
    }

    [Fact]
    public void El_hueco_del_modelo_no_se_convierte_en_una_senal_de_menos_trescientos()
    {
        // -307 es como el modelo marca lo que no ha calculado. Ensenarlo como nivel de senal
        // seria dar por medido un hueco.
        var filas = InformeIturHfProp.Leer(InformeDeEjemplo);

        filas[1].PotenciaRecibidaDbw.Should().BeNull();
        filas[1].RelacionSenalRuidoDb.Should().BeNull();
        filas[1].FiabilidadBasica.Should().Be(0.0);
    }

    [Fact]
    public void Un_informe_que_no_se_entiende_no_devuelve_filas_a_medias()
    {
        InformeIturHfProp.Leer(null).Should().BeEmpty();
        InformeIturHfProp.Leer(string.Empty).Should().BeEmpty();
        InformeIturHfProp.Leer("cualquier cosa que no sea un informe").Should().BeEmpty();
        // Con cabecera pero sin la columna de fiabilidad no hay nada que ensenar.
        InformeIturHfProp.Leer("Column 01: Month\nColumn 03: Frequency (MHz)\n09, 13, 14.150\n")
            .Should().BeEmpty();
    }

    [Fact]
    public void Los_saltos_salen_del_modo_mas_bajo_de_la_capa_f2()
    {
        var conDosSaltos = InformeDeEjemplo.Replace("  1F2 ", "  2F2 ", StringComparison.Ordinal);

        InformeIturHfProp.Leer(conDosSaltos)[0].Saltos.Should().Be(2);
    }

    private static string Componer(DateTimeOffset? momento = null) => EntradaIturHfProp.Componer(
        Tenerife,
        Madrid,
        momento ?? Momento("2026-09-22T13:00:00Z"),
        [14.15],
        52,
        100,
        new OpcionesPropagacion(),
        "/datos/");

    private static DateTimeOffset Momento(string texto) =>
        DateTimeOffset.Parse(texto, CultureInfo.InvariantCulture);

    /// <summary>
    /// Trozo de informe con la forma real que produce ITURHFProp, incluido el hueco de -307.
    /// </summary>
    private const string InformeDeEjemplo = """
        ******************************** Data Format ***********************************

        Column 01: Month
        Column 02: Hour
        Column 03: Frequency (MHz)
        Column 04: D - Path distance (km)
        Column 05: BMUF - Path basic MUF (MHz)
        Column 06: Lowest order mode for the F2 layer
        Column 07: Pr - Median receiver power (dB)
        Column 08: SNR - Median signal-to-noise ratio (dB)
        Column 09: DuSN - Upper decile deviation of signal-to-noise ratio (dB)
        Column 10: BCR - Basic circuit reliability (%)

        ************************** End Data Format ********************************

        ************************ Calculated Parameters ****************************

        09, 13,   14.150,  1754.26,  20.22,   1F2 ,-104.86,  23.62,   9.40,  96.08
        09, 13,    1.840,  1754.26,  20.22,   1F2 ,-307.00,-307.00,   9.41,   0.00

        **************************End Calculated Parameters ***********************
        """;
}
