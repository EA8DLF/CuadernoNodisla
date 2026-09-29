using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Radio.Control;
using Nodisla.Cuaderno.Radio.Control.Ft710;
using Nodisla.Cuaderno.Radio.Control.Yaesu;
using Nodisla.Cuaderno.Radio.Modelos;
using Nodisla.Cuaderno.Radio.Pruebas.Dobles;

namespace Nodisla.Cuaderno.Radio.Pruebas;

/// <summary>
/// El resto de la familia Yaesu de CAT nuevo, contra dobles que contestan como dice el manual
/// CAT de cada modelo. <b>Programado segun el manual, sin probar con la radio</b> (docs/16-modelos.md).
/// </summary>
public class ModelosYaesuPruebas
{
    private static async Task<ControlFt710> ConectarAsync(YaesuAsciiDeMentira canal, PerfilYaesu perfil)
    {
        var control = new ControlFt710(canal, new OpcionesFt710
        {
            IntervaloDeSondeo = TimeSpan.FromHours(1),
            Esperar = (_, _) => Task.CompletedTask,
        }, registro: null, perfil);
        await control.ConectarAsync();
        return control;
    }

    /// <summary>FTDX1200 segun su manual: frecuencia de 8 cifras, sin ST ni MD1.</summary>
    private static YaesuAsciiDeMentira Ftdx1200() => new(new Dictionary<string, string>
    {
        ["ID;"] = "ID0583",
        ["VS;"] = "VS0",
        ["FA;"] = "FA14250000",
        ["FB;"] = "FB07100000",
        ["FT;"] = "FT0",
        ["IF;"] = "IF00014250000+012010200000",
        ["MD0;"] = "MD02",
        ["PC;"] = "PC100",
        ["SM0;"] = "SM0128",
        ["AG0;"] = "AG0100",
        ["RA0;"] = "RA00",
        ["SH0;"] = "SH010",
        ["RT;"] = "RT0",
    });

    [Fact]
    public async Task El_FTDX1200_lee_la_frecuencia_de_ocho_cifras_y_el_clarificador_del_IF()
    {
        var canal = Ftdx1200();
        await using var control = await ConectarAsync(canal, PerfilesYaesu.Ftdx1200);

        control.Modelo.Clave.Should().Be("yaesu-ftdx1200");
        control.Estado.Frecuencia.Should().Be(Frecuencia.DesdeHercios(14_250_000));
        control.Vfos.B.Frecuencia.Should().Be(Frecuencia.DesdeHercios(7_100_000));
        control.Estado.Modo.NombreUsual.Should().Be("USB");
        control.Vfos.DesplazamientoRitHz.Should().Be(120);
        control.Vfos.Rit.Should().BeTrue();
    }

    [Fact]
    public async Task El_FTDX1200_escribe_la_frecuencia_con_ocho_cifras()
    {
        var canal = Ftdx1200();
        await using var control = await ConectarAsync(canal, PerfilesYaesu.Ftdx1200);

        await control.PonerFrecuenciaAsync(Frecuencia.DesdeHercios(7_074_000));

        canal.Recibidas.Should().Contain("FA07074000;");
    }

    [Fact]
    public async Task Sin_ST_el_split_se_lee_de_FT_y_se_pone_con_FT3()
    {
        var canal = Ftdx1200();
        await using var control = await ConectarAsync(canal, PerfilesYaesu.Ftdx1200);

        canal.Responder("FT;", "FT1");
        await control.LeerEstadoAsync();
        control.Vfos.Split.Should().BeTrue();
        control.Estado.Frecuencia.Should().Be(Frecuencia.DesdeHercios(7_100_000), "con split se apunta la de transmision");
        control.Estado.FrecuenciaRx.Should().Be(Frecuencia.DesdeHercios(14_250_000));

        control.Mandos.Should().Contain(MandoDeEquipo.Split);
        await control.EscribirMandoAsync(MandoDeEquipo.Split, 0);
        canal.Recibidas.Should().Contain("FT2;");
    }

    [Fact]
    public async Task Los_mandos_que_contestan_interrogacion_no_se_ofrecen()
    {
        var canal = Ftdx1200();
        await using var control = await ConectarAsync(canal, PerfilesYaesu.Ftdx1200);

        control.Mandos.Should().Contain([MandoDeEquipo.Volumen, MandoDeEquipo.Atenuador, MandoDeEquipo.AnchoDeFiltro, MandoDeEquipo.Rit]);
        control.Mandos.Should().NotContain([MandoDeEquipo.GananciaRf, MandoDeEquipo.EspectroModo, MandoDeEquipo.FuncionDelMandoFunc]);
        control.Rango(MandoDeEquipo.AnchoDeFiltro)!.Unidad.Should().Be("índice", "la tabla de anchos en hercios es solo la del FT-710");
    }

