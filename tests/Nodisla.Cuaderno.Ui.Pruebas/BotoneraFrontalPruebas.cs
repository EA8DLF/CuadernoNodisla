using System.Globalization;
using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.CasosDeUso;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Ui.VistaModelos;

namespace Nodisla.Cuaderno.Ui.Pruebas;

/// <summary>
/// La botonera a los lados del frontal (HAM y canales CB) con el camino real del FT-710 y las
/// respuestas reales de la radio del operador: cada tecla manda su orden al VFO activo y ninguna
/// transmite (el canal falla la prueba con TX1, TX2, MX1, AC003 o PS0).
/// </summary>
public sealed partial class FrontalFt710Pruebas
{
    private static readonly string[] Transmiten = ["TX1", "TX2", "MX1", "AC003", "PS0"];

    [Fact]
    public async Task La_botonera_tiene_las_bandas_del_FT710_y_resalta_la_del_dial()
    {
        var (_, equipo, conmutable) = await MontarAsync();
        await using var _ = conmutable;

        equipo.HayBotonera.Should().BeTrue();
        equipo.TeclasDeBanda.Select(t => t.Rotulo).Should().Equal("1.8", "3.5", "5", "7", "10", "14", "18", "21", "24", "28", "50", "GEN");

        // La captura tiene el VFO A en 14.155.000 USB: 20 m y USB en azul, nada mas.
        equipo.TeclasDeBanda.Where(t => t.Activa).Select(t => t.Rotulo).Should().Equal("14");
        equipo.TeclasDeModo.Where(t => t.Activa).Select(t => t.Rotulo).Should().Equal("USB");
        equipo.MotivoDeLaBotonera.Should().Contain("VFO activo");
    }

    [Fact]
    public async Task Cada_tecla_de_banda_manda_BS_y_nada_mas_que_toque_el_dial()
    {
        var (canal, equipo, conmutable) = await MontarAsync();
        await using var _ = conmutable;

        foreach (var tecla in equipo.TeclasDeBanda)
        {
            equipo.IrABandaCommand.CanExecute(tecla).Should().BeTrue();
            await equipo.IrABandaCommand.ExecuteAsync(tecla);
        }

        lock (canal.Mandadas)
        {
            canal.Mandadas.Should().Equal(
                "BS00;", "BS01;", "BS02;", "BS03;", "BS04;", "BS05;", "BS06;", "BS07;", "BS08;", "BS09;", "BS10;", "BS11;");
        }
    }

    [Theory]
    [InlineData("LSB", "MD01;")]
    [InlineData("USB", "MD02;")]
    [InlineData("CW", "MD03;")]
    [InlineData("AM", "MD05;")]
    [InlineData("FM", "MD04;")]
    [InlineData("DATA", "MD0C;")]
    public async Task Cada_tecla_de_modo_manda_MD0_al_VFO_activo(string rotulo, string orden)
    {
        var (canal, equipo, conmutable) = await MontarAsync();
        await using var _ = conmutable;

        await equipo.PonerModoDeTeclaCommand.ExecuteAsync(equipo.TeclasDeModo.Single(t => t.Rotulo == rotulo));

        lock (canal.Mandadas) canal.Mandadas.Should().Equal(orden);
    }

    [Fact]
    public async Task Las_memorias_M1_a_M6_recuperan_su_canal()
    {
        var (canal, equipo, conmutable) = await MontarAsync();
        await using var _ = conmutable;

        foreach (var tecla in equipo.TeclasDeMemoria)
        {
            await equipo.RecuperarMemoriaCommand.ExecuteAsync(tecla);
        }

        lock (canal.Mandadas) canal.Mandadas.Should().Equal("MC001;", "MC002;", "MC003;", "MC004;", "MC005;", "MC006;");
        equipo.TeclasDeMemoria.Select(t => t.Rotulo).Should().Equal("M1", "M2", "M3", "M4", "M5", "M6");
    }

    [Theory]
    [InlineData("VS0;", "FA027185000;")]
    [InlineData("VS1;", "FB027185000;")]
    public async Task Un_canal_CB_lleva_el_VFO_activo_a_su_frecuencia(string vs, string orden)
    {
        var (canal, equipo, conmutable) = await MontarAsync();
        await using var _ = conmutable;
        canal.Poner("VS;", vs);
        equipo.PlanCb = PlanCb.Cept;

        await equipo.IrAlCanalCommand.ExecuteAsync(equipo.CanalesDeCb[18]);

        lock (canal.Mandadas) canal.Mandadas.Should().Equal(orden);
        await EsperaALaVentana.DrenarAsync();
        if (vs == "VS0;")
        {
            equipo.CanalesDeCb.Where(t => t.Activa).Select(t => t.Rotulo).Should().Equal("19");
            equipo.TeclasDeBanda.Where(t => t.Activa).Select(t => t.Rotulo).Should().ContainSingle().Which.Should().Be("GEN", "27 MHz no es de aficionado");
        }
    }

