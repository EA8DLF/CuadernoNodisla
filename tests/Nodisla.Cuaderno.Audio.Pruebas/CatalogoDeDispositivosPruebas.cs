using FluentAssertions;
using Nodisla.Cuaderno.Audio.Dispositivos;

namespace Nodisla.Cuaderno.Audio.Pruebas;

/// <summary>
/// El marcado del dispositivo del equipo entre todos los del sistema.
/// </summary>
/// <remarks>
/// En esta maquina hay mas de veinte dispositivos de sonido y varios se llaman parecido. Lo que
/// se prueba aqui es la parte que no depende de Windows: que el que aparece en la lista de
/// extremos del equipo sale marcado y el primero, para que el operador no tenga que adivinar
/// cual es su radio.
/// </remarks>
public class CatalogoDeDispositivosPruebas
{
    private static readonly (string Id, string Nombre)[] LoQueHayEnLaMaquina =
    [
        ("{0.0.1.00000000}.{aaa}", "Micrófono (Realtek Audio)"),
        ("{0.0.1.00000000}.{bbb}", "Micrófono (USB Audio Device)"),
        ("{0.0.1.00000000}.{ccc}", "CABLE Output (VB-Audio Virtual Cable)"),
    ];

    [Fact]
    public void ElDelEquipoSaleMarcadoYElPrimero()
    {
        var delEquipo = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "{0.0.1.00000000}.{BBB}",
        };

        var lista = CatalogoDeDispositivos.Componer(LoQueHayEnLaMaquina, delEquipo, esDeEntrada: true);

        lista.Should().HaveCount(3);
        lista[0].EsDelEquipo.Should().BeTrue();
        lista[0].Nombre.Should().Be("Micrófono (USB Audio Device)");
        lista.Where(dispositivo => dispositivo.EsDelEquipo).Should().ContainSingle();
        lista.Should().OnlyContain(dispositivo => dispositivo.EsDeEntrada);
    }

    [Fact]
    public void ConLaRadioApagadaNoHayNingunoMarcado()
    {
        var lista = CatalogoDeDispositivos.Componer(
            LoQueHayEnLaMaquina,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            esDeEntrada: true);

        lista.Should().HaveCount(3);
        lista.Should().NotContain(dispositivo => dispositivo.EsDelEquipo);
    }
}
