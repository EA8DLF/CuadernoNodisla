using FluentAssertions;
using Nodisla.Cuaderno.Ui.Digital;

namespace Nodisla.Cuaderno.Ui.Pruebas;

/// <summary>Los mensajes de 77 bits: como se entienden y como se componen.</summary>
public sealed class GramaticaDeMensajesPruebas
{
    [Theory]
    [InlineData("CQ EA8DLF IL18", ClaseDeMensaje.Cq, "", "EA8DLF", "IL18")]
    [InlineData("CQ DX EA8DLF IL18", ClaseDeMensaje.Cq, "", "EA8DLF", "IL18")]
    [InlineData("CQ POTA K1ABC FN42", ClaseDeMensaje.Cq, "", "K1ABC", "FN42")]
    [InlineData("EA8DLF IZ2ABC JN45", ClaseDeMensaje.Llamada, "EA8DLF", "IZ2ABC", "JN45")]
    [InlineData("EA8DLF IZ2ABC -07", ClaseDeMensaje.Informe, "EA8DLF", "IZ2ABC", "")]
    [InlineData("EA8DLF IZ2ABC R+05", ClaseDeMensaje.InformeConR, "EA8DLF", "IZ2ABC", "")]
    [InlineData("EA8DLF IZ2ABC RR73", ClaseDeMensaje.Rr73, "EA8DLF", "IZ2ABC", "")]
    [InlineData("EA8DLF IZ2ABC RRR", ClaseDeMensaje.Rrr, "EA8DLF", "IZ2ABC", "")]
    [InlineData("EA8DLF IZ2ABC 73", ClaseDeMensaje.S73, "EA8DLF", "IZ2ABC", "")]
    [InlineData("EA8DLF <KH1/KH7Z> -10", ClaseDeMensaje.Informe, "EA8DLF", "KH1/KH7Z", "")]
    [InlineData("EA8DLF K1ABC R FN42", ClaseDeMensaje.InformeConR, "EA8DLF", "K1ABC", "FN42")]
    [InlineData("EA8DLF W9XYZ 2A EMA", ClaseDeMensaje.Informe, "EA8DLF", "W9XYZ", "")]
    [InlineData("EA8DLF W9XYZ R 579 MA", ClaseDeMensaje.InformeConR, "EA8DLF", "W9XYZ", "")]
    [InlineData("EA8DLF G4ABC 570123 IO91NP", ClaseDeMensaje.Informe, "EA8DLF", "G4ABC", "IO91NP")]
    public void SeEntiendenLosMensajesEstandar(string texto, ClaseDeMensaje clase, string llamado, string llamante, string locator)
    {
        var m = InterpreteDeMensajes.Analizar(texto);

        m.Clase.Should().Be(clase);
        m.Llamado.Should().Be(llamado);
        m.Llamante.Should().Be(llamante);
        m.Locator.Should().Be(locator);
    }

    [Fact]
    public void Rr73NoEsUnLocalizador()
    {
        InterpreteDeMensajes.EsLocalizador("RR73").Should().BeFalse();
        InterpreteDeMensajes.EsLocalizador("IL18").Should().BeTrue();
        InterpreteDeMensajes.EsLocalizador("IO91NP").Should().BeTrue();
    }

    [Fact]
    public void ElInformeSeLee()
    {
        InterpreteDeMensajes.Analizar("EA8DLF IZ2ABC -07").Informe.Should().Be(-7);
        InterpreteDeMensajes.Analizar("EA8DLF IZ2ABC R+05").Informe.Should().Be(5);
    }

    [Fact]
    public void ElMensajeDobleDelFoxTraeLosDos()
    {
        var m = InterpreteDeMensajes.Analizar("K1ABC RR73; EA8DLF <KH1/KH7Z> -08");

        m.Clase.Should().Be(ClaseDeMensaje.Rr73, "la primera mitad es el RR73 del fox a otro");
        m.Llamado.Should().Be("K1ABC");
        m.Segundo.Should().NotBeNull();
        m.Segundo!.Llamado.Should().Be("EA8DLF");
        m.Segundo.Llamante.Should().Be("KH1/KH7Z");
        m.Segundo.Informe.Should().Be(-8);
    }

    [Fact]
    public void LaBasuraNoRevienta()
    {
        InterpreteDeMensajes.Analizar(null).Clase.Should().Be(ClaseDeMensaje.Otro);
        InterpreteDeMensajes.Analizar("   ").Clase.Should().Be(ClaseDeMensaje.Otro);
        InterpreteDeMensajes.Analizar("<...> -07").Clase.Should().Be(ClaseDeMensaje.Otro);
    }

    [Fact]
    public void SeComponenLosSeisMensajesNormales()
    {
        var textos = GramaticaDeMensajes.Generar(new DatosDeLosMensajes(
            "EA8DLF", "IL18gs", "IZ2ABC", "JN45", -7, "", "", false, TipoDeOperacion.Normal));

        textos.Should().Equal(
            "IZ2ABC EA8DLF IL18",
            "IZ2ABC EA8DLF -07",
            "IZ2ABC EA8DLF R-07",
            "IZ2ABC EA8DLF RR73",
            "IZ2ABC EA8DLF 73",
            "CQ EA8DLF IL18");
    }

    [Fact]
    public void ElCqDirigidoYElRrrSeRespetan()
    {
        var textos = GramaticaDeMensajes.Generar(new DatosDeLosMensajes(
            "EA8DLF", "IL18", "IZ2ABC", "JN45", 3, "dx", "", true, TipoDeOperacion.Normal));

        textos[1].Should().Be("IZ2ABC EA8DLF +03");
        textos[3].Should().Be("IZ2ABC EA8DLF RRR");
        textos[5].Should().Be("CQ DX EA8DLF IL18");
    }

    [Theory]
    [InlineData(TipoDeOperacion.NaVhf, "K1ABC EA8DLF IL18", "K1ABC EA8DLF R IL18", "CQ TEST EA8DLF IL18")]
    [InlineData(TipoDeOperacion.WwDigi, "K1ABC EA8DLF IL18", "K1ABC EA8DLF R IL18", "CQ WW EA8DLF IL18")]
    [InlineData(TipoDeOperacion.FieldDay, "K1ABC EA8DLF 2A EMA", "K1ABC EA8DLF R 2A EMA", "CQ FD EA8DLF IL18")]
    [InlineData(TipoDeOperacion.RttyRoundup, "K1ABC EA8DLF 2A EMA", "K1ABC EA8DLF R 2A EMA", "CQ RU EA8DLF IL18")]
    [InlineData(TipoDeOperacion.EuVhf, "K1ABC EA8DLF 2A EMA", "K1ABC EA8DLF R 2A EMA", "CQ TEST EA8DLF IL18")]
    public void LosConcursosCambianElIntercambioYElCq(TipoDeOperacion operacion, string tx2, string tx3, string tx6)
    {
        var textos = GramaticaDeMensajes.Generar(new DatosDeLosMensajes(
            "EA8DLF", "IL18", "K1ABC", "FN42", -7, "", "2A EMA", false, operacion));

        textos[1].Should().Be(tx2);
        textos[2].Should().Be(tx3);
        textos[5].Should().Be(tx6);
    }

    [Fact]
    public void LosHuecosSeVen()
    {
        var textos = GramaticaDeMensajes.Generar(new DatosDeLosMensajes(
            "", "", "", "", 0, "", "", false, TipoDeOperacion.Normal));

        textos[0].Should().Be("<DX> <YO> <LOC>");
        textos[5].Should().Be("CQ <YO> <LOC>");
    }
}
