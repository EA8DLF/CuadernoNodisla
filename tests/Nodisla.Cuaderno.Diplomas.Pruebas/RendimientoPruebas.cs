using System.Diagnostics;
using FluentAssertions;
using Nodisla.Cuaderno.Dominio.Valores;
using Xunit.Abstractions;

namespace Nodisla.Cuaderno.Diplomas.Pruebas;

/// <summary>
/// <c>QueAportaAsync</c> se dispara con cada tecla del indicativo y con cada anuncio del
/// cluster, asi que tiene que ser inmediata con un cuaderno de verdad.
/// </summary>
/// <param name="salida">Por donde se imprimen los tiempos medidos.</param>
public sealed class RendimientoPruebas(ITestOutputHelper salida) : IAsyncLifetime
{
    private const int Contactos = 50_000;
    private const int Medidas = 500;

    private readonly CuadernoDePrueba _cuaderno = new();

    public Task InitializeAsync()
    {
        var reloj = Stopwatch.StartNew();
        var azar = new Random(20260922);
        var entidades = new[] { 291, 29, 281, 230, 1, 5, 100, 108, 339, 287, 15, 54, 214, 227, 262 };
        var bandas = new[] { "160m", "80m", "40m", "20m", "15m", "10m", "6m" };
        var modos = new[] { "SSB", "CW", "FT8", "RTTY" };
        var estados = new[] { "CA", "NY", "TX", "FL", "WA", "OH", "AZ" };

        _cuaderno.EnLote(c =>
        {
            for (var i = 0; i < Contactos; i++)
            {
                var dxcc = entidades[azar.Next(entidades.Length)];
                var id = c.AnadirQso(
                    $"K{azar.Next(0, 10)}{Letras(azar)}",
                    banda: bandas[azar.Next(bandas.Length)],
                    modo: modos[azar.Next(modos.Length)],
                    fecha: new DateTimeOffset(2015, 1, 1, 0, 0, 0, TimeSpan.Zero).AddMinutes(i * 7),
                    dxcc: dxcc,
                    state: dxcc == 291 ? estados[azar.Next(estados.Length)] : null,
                    cqz: 1 + azar.Next(40),
                    ituz: 1 + azar.Next(75),
                    cont: "NA",
                    pfx: $"K{azar.Next(0, 10)}");

                if (azar.Next(100) < 60) c.AnadirConfirmacion(id, "LOTW", "Y");
                if (azar.Next(100) < 30) c.AnadirConfirmacion(id, "QSL", "Y");
            }
        });

        salida.WriteLine(
            $"Cuaderno de prueba: {Contactos} contactos cargados en {reloj.Elapsed.TotalSeconds:F1} s.");
        return Task.CompletedTask;

        static string Letras(Random azar) =>
            new([(char)('A' + azar.Next(26)), (char)('A' + azar.Next(26)), (char)('A' + azar.Next(26))]);
    }

    public Task DisposeAsync() => _cuaderno.DisposeAsync().AsTask();

