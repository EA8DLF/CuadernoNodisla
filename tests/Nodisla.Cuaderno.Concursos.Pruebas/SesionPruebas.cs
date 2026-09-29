using FluentAssertions;
using Nodisla.Cuaderno.Concursos.Catalogo;
using Nodisla.Cuaderno.Concursos.Sesion;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Concursos.Pruebas;

/// <summary>La sesion de concurso produce contactos normales del cuaderno.</summary>
public sealed class SesionPruebas
{
    private static readonly DatosDeMiEstacion Ea8 =
        new(Indicativo.Parse("EA8DLF"), 29, "AF", 33, 36, Locator.Parse("IL18"));

    private static SesionDeConcurso Abrir(string codigo) =>
        new(new OpcionesDeSesion(CatalogoDeConcursos.Predeterminado.Buscar(codigo)!, Ea8));

    private static ApunteDeConcurso Contacto(string call, string banda = "20m", string modo = "CW") =>
        new(Indicativo.Parse(call), Banda.Parse(banda), Modo.Parse(modo))
        {
            Dxcc = 281,
            Continente = "EU",
            ZonaCq = 14,
            ZonaItu = 37,
            InicioUtc = new DateTimeOffset(2026, 11, 28, 12, 0, 0, TimeSpan.Zero),
        };

    [Fact]
    public void ElContactoDeConcursoEsUnContactoNormalConSusCamposPuestos()
    {
        var sesion = Abrir("CQ-WW-CW");
        var registro = sesion.Registrar(Contacto("EA1ABC"));

        registro.Qso.Should().BeOfType<Qso>();
        registro.Qso.ContestId.Should().Be("CQ-WW-CW");
        registro.Qso.Call.Valor.Should().Be("EA1ABC");
        registro.Qso.StationCallsign.Valor.Should().Be("EA8DLF");
        registro.Qso.StxString.Should().Be("33", "el CQ WW intercambia la zona, no un numero de serie");
        registro.Qso.Stx.Should().BeNull();
        registro.Qso.SrxString.Should().Be("14");
        registro.Qso.MyDxcc.Should().Be(29);
        registro.Qso.Origen.Should().Be("Concurso");
        registro.Puntos.Should().Be(3);
    }

    [Fact]
    public void ElNumeroDeSerieAvanzaSoloYSeEnviaConTresCifras()
    {
        var sesion = Abrir("CQ-WPX-CW");
        sesion.ProximaSerie.Should().Be(1);

        var primero = sesion.Registrar(Contacto("EA1ABC"));
        var segundo = sesion.Registrar(Contacto("EA1XYZ"));

        primero.SerieEnviada.Should().Be(1);
        primero.Qso.Stx.Should().Be(1);
        primero.Qso.StxString.Should().Be("001");
        segundo.SerieEnviada.Should().Be(2);
        sesion.ProximaSerie.Should().Be(3);
    }

    [Fact]
    public void LoQueEscribeElOperadorMandaSobreLoQueDeduceElPrograma()
    {
        var sesion = Abrir("CQ-WW-CW");
        var registro = sesion.Registrar(Contacto("EA1ABC") with { IntercambioRecibido = "14A" });

        registro.Qso.SrxString.Should().Be("14A");
    }

    [Fact]
    public void UnDuplicadoSeGuardaPeroNoSuma()
    {
        var sesion = Abrir("CQ-WW-CW");
        sesion.Registrar(Contacto("EA1ABC"));
        var repetido = sesion.Registrar(Contacto("EA1ABC"));

        repetido.EraDuplicado.Should().BeTrue();
        repetido.Puntos.Should().Be(0);
        repetido.MultiplicadoresNuevos.Should().BeEmpty();
        sesion.Marcador.Contactos.Should().Be(1);
        sesion.Marcador.Duplicados.Should().Be(1);
        sesion.Contactos.Should().HaveCount(2, "el contacto se guarda igual; ya lo descarta el organizador");
    }

