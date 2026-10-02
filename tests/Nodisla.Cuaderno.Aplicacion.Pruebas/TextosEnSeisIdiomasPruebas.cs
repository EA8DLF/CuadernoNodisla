using System.Globalization;
using System.Text.RegularExpressions;
using FluentAssertions;
using Nodisla.Cuaderno.Idiomas;

namespace Nodisla.Cuaderno.Aplicacion.Pruebas;

/// <summary>
/// Las pruebas que cambian el idioma del programa van solas: el idioma es de todo el proceso, y
/// una prueba de otra clase que mirase un texto en español mientras tanto lo vería en alemán.
/// </summary>
[CollectionDefinition(nameof(ColeccionDelIdioma), DisableParallelization = true)]
public sealed class ColeccionDelIdioma
{
}

/// <summary>
/// Que el programa habla los seis idiomas de verdad: ninguna clave sin traducir, ninguna vacía,
/// los mismos huecos en todos, y el idioma por omisión bien elegido.
/// </summary>
[Collection(nameof(ColeccionDelIdioma))]
public sealed partial class TextosEnSeisIdiomasPruebas
{
    private static readonly string[] Codigos = ["es", "en", "pt", "fr", "it", "de"];

    [GeneratedRegex(@"\{(\d+)[^}]*\}")]
    private static partial Regex Hueco();

    public static TheoryData<string> Apartados()
    {
        var datos = new TheoryData<string>();
        foreach (var a in Textos.Apartados) datos.Add(a);
        return datos;
    }

