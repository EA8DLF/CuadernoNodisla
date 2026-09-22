using System.Diagnostics;
using Nodisla.Cuaderno.Dominio.Valores;
using Xunit.Abstractions;

namespace Nodisla.Cuaderno.Diplomas.Pruebas;

public sealed class DiagnosticoTemporal(ITestOutputHelper salida) : IAsyncLifetime
{
    private readonly CuadernoDePrueba _cuaderno = new();

    public Task InitializeAsync()
    {
        var azar = new Random(1);
        var entidades = new[] { 291, 29, 281, 230, 1, 5, 100, 108, 339, 287 };
        var bandas = new[] { "160m", "80m", "40m", "20m", "15m", "10m" };
        _cuaderno.EnLote(c =>
        {
            for (var i = 0; i < 50_000; i++)
            {
                var d = entidades[azar.Next(entidades.Length)];
                var id = c.AnadirQso($"K{azar.Next(10)}{(char)('A' + azar.Next(26))}{(char)('A' + azar.Next(26))}{(char)('A' + azar.Next(26))}",
                    banda: bandas[azar.Next(bandas.Length)], dxcc: d,
                    state: d == 291 ? "CA" : null, pfx: $"K{azar.Next(10)}",
                    fecha: new DateTimeOffset(2015, 1, 1, 0, 0, 0, TimeSpan.Zero).AddMinutes(i * 7));
                if (azar.Next(100) < 60) c.AnadirConfirmacion(id, "LOTW", "Y");
            }
        });
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => _cuaderno.DisposeAsync().AsTask();

    [Fact]
    public async Task Medir_cada_variante()
    {
        var motor = _cuaderno.Motor(o => o.IntervaloDeComprobacion = TimeSpan.FromHours(1));
        await motor.PrepararAsync();

        foreach (var clave in new[] { "DXCC/MIXED", "DXCC/20m", "WAS/WAS", "WPX/WPX-MIXED", "CCC/GENERAL", "IOTA/IOTA_BASICS", "AA/GENERAL" })
        {
            var p = clave.Split('/');
            motor.AvisarDeCambioGeneral();
            var r = Stopwatch.StartNew();
            var pr = await motor.ProgresoAsync(p[0], p[1]);
            r.Stop();
            salida.WriteLine($"{clave}: {r.Elapsed.TotalMilliseconds:F0} ms ({pr.Trabajadas}/{pr.Confirmadas})");
        }

        var marca = Stopwatch.StartNew();
        using (var c = _cuaderno.AbrirConCatalogo())
        {
            for (var i = 0; i < 20; i++) await Cache.MarcaDeAgua.LeerAsync(c);
        }
        marca.Stop();
        salida.WriteLine($"MarcaDeAgua x20: {marca.Elapsed.TotalMilliseconds / 20:F1} ms por lectura");
    }
}