    [Fact]
    public void SiLaSesionNoAdmiteDuplicadosLoDiceEnCastellano()
    {
        var reglas = CatalogoDeConcursos.Predeterminado.Buscar("CQ-WW-CW")!;
        var sesion = new SesionDeConcurso(new OpcionesDeSesion(reglas, Ea8) { AdmiteDuplicados = false });
        sesion.Registrar(Contacto("EA1ABC"));

        var accion = () => sesion.Registrar(Contacto("EA1ABC"));
        accion.Should().Throw<InvalidOperationException>().WithMessage("*ya está trabajado*");
    }

    [Fact]
    public void ElMarcadorMultiplicaPuntosPorMultiplicadores()
    {
        var sesion = Abrir("CQ-WW-CW");
        sesion.Registrar(Contacto("EA1ABC"));                                  // 3 puntos, zona 14 y pais 281
        sesion.Registrar(Contacto("EA1XYZ", banda: "40m"));                    // 3 puntos, los mismos en otra banda

        var marcador = sesion.Marcador;
        marcador.Contactos.Should().Be(2);
        marcador.Puntos.Should().Be(6);
        marcador.Multiplicadores.Should().Be(4);
        marcador.Total.Should().Be(24);
    }

    [Fact]
    public void UnConcursoSinMultiplicadoresNoDejaElMarcadorACero()
    {
        var reglas = CatalogoDeConcursos.Predeterminado.Buscar("ARRL-DX-CW")! with { Multiplicadores = [] };
        var sesion = new SesionDeConcurso(new OpcionesDeSesion(reglas, Ea8));
        sesion.Registrar(Contacto("K1ABC"));

        sesion.Marcador.Total.Should().Be(3);
    }

    [Fact]
    public void PrevisualizarAvisaSinTocarElMarcador()
    {
        var sesion = Abrir("CQ-WW-CW");
        var aviso = sesion.Previsualizar(Contacto("EA1ABC"));

        aviso.EsDuplicado.Should().BeFalse();
        aviso.Puntos.Should().Be(3);
        aviso.TraeMultiplicador.Should().BeTrue();
        sesion.Marcador.Should().Be(MarcadorDeConcurso.Cero);
    }

    [Fact]
    public void PrevisualizarUnDuplicadoNoOfrecePuntos()
    {
        var sesion = Abrir("CQ-WW-CW");
        sesion.Registrar(Contacto("EA1ABC"));

        var aviso = sesion.Previsualizar(Contacto("EA1ABC"));
        aviso.EsDuplicado.Should().BeTrue();
        aviso.Puntos.Should().Be(0);
        aviso.MultiplicadoresNuevos.Should().BeEmpty();
    }

    [Fact]
    public void UnaSesionInterrumpidaSeRecuperaDelCuaderno()
    {
        var sesion = Abrir("CQ-WPX-CW");
        var deAnoche = new Qso
        {
            Call = Indicativo.Parse("EA1ABC"),
            Band = Banda.Parse("40m"),
            Mode = Modo.Parse("CW"),
            InicioUtc = new DateTimeOffset(2026, 5, 30, 2, 0, 0, TimeSpan.Zero),
            ContestId = "CQ-WPX-CW",
            Stx = 137,
        };

        sesion.Precargar([deAnoche]);

        sesion.ProximaSerie.Should().Be(138, "no se puede repetir un numero de serie ya enviado");
        sesion.Comprobar("EA1ABC", Banda.Parse("40m"), Modo.Parse("CW"))
            .Should().Be(EstadoDeDuplicado.Duplicado);
    }

    [Fact]
    public void LasReglasSinContrastarSeAvisan()
    {
        Abrir("CQ-WW-CW").ReglasSinContrastar.Should().BeTrue();
        Abrir("7QP").ReglasSinContrastar.Should().BeTrue();
    }

    [Fact]
    public void UnContactoSinIndicativoNoSeRegistra()
    {
        var sesion = Abrir("CQ-WW-CW");
        var accion = () => sesion.Registrar(
            new ApunteDeConcurso(Indicativo.Vacio, Banda.Parse("20m"), Modo.Parse("CW")));

        accion.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void SinInformeSePoneElDeCostumbreDelModo()
    {
        var sesion = Abrir("CQ-WW-CW");
        var registro = sesion.Registrar(Contacto("EA1ABC"));

        registro.Qso.RstSent.Texto.Should().Be("599");
        registro.Qso.RstRcvd.Texto.Should().Be("599");
    }
}
