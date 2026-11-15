using System.IO;
using System.Text;
using FluentAssertions;
using Nodisla.Cuaderno.Ui.Ajustes;
using Xunit;

namespace Nodisla.Cuaderno.Ui.Pruebas;

/// <summary>
/// <c>ajustes.json</c> no se pierde: ni por dos guardados a la vez, ni por un fichero danado.
/// </summary>
/// <remarks>
/// En los registros del 15-11-2026 hay «ajustes.json.nuevo … being used by another process»:
/// el temporal de nombre fijo, sin cerrojo, y el fallo quedaba en un aviso mientras el cambio
/// del operador se perdia. Y un JSON danado se leia como «de fabrica» y el siguiente guardado
/// lo machacaba.
/// </remarks>
public sealed class AjustesGuardadoSeguroPruebas : IDisposable
{
    private readonly string _carpeta = Path.Combine(
        Path.GetTempPath(), "cuaderno-ajustes-seguros-" + Guid.NewGuid().ToString("N"));

    private string Ruta => Path.Combine(_carpeta, AjustesDelPrograma.NombreDelFichero);

    public AjustesGuardadoSeguroPruebas() => Directory.CreateDirectory(_carpeta);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_carpeta)) Directory.Delete(_carpeta, recursive: true);
        }
        catch (IOException)
        {
            // Carpeta temporal: si Windows la tiene cogida, ya la limpiara.
        }
    }

    [Fact]
    public void Un_fichero_danado_se_aparta_y_el_siguiente_guardado_no_lo_machaca()
    {
        const string loDelOperador = "{ \"Idioma\": \"de\", \"Equipo\": { esto se quedo a medias";
        File.WriteAllText(Ruta, loDelOperador);

        var leidos = AjustesDelPrograma.Leer(_carpeta);
        leidos.Guardar(_carpeta).Should().BeTrue();

        var apartados = Directory.GetFiles(_carpeta, AjustesDelPrograma.NombreDelFichero + ".dañado-*");
        apartados.Should().ContainSingle();
        File.ReadAllText(apartados[0]).Should().Be(loDelOperador);
        AjustesDelPrograma.FicheroIlegible.Should().NotBeNullOrEmpty();
        AjustesDelPrograma.Leer(_carpeta).Idioma.Should().BeNull();
    }

    [Fact]
    public void Un_fichero_vacio_tambien_se_aparta()
    {
        // Lo que deja un corte de luz en mitad de una escritura sin temporal.
        File.WriteAllText(Ruta, string.Empty);

        AjustesDelPrograma.Leer(_carpeta);

        File.Exists(Ruta).Should().BeFalse();
        Directory.GetFiles(_carpeta, "*.dañado-*").Should().ContainSingle();
    }

    [Fact]
    public void Un_fichero_bueno_no_se_aparta()
    {
        new AjustesDelPrograma { Idioma = "pt" }.Guardar(_carpeta);

        AjustesDelPrograma.Leer(_carpeta).Idioma.Should().Be("pt");

        Directory.GetFiles(_carpeta).Should().ContainSingle().Which.Should().Be(Ruta);
    }

    [Fact]
    public void Se_escribe_sin_bom_y_se_lee_con_el_y_sin_el()
    {
        new AjustesDelPrograma { Idioma = "fr" }.Guardar(_carpeta);

        var octetos = File.ReadAllBytes(Ruta);
        octetos.Take(3).Should().NotEqual(new byte[] { 0xEF, 0xBB, 0xBF });
        AjustesDelPrograma.Leer(_carpeta).Idioma.Should().Be("fr");

        // Guardado a mano con un editor que pone BOM (o con PowerShell).
        File.WriteAllText(Ruta, Encoding.UTF8.GetString(octetos), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        File.ReadAllBytes(Ruta).Take(3).Should().Equal(0xEF, 0xBB, 0xBF);

        AjustesDelPrograma.Leer(_carpeta).Idioma.Should().Be("fr");
        Directory.GetFiles(_carpeta).Should().ContainSingle();
    }

    [Fact]
    public void Guardados_a_la_vez_desde_varios_sitios_no_se_pisan()
    {
        var resultados = new System.Collections.Concurrent.ConcurrentBag<bool>();

        Parallel.For(0, 48, new ParallelOptions { MaxDegreeOfParallelism = 8 }, i =>
            resultados.Add(new AjustesDelPrograma { Idioma = i % 2 == 0 ? "es" : "en" }.Guardar(_carpeta)));

        resultados.Should().HaveCount(48).And.OnlyContain(guardado => guardado);
        AjustesDelPrograma.Leer(_carpeta).Idioma.Should().BeOneOf("es", "en");
        Directory.GetFiles(_carpeta).Should().ContainSingle().Which.Should().Be(Ruta);
    }

    [Fact]
    public void Si_no_se_puede_guardar_lo_dice_y_deja_el_fichero_como_estaba()
    {
        new AjustesDelPrograma { Idioma = "it" }.Guardar(_carpeta);
        var antes = File.ReadAllText(Ruta);

        bool guardado;
        using (new FileStream(Ruta, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            guardado = new AjustesDelPrograma { Idioma = "de" }.Guardar(_carpeta);
        }

        guardado.Should().BeFalse();
        File.ReadAllText(Ruta).Should().Be(antes);
        Directory.GetFiles(_carpeta).Should().ContainSingle();
    }
}
