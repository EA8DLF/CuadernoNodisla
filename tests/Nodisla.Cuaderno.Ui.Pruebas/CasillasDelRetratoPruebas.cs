using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.CasosDeUso;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Ui.VistaModelos;

namespace Nodisla.Cuaderno.Ui.Pruebas;

/// <summary>
/// Lo que ENSEÑAN las dos rejillas del retrato del indicativo.
/// </summary>
/// <remarks>
/// No se abre ninguna ventana: las casillas son datos ya escritos para la pantalla y se pueden
/// leer sin pintar nada.
///
/// Lo que se fija aquí es que los tres estados —sin trabajar, trabajado sin confirmar y
/// confirmado— <b>se distingan sin mirar el color</b>. Uno de cada doce hombres no separa el
/// verde del ámbar; si la única diferencia fuera el color, para ese operador las dos rejillas
/// serían un adorno.
/// </remarks>
public sealed class CasillasDelRetratoPruebas
{
    [Fact]
    public void La_casilla_de_novedad_dice_cada_estado_con_una_palabra()
    {
        Novedad(trabajado: false, confirmado: false).Texto.Should().Be("NUEVO");
        Novedad(trabajado: true, confirmado: false).Texto.Should().Be("sin QSL");
        Novedad(trabajado: true, confirmado: true).Texto.Should().Be("✓ OK");
    }

    [Fact]
    public void Lo_que_no_se_ha_podido_calcular_no_se_pinta_de_nuevo()
    {
        // Sin entidad resuelta, o sin banda y modo escritos, la casilla no sabe. Decir «NUEVO»
        // ahí mandaría al operador a llamar por un dato que nadie ha calculado.
        var sinDato = new CasillaVista(
            EjeDeNovedad.Banda, MedioDeConfirmacion.Lotw, false, false, Aplica: false);

        sinDato.Texto.Should().Be("—");
        sinDato.Texto.Should().NotBe("NUEVO");
        sinDato.NombreAccesible.Should().Contain("sin dato");
    }

    [Fact]
    public void Las_tres_palabras_de_la_novedad_son_distintas_entre_si()
    {
        var textos = new[]
        {
            Novedad(false, false).Texto,
            Novedad(true, false).Texto,
            Novedad(true, true).Texto,
            new CasillaVista(EjeDeNovedad.Pais, MedioDeConfirmacion.Papel, false, false, false).Texto,
        };

        textos.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void El_nombre_accesible_de_la_novedad_nombra_el_eje_y_la_via()
    {
        var casilla = new CasillaVista(EjeDeNovedad.Modo, MedioDeConfirmacion.Eqsl, true, false);

        casilla.NombreAccesible.Should().Be("Modo por eQSL: sin QSL");
    }

    [Fact]
    public void La_casilla_de_banda_distingue_los_tres_estados_sin_color()
    {
        // Vacía: no se ha trabajado.
        Rejilla(contactos: 0, confirmados: 0).Texto.Should().BeEmpty();

        // Un número: trabajado, pero sin confirmar.
        Rejilla(contactos: 3, confirmados: 0).Texto.Should().Be("3");

        // El visto: confirmado. Es lo que separa las dos casillas de color parecido.
        Rejilla(contactos: 3, confirmados: 1).Texto.Should().Be("✓");
    }

    [Fact]
    public void Muchos_contactos_sin_confirmar_no_desbordan_la_casilla()
    {
        Rejilla(contactos: 41, confirmados: 0).Texto.Should().Be("9+");
        Rejilla(contactos: 9, confirmados: 0).Texto.Should().Be("9");
    }

    [Fact]
    public void El_rotulo_emergente_dice_cuantos_hay_detras_del_visto()
    {
        // El visto se come la cifra en la casilla, así que la cifra tiene que estar en el
        // rótulo emergente y en el lector de pantalla: si no, se perdería el dato.
        Rejilla(contactos: 4, confirmados: 2).Detalle
            .Should().Be("20m en fonía: 4 contacto(s), 2 confirmado(s)");

        Rejilla(contactos: 4, confirmados: 0).Detalle
            .Should().Be("20m en fonía: 4 contacto(s), ninguno confirmado");

        Rejilla(contactos: 0, confirmados: 0).Detalle
            .Should().Be("20m en fonía: sin trabajar");
    }

    [Fact]
    public void Trabajado_y_confirmado_responden_a_lo_que_hay()
    {
        Rejilla(0, 0).Trabajado.Should().BeFalse();
        Rejilla(1, 0).Trabajado.Should().BeTrue();
        Rejilla(1, 0).Confirmado.Should().BeFalse();
        Rejilla(1, 1).Confirmado.Should().BeTrue();
    }

    [Fact]
    public void La_rejilla_lleva_las_catorce_bandas_de_siempre()
    {
        // Catorce columnas por tres filas es toda la rejilla: si se añade una banda hay que
        // mirar que siga cabiendo en el tercio de ventana donde vive.
        VistaModeloRetrato.BandasDeLaRejilla.Should().HaveCount(14);
        VistaModeloRetrato.BandasDeLaRejilla.Should().StartWith(["160m", "80m", "60m", "40m"]);
        VistaModeloRetrato.BandasDeLaRejilla.Should().EndWith(["6m", "4m", "2m", "70cm"]);
        VistaModeloRetrato.BandasDeLaRejilla.Should().OnlyHaveUniqueItems();
    }

    private static CasillaVista Novedad(bool trabajado, bool confirmado) =>
        new(EjeDeNovedad.Pais, MedioDeConfirmacion.Papel, trabajado, confirmado);

    private static CasillaDeRejilla Rejilla(int contactos, int confirmados) =>
        new("20m", FamiliaDeModo.Fonia, contactos, confirmados);
}
