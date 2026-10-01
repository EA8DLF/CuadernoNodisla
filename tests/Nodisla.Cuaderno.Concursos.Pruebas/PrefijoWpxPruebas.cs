using FluentAssertions;
using Nodisla.Cuaderno.Concursos.Calculo;

namespace Nodisla.Cuaderno.Concursos.Pruebas;

/// <summary>El prefijo del WPX, que es el multiplicador y por tanto la mitad del marcador.</summary>
public sealed class PrefijoWpxPruebas
{
    [Theory]
    [InlineData("EA8DLF", "EA8")]
    [InlineData("EA1ABC", "EA1")]
    [InlineData("K1ABC", "K1")]
    [InlineData("W0XYZ", "W0")]
    [InlineData("VP2EABC", "VP2")]
    [InlineData("3DA0RU", "3DA0")]
    [InlineData("CT3ABC", "CT3")]
    public void ElPrefijoLlegaHastaElUltimoNumero(string indicativo, string esperado)
    {
        PrefijoWpx.De(indicativo).Should().Be(esperado);
    }

    [Theory]
    // Un indicativo sin numero recibe un cero: es la regla explicita del WPX.
    [InlineData("RAEM", "RA0")]
    public void SinNumeroSeAnadeUnCero(string indicativo, string esperado)
    {
        PrefijoWpx.De(indicativo).Should().Be(esperado);
    }

    [Theory]
    // Un solo numero detras cambia el numero del prefijo.
    [InlineData("N8BJQ/9", "N9")]
    [InlineData("EA8DLF/1", "EA1")]
    // Un prefijo entero delante manda sobre el indicativo.
    [InlineData("EA8/DL1ABC", "EA8")]
    // El designador portable pasa a ser el prefijo entero: VP2E y VP2M son de islas distintas
    // y cuentan como dos multiplicadores, asi que no se recortan a VP2.
    [InlineData("VP2E/K1ABC", "VP2E")]
    [InlineData("VP2M/K1ABC", "VP2M")]
    [InlineData("N8BJQ/KH9", "KH9")]
    // Un designador sin numero recibe un cero tras la segunda letra.
    [InlineData("PA/N8BJQ", "PA0")]
    // Los anadidos que solo dicen como se opera no cambian nada.
    [InlineData("EA8DLF/P", "EA8")]
    [InlineData("EA8DLF/MM", "EA8")]
    [InlineData("EA8DLF/QRP", "EA8")]
    public void LosPortablesSiguenSusPropiasReglas(string indicativo, string esperado)
    {
        PrefijoWpx.De(indicativo).Should().Be(esperado);
    }

    [Fact]
    public void UnIndicativoVacioNoRompeNada()
    {
        PrefijoWpx.De((string?)null).Should().BeEmpty();
        PrefijoWpx.De("   ").Should().BeEmpty();
    }
}
