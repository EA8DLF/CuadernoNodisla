using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.CasosDeUso;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Aplicacion.Pruebas;

/// <summary>
/// Los filtros del cluster son lo que separa un chorro de anuncios inútil de una lista donde
/// se ve lo que interesa. Estas pruebas fijan que filtrar por banda, modo o continente no se
/// coma spots que debería dejar pasar, y que un anuncio sin modo se deduzca del trozo de banda
/// en vez de quedarse fuera de todos los filtros.
/// </summary>
public sealed class FiltroDeSpotsPruebas
{
    private static Spot Anuncio(
        string indicativo = "JA1ABC",
        double mhz = 14.025,
        string? modo = null,
        string? continente = "AS",
        bool entidadNueva = false,
        bool huecoNuevo = false) =>
        new(
            Indicativo.Parse(indicativo),
            Frecuencia.DesdeMegahercios((decimal)mhz),
            Indicativo.Parse("EA8DLF"),
            null,
            DateTimeOffset.UtcNow,
            "Cluster de pruebas")
        {
            ModoAnunciado = modo is null ? Modo.Vacio : Modo.Crudo(modo),
            Continente = continente,
            EsEntidadNueva = entidadNueva,
            EsNuevoEnBandaYModo = huecoNuevo,
        };

    [Fact]
    public void Sin_filtro_pasa_todo()
    {
        FiltroDeSpots.Todo.Admite(Anuncio()).Should().BeTrue();
    }

    [Fact]
    public void El_filtro_de_banda_deja_pasar_solo_esa_banda()
    {
        var filtro = new FiltroDeSpots(Banda: "20m");

        filtro.Admite(Anuncio(mhz: 14.025)).Should().BeTrue();
        filtro.Admite(Anuncio(mhz: 7.025)).Should().BeFalse();
    }

    [Fact]
    public void El_filtro_de_continente_no_distingue_mayusculas()
    {
        var filtro = new FiltroDeSpots(Continente: "as");

        filtro.Admite(Anuncio(continente: "AS")).Should().BeTrue();
        filtro.Admite(Anuncio(continente: "EU")).Should().BeFalse();
    }

    [Fact]
    public void El_filtro_de_modo_usa_el_modo_que_anuncia_el_cluster()
    {
        var filtro = new FiltroDeSpots(Modo: "CW");

        filtro.Admite(Anuncio(modo: "CW")).Should().BeTrue();
        filtro.Admite(Anuncio(modo: "SSB")).Should().BeFalse();
    }

    [Fact]
    public void Un_spot_sin_modo_no_se_queda_fuera_de_todos_los_filtros()
    {
        // 14.025 MHz es telegrafía en cualquier plan de bandas del mundo.
        new FiltroDeSpots(Modo: "CW").Admite(Anuncio(mhz: 14.025, modo: null)).Should().BeTrue();
    }

    [Theory]
    [InlineData(14.074, "FT8")]
    [InlineData(7.074, "FT8")]
    [InlineData(28.074, "FT8")]
    [InlineData(14.025, "CW")]
    [InlineData(7.010, "CW")]
    [InlineData(14.250, "SSB")]
    [InlineData(21.300, "SSB")]
    public void El_modo_probable_sale_del_plan_de_bandas(double mhz, string esperado)
    {
        FiltroDeSpots.ModoProbable(Frecuencia.DesdeMegahercios((decimal)mhz)).Should().Be(esperado);
    }

    [Fact]
    public void Se_pueden_esconder_los_anuncios_de_las_redes_de_escucha_automatica()
    {
        var filtro = new FiltroDeSpots { SinEscuchaAutomatica = true };

        var automatico = Anuncio() with { EsDeEscuchaAutomatica = true };

        filtro.Admite(Anuncio()).Should().BeTrue();
        filtro.Admite(automatico).Should().BeFalse();

        // Sin el interruptor puesto, pasan los dos.
        FiltroDeSpots.Todo.Admite(automatico).Should().BeTrue();
    }

    [Fact]
    public void Solo_nuevos_deja_pasar_la_entidad_nueva_y_el_hueco_nuevo()
    {
        var filtro = new FiltroDeSpots(SoloNuevos: true);

        filtro.Admite(Anuncio(entidadNueva: true)).Should().BeTrue();
        filtro.Admite(Anuncio(huecoNuevo: true)).Should().BeTrue();
        filtro.Admite(Anuncio()).Should().BeFalse();
    }

    [Fact]
    public void Los_filtros_se_acumulan()
    {
        var filtro = new FiltroDeSpots(Banda: "20m", Modo: "CW", Continente: "AS");

        filtro.Admite(Anuncio(mhz: 14.025, modo: "CW", continente: "AS")).Should().BeTrue();
        filtro.Admite(Anuncio(mhz: 14.025, modo: "CW", continente: "EU")).Should().BeFalse();
        filtro.Admite(Anuncio(mhz: 7.025, modo: "CW", continente: "AS")).Should().BeFalse();
    }
}