    [Fact]
    public async Task Sin_MD1_intercambiar_los_VFO_es_la_tecla_SV_y_no_se_toca_el_modo_del_otro()
    {
        var canal = Ftdx1200();
        await using var control = await ConectarAsync(canal, PerfilesYaesu.Ftdx1200);

        await control.IntercambiarVfosAsync();
        canal.Recibidas.Should().Contain("SV;").And.NotContain(o => o.StartsWith("MD1", StringComparison.Ordinal));

        var cambiarElOtro = () => control.PonerModoDeAsync(NombreDeVfo.B, Modo.Parse("CW"));
        await cambiarElOtro.Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public async Task En_el_FTDX101_MD0_es_el_VFO_A_y_MD1_el_B_aunque_se_opere_en_el_B()
    {
        var canal = new YaesuAsciiDeMentira(new Dictionary<string, string>
        {
            ["ID;"] = "ID0681",
            ["VS;"] = "VS1",
            ["FA;"] = "FA014074000",
            ["FB;"] = "FB007074000",
            ["ST;"] = "ST0",
            ["FT;"] = "FT1",
            ["MD0;"] = "MD02",
            ["MD1;"] = "MD11",
        });
        await using var control = await ConectarAsync(canal, PerfilesYaesu.Ftdx101D);

        control.Vfos.A.Modo.NombreUsual.Should().Be("USB");
        control.Vfos.B.Modo.NombreUsual.Should().Be("LSB");
        control.Estado.Modo.NombreUsual.Should().Be("LSB", "se opera en el B");
        control.Vfos.B.Transmite.Should().BeTrue("FT1 es el B, sin mirar el activo");

        await control.PonerModoDeTeclaAsync(TeclaDeModo.Usb);
        canal.Recibidas.Should().Contain("MD12;");
    }

    private static YaesuAsciiDeMentira Ft991A() => new(new Dictionary<string, string>
    {
        ["ID;"] = "ID0670",
        ["FA;"] = "FA145500000",
        ["FB;"] = "FB007100000",
        ["FT;"] = "FT0",
        ["MD0;"] = "MD04",
    });

    [Fact]
    public async Task El_FT_991A_se_reconoce_por_ID0670_y_tiene_las_teclas_de_2m_y_70cm()
    {
        PerfilesYaesu.PorIdentificador("0670").Should().BeSameAs(PerfilesYaesu.Ft991A);
        PerfilesYaesu.PorIdentificador("0570").Should().BeSameAs(PerfilesYaesu.Ft991);

        var canal = Ft991A();
        await using var control = await ConectarAsync(canal, PerfilesYaesu.Ft991A);
        control.NombreDelEquipo.Should().Be("Yaesu FT-991A");
        control.Estado.Frecuencia.Should().Be(Frecuencia.DesdeHercios(145_500_000));

        var dosMetros = control.TeclasDeBanda.Single(t => t.Rotulo == "144");
        await control.IrABandaAsync(dosMetros);
        canal.Recibidas.Should().Contain("BS15;");
    }

    [Fact]
    public async Task El_FTDX10_sintoniza_con_AC002_y_al_soltar_manda_TX0_y_AC001()
    {
        var canal = new YaesuAsciiDeMentira(new Dictionary<string, string>
        {
            ["ID;"] = "ID0761",
            ["VS;"] = "VS0",
            ["FA;"] = "FA014200000",
            ["FB;"] = "FB007100000",
            ["ST;"] = "ST0",
            ["FT;"] = "FT0",
            ["MD0;"] = "MD02",
            ["MD1;"] = "MD11",
            ["AC;"] = "AC001",
            ["RT;"] = "RT0",
        });
        await using var control = await ConectarAsync(canal, PerfilesYaesu.Ftdx10);
        var ptt = (IPttDirecto)control;

        control.VeElFinDeLaSintonia.Should().BeFalse("el RI del FTDX10 no dice si el acoplador sintoniza");
        control.PrepararSintonia();
        await ptt.PonerPttDirectoAsync(true, CancellationToken.None);
        (await control.EsperarFinDeSintoniaAsync(() => { }, TimeSpan.FromSeconds(1))).Should().BeFalse();
        await ptt.PonerPttDirectoAsync(false, CancellationToken.None);

        canal.Recibidas.Should().ContainInOrder("AC002;", "TX0;", "AC001;");
        canal.Recibidas.Should().NotContain("AC003;").And.NotContain("TX1;");
    }

    [Fact]
    public async Task El_FTDX10_borra_el_clarificador_con_RC_y_no_tiene_DSP_RESET()
    {
        var canal = new YaesuAsciiDeMentira(new Dictionary<string, string> { ["ID;"] = "ID0761", ["RT;"] = "RT0" });
        await using var control = await ConectarAsync(canal, PerfilesYaesu.Ftdx10);

        await control.PulsarAsync(TeclaDelEquipo.BorrarClarificador);
        canal.Recibidas.Should().Contain("RC;");

        control.Teclas.Should().NotContain(TeclaDelEquipo.RestablecerDsp);
        var dspReset = () => control.PulsarAsync(TeclaDelEquipo.RestablecerDsp);
        await dspReset.Should().ThrowAsync<NotSupportedException>();

        await control.EscribirMandoAsync(MandoDeEquipo.Rit, 1);
        canal.Recibidas.Should().Contain("RT1;");
    }

    [Fact]
    public void Sin_perfil_el_control_sigue_siendo_el_FT_710_de_siempre()
    {
        var control = new ControlFt710(new YaesuAsciiDeMentira(new Dictionary<string, string>()));

        control.Modelo.Should().BeSameAs(PerfilesYaesu.Ft710.Modelo);
        control.Modelo.ProbadoConRadio.Should().BeTrue();
        control.Modelo.Frontal.Should().Be("FrontalFt710");
        control.NombreDelEquipo.Should().Be("Yaesu FT-710");
        control.Teclas.Should().HaveCount(Enum.GetValues<TeclaDelEquipo>().Length);
        control.VeElFinDeLaSintonia.Should().BeTrue();
    }

    [Fact]
    public void El_catalogo_tiene_todos_los_Yaesu_con_claves_e_identificadores_unicos()
    {
        var yaesu = CatalogoDeModelos.De(Fabricante.Yaesu);

        yaesu.Select(m => m.Clave).Should().Contain(
        [
            "yaesu-ft710", "yaesu-ftdx10", "yaesu-ftdx101d", "yaesu-ftdx101mp", "yaesu-ft991", "yaesu-ft991a",
            "yaesu-ft891", "yaesu-ftdx3000", "yaesu-ftdx1200", "yaesu-ftdx5000",
            "yaesu-ft817", "yaesu-ft818", "yaesu-ft857", "yaesu-ft897",
        ]);
        CatalogoDeModelos.Todos.Select(m => m.Clave).Should().OnlyHaveUniqueItems();
        yaesu.Where(m => m.IdentificadorYaesu is not null).Select(m => m.IdentificadorYaesu).Should().OnlyHaveUniqueItems();
        yaesu.Where(m => m.ProbadoConRadio).Select(m => m.Clave).Should().Equal("yaesu-ft710");
        yaesu.Where(m => !m.ProbadoConRadio).Should().OnlyContain(m => !string.IsNullOrWhiteSpace(m.Notas));
        CatalogoDeModelos.PorIdentificadorYaesu("0800").Should().BeSameAs(PerfilesYaesu.Ft710.Modelo);
        CatalogoDeModelos.Buscar(CatalogoDeModelos.Automatico).Should().BeNull();
        CatalogoDeModelos.ProveedorDe(PerfilesYaesu.Ftdx10.Modelo).Should().BeOfType<ProveedorYaesuAscii>();
        CatalogoDeModelos.ProveedorDe(ProveedorYaesuBinario.Ft817).Should().BeOfType<ProveedorYaesuBinario>();
    }

    [Fact]
    public async Task La_fabrica_crea_el_control_del_modelo_elegido_sin_abrir_el_puerto()
    {
        static OpcionesDeRadio Con(string? modelo) => new()
        {
            Via = ViaDeControl.CatNativo,
            Modelo = modelo,
            Ft710 = { Puerto = "COM250", Baudios = 38400 },
        };

        await using var ftdx10 = FabricaDeControlEquipo.Crear(Con("yaesu-ftdx10"));
        ftdx10.Should().BeOfType<ControlFt710>().Which.Modelo.Clave.Should().Be("yaesu-ftdx10");

        await using var ft817 = FabricaDeControlEquipo.Crear(Con("yaesu-ft817"));
        ft817.Should().BeOfType<ControlYaesuBinario>().Which.Modelo.Clave.Should().Be("yaesu-ft817");

        await using var automatico = FabricaDeControlEquipo.Crear(Con(null));
        automatico.Should().BeOfType<ControlFt710>().Which.Modelo.Clave.Should().Be("yaesu-ft710");
    }
}