    [Fact]
    public async Task Cambiar_de_plan_cambia_la_frecuencia_del_canal_y_el_resaltado()
    {
        var (canal, equipo, conmutable) = await MontarAsync();
        await using var _ = conmutable;

        equipo.PlanCb = PlanCb.Pl;
        await equipo.IrAlCanalCommand.ExecuteAsync(equipo.CanalesDeCb[0]);
        equipo.PlanCb = PlanCb.Uk;
        await equipo.IrAlCanalCommand.ExecuteAsync(equipo.CanalesDeCb[39]);

        lock (canal.Mandadas) canal.Mandadas.Should().Equal("FA026960000;", "FA027991250;");
        equipo.PlanesDeCb.Where(t => t.Activa).Select(t => t.Rotulo).Should().Equal("UK");
        await EsperaALaVentana.DrenarAsync();
        equipo.CanalesDeCb.Where(t => t.Activa).Select(t => t.Rotulo).Should().Equal("40");
    }

    [Fact]
    public async Task Ninguna_tecla_de_la_botonera_transmite()
    {
        var (canal, equipo, conmutable) = await MontarAsync();
        await using var _ = conmutable;
        equipo.Aviso = string.Empty;

        foreach (var t in equipo.TeclasDeBanda) await equipo.IrABandaCommand.ExecuteAsync(t);
        foreach (var t in equipo.TeclasDeModo) await equipo.PonerModoDeTeclaCommand.ExecuteAsync(t);
        foreach (var t in equipo.TeclasDeMemoria) await equipo.RecuperarMemoriaCommand.ExecuteAsync(t);
        foreach (var plan in equipo.PlanesDeCb)
        {
            equipo.ElegirPlanCbCommand.Execute(plan);
            foreach (var t in equipo.CanalesDeCb) await equipo.IrAlCanalCommand.ExecuteAsync(t);
        }

        lock (canal.Mandadas)
        {
            canal.Mandadas.Should().NotContain(o => Transmiten.Any(t => o.StartsWith(t, StringComparison.Ordinal)));
            canal.Mandadas.Count.Should().Be(12 + 6 + 6 + (5 * 40));
        }

        equipo.Aviso.Should().BeEmpty();
    }

    [Fact]
    public async Task Sin_conexion_la_botonera_esta_apagada_y_dice_por_que()
    {
        var (_, equipo, conmutable) = await MontarAsync();
        await using var _ = conmutable;
        await equipo.DesconectarAsync();
        await EsperaALaVentana.DrenarAsync();

        equipo.IrABandaCommand.CanExecute(equipo.TeclasDeBanda[0]).Should().BeFalse();
        equipo.IrAlCanalCommand.CanExecute(equipo.CanalesDeCb[0]).Should().BeFalse();
        equipo.MotivoDeLaBotonera.Should().Contain("Conecte");
    }

    [Theory]
    [InlineData(MandoDeEquipo.Preamplificador, 0, "Sin preamplificador", "IPO")]
    [InlineData(MandoDeEquipo.Preamplificador, 1, "Amplificador 1", "AMP1")]
    [InlineData(MandoDeEquipo.Preamplificador, 2, "Amplificador 2", "AMP2")]
    [InlineData(MandoDeEquipo.Atenuador, 0, "Apagado", "OFF")]
    [InlineData(MandoDeEquipo.Atenuador, 2, "12 dB", "12dB")]
    [InlineData(MandoDeEquipo.Agc, 0, "Apagado", "OFF")]
    [InlineData(MandoDeEquipo.Agc, 1, "Rápido", "FAST")]
    [InlineData(MandoDeEquipo.Agc, 2, "Medio", "MID")]
    [InlineData(MandoDeEquipo.Agc, 3, "Lento", "SLOW")]
    [InlineData(MandoDeEquipo.Agc, 6, "Automático lento", "AUTO")]
    public void Los_indicadores_del_visor_van_en_corto_como_en_la_radio(MandoDeEquipo mando, int posicion, string largo, string corto) =>
        VistaModeloEquipo.TextoCorto(mando, posicion, largo).Should().Be(corto);

    [Fact]
    public void El_aviso_del_plan_de_bandas_va_en_corto_y_la_explicacion_aparte()
    {
        var plan = new PlanDePrueba();
        var vfo = new VistaModeloVfo(NombreDeVfo.A, plan);

        vfo.Recoger(new EstadoDeUnVfo(NombreDeVfo.A, Frecuencia.DesdeHercios(27_555_000), Modo.Parse("AM"), true, true, true, null));
        vfo.AvisoCortoDelBandplan.Should().Be("Fuera de banda");
        vfo.AvisoDelBandplan.Should().Contain("IARU", "la frase larga sigue para la ayuda emergente");

        vfo.Recoger(new EstadoDeUnVfo(NombreDeVfo.A, Frecuencia.DesdeHercios(14_020_000), Modo.Parse("SSB", "USB"), true, true, true, null));
        vfo.AvisoCortoDelBandplan.Should().Be("Tramo de CW");

        vfo.Recoger(new EstadoDeUnVfo(NombreDeVfo.A, Frecuencia.DesdeHercios(14_200_000), Modo.Parse("SSB", "USB"), true, true, true, null));
        vfo.AvisoCortoDelBandplan.Should().BeEmpty();
    }

