using System.IO;
using FluentAssertions;
using Nodisla.Cuaderno.Ui.Soporte;
using Xunit;

namespace Nodisla.Cuaderno.Ui.Pruebas;

/// <summary>
/// Se conserva un mes de registros.
/// </summary>
/// <remarks>
/// Con 14 ficheros, y contando tambien los <c>_001</c> que dejaban los arranques dobles, una
/// averia de hace diez dias ya no tenia registro que mirar. La auditoria de noviembre de 2026
/// se pudo hacer porque el programa llevaba poco tiempo instalado.
/// </remarks>
public sealed class RetencionDelRegistroPruebas : IDisposable
{
    private readonly string _carpeta = Path.Combine(
        Path.GetTempPath(), "cuaderno-retencion-" + Guid.NewGuid().ToString("N"));

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
    public void Se_conservan_al_menos_treinta_y_un_ficheros() =>
        RegistroDelPrograma.FicherosAConservar.Should().BeGreaterThanOrEqualTo(31);

    [Fact]
    public void Con_mas_de_un_mes_de_registros_se_quedan_los_treinta_y_uno_mas_recientes()
    {
        Directory.CreateDirectory(_carpeta);
        var primerDia = new DateTime(2020, 1, 1);
        for (var dia = 0; dia < 45; dia++)
        {
            File.WriteAllText(
                Path.Combine(_carpeta, $"cuaderno-{primerDia.AddDays(dia):yyyyMMdd}.log"),
                "registro viejo");
        }

        using (var registro = RegistroDelPrograma.Crear(_carpeta))
        {
            registro.Information("Arranque de hoy.");
        }

        var quedan = Directory.GetFiles(_carpeta, "cuaderno-*.log").Select(Path.GetFileName).ToList();
        quedan.Should().HaveCount(31);
        quedan.Should().Contain($"cuaderno-{primerDia.AddDays(44):yyyyMMdd}.log");
        quedan.Should().NotContain($"cuaderno-{primerDia:yyyyMMdd}.log");
    }
}
