using System.Runtime.CompilerServices;
using FluentAssertions;
using Nodisla.Cuaderno.Diplomas.Catalogo;

namespace Nodisla.Cuaderno.Diplomas.Pruebas;

/// <summary>
/// Apunta el lector al catalogo reducido de las pruebas antes de que corra ninguna.
/// </summary>
/// <remarks>
/// El catalogo completo (553.064 referencias sacadas de Log4OM) no se publica. Las pruebas usan
/// <c>Datos/catalogo-de-prueba.tsv.gz</c>: las definiciones de los 87 diplomas y solo las
/// referencias factuales que hacen falta. Con <c>CUADERNO_CATALOGO_DIPLOMAS</c> se puede pasar
/// el completo.
/// </remarks>
internal static class CatalogoDeLasPruebas
{
    /// <summary>Ruta del catalogo reducido junto a los binarios de las pruebas.</summary>
    public static string Ruta { get; } =
        Environment.GetEnvironmentVariable("CUADERNO_CATALOGO_DIPLOMAS") is { Length: > 0 } otro
            ? otro
            : Path.Combine(AppContext.BaseDirectory, "Datos", "catalogo-de-prueba.tsv.gz");

    [ModuleInitializer]
    internal static void Preparar() => LectorDelRecurso.RutaDelFichero = Ruta;
}

/// <summary>Sin catalogo instalado el motor no se cae: trabaja con un catalogo vacio.</summary>
public sealed class SinCatalogoPruebas
{
    [Fact]
    public async Task Sin_catalogo_el_motor_no_tiene_diplomas_y_no_revienta()
    {
        var anterior = LectorDelRecurso.RutaDelFichero;
        LectorDelRecurso.RutaDelFichero = Path.Combine(Path.GetTempPath(), "no-existe-" + Guid.NewGuid().ToString("N") + ".gz");
        try
        {
            LectorDelRecurso.Disponible.Should().BeFalse();
            LectorDelRecurso.LeerCatalogo().Diplomas.Should().BeEmpty();
            LectorDelRecurso.LeerReferencias().Should().BeEmpty();

            await using var cuaderno = new CuadernoDePrueba();
            var motor = cuaderno.NuevoMotor(o => o.RutaDelCatalogo = Path.Combine(
                Path.GetTempPath(), "nodisla-sin-catalogo-" + Guid.NewGuid().ToString("N") + ".sqlite"));
            (await motor.CatalogoAsync()).Should().BeEmpty();
            (await motor.ProgresoDeMisDiplomasAsync()).Should().BeEmpty();
        }
        finally
        {
            LectorDelRecurso.RutaDelFichero = anterior;
        }
    }

    [Fact]
    public void Con_el_catalogo_de_las_pruebas_hay_catalogo() =>
        LectorDelRecurso.Disponible.Should().BeTrue("las pruebas apuntan a Datos/catalogo-de-prueba.tsv.gz");
}
