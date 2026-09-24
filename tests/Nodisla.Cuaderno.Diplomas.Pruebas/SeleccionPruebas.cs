using FluentAssertions;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Diplomas.Pruebas;

/// <summary>
/// Qué diplomas sigue el operador lo guarda el motor, no la pantalla.
/// </summary>
/// <remarks>
/// Si la elección viviera en la interfaz, cualquier otra cosa que quisiera saberlo —el aviso al
/// teclear un indicativo, un resumen al arrancar— tendría que preguntárselo a la ventana. Y
/// tiene que sobrevivir a cerrar el programa: una elección que se pierde al salir no es una
/// elección.
/// </remarks>
public sealed class SeleccionPruebas : IAsyncLifetime
{
    private readonly CuadernoDePrueba _cuaderno = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => _cuaderno.DisposeAsync().AsTask();

    [Fact]
    public async Task La_seleccion_sobrevive_a_cerrar_y_abrir_el_programa()
    {
        var antes = _cuaderno.Motor();
        await antes.FijarMisDiplomasAsync(["DXCC/MIXED", "WAS/WAS"]);

        // Otro motor sobre el mismo cuaderno: es lo que pasa al arrancar mañana.
        var despues = _cuaderno.NuevoMotor();

        (await despues.MisDiplomasAsync()).Should().BeEquivalentTo(["DXCC/MIXED", "WAS/WAS"]);
    }

    [Fact]
    public async Task Lo_guardado_manda_sobre_la_seleccion_de_partida()
    {
        var antes = _cuaderno.Motor(o => o.MisDiplomas.Add("DXCC/MIXED"));
        (await antes.MisDiplomasAsync()).Should().BeEquivalentTo(["DXCC/MIXED"]);

        await antes.FijarMisDiplomasAsync(["WAS/WAS"]);

        var despues = _cuaderno.NuevoMotor(o => o.MisDiplomas.Add("DXCC/MIXED"));
        (await despues.MisDiplomasAsync()).Should().BeEquivalentTo(
            ["WAS/WAS"], "la opción es el valor de partida, no la fuente de verdad");
    }

    [Fact]
    public async Task No_seguir_ninguno_es_una_eleccion_y_tambien_se_guarda()
    {
        var antes = _cuaderno.Motor(o => o.MisDiplomas.Add("DXCC/MIXED"));
        await antes.FijarMisDiplomasAsync([]);

        var despues = _cuaderno.NuevoMotor(o => o.MisDiplomas.Add("DXCC/MIXED"));

        // Si «ninguno» no se guardara, la opción de partida volvería a colarse mañana.
        (await despues.MisDiplomasAsync()).Should().BeEmpty();
        (await despues.ProgresoDeMisDiplomasAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Fijar_la_seleccion_invalida_lo_cacheado()
    {
        _cuaderno.AnadirQso("K1ABC", dxcc: 291);
        var motor = _cuaderno.Motor(o =>
        {
            o.IntervaloDeComprobacion = TimeSpan.FromHours(1);
            o.MisDiplomas.Add("DXCC/MIXED");
        });

        (await motor.ProgresoDeMisDiplomasAsync()).Should().ContainSingle();
        var avisos = await motor.QueAportaAsync(
            Indicativo.Crudo("EA8DLF"), Banda.Parse("20m"), Modo.Crudo("SSB"));
        avisos.Should().Contain(a => a.Contains("Centenario DXCC"));

        await motor.FijarMisDiplomasAsync(["CCC"]);

        // El progreso cacheado del DXCC y los conjuntos del aviso al teclear tienen que irse.
        var mios = await motor.ProgresoDeMisDiplomasAsync();
        mios.Should().ContainSingle().Which.Codigo.Should().Be("CCC");

        var despues = await motor.QueAportaAsync(
            Indicativo.Crudo("EA8DLF"), Banda.Parse("20m"), Modo.Crudo("SSB"));
        despues.Should().NotContain(a => a.Contains("Centenario DXCC"));
    }

    [Fact]
    public async Task Un_diploma_a_secas_se_completa_con_su_clase_menos_restrictiva()
    {
        var motor = _cuaderno.Motor();
        await motor.FijarMisDiplomasAsync(["DXCC", "WAS"]);

        // La mixta del DXCC cuenta en cualquier banda y modo; la de 20 m, no.
        (await motor.MisDiplomasAsync()).Should().BeEquivalentTo(["DXCC/MIXED", "WAS/WAS"]);
    }

    [Theory]
    [InlineData("NO_EXISTE")]
    [InlineData("DXCC/NO_EXISTE")]
    public async Task Una_clave_que_no_esta_en_el_catalogo_se_rechaza_entera(string clave)
    {
        var motor = _cuaderno.Motor();
        await motor.FijarMisDiplomasAsync(["DXCC/MIXED"]);

        var fijar = async () => await motor.FijarMisDiplomasAsync(["WAS/WAS", clave]);

        await fijar.Should().ThrowAsync<ArgumentException>();
        (await motor.MisDiplomasAsync()).Should().BeEquivalentTo(
            ["DXCC/MIXED"], "una selección a medias es peor que no cambiar nada");
    }

    [Fact]
    public async Task La_seleccion_se_guarda_en_texto_junto_al_cuaderno()
    {
        await _cuaderno.Motor().FijarMisDiplomasAsync(["DXCC/MIXED"]);

        var fichero = Path.Combine(
            Path.GetDirectoryName(_cuaderno.Ruta)!, OpcionesDeDiplomas.NombreDeMisDiplomas);

        File.Exists(fichero).Should().BeTrue("el catálogo se reconstruye y no puede llevársela");
        (await File.ReadAllTextAsync(fichero)).Should().Contain("DXCC/MIXED");
    }
}
