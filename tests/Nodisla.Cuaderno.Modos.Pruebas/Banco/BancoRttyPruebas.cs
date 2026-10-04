using FluentAssertions;
using Nodisla.Cuaderno.Modos.Rtty;

namespace Nodisla.Cuaderno.Modos.Pruebas.Banco;

/// <summary>
/// Bucle cerrado de RTTY: genera con <see cref="GeneradorRtty"/>, demodula con
/// <see cref="CanalRtty"/>, a varias relaciones señal-ruido. Lo que no se negocia: con ruido puro
/// no sale ni un carácter (ni engancha).
/// </summary>
public class BancoRttyPruebas
{
    private static readonly string[] Textos =
    [
        "CQ CQ CQ DE EA8DLF EA8DLF K",
        "RST 599 599 NAME JOSE QTH TENERIFE",
        "TEST 1234567890 UR RST 579 TU 73",
    ];

    [Theory]
    [InlineData(6)]
    [InlineData(0)]
    [InlineData(-6)]
    public void DecodificaBienPorEncimaDeUnUmbralRazonable(double db)
    {
        var semilla = 1000 + (int)db;
        foreach (var texto in Textos)
        {
            var azar = new Random(semilla++);
            var parametros = new ParametrosRtty();
            var audio = BancoRtty.Senal(texto, 1500, BancoRtty.Frecuencia, parametros, db, azar);
            var (leido, enganchado) = BancoRtty.Decodificar(audio, BancoRtty.Frecuencia, 1500, parametros);

            enganchado.Should().BeTrue($"con {db} dB tiene que engancharse: «{texto}»");
            var normalizado = Normalizar(leido);
            var cer = 100.0 * BancoRtty.Distancia(Normalizar(texto), normalizado) / texto.Length;
            cer.Should().BeLessThan(15, $"a {db} dB «{texto}» se leyó como «{normalizado}»");
        }
    }

    [Theory]
    [InlineData(170)]
    [InlineData(850)]
    public void FuncionaConLosDosDesplazamientosDeSerie(double desplazamientoHz)
    {
        var azar = new Random(55);
        var parametros = new ParametrosRtty(DesplazamientoHz: desplazamientoHz);
        var texto = Textos[0];
        var audio = BancoRtty.Senal(texto, 1500, BancoRtty.Frecuencia, parametros, 3, azar);
        var (leido, enganchado) = BancoRtty.Decodificar(audio, BancoRtty.Frecuencia, 1500, parametros);

        enganchado.Should().BeTrue();
        Normalizar(leido).Should().Contain("EA8DLF");
    }

    [Fact]
    public void FuncionaInvertido()
    {
        var azar = new Random(77);
        var parametros = new ParametrosRtty(Invertido: true);
        var texto = Textos[0];
        var audio = BancoRtty.Senal(texto, 1500, BancoRtty.Frecuencia, parametros, 3, azar);
        var (leido, enganchado) = BancoRtty.Decodificar(audio, BancoRtty.Frecuencia, 1500, parametros);

        enganchado.Should().BeTrue();
        Normalizar(leido).Should().Contain("EA8DLF");
    }

    [Theory]
    [InlineData(4242)]
    [InlineData(777)]
    [InlineData(99)]
    public void ConRuidoPuroNoSaleTextoNiSeEngancha(int semilla)
    {
        var azar = new Random(semilla);
        const int segundos = 20;
        var audio = new float[BancoRtty.Frecuencia * segundos];
        // Ruido con una varianza comparable a la de una señal de 0,3 de amplitud a 0 dB de referencia.
        Nodisla.Cuaderno.Modos.Banco.GeneradorDeSenal.AnadirRuido(audio, 0.3 * 0.3 / 2, 0, BancoRtty.Frecuencia, azar);

        var (leido, enganchado) = BancoRtty.Decodificar(audio, BancoRtty.Frecuencia, 1500, new ParametrosRtty());

        leido.Should().BeEmpty("con puro ruido no debe salir ni un carácter");
        enganchado.Should().BeFalse();
    }

    private static string Normalizar(string texto) =>
        string.Join(' ', texto.ToUpperInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
}
