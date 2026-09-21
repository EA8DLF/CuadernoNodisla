using System.Globalization;
using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.CasosDeUso;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Aplicacion.Pruebas;

/// <summary>
/// Estas pruebas fijan una regla que alguien intentará «arreglar» algún día: el programa habla
/// español, pero la frecuencia y los informes en decibelios se escriben con PUNTO decimal,
/// porque así lo manda ADIF y así viene el dial de cualquier equipo. Si estas pruebas fallan
/// es que se ha españolizado algo que no se debía.
/// </summary>
public sealed class TextoDeFrecuenciaPruebas
{
    /// <summary>Ejecuta la comprobacion con la cultura espanola puesta, como en la aplicacion.</summary>
    private static void EnEspanol(Action comprobacion)
    {
        var anterior = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("es-ES");
            comprobacion();
        }
        finally
        {
            CultureInfo.CurrentCulture = anterior;
        }
    }

    [Theory]
    [InlineData("14.200", 14.2)]
    [InlineData("14.074", 14.074)]
    [InlineData("7.1", 7.1)]
    [InlineData("144.300", 144.3)]
    [InlineData("1296.2", 1296.2)]
    public void El_punto_siempre_es_decimal_aunque_el_programa_hable_espanol(string texto, double esperado)
    {
        EnEspanol(() =>
        {
            TextoDeFrecuencia.TryLeer(texto, out var f).Should().BeTrue();
            f.Megahercios.Should().Be((decimal)esperado);
        });
    }

    [Theory]
    [InlineData("14,200", 14.2)]
    [InlineData("7,074", 7.074)]
    [InlineData(" 50,313 ", 50.313)]
    public void La_coma_del_teclado_numerico_se_entiende_como_punto_decimal(string texto, double esperado)
    {
        EnEspanol(() =>
        {
            TextoDeFrecuencia.TryLeer(texto, out var f).Should().BeTrue();
            f.Megahercios.Should().Be((decimal)esperado);
        });
    }

    [Theory]
    [InlineData("14.200,5")]
    [InlineData("14,200.5")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no es una frecuencia")]
    public void Lo_que_no_se_entiende_se_rechaza_en_vez_de_adivinarlo(string texto)
    {
        EnEspanol(() =>
        {
            TextoDeFrecuencia.TryLeer(texto, out _).Should().BeFalse();
            TextoDeFrecuencia.Leer(texto).EsCero.Should().BeTrue();
        });
    }

    [Fact]
    public void La_frecuencia_se_escribe_con_punto_decimal_y_sin_separador_de_millares()
    {
        EnEspanol(() =>
        {
            TextoDeFrecuencia.Escribir(Frecuencia.DesdeMegahercios(14.2m)).Should().Be("14.2");
            TextoDeFrecuencia.Escribir(Frecuencia.DesdeMegahercios(14.074m)).Should().Be("14.074");
            TextoDeFrecuencia.Escribir(Frecuencia.DesdeMegahercios(1296.2m)).Should().Be("1296.2");
            TextoDeFrecuencia.Escribir(Frecuencia.DesdeMegahercios(10368.1m)).Should().Be("10368.1");
        });
    }

    [Fact]
    public void La_frecuencia_cero_se_escribe_vacia()
    {
        TextoDeFrecuencia.Escribir(Frecuencia.Cero).Should().BeEmpty();
    }

    [Fact]
    public void Lo_escrito_se_vuelve_a_leer_igual()
    {
        EnEspanol(() =>
        {
            var original = Frecuencia.DesdeMegahercios(18.1005m);
            var texto = TextoDeFrecuencia.Escribir(original);
            TextoDeFrecuencia.Leer(texto).Should().Be(original);
        });
    }

    [Theory]
    [InlineData("-06", -6)]
    [InlineData("+04", 4)]
    [InlineData("-15", -15)]
    public void Los_informes_en_decibelios_no_dependen_del_idioma(string texto, int decibelios)
    {
        EnEspanol(() =>
        {
            var informe = Informe.Parse(texto);
            informe.Forma.Should().Be(FormaDeInforme.Decibelios);
            informe.Decibelios.Should().Be(decibelios);
            Informe.DesdeDecibelios(decibelios).Decibelios.Should().Be(decibelios);
        });
    }

    [Fact]
    public void El_informe_en_decibelios_se_escribe_con_signo_y_sin_separadores()
    {
        EnEspanol(() => Informe.DesdeDecibelios(-6).Texto.Should().Be("-06"));
    }
}
