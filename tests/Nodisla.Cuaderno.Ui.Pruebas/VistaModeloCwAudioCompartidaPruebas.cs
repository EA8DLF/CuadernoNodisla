using FluentAssertions;
using Nodisla.Cuaderno.Ui.Ajustes;
using Nodisla.Cuaderno.Ui.Desarrollo;
using Nodisla.Cuaderno.Ui.VistaModelos;

namespace Nodisla.Cuaderno.Ui.Pruebas;

/// <summary>
/// «Pausa» en CW tiene que soltar de verdad la entrada de audio compartida, no solo dejar de
/// decodificar. Antes del arreglo, <c>MirarLaEscucha</c> solo desenganchaba
/// <c>BloqueCapturado</c>: la tarjeta (un <c>AddSingleton</c> que comparten CW, el módem propio y
/// la fonía) se quedaba abierta para siempre, y con ella el hilo de captura de Windows.
/// </summary>
/// <remarks>
/// <see cref="EntradaDeAudioSimulada"/> es un doble fiel para esto: al abrir arranca de verdad un
/// <see cref="System.Threading.Timer"/> que inventa audio cada 20 ms, y al cerrar lo para y pone
/// <c>Abierto</c> a nulo. Que <c>Abierto</c> vuelva a nulo tras la pausa demuestra que se ha
/// llamado a <c>CerrarAsync</c> de verdad, no solo que ha cambiado una propiedad de la pantalla.
/// </remarks>
[Collection(nameof(ColeccionDeLaVentana))]
public sealed class VistaModeloCwAudioCompartidaPruebas
{
    [Fact]
    public async Task PausaSueltaLaEntradaCuandoNadieMasLaNecesita()
    {
        var entrada = new EntradaDeAudioSimulada();
        var modelo = new VistaModeloCw(new AjustesDelPrograma(), entrada, conReloj: false) { MostrarAMano = true };

        await modelo.EscucharAsync();
        entrada.Abierto.Should().NotBeNull("al escuchar se pide la entrada compartida");

        modelo.AlternarPausa();
        await Task.Delay(50); // la pausa suelta la entrada en segundo plano; se espera a que termine

        modelo.Escuchando.Should().BeFalse();
        entrada.Abierto.Should().BeNull("en pausa, y sin nadie más usándola, la tarjeta se cierra de verdad");
    }

    [Fact]
    public async Task AlSeguirSinPausaVuelveAAbrirLaEntrada()
    {
        var entrada = new EntradaDeAudioSimulada();
        var modelo = new VistaModeloCw(new AjustesDelPrograma(), entrada, conReloj: false) { MostrarAMano = true };

        await modelo.EscucharAsync();
        modelo.AlternarPausa();
        await Task.Delay(50);
        entrada.Abierto.Should().BeNull();

        modelo.AlternarPausa(); // seguir
        await Task.Delay(50);

        modelo.Escuchando.Should().BeTrue();
        entrada.Abierto.Should().NotBeNull("al salir de pausa se vuelve a pedir la entrada");
    }

    [Fact]
    public async Task UnEscucharYUnaPausaCasiALaVezNoDejanLaEntradaAFueraNiDosVecesPedida()
    {
        // Simula el «Escuchar» a mano y el cambio de visibilidad disparando MirarLaEscucha casi
        // a la vez: con el recuento de referencias de EntradaDeAudioCompartida esto podría pedir
        // la tarjeta dos veces y dejar, tras una sola pausa, una referencia fantasma sin soltar.
        // Aquí no hay EntradaDeAudioCompartida de por medio (eso se prueba aparte, en
        // Nodisla.Cuaderno.Audio.Pruebas) pero sí se comprueba que VistaModeloCw por sí solo no
        // pide la entrada más de una vez por un mismo «quiero audio».
        var entrada = new EntradaDeAudioSimulada();
        var modelo = new VistaModeloCw(new AjustesDelPrograma(), entrada, conReloj: false) { MostrarAMano = true };

        var t1 = modelo.EscucharAsync();
        var t2 = modelo.EscucharAsync();
        await Task.WhenAll(t1, t2);

        entrada.Abierto.Should().NotBeNull();

        modelo.AlternarPausa();
        await Task.Delay(50);

        entrada.Abierto.Should().BeNull("una sola pausa tiene que bastar para soltarla, sin referencias fantasma");
    }
}
