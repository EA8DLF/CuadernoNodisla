using FluentAssertions;
using Nodisla.Cuaderno.Concursos.Catalogo;

namespace Nodisla.Cuaderno.Concursos.Pruebas;

/// <summary>«El ultimo fin de semana completo» no es «el ultimo sabado».</summary>
public sealed class CalendarioPruebas
{
    [Theory]
    // Noviembre de 2026 acaba en lunes: el ultimo sabado es el 28 y cabe entero.
    [InlineData(2026, 11, 5, true, 28)]
    // Octubre de 2026 acaba en sabado: el ultimo sabado (31) se sale, asi que toca el 24.
    [InlineData(2026, 10, 5, true, 24)]
    // Sin exigir semana completa, el de octubre si seria el 31.
    [InlineData(2026, 10, 5, false, 31)]
    [InlineData(2026, 2, 3, true, 21)]
    [InlineData(2026, 1, 1, true, 3)]
    public void ElDiaDelMesSeCalculaBien(int anio, int mes, int ordinal, bool completa, int esperado)
    {
        CalendarioDeConcurso.DiaDelMes(anio, mes, DayOfWeek.Saturday, ordinal, completa)
            .Day.Should().Be(esperado);
    }

    [Fact]
    public void ElCqWwCwDe2026EmpiezaElUltimoFinDeSemanaCompletoDeNoviembre()
    {
        var cqww = CatalogoDeConcursos.Predeterminado.Buscar("CQ-WW-CW")!;
        var edicion = CalendarioDeConcurso.Edicion(cqww, 2026)!;

        edicion.InicioUtc.Should().Be(new DateTimeOffset(2026, 11, 28, 0, 0, 0, TimeSpan.Zero));
        edicion.FinUtc.Should().Be(new DateTimeOffset(2026, 11, 30, 0, 0, 0, TimeSpan.Zero));
        edicion.Contiene(new DateTimeOffset(2026, 11, 29, 12, 0, 0, TimeSpan.Zero)).Should().BeTrue();
        edicion.Contiene(new DateTimeOffset(2026, 11, 27, 12, 0, 0, TimeSpan.Zero)).Should().BeFalse();
    }

    [Fact]
    public void ElIaruEmpiezaAMediodiaYDura24Horas()
    {
        var iaru = CatalogoDeConcursos.Predeterminado.Buscar("IARU-HF")!;
        var edicion = CalendarioDeConcurso.Edicion(iaru, 2026)!;

        edicion.InicioUtc.Hour.Should().Be(12);
        (edicion.FinUtc - edicion.InicioUtc).Should().Be(TimeSpan.FromHours(24));
    }

    [Fact]
    public void UnConcursoSinFechaNoInventaUna()
    {
        var sinFecha = CatalogoDeConcursos.Predeterminado.Buscar("EA-RTTY")!;
        sinFecha.Celebracion.Should().BeNull();
        CalendarioDeConcurso.Edicion(sinFecha, 2026).Should().BeNull();
        CalendarioDeConcurso.Proxima(sinFecha, DateTimeOffset.UnixEpoch).Should().BeNull();
    }

    [Fact]
    public void LaProximaEdicionSaltaDeAnioCuandoLaDeEsteYaPaso()
    {
        var cqww = CatalogoDeConcursos.Predeterminado.Buscar("CQ-WW-CW")!;
        var despues = new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero);

        CalendarioDeConcurso.Proxima(cqww, despues)!.InicioUtc.Year.Should().Be(2027);
    }

    [Fact]
    public void SoloEstanEnMarchaLosQueDeVerdadLoEstan()
    {
        var durante = new DateTimeOffset(2026, 11, 29, 3, 0, 0, TimeSpan.Zero);
        var enMarcha = CalendarioDeConcurso.EnMarcha(CatalogoDeConcursos.Predeterminado, durante);

        enMarcha.Select(e => e.Codigo).Should().Contain("CQ-WW-CW");
        enMarcha.Select(e => e.Codigo).Should().NotContain("CQ-WW-SSB");
    }
}