    [Fact]
    public async Task Que_aporta_responde_al_instante_con_decenas_de_miles_de_contactos()
    {
        var motor = _cuaderno.Motor(o =>
        {
            o.MisDiplomas.Add("DXCC/MIXED");
            o.MisDiplomas.Add("DXCC/20m");
            o.MisDiplomas.Add("WAS/WAS");
            o.MisDiplomas.Add("WPX/WPX-MIXED");
            o.MisDiplomas.Add("CCC/GENERAL");
        });

        var preparacion = Stopwatch.StartNew();
        await motor.PrepararAsync();
        preparacion.Stop();

        var indicativos = new[]
        {
            "EA8DLF", "K1ABC", "VK3XYZ", "3B8ABC", "JA1AA", "PY2XYZ", "ZS6ABC", "LU1DZ",
            "9A1AA", "OH2BH", "VU2ABC", "HB9ABC", "CE3AA", "5B4ABC", "TF3ABC", "KH6XX",
        };
        var banda = Banda.Parse("20m");
        var modo = Modo.Crudo("SSB");

        // Primera llamada: incluye construir los conjuntos en memoria a partir del cuaderno.
        var primera = Stopwatch.StartNew();
        await motor.QueAportaAsync(Indicativo.Crudo(indicativos[0]), banda, modo);
        primera.Stop();

        var tiempos = new List<double>(Medidas);
        for (var i = 0; i < Medidas; i++)
        {
            var reloj = Stopwatch.StartNew();
            await motor.QueAportaAsync(Indicativo.Crudo(indicativos[i % indicativos.Length]), banda, modo);
            reloj.Stop();
            tiempos.Add(reloj.Elapsed.TotalMilliseconds);
        }

        tiempos.Sort();
        var mediana = tiempos[tiempos.Count / 2];
        var percentil99 = tiempos[(int)(tiempos.Count * 0.99)];
        var media = tiempos.Average();

        salida.WriteLine(
            "Preparación (leer recurso, compilar catálogo si hacía falta y abrir la base): " +
            $"{preparacion.Elapsed.TotalMilliseconds:F0} ms");
        salida.WriteLine(
            "Primera llamada a QueAportaAsync (construye los conjuntos): " +
            $"{primera.Elapsed.TotalMilliseconds:F1} ms");
        salida.WriteLine(
            $"QueAportaAsync en caliente sobre {Contactos} contactos y 5 variantes, " +
            $"{Medidas} llamadas: mediana {mediana:F3} ms · media {media:F3} ms · p99 {percentil99:F3} ms");

        mediana.Should().BeLessThan(5, "se dispara con cada tecla del indicativo");
        percentil99.Should().BeLessThan(50);
    }

    [Fact]
    public async Task Que_aporta_sigue_siendo_rapida_comprobando_el_cuaderno_en_cada_llamada()
    {
        var motor = _cuaderno.Motor(o =>
        {
            o.IntervaloDeComprobacion = TimeSpan.Zero;
            o.MisDiplomas.Add("DXCC/MIXED");
            o.MisDiplomas.Add("WAS/WAS");
        });

        await motor.PrepararAsync();
        var banda = Banda.Parse("20m");
        var modo = Modo.Crudo("SSB");
        await motor.QueAportaAsync(Indicativo.Crudo("EA8DLF"), banda, modo);

        var tiempos = new List<double>(100);
        for (var i = 0; i < 100; i++)
        {
            var reloj = Stopwatch.StartNew();
            await motor.QueAportaAsync(Indicativo.Crudo("EA8DLF"), banda, modo);
            reloj.Stop();
            tiempos.Add(reloj.Elapsed.TotalMilliseconds);
        }

        tiempos.Sort();
        salida.WriteLine(
            "QueAportaAsync comprobando la marca de agua en cada llamada: " +
            $"mediana {tiempos[tiempos.Count / 2]:F3} ms · media {tiempos.Average():F3} ms");

        // Aqui cada llamada mira el cuaderno, asi que el listón es más alto que en caliente.
        tiempos[tiempos.Count / 2].Should().BeLessThan(50);
    }

    [Fact]
    public async Task El_progreso_completo_de_una_variante_se_resuelve_en_una_consulta()
    {
        var motor = _cuaderno.Motor(o => o.IntervaloDeComprobacion = TimeSpan.Zero);
        await motor.PrepararAsync();

        var reloj = Stopwatch.StartNew();
        var progreso = await motor.ProgresoAsync("DXCC", "MIXED");
        reloj.Stop();

        salida.WriteLine(
            $"ProgresoAsync(DXCC/MIXED) sin caché sobre {Contactos} contactos: " +
            $"{reloj.Elapsed.TotalMilliseconds:F1} ms · {progreso.Trabajadas} trabajadas, " +
            $"{progreso.Confirmadas} confirmadas");

        progreso.Trabajadas.Should().BeGreaterThan(0);
        reloj.Elapsed.TotalMilliseconds.Should().BeLessThan(2000);
    }
}
