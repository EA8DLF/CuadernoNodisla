using FluentAssertions;
using Nodisla.Cuaderno.Modos.Rtty;

namespace Nodisla.Cuaderno.Modos.Pruebas.Rtty;

/// <summary>Los tramos y el audio que fabrica <see cref="GeneradorRtty"/>: solo función pura, sin PTT ni hardware.</summary>
public class GeneradorRttyPruebas
{
    [Fact]
    public void CadaCaracterSonArranqueEspacioCincoDatosYParadaMarca()
    {
        var p = new ParametrosRtty();
        var codigos = GeneradorRtty.Codigos("E"); // 'E' = 00001, un único código, sin cambios de juego
        codigos.Should().ContainSingle();

        var tramos = GeneradorRtty.Tramos("E", p);
        tramos.Should().HaveCount(7, "arranque + 5 datos + parada");
        tramos[0].Marca.Should().BeFalse("el bit de arranque siempre es espacio");
        tramos[^1].Marca.Should().BeTrue("el bit de parada siempre es marca");
        tramos[^1].Segundos.Should().BeApproximately(p.DuracionDelBit * p.BitsDeParada, 1e-9);

        // 'E' = 00001: LSB primero es 1,0,0,0,0 → el primer bit de datos es marca y los otros cuatro espacio.
        tramos[1].Marca.Should().BeTrue();
        for (var i = 2; i <= 5; i++) tramos[i].Marca.Should().BeFalse();
    }

    [Fact]
    public void LosBitsDeDatosVanLsbPrimero()
    {
        // 'A' = 00011 (3): LSB primero → 1,1,0,0,0.
        var tramos = GeneradorRtty.Tramos("A");
        tramos[1].Marca.Should().BeTrue();
        tramos[2].Marca.Should().BeTrue();
        tramos[3].Marca.Should().BeFalse();
        tramos[4].Marca.Should().BeFalse();
        tramos[5].Marca.Should().BeFalse();
    }

    [Fact]
    public void UnDigitoInsertaElCambioACifrasYUnaLetraElCambioALetras()
    {
        var codigos = GeneradorRtty.Codigos("A1B");
        codigos.Should().Equal(
            TablaBaudot.Codigo('A', JuegoBaudot.Letras)!.Value,
            TablaBaudot.CambioACifras,
            TablaBaudot.Codigo('1', JuegoBaudot.Cifras)!.Value,
            TablaBaudot.CambioALetras,
            TablaBaudot.Codigo('B', JuegoBaudot.Letras)!.Value);
    }

    [Fact]
    public void UnCaracterQueBaudotNoTieneLanzaFormatException()
    {
        var generar = () => GeneradorRtty.Codigos("Ñ");
        generar.Should().Throw<FormatException>();
    }

    [Fact]
    public void ElAudioGeneradoTieneLaDuracionEsperadaYNoSeSaleDeRango()
    {
        const int Fs = 8000;
        var p = new ParametrosRtty();
        var audio = GeneradorRtty.Generar("CQ", 1500, Fs, p, amplitud: 0.4, silencioDelante: 0.1, silencioDetras: 0.1);

        var tramos = GeneradorRtty.Tramos("CQ", p);
        var esperado = 0.1 + tramos.Sum(t => t.Segundos) + 0.1;
        (audio.Length / (double)Fs).Should().BeApproximately(esperado, 0.01);

        audio.Should().OnlyContain(x => x >= -0.41f && x <= 0.41f, "la amplitud de pico no debe superarse");
        audio.Should().Contain(x => Math.Abs(x) > 0.1f, "tiene que sonar, no ser todo silencio");
    }

    [Fact]
    public void EnReposoElTonoEsElDeMarca()
    {
        // Con silencioDelante largo y nada que mandar hasta entonces, las primeras muestras están
        // en el tono de marca: se puede comprobar con un Goertzel simple sin montar un canal entero.
        const int Fs = 8000;
        const double TonoMarca = 1500;
        var audio = GeneradorRtty.Generar("E", TonoMarca, Fs, silencioDelante: 0.5, silencioDetras: 0);

        var energiaEnMarca = EnergiaDeGoertzel(audio.AsSpan(0, 4000), TonoMarca, Fs);
        var energiaFueraDeTono = EnergiaDeGoertzel(audio.AsSpan(0, 4000), TonoMarca - 170, Fs);
        energiaEnMarca.Should().BeGreaterThan(energiaFueraDeTono * 10, "en reposo solo debe sonar el tono de marca");
    }

    private static double EnergiaDeGoertzel(ReadOnlySpan<float> muestras, double tonoHz, int frecuenciaDeMuestreo)
    {
        var w = 2 * Math.PI * tonoHz / frecuenciaDeMuestreo;
        double s0, s1 = 0, s2 = 0;
        var coseno = 2 * Math.Cos(w);
        foreach (var x in muestras)
        {
            s0 = x + (coseno * s1) - s2;
            s2 = s1;
            s1 = s0;
        }

        return (s1 * s1) + (s2 * s2) - (coseno * s1 * s2);
    }
}
