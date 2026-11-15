using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Radio.Control.Ft710;
using Nodisla.Cuaderno.Radio.Control.Yaesu;
using Nodisla.Cuaderno.Radio.Modelos;
using Nodisla.Cuaderno.Radio.Ptt;

namespace Nodisla.Cuaderno.Radio.Pruebas;

/// <summary>
/// Abrir el puerto serie del FT-710 no puede levantar <c>RTS</c> ni <c>DTR</c>.
/// </summary>
/// <remarks>
/// <para>
/// El fallo (15-11-2026, con la radio de EA8DLF): el canal abria con las dos lineas en cierto
/// cuando el PTT iba por CAT. Por el puerto <i>Enhanced</i> no pasaba nada, pero la busqueda
/// del equipo dio por bueno el <i>Standard</i> y lo guardo; desde entonces, «Conectar» dejaba
/// la radio en antena. El manual CAT del FT-710 (2306-C, pags. 1-2) lo explica: el
/// <i>Standard</i> (CAT-2) es el de «TX Controls (PTT control, CW Keying, Digital Mode
/// Operation)» y ahi <c>RTS</c>/<c>DTR</c> transmiten segun RPTT SELECT y PC KEYING.
/// </para>
/// <para>
/// Aqui no se abre ningun puerto: se comprueba lo que el canal <b>va a hacer</b> con las lineas,
/// que es una funcion pura, y que la fabrica monta el canal del FT-710 con esa politica.
/// </para>
/// </remarks>
public class LineasDelPuertoPruebas
{
    [Theory]
    [InlineData(ViaDePtt.Cat)]
    [InlineData(ViaDePtt.Rts)]
    [InlineData(ViaDePtt.Dtr)]
    public void El_canal_del_FT710_abre_con_las_dos_lineas_en_falso(ViaDePtt via)
    {
        CanalSerieCat.LineasAlAbrir(via, lineasEnAlto: false).Should().Be((false, false));
    }

    [Fact]
    public void Un_canal_creado_sin_decir_nada_abre_con_las_dos_lineas_en_falso()
    {
        // Es el que usa la busqueda del equipo, que abre puertos que no sabe de quien son.
        var canal = new CanalSerieCat("COM_DE_MENTIRA", 38400);

        canal.LineasDeApertura.Should().Be((false, false));
    }

    [Theory]
    [InlineData(ViaDePtt.Rts, true, false)]
    [InlineData(ViaDePtt.Dtr, false, true)]
    [InlineData(ViaDePtt.Cat, true, true)]
    public void La_linea_que_hace_de_PTT_abre_en_falso_tambien_en_los_modelos_que_piden_lineas(
        ViaDePtt via,
        bool dtr,
        bool rts)
    {
        CanalSerieCat.LineasAlAbrir(via, lineasEnAlto: true).Should().Be((dtr, rts));
    }

    [Fact]
    public void El_perfil_del_FT710_no_pide_lineas_y_los_demas_se_quedan_como_estaban()
    {
        PerfilesYaesu.Ft710.LineasEnAltoAlAbrir.Should().BeFalse();
        PerfilesYaesu.Todos.Where(perfil => perfil != PerfilesYaesu.Ft710)
            .Should().OnlyContain(perfil => perfil.LineasEnAltoAlAbrir);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("auto")]
    [InlineData("yaesu-ft710")]
    public void La_fabrica_monta_el_FT710_con_las_lineas_en_falso(string? modelo)
    {
        // Lo que habia guardado el 15-11-2026: PTT por CAT y puerto dicho a mano.
        var opciones = new OpcionesDeRadio
        {
            Via = ViaDeControl.CatNativo,
            Modelo = modelo,
            Ft710 = { Puerto = "COM_DE_MENTIRA", Baudios = 38400, ViaDePtt = ViaDePtt.Cat },
        };

        var control = FabricaDeControlEquipo.Crear(opciones).Should().BeOfType<ControlFt710>().Subject;

        control.Canal.Should().BeOfType<CanalSerieCat>()
            .Which.LineasDeApertura.Should().Be((false, false));
    }

    [Fact]
    public void La_fabrica_monta_el_FT710_encontrado_con_las_lineas_en_falso()
    {
        var opciones = new OpcionesDeRadio { Via = ViaDeControl.CatNativo };
        var encontrado = new EquipoIdentificado("COM_DE_MENTIRA", 38400, ProtocoloCat.YaesuAscii, "0800", PerfilesYaesu.Ft710.Modelo);

        var control = FabricaDeControlEquipo.CrearPara(encontrado, opciones, null).Should().BeOfType<ControlFt710>().Subject;

        control.Canal.Should().BeOfType<CanalSerieCat>()
            .Which.LineasDeApertura.Should().Be((false, false));
    }
}
