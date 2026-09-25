using FluentAssertions;
using Nodisla.Cuaderno.Concursos.Telegrafia;

namespace Nodisla.Cuaderno.Concursos.Pruebas;

/// <summary>Nada transmite por su cuenta: sin salida vigilada, el manipulador se niega.</summary>
public sealed class ManipuladorPorAudioPruebas
{
    [Fact]
    public async Task SinSalidaVigiladaNoTransmiteYLoDice()
    {
        using var manipulador = new ManipuladorPorAudio();

        manipulador.PuedeTransmitir.Should().BeFalse();
        var accion = async () => await manipulador.EnviarAsync("CQ TEST DE EA8DLF");
        (await accion.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*no transmite*");
    }

    [Fact]
    public void GenerarAudioNoEsTransmitir()
    {
        // Se puede escuchar el monitor sin salida conectada: eso no toca la radio.
        using var manipulador = new ManipuladorPorAudio();
        manipulador.Generar("CQ").Should().NotBeEmpty();
    }

    [Fact]
    public async Task ConSalidaConectadaEmiteUnaSolaVezYConMotivo()
    {
        var emitido = new List<(int Muestras, string Motivo)>();
        Task Emitir(ReadOnlyMemory<float> muestras, string motivo, CancellationToken ct)
        {
            emitido.Add((muestras.Length, motivo));
            return Task.CompletedTask;
        }

        using var manipulador = new ManipuladorPorAudio(Emitir, new OpcionesDeTelegrafia { Ppm = 30 });
        await manipulador.EnviarAsync("CQ TEST");

        emitido.Should().ContainSingle("el texto se manipula entero, sin trocear el PTT");
        emitido[0].Muestras.Should().BeGreaterThan(0);
        emitido[0].Motivo.Should().StartWith("Telegrafía");
    }

    [Fact]
    public async Task UnTextoVacioNoPoneLaRadioEnAntena()
    {
        var veces = 0;
        Task Emitir(ReadOnlyMemory<float> m, string motivo, CancellationToken ct)
        {
            veces++;
            return Task.CompletedTask;
        }

        using var manipulador = new ManipuladorPorAudio(Emitir);
        await manipulador.EnviarAsync("   ");
        await manipulador.EnviarAsync("¡¿");

        veces.Should().Be(0);
    }

    [Fact]
    public async Task SePuedeCortarAMediaLlamada()
    {
        var empezo = new TaskCompletionSource();
        async Task Emitir(ReadOnlyMemory<float> muestras, string motivo, CancellationToken ct)
        {
            empezo.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
        }

        using var manipulador = new ManipuladorPorAudio(Emitir);
        var envio = manipulador.EnviarAsync("CQ CQ CQ TEST DE EA8DLF EA8DLF");
        await empezo.Task;
        manipulador.Manipulando.Should().BeTrue();

        await manipulador.AbortarAsync();
        await envio;

        manipulador.Manipulando.Should().BeFalse("cortar no es un error, es lo normal en un pileup");
    }

    [Fact]
    public async Task CancelarDesdeFueraSiPropagaLaCancelacion()
    {
        var empezo = new TaskCompletionSource();
        async Task Emitir(ReadOnlyMemory<float> muestras, string motivo, CancellationToken ct)
        {
            empezo.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
        }

        using var manipulador = new ManipuladorPorAudio(Emitir);
        using var cancelacion = new CancellationTokenSource();
        var envio = manipulador.EnviarAsync("CQ TEST", cancelacion.Token);
        await empezo.Task;
        await cancelacion.CancelAsync();

        var accion = async () => await envio;
        await accion.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task CambiarLaVelocidadAcortaElAudio()
    {
        using var manipulador = new ManipuladorPorAudio();
        var lento = manipulador.Generar("CQ").Length;

        await manipulador.CambiarVelocidadAsync(40);

        manipulador.Ppm.Should().Be(40);
        manipulador.Generar("CQ").Length.Should().BeLessThan(lento);
    }

    [Fact]
    public async Task UnaVelocidadImposibleNoDejaElManipuladorAMedias()
    {
        using var manipulador = new ManipuladorPorAudio(opciones: new OpcionesDeTelegrafia { Ppm = 25 });

        var accion = async () => await manipulador.CambiarVelocidadAsync(500);
        await accion.Should().ThrowAsync<ArgumentOutOfRangeException>();

        manipulador.Ppm.Should().Be(25, "los ajustes de antes siguen en pie");
    }
}