    private sealed class PlanDePrueba : IBandplan
    {
        public RegionIaru Region => RegionIaru.Region1;

        public IReadOnlyList<TramoDeBanda> TramosDe(Banda banda) => [];

        public IReadOnlyList<(Frecuencia Frecuencia, string Descripcion)> FrecuenciasSenaladas(Banda banda) => [];

        public ConsultaDeBandplan Consultar(Frecuencia frecuencia, Modo modo = default)
        {
            if (frecuencia.Hercios > 27_000_000)
            {
                return new(frecuencia, null, false, null, "27.555,0 kHz queda fuera del plan de bandas de IARU Región 1.");
            }

            var cw = new TramoDeBanda(Banda.DesdeFrecuencia(frecuencia), Frecuencia.DesdeHercios(14_000_000), Frecuencia.DesdeHercios(14_070_000), UsoDelTramo.Cw, "Telegrafía", 200);
            return frecuencia.Hercios < 14_070_000
                ? new(frecuencia, cw, true, false, "Este tramo es de telegrafía y el modo es fonía.")
                : new(frecuencia, null, true, true, null);
        }
    }
}

/// <summary>Las tablas de canales de 27 MHz, canal a canal, contra la fuente publica.</summary>
public sealed class CanalesCbPruebas
{
    /// <summary>FCC 47 CFR 95.963, en MHz: la misma tabla que CEPT (ECC/DEC/(11)03).</summary>
    private static readonly string[] Fcc =
    [
        "26.965", "26.975", "26.985", "27.005", "27.015", "27.025", "27.035", "27.055", "27.065", "27.075",
        "27.085", "27.105", "27.115", "27.125", "27.135", "27.155", "27.165", "27.175", "27.185", "27.205",
        "27.215", "27.225", "27.255", "27.235", "27.245", "27.265", "27.275", "27.285", "27.295", "27.305",
        "27.315", "27.325", "27.335", "27.345", "27.355", "27.365", "27.375", "27.385", "27.395", "27.405",
    ];

    private static long Hz(string mhz) => (long)Math.Round(decimal.Parse(mhz, CultureInfo.InvariantCulture) * 1_000_000m);

    [Theory]
    [InlineData(PlanCb.Cept)]
    [InlineData(PlanCb.Usa)]
    [InlineData(PlanCb.Once)]
    public void Cept_USA_y_11m_son_la_tabla_de_los_40_canales(PlanCb plan)
    {
        for (var canal = 1; canal <= 40; canal++)
        {
            CanalesCb.Hercios(plan, canal).Should().Be(Hz(Fcc[canal - 1]), $"canal {canal}");
        }
    }

    [Fact]
    public void Polonia_es_la_tabla_5_kHz_por_debajo()
    {
        for (var canal = 1; canal <= 40; canal++)
        {
            CanalesCb.Hercios(PlanCb.Pl, canal).Should().Be(Hz(Fcc[canal - 1]) - 5_000, $"canal {canal}");
        }

        CanalesCb.Hercios(PlanCb.Pl, 1).Should().Be(26_960_000);
    }

    [Fact]
    public void Reino_Unido_27_81_va_de_27_60125_a_27_99125_cada_10_kHz()
    {
        for (var canal = 1; canal <= 40; canal++)
        {
            CanalesCb.Hercios(PlanCb.Uk, canal).Should().Be(27_601_250 + ((canal - 1) * 10_000L), $"canal {canal}");
        }

        CanalesCb.Hercios(PlanCb.Uk, 40).Should().Be(27_991_250);
    }

    [Fact]
    public void Los_huecos_y_el_23_24_25_desordenado_estan_donde_dice_la_FCC()
    {
        (CanalesCb.Hercios(PlanCb.Cept, 4) - CanalesCb.Hercios(PlanCb.Cept, 3)).Should().Be(20_000);
        (CanalesCb.Hercios(PlanCb.Cept, 20) - CanalesCb.Hercios(PlanCb.Cept, 19)).Should().Be(20_000);
        CanalesCb.Hercios(PlanCb.Cept, 23).Should().Be(27_255_000);
        CanalesCb.Hercios(PlanCb.Cept, 24).Should().Be(27_235_000);
        CanalesCb.Hercios(PlanCb.Cept, 25).Should().Be(27_245_000);
        CanalesCb.CanalEn(PlanCb.Cept, 27_555_000).Should().BeNull("27.555 no es canal");
        CanalesCb.CanalEn(PlanCb.Cept, 27_185_000).Should().Be(19);
    }

    [Fact]
    public void Un_canal_fuera_de_1_a_40_no_existe()
    {
        var accion = () => CanalesCb.Hercios(PlanCb.Cept, 41);
        accion.Should().Throw<ArgumentOutOfRangeException>();
    }
}
