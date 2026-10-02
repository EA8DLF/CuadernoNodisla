using FluentAssertions;
using Nodisla.Cuaderno.Ui.Conversores;
using Nodisla.Cuaderno.Ui.VistaModelos;

namespace Nodisla.Cuaderno.Ui.Pruebas;

/// <summary>
/// La barra de la ventana agrupa las páginas por uso, sin mover los índices de siempre (que se
/// guardan al cerrar y van detrás de Ctrl 1…Ctrl 9).
/// </summary>
public class NavegacionAgrupadaPruebas
{
    [Theory]
    [InlineData(0, "Operar")]
    [InlineData(1, "Operar")]
    [InlineData(6, "Operar")]
    [InlineData(8, "Operar")]
    [InlineData(2, "Libro")]
    [InlineData(3, "Libro")]
    [InlineData(7, "QSL")]
    [InlineData(9, "QSL")]
    [InlineData(10, "QSL")]
    [InlineData(11, "Ayuda")]
    [InlineData(4, "Diplomas")]
    [InlineData(5, "Configuración")]
    public void Cada_pagina_esta_en_su_grupo(int pagina, string grupo) =>
        VistaModeloPrincipal.GrupoDe(pagina).Should().Be(grupo);

    [Fact]
    public void Todas_las_paginas_estan_en_un_grupo_y_solo_en_uno()
    {
        var todas = VistaModeloPrincipal.Grupos.Values.SelectMany(p => p).ToList();
        todas.Should().OnlyHaveUniqueItems();
        todas.Should().BeEquivalentTo(Enumerable.Range(0, 13));
    }

    [Fact]
    public void Los_indices_de_siempre_no_se_mueven()
    {
        VistaModeloPrincipal.PaginaOperar.Should().Be(0);
        VistaModeloPrincipal.PaginaConfiguracion.Should().Be(5);
        VistaModeloPrincipal.PaginaQsl.Should().Be(7);
        VistaModeloPrincipal.PaginaRonda.Should().Be(8);
        VistaModeloPrincipal.PaginaEtiquetas.Should().Be(9);
        VistaModeloPrincipal.PaginaDisenadorDeDiplomas.Should().Be(10);
        VistaModeloPrincipal.PaginaAyuda.Should().Be(11);
        VistaModeloPrincipal.PaginaCw.Should().Be(12);
    }

    [Fact]
    public void Cada_pagina_menos_la_ayuda_tiene_su_capitulo_y_el_capitulo_existe()
    {
        var libro = Nodisla.Cuaderno.Ui.Soporte.LibroDeAyuda.DelEnsamblado(typeof(VistaModeloPrincipal).Assembly);
        foreach (var pagina in Enumerable.Range(0, 13).Where(p => p != VistaModeloPrincipal.PaginaAyuda))
        {
            VistaModeloPrincipal.CapituloDeCadaPagina.Should().ContainKey(pagina);
            libro.Buscar(VistaModeloPrincipal.CapituloDeCadaPagina[pagina]).Should().NotBeNull($"la página {pagina} abre su capítulo con F1");
        }
    }

    [Theory]
    [InlineData(6, "0,1,6,8", true)]
    [InlineData(2, "0,1,6,8", false)]
    [InlineData(9, "7, 9", true)]
    [InlineData(5, "", false)]
    public void El_conversor_de_la_barra_mira_la_lista(int pagina, string lista, bool esta) =>
        EstaEnLaLista.Esta(pagina, lista).Should().Be(esta);
}
