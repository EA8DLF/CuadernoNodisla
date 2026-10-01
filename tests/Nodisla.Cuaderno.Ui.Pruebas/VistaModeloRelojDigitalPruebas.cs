using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Ui.Desarrollo;
using Nodisla.Cuaderno.Ui.VistaModelos;

namespace Nodisla.Cuaderno.Ui.Pruebas;

/// <summary>
/// Pruebas de la tira del reloj.
/// </summary>
/// <remarks>
/// Ninguna sale a la red: el reloj es de mentira. Lo que se comprueba es que la pantalla
/// <b>pinta lo que el reloj dice</b> y no se inventa la regla de cuantos milisegundos son
/// demasiados, que es del reloj.
/// </remarks>
public sealed class VistaModeloRelojDigitalPruebas
{
    [Fact]
    public void ElDesvioDeVerdadDeEstaMaquinaSaleEnRojo()
    {
        var modelo = new VistaModeloRelojDigital(new RelojSimulado());

        // -1,29 s: lo que llego a desviarse esta maquina en tres dias.
        modelo.RelojFueraDeVentana.Should().BeTrue();
        modelo.RelojBien.Should().BeFalse();
        // El separador decimal depende de la cultura: en la aplicación es coma, en la suite
        // puede ser punto. Lo que se comprueba es el número, no el adorno.
        modelo.DesvioTexto.Should().MatchRegex(@"^-1[.,]29 s$");
        modelo.HayQueHacerAlgo.Should().BeTrue();
        modelo.Consejo.Should().Contain("fuera de ventana");
    }

    [Fact]
    public async Task PonerElRelojEnHoraLoDejaEnVerdeYLoCuenta()
    {
        var reloj = new RelojSimulado();
        var modelo = new VistaModeloRelojDigital(reloj, reloj);

        modelo.SePuedePonerEnHora.Should().BeTrue();

        await modelo.PonerEnHoraCommand.ExecuteAsync(null);

        modelo.RelojBien.Should().BeTrue();
        modelo.HayParte.Should().BeTrue();
        modelo.Parte.Should().Contain("Antes:");
        modelo.Parte.Should().Contain("Después:");
    }

    [Fact]
    public void SinSincronizadorSeVeElDesvioPeroNoSeOfreceArreglarlo()
    {
        var modelo = new VistaModeloRelojDigital(new RelojSimulado());

        modelo.HaySincronizador.Should().BeFalse();
        modelo.SePuedePonerEnHora.Should().BeFalse();
        modelo.PonerEnHoraCommand.CanExecute(null).Should().BeFalse();

        // Medir sí se puede: saber que el reloj está mal ya vale de algo aunque no se pueda
        // arreglar desde aquí.
        modelo.MedirCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task MedirActualizaElEstado()
    {
        var reloj = new RelojSimulado();
        var modelo = new VistaModeloRelojDigital(reloj, reloj);

        await modelo.MedirCommand.ExecuteAsync(null);

        modelo.Estado.Desvio.EsFiable.Should().BeTrue();
        modelo.Estado.Calidad.Should().Be(CalidadDelReloj.FueraDeVentana);
    }
}
