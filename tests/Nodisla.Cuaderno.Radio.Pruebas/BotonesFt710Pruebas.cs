using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Radio.Control.Ft710;
using Nodisla.Cuaderno.Radio.Ptt;
using Nodisla.Cuaderno.Radio.Pruebas.Dobles;

namespace Nodisla.Cuaderno.Radio.Pruebas;

/// <summary>
/// Las teclas, diales y mandos del frontal contra las respuestas REALES del FT-710 de EA8DLF
/// (28-09-2026, docs/15-botones-ft710-validados.md).
/// </summary>
/// <remarks>
/// Cada orden que se espera aqui es la que se mando a la radio de verdad por el camino del
/// programa y se leyo de vuelta (<c>Capturas/ft710-botones-2026-09-28.tsv</c>). El canal es en
/// memoria y sin esperas: no depende del reloj ni de que la radio este encendida.
/// </remarks>
public class BotonesFt710Pruebas
{
    /// <summary>Ordenes que ponen el equipo en antena. Ningun boton salvo MOX/TUNE/PTT las manda.</summary>
    private static readonly string[] Transmiten = ["TX1", "TX2", "MX1", "AC003", "KY"];

    private static async Task<(CanalCatDeCaptura Canal, ControlFt710 Control)> MontarAsync()
    {
        var canal = new CanalCatDeCaptura();
        var control = new ControlFt710(canal, new OpcionesFt710
        {
            IntervaloDeSondeo = TimeSpan.FromHours(1),
            Esperar = (_, _) => Task.CompletedTask,
        });
        await control.ConectarAsync();
        return (canal, control);
    }

    [Theory]
    [InlineData(TeclaDelEquipo.MemoriaAVfo, new[] { "MA;" })]
    [InlineData(TeclaDelEquipo.VfoOMemoria, new[] { "VM;" })]
    [InlineData(TeclaDelEquipo.RecuperarMemoriaRapida, new[] { "QR;" })]
    [InlineData(TeclaDelEquipo.GuardarMemoriaRapida, new[] { "QI;" })]
    [InlineData(TeclaDelEquipo.BandaArriba, new[] { "BU0;" })]
    [InlineData(TeclaDelEquipo.BandaAbajo, new[] { "BD0;" })]
    [InlineData(TeclaDelEquipo.AjusteACero, new[] { "ZI0;" })]
    [InlineData(TeclaDelEquipo.AlternarVfo, new[] { "SV;" })]
    [InlineData(TeclaDelEquipo.BorrarClarificador, new[] { "CF001+0000;" })]
    [InlineData(TeclaDelEquipo.RestablecerDsp, new[] { "IS00+0000;", "SH0000;", "BP00000;", "CO000000;", "CO020000;" })]
    public async Task Cada_tecla_manda_lo_que_se_mando_a_la_radio_real(TeclaDelEquipo tecla, string[] ordenes)
    {
        var (canal, control) = await MontarAsync();
        await using var _ = control;
        var desde = canal.Recibidas.Count;

        await control.PulsarAsync(tecla);

        canal.Recibidas.Skip(desde).Where(o => ordenes.Contains(o)).Should().Equal(ordenes);
    }