    [Theory]
    [MemberData(nameof(Apartados))]
    public void Cada_clave_esta_en_los_seis_idiomas_y_ninguna_vacia(string apartado)
    {
        var referencia = Textos.TextosDe(apartado, "es");
        var faltan = new List<string>();

        foreach (var codigo in Codigos)
        {
            var textos = Textos.TextosDe(apartado, codigo);
            foreach (var clave in referencia.Keys)
            {
                if (!textos.TryGetValue(clave, out var texto)) faltan.Add($"{codigo}: falta {clave}");
                else if (string.IsNullOrWhiteSpace(texto)) faltan.Add($"{codigo}: {clave} está vacía");
            }

            foreach (var clave in textos.Keys.Except(referencia.Keys))
            {
                faltan.Add($"{codigo}: {clave} no está en el español, que es la referencia");
            }
        }

        faltan.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(Apartados))]
    public void Las_claves_llevan_delante_su_apartado(string apartado)
    {
        Textos.TextosDe(apartado, "es").Keys
            .Where(c => !c.StartsWith(apartado + ".", StringComparison.Ordinal))
            .Should().BeEmpty("la clave dice en qué fichero está");
    }

    [Theory]
    [MemberData(nameof(Apartados))]
    public void Los_huecos_son_los_mismos_en_todos_los_idiomas_y_el_formato_es_valido(string apartado)
    {
        var referencia = Textos.TextosDe(apartado, "es");
        var mal = new List<string>();

        foreach (var codigo in Codigos)
        {
            foreach (var (clave, texto) in Textos.TextosDe(apartado, codigo))
            {
                if (!referencia.TryGetValue(clave, out var espanol)) continue;
                if (Huecos(texto) != Huecos(espanol)) mal.Add($"{codigo}: {clave} «{texto}»");

                try
                {
                    _ = string.Format(CultureInfo.InvariantCulture, texto, Enumerable.Repeat<object>(1, 10).ToArray());
                }
                catch (FormatException)
                {
                    mal.Add($"{codigo}: {clave} no es un formato válido «{texto}»");
                }
            }
        }

        mal.Should().BeEmpty();
    }

    [Fact]
    public void Ninguna_clave_esta_repetida_entre_apartados()
    {
        Textos.Apartados
            .SelectMany(a => Textos.TextosDe(a, "es").Keys)
            .GroupBy(c => c)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .Should().BeEmpty();
    }

    [Fact]
    public void Hay_textos_de_verdad()
    {
        Textos.Apartados.Sum(a => Textos.TextosDe(a, "es").Count).Should().BeGreaterThan(30);
    }

    [Theory]
    [InlineData("es-ES", "es")]
    [InlineData("es-MX", "es")]
    [InlineData("en-US", "en")]
    [InlineData("pt-BR", "pt")]
    [InlineData("fr-CA", "fr")]
    [InlineData("it-IT", "it")]
    [InlineData("de-AT", "de")]
    [InlineData("ja-JP", "en")]
    [InlineData("nl-NL", "en")]
    public void Sin_elegir_se_usa_el_idioma_del_sistema_y_si_no_es_de_los_seis_el_ingles(string sistema, string esperado)
    {
        Textos.IdiomaDelSistema(new CultureInfo(sistema)).Should().Be(esperado);
    }

    [Theory]
    [InlineData("de", "de")]
    [InlineData("DE", "de")]
    [InlineData("pt-BR", "pt")]
    [InlineData("xx", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Lo_guardado_se_normaliza(string? guardado, string? esperado)
    {
        Textos.Resolver(guardado).Should().Be(esperado ?? Textos.IdiomaDelSistema());
    }

    [Fact]
    public void Cambiar_de_idioma_cambia_los_textos_y_las_culturas_y_avisa()
    {
        var avisos = 0;
        void Contar(object? s, EventArgs e) => avisos++;
        Textos.IdiomaCambiado += Contar;
        try
        {
            Textos.Cambiar("de");
            Textos.T("Comun.Cerrar").Should().Be("Schließen");
            Textos.Cultura.TwoLetterISOLanguageName.Should().Be("de");
            Textos.F("Ajustes.Idioma.DelSistema", "Deutsch").Should().Be("Systemsprache (Deutsch)");

            Textos.Cambiar("en");
            Textos.T("Comun.Indicativo").Should().Be("Call sign");

            Textos.Cambiar("it");
            Textos.T("Comun.Indicativo").Should().Be("Nominativo");

            avisos.Should().Be(3);
        }
        finally
        {
            Textos.IdiomaCambiado -= Contar;
            Textos.Cambiar(new CultureInfo("es-ES", useUserOverride: false));
        }

        Textos.T("Comun.Cerrar").Should().Be("Cerrar");
    }

    [Fact]
    public void Una_clave_que_no_existe_se_ve_tal_cual()
    {
        Textos.T("Comun.NoExisteEstaClave").Should().Be("Comun.NoExisteEstaClave");
        Textos.T("SinApartado").Should().Be("SinApartado");
    }

    [Fact]
    public void Quien_se_suscribe_se_entera_y_no_queda_retenido()
    {
        var oyente = new Oyente();
        Textos.AlCambiar(oyente, static o => o.Veces++);
        var debil = Suscribir();
        try
        {
            Textos.Cambiar("fr");
            oyente.Veces.Should().Be(1);
        }
        finally
        {
            Textos.Cambiar(new CultureInfo("es-ES", useUserOverride: false));
        }

        oyente.Veces.Should().Be(2);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        debil.IsAlive.Should().BeFalse("una pantalla cerrada no se queda viva por haberse suscrito al idioma");
    }

    private static WeakReference Suscribir()
    {
        var efimero = new Oyente();
        Textos.AlCambiar(efimero, static o => o.Veces++);
        return new WeakReference(efimero);
    }

    private static string Huecos(string texto) =>
        string.Join(',', Hueco().Matches(texto.Replace("{{", string.Empty, StringComparison.Ordinal).Replace("}}", string.Empty, StringComparison.Ordinal))
            .Select(m => m.Groups[1].Value).Distinct().Order(StringComparer.Ordinal));

    private sealed class Oyente
    {
        public int Veces { get; set; }
    }
}
