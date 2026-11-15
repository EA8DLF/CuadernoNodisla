using System.Text;
using FluentAssertions;
using Nodisla.Cuaderno.Servicios.Ficheros;

namespace Nodisla.Cuaderno.Servicios.Pruebas;

/// <summary>
/// Los ficheros pequenos del programa se escriben sin pisarse y sin machacar lo ilegible.
/// </summary>
public sealed class FicheroSeguroPruebas : IDisposable
{
    private readonly string _carpeta =
        Path.Combine(Path.GetTempPath(), "cuaderno-fichero-seguro-" + Guid.NewGuid().ToString("N"));

    private string Ruta => Path.Combine(_carpeta, "ajustes.json");

    public FicheroSeguroPruebas() => Directory.CreateDirectory(_carpeta);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_carpeta, recursive: true);
        }
        catch (IOException)
        {
            // Carpeta temporal: si Windows la tiene cogida, ya la limpiara.
        }
    }

    [Fact]
    public void Escribe_en_utf8_sin_bom_y_no_deja_temporales()
    {
        FicheroSeguro.Escribir(Ruta, "{\"Idioma\":\"español\"}");

        var octetos = File.ReadAllBytes(Ruta);
        octetos.Take(3).Should().NotEqual(new byte[] { 0xEF, 0xBB, 0xBF });
        Encoding.UTF8.GetString(octetos).Should().Be("{\"Idioma\":\"español\"}");
        Directory.GetFiles(_carpeta).Should().ContainSingle();
    }

    [Fact]
    public void Lee_con_bom_y_sin_bom()
    {
        File.WriteAllText(Ruta, "{\"a\":1}", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        FicheroSeguro.Leer(Ruta).Should().Be("{\"a\":1}");

        File.WriteAllText(Ruta, "{\"a\":2}", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        FicheroSeguro.Leer(Ruta).Should().Be("{\"a\":2}");
    }

    [Fact]
    public void Muchos_guardados_a_la_vez_no_se_pisan_el_temporal()
    {
        // Con el temporal de nombre fijo y sin cerrojo esto daba «being used by another
        // process» y se perdia el guardado.
        var fallos = new System.Collections.Concurrent.ConcurrentBag<Exception>();

        Parallel.For(0, 64, new ParallelOptions { MaxDegreeOfParallelism = 8 }, i =>
        {
            try
            {
                FicheroSeguro.Escribir(Ruta, $"{{\"n\":{i},\"relleno\":\"{new string('x', 4000)}\"}}");
            }
            catch (Exception ex)
            {
                fallos.Add(ex);
            }
        });

        fallos.Should().BeEmpty();
        File.ReadAllText(Ruta).Should().StartWith("{\"n\":").And.EndWith("\"}");
        Directory.GetFiles(_carpeta).Should().ContainSingle();
    }

    [Fact]
    public void Si_windows_tiene_el_fichero_cogido_un_momento_reintenta()
    {
        File.WriteAllText(Ruta, "viejo");
        var cogido = new FileStream(Ruta, FileMode.Open, FileAccess.Read, FileShare.None);
        var esperas = 0;

        FicheroSeguro.Escribir(Ruta, "nuevo", _ =>
        {
            // A la segunda espera lo suelta, como hace un antivirus.
            if (++esperas == 2) cogido.Dispose();
        });

        esperas.Should().Be(2);
        File.ReadAllText(Ruta).Should().Be("nuevo");
    }

    [Fact]
    public void Si_no_hay_manera_lanza_deja_el_fichero_como_estaba_y_limpia_el_temporal()
    {
        File.WriteAllText(Ruta, "viejo");
        using (new FileStream(Ruta, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var accion = () => FicheroSeguro.Escribir(Ruta, "nuevo", _ => { });
            accion.Should().Throw<Exception>();
        }

        File.ReadAllText(Ruta).Should().Be("viejo");
        Directory.GetFiles(_carpeta).Should().ContainSingle();
    }

    [Fact]
    public void Aparta_el_fichero_ilegible_con_la_fecha_en_el_nombre()
    {
        File.WriteAllText(Ruta, "{ esto no es json");

        var apartado = FicheroSeguro.ApartarIlegible(Ruta, new DateTimeOffset(2026, 11, 15, 12, 39, 7, TimeSpan.Zero));

        apartado.Should().Be(Ruta + ".dañado-20261115-123907");
        File.Exists(Ruta).Should().BeFalse();
        File.ReadAllText(apartado!).Should().Be("{ esto no es json");
    }

    [Fact]
    public void Dos_apartados_en_el_mismo_segundo_no_se_pisan()
    {
        var momento = new DateTimeOffset(2026, 11, 15, 12, 39, 7, TimeSpan.Zero);
        File.WriteAllText(Ruta, "uno");
        var primero = FicheroSeguro.ApartarIlegible(Ruta, momento);
        File.WriteAllText(Ruta, "dos");
        var segundo = FicheroSeguro.ApartarIlegible(Ruta, momento);

        segundo.Should().NotBe(primero);
        File.ReadAllText(primero!).Should().Be("uno");
        File.ReadAllText(segundo!).Should().Be("dos");
    }

    [Fact]
    public void Lo_que_no_se_pudo_leer_se_aparta_antes_de_escribir_encima()
    {
        File.WriteAllText(Ruta, "lo del operador");
        FicheroSeguro.MarcarIlegible(Ruta);

        FicheroSeguro.Escribir(Ruta, "de fabrica");

        File.ReadAllText(Ruta).Should().Be("de fabrica");
        var apartados = Directory.GetFiles(_carpeta, "ajustes.json.dañado-*");
        apartados.Should().ContainSingle();
        File.ReadAllText(apartados[0]).Should().Be("lo del operador");
    }

    [Fact]
    public void Si_no_se_puede_apartar_lo_ilegible_no_se_escribe_encima()
    {
        File.WriteAllText(Ruta, "lo del operador");
        using (new FileStream(Ruta, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            // Cogido por otro: ni se deja apartar ahora ni al escribir.
            FicheroSeguro.ApartarIlegible(Ruta).Should().BeNull();
            var accion = () => FicheroSeguro.Escribir(Ruta, "de fabrica", _ => { });
            accion.Should().Throw<Exception>();
        }

        File.ReadAllText(Ruta).Should().Be("lo del operador");
    }
}
