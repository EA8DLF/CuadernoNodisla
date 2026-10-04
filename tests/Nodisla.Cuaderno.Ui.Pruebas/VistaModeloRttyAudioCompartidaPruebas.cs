using FluentAssertions;
using Nodisla.Cuaderno.Ui.Ajustes;
using Nodisla.Cuaderno.Ui.Desarrollo;
using Nodisla.Cuaderno.Ui.VistaModelos;

namespace Nodisla.Cuaderno.Ui.Pruebas;

/// <summary>
/// RTTY tiene que nacer respetando el mismo recuento de referencias que se arregló para CW: solo
/// pide la entrada compartida mientras su página está visible y no está en pausa, y la suelta de
/// verdad —no solo deja de decodificar— en cuanto deja de hacer falta.
/// </summary>
/// <remarks>
/// Mismo doble y mismo razonamiento que <c>VistaModeloCwAudioCompartidaPruebas</c>: ver esa clase
/// para el porqué de <see cref="EntradaDeAudioSimulada"/>.
/// </remarks>
[Collection(nameof(ColeccionDeLaVentana))]
public sealed class VistaModeloRttyAudioCompartidaPruebas
{
    [Fact]
    public async Task PausaSueltaLaEntradaCuandoNadieMasLaNecesita()
    {
        var entrada = new EntradaDeAudioSimulada();
        var modelo = new VistaModeloRtty(new AjustesDelPrograma(), entrada, conReloj: false) { PaginaVisible = true };

        await modelo.EscucharAsync();
        entrada.Abierto.Should().NotBeNull("al escuchar se pide la entrada compartida");

        modelo.AlternarPausa();
        await Task.Delay(50); // la pausa suelta la entrada en segundo plano; se espera a que termine

        modelo.Escuchando.Should().BeFalse();
        entrada.Abierto.Should().BeNull("en pausa, y sin nadie más usándola, la tarjeta se cierra de verdad");
    }
}
