using System.IO;
using FluentAssertions;
using Nodisla.Cuaderno.Ui.Soporte;
using Xunit;

namespace Nodisla.Cuaderno.Ui.Pruebas;

/// <summary>
/// Una sola copia del programa por carpeta de datos, sin estorbar a las de verificacion.
/// </summary>
public class InstanciaUnicaPruebas
{
    // Cada prueba con su nombre: el del programa de verdad no se toca, que el operador puede
    // tenerlo abierto mientras corren las pruebas.
    private static string NombreDePrueba() => "NODISLA.CuadernoNodisla.Pruebas." + Guid.NewGuid().ToString("N");

    [Fact]
    public void La_primera_copia_toma_el_cerrojo_y_la_segunda_no()
    {
        var nombre = NombreDePrueba();

        using var primera = InstanciaUnica.Tomar(nombre);
        using var segunda = InstanciaUnica.Tomar(nombre);

        primera.Should().NotBeNull();
        segunda.Should().BeNull();
    }

    [Fact]
    public void Al_cerrarse_la_primera_ya_se_puede_abrir_otra()
    {
        var nombre = NombreDePrueba();
        InstanciaUnica.Tomar(nombre)!.Dispose();

        using var otra = InstanciaUnica.Tomar(nombre);

        otra.Should().NotBeNull();
    }

    [Fact]
    public void La_segunda_copia_avisa_a_la_primera_para_que_se_ponga_delante()
    {
        var nombre = NombreDePrueba();
        using var primera = InstanciaUnica.Tomar(nombre)!;
        using var llamada = new ManualResetEventSlim();
        primera.AlLlamarOtraCopia(llamada.Set);

        InstanciaUnica.LlamarALaPrimera(nombre).Should().BeTrue();

        llamada.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
    }

    [Fact]
    public void Sin_primera_copia_no_hay_a_quien_llamar() =>
        InstanciaUnica.LlamarALaPrimera(NombreDePrueba()).Should().BeFalse();

    [Fact]
    public void El_programa_normal_lleva_el_cerrojo_que_conoce_el_instalador() =>
        InstanciaUnica.NombreDelCerrojo(carpetaForzada: null, simulado: false)
            .Should().Be(InstanciaUnica.NombreDelPrograma);

    [Fact]
    public void El_modo_de_demostracion_no_lleva_cerrojo()
    {
        // Las capturas de la ayuda y las verificaciones se lanzan con CUADERNO_SIMULADO
        // mientras el operador tiene abierto el programa de verdad: tienen que arrancar.
        InstanciaUnica.NombreDelCerrojo(carpetaForzada: null, simulado: true).Should().BeNull();
        InstanciaUnica.NombreDelCerrojo(@"C:\temp\verificacion", simulado: true).Should().BeNull();
    }

    [Fact]
    public void Una_carpeta_de_verificacion_tiene_su_propio_cerrojo()
    {
        var deVerificacion = InstanciaUnica.NombreDelCerrojo(@"C:\temp\verificacion", simulado: false);

        deVerificacion.Should().NotBe(InstanciaUnica.NombreDelPrograma);
        deVerificacion.Should().StartWith(InstanciaUnica.NombreDelPrograma + ".");

        // La misma carpeta escrita de otra forma es la misma carpeta.
        InstanciaUnica.NombreDelCerrojo(@"c:\TEMP\verificacion\", simulado: false).Should().Be(deVerificacion);
        InstanciaUnica.NombreDelCerrojo(@"C:\temp\otra", simulado: false).Should().NotBe(deVerificacion);
    }

    [Fact]
    public void El_instalador_espera_al_mismo_cerrojo_que_usa_el_programa()
    {
        var carpeta = new DirectoryInfo(AppContext.BaseDirectory);
        while (carpeta is not null && !File.Exists(Path.Combine(carpeta.FullName, "CuadernoNodisla.sln")))
        {
            carpeta = carpeta.Parent;
        }

        carpeta.Should().NotBeNull("las pruebas se ejecutan dentro del repositorio");
        var guion = File.ReadAllLines(Path.Combine(carpeta!.FullName, "instalador", "CuadernoNodisla.iss"));

        guion.Should().Contain("AppMutex=" + InstanciaUnica.NombreDelPrograma);
    }
}