    [Fact]
    public async Task Ninguna_tecla_ni_mando_manda_una_orden_de_transmitir()
    {
        var (canal, control) = await MontarAsync();
        await using var _ = control;

        foreach (var tecla in control.Teclas)
        {
            await control.PulsarAsync(tecla);
        }

        foreach (var mando in control.Mandos)
        {
            var rango = control.Rango(mando)!;
            if (rango.SoloLectura) continue;

            // La ultima posicion del acoplador es «Sintonizar»: sin transmision vigilada se rechaza.
            var tope = rango.TransmiteAlAccionar ? rango.Maximo - 1 : rango.Maximo;
            await control.EscribirMandoAsync(mando, rango.Minimo);
            await control.EscribirMandoAsync(mando, tope);
        }

        await control.GirarDialAsync(3);
        await control.GirarDialAsync(-3);
        await control.GirarPasosAsync(1);
        await control.GirarPasosAsync(-1);

        canal.Recibidas.Should().NotContain(o => Transmiten.Any(t => o.StartsWith(t, StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Sintonizar_sin_transmision_vigilada_se_rechaza()
    {
        var (canal, control) = await MontarAsync();
        await using var _ = control;

        var sintonizar = () => control.EscribirMandoAsync(MandoDeEquipo.Sintonizador, 2);

        await sintonizar.Should().ThrowAsync<InvalidOperationException>();
        canal.Recibidas.Should().NotContain("AC003;");
    }

    [Fact]
    public async Task El_clarificador_va_por_cf_y_no_pisa_el_otro_interruptor()
    {
        var (canal, control) = await MontarAsync();
        await using var _ = control;

        // RT/XT/RC contestan «?;» (captura del 28-09); CF000 da los dos interruptores.
        control.Mandos.Should().Contain([MandoDeEquipo.Rit, MandoDeEquipo.Xit, MandoDeEquipo.DesplazamientoRit]);
        await control.EscribirMandoAsync(MandoDeEquipo.Rit, 1);
        canal.Recibidas.Should().Contain("CF00010000;");

        // Con el de recepcion puesto (lo que contesto la radio), el de transmision lo respeta.
        canal.Responder("CF000;", "CF00010000;");
        await control.EscribirMandoAsync(MandoDeEquipo.Xit, 1);
        canal.Recibidas.Should().Contain("CF00011000;");

        await control.EscribirMandoAsync(MandoDeEquipo.DesplazamientoRit, 120);
        canal.Recibidas.Should().Contain("CF001+0120;");
        canal.Responder("CF001;", "CF001-0050;");
        (await control.LeerMandoAsync(MandoDeEquipo.DesplazamientoRit)).Should().Be(-50);
    }

    [Fact]
    public async Task El_visor_lee_el_clarificador_y_las_memorias_del_if()
    {
        var (canal, control) = await MontarAsync();
        await using var _ = control;

        // Respuesta real con CLAR de recepcion a +120 Hz (28-09-2026, 13:36:46).
        canal.Responder("IF;", "IF000014155000+012010200000;");
        var vfos = await control.LeerVfosAsync();
        vfos.Rit.Should().BeTrue();
        vfos.Xit.Should().BeFalse();
        vfos.DesplazamientoRitHz.Should().Be(120);
        vfos.EnMemoria.Should().BeFalse();

        // Tras V/M: memoria 001, 7.000.000 LSB.
        canal.Responder("IF;", "IF001007000000+000000110000;");
        (await control.LeerVfosAsync()).EnMemoria.Should().BeTrue();
    }

    [Theory]
    [InlineData("SS0640000;", "W/F CENTER")]
    [InlineData("SS0650000;", "W/F CENTER EXPAND")]
    [InlineData("SS0680000;", "W/F CURSOR EXPAND")]
    [InlineData("SS06B0000;", "W/F FIX EXPAND")]
    [InlineData("SS0600000;", "3DSS CENTER")]
    [InlineData("SS06A0000;", "W/F FIX")]
    public async Task El_modo_del_analizador_se_lee_como_contesta_la_radio(string respuesta, string modo)
    {
        var (canal, control) = await MontarAsync();
        await using var _ = control;

        canal.Responder("SS06;", respuesta);
        var indice = await control.LeerMandoAsync(MandoDeEquipo.EspectroModo);

        ModosDelAnalizadorFt710.Todos[(int)indice!.Value].Nombre.Should().Be(modo);
    }

    [Fact]
    public async Task Los_modos_ampliados_se_escriben_como_dice_el_manual()
    {
        var (canal, control) = await MontarAsync();
        await using var _ = control;

        // Se manda 3/6/9 (el manual) aunque la radio luego conteste 5/8/B; mandar 5 da «?;».
        await control.EscribirMandoAsync(MandoDeEquipo.EspectroModo, ModosDelAnalizadorFt710.Indice(false, 0, true));
        await control.EscribirMandoAsync(MandoDeEquipo.EspectroModo, ModosDelAnalizadorFt710.Indice(false, 2, true));
        await control.EscribirMandoAsync(MandoDeEquipo.EspectroAncho, 8);
        await control.EscribirMandoAsync(MandoDeEquipo.EspectroVelocidad, 3);
        await control.EscribirMandoAsync(MandoDeEquipo.EspectroNivel, 10.5);

        canal.Recibidas.Should().ContainInOrder("SS0630000;", "SS0690000;", "SS0580000;", "SS0030000;", "SS04+10.5;");
        (await control.LeerMandoAsync(MandoDeEquipo.EspectroNivel)).Should().Be(10.5, "la radio contesto SS04+10.5");
    }

    [Fact]
    public void Las_teclas_center_3dss_y_expand_recorren_los_modos_como_la_radio()
    {
        var wfCenter = ModosDelAnalizadorFt710.Indice(false, 0, false);

        ModosDelAnalizadorFt710.Todos[ModosDelAnalizadorFt710.TrasCenter(wfCenter)].Nombre.Should().Be("W/F CURSOR");
        ModosDelAnalizadorFt710.Todos[ModosDelAnalizadorFt710.TrasExpand(wfCenter)].Nombre.Should().Be("W/F CENTER EXPAND");
        ModosDelAnalizadorFt710.Todos[ModosDelAnalizadorFt710.TrasTresD(wfCenter)].Nombre.Should().Be("3DSS CENTER");

        var tresD = ModosDelAnalizadorFt710.Indice(true, 1, false);
        ModosDelAnalizadorFt710.TrasExpand(tresD).Should().Be(tresD, "en 3DSS no hay ampliado");
    }

    [Fact]
    public async Task Func_y_dsp_se_leen_y_se_eligen_con_sf()
    {
        var (canal, control) = await MontarAsync();
        await using var _ = control;

        // SF0D = RF POWER y SF11 = SHIFT: lo que tenia la radio del operador.
        (await control.LeerMandoAsync(MandoDeEquipo.FuncionDelMandoFunc)).Should().Be(13);
        MandosFt710.FuncionesDelMandoFunc[12].Should().Be("RF POWER");
        (await control.LeerMandoAsync(MandoDeEquipo.FuncionDelMandoDsp)).Should().Be(1);

        await control.EscribirMandoAsync(MandoDeEquipo.FuncionDelMandoFunc, 14);
        await control.EscribirMandoAsync(MandoDeEquipo.FuncionDelMandoFunc, 17);
        await control.EscribirMandoAsync(MandoDeEquipo.FuncionDelMandoDsp, 3);

        canal.Recibidas.Should().ContainInOrder("SF0E;", "SF0H;", "SF13;");
    }

    [Fact]
    public async Task El_contraste_se_cambia_sin_tocar_el_brillo()
    {
        var (canal, control) = await MontarAsync();
        await using var _ = control;

        // DA00101520: contraste 10, brillo 15, pilotos 20.
        (await control.LeerMandoAsync(MandoDeEquipo.BrilloPantalla)).Should().Be(15);
        await control.EscribirMandoAsync(MandoDeEquipo.ContrastePantalla, 12);

        canal.Recibidas.Should().Contain("DA00121520;");
    }

    [Theory]
    [InlineData("FN0;", 1, "FA014155200;")]
    [InlineData("FN1;", 1, "FA014155020;")]
    [InlineData("FN2;", 1, "FA014157000;")]
    [InlineData("FN0;", -1, "FA014154800;")]
    public async Task El_dial_da_diez_pasos_por_muesca_y_fine_fast_lo_cambian(string fina, int muescas, string esperada)
    {
        var (canal, control) = await MontarAsync();
        await using var _ = control;

        // Radio real: paso 20 Hz (EX0305012), 14.155.000 en IF. En la radio, con FN0 una muesca
        // subio 200 Hz, con FN1 20 Hz y con FN2 2000 Hz.
        canal.Responder("IF;", "IF000014155000+000000200000;");
        canal.Responder("FN;", fina);
        await control.GirarDialAsync(muescas);

        canal.Recibidas.Should().Contain(esperada);
    }

    [Fact]
    public async Task Con_lock_el_dial_no_se_mueve()
    {
        var (canal, control) = await MontarAsync();
        await using var _ = control;

        canal.Responder("LK;", "LK1;");
        await control.GirarDialAsync(5);

        canal.Recibidas.Should().NotContain(o => o.StartsWith("FA", StringComparison.Ordinal) && o.Length == 12);
    }

    [Fact]
    public async Task Step_mch_salta_a_la_rejilla_del_canal_y_en_memorias_cambia_de_canal()
    {
        var (canal, control) = await MontarAsync();
        await using var _ = control;

        // CH STEP 5 kHz (EX0305032). En la radio: 7.002.020 -> 7.005.000 -> 7.000.000.
        canal.Responder("IF;", "IF000007002020+000000100000;");
        await control.GirarPasosAsync(1);
        canal.Recibidas.Should().Contain("FA007005000;");

        canal.Responder("IF;", "IF000007005000+000000100000;");
        await control.GirarPasosAsync(-1);
        canal.Recibidas.Should().Contain("FA007000000;");

        canal.Responder("IF;", "IF001007000000+000000110000;");
        await control.GirarPasosAsync(1);
        canal.Recibidas.Should().Contain("CH0;");
    }

    [Fact]
    public async Task Intercambiar_escribe_cada_vfo_en_el_otro_porque_sv_no_intercambia()
    {
        var (canal, control) = await MontarAsync();
        await using var _ = control;
        canal.Responder("FB;", "FB018100000;");
        canal.Responder("MD1;", "MD1C;");

        await control.IntercambiarVfosAsync();

        canal.Recibidas.Should().ContainInOrder("MD0C;", "MD12;", "FA018100000;", "FB027555000;");
        canal.Recibidas.Should().NotContain("SV;");
    }

    [Fact]
    public async Task Tune_sube_con_ac003_por_el_vigilante_y_baja_con_tx0_y_el_acoplador_en_linea()
    {
        var (canal, control) = await MontarAsync();
        await using var _ = control;
        await using var vigilante = new VigilantePtt(control, new OpcionesDelVigilante
        {
            TiempoMaximo = TimeSpan.FromSeconds(10),
            TiempoSinLatido = TimeSpan.FromSeconds(10),
            EngancharseAlCierreDelProceso = false,
            Seguridad = Dobles.SeguridadDePrueba.SinPlanNiRoe,
        });

        // Lo que contesto RI0; en la radio mientras sintonizaba (28-09-2026, 15:07): TX y
        // sintonizando, a ratos con Hi-SWR, y al final ceros.
        canal.ResponderEnSecuencia("RI0;", ["RI00010100;", "RI01010100;", "RI00010100;", "RI00000000;"]);

        control.PrepararSintonia();
        bool termino;
        await using (var antena = await vigilante.PedirAntenaAsync("TUNE"))
        {
            termino = await control.EsperarFinDeSintoniaAsync(antena.Latir, TimeSpan.FromSeconds(10));
        }

        termino.Should().BeTrue();
        canal.Recibidas.Should().ContainInOrder("AC003;", "TX0;", "AC001;");
        canal.Recibidas.Should().NotContain("TX1;", "TUNE la hace el acoplador, no el CAT");
        control.Estado.Transmitiendo.Should().BeFalse();
    }

    [Fact]
    public async Task Tune_cortado_por_el_tope_para_el_acoplador()
    {
        var (canal, control) = await MontarAsync();
        await using var _ = control;
        await using var vigilante = new VigilantePtt(control, new OpcionesDelVigilante
        {
            TiempoMaximo = TimeSpan.FromSeconds(10),
            TiempoSinLatido = TimeSpan.FromSeconds(10),
            EngancharseAlCierreDelProceso = false,
            Seguridad = Dobles.SeguridadDePrueba.SinPlanNiRoe,
        });

        // Sintonizando siempre: se corta al tope y se para con AC000 antes de dejarlo en linea.
        canal.Responder("RI0;", "RI00010100;");
        control.PrepararSintonia();
        bool termino;
        await using (var antena = await vigilante.PedirAntenaAsync("TUNE"))
        {
            termino = await control.EsperarFinDeSintoniaAsync(antena.Latir, TimeSpan.FromSeconds(1));
        }

        termino.Should().BeFalse();
        canal.Recibidas.Should().ContainInOrder("AC003;", "TX0;", "AC000;", "AC001;");
    }
}
