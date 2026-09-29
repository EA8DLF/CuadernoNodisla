using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Radio.Control;
using Nodisla.Cuaderno.Radio.Control.Ft710;
using Nodisla.Cuaderno.Radio.Pruebas.Dobles;

namespace Nodisla.Cuaderno.Radio.Pruebas;

/// <summary>
/// El control del FT-710 contra las respuestas <b>reales</b> del equipo de EA8DLF (27-09-2026).
/// </summary>
/// <remarks>
/// Los guiones en VFO A y B salieron porque todo se habia probado contra el simulador. Estas
/// pruebas usan lo que contesto la radio de verdad (<c>Capturas/ft710-2026-09-27.tsv</c>) y un
/// canal en memoria sin esperas: no dependen del reloj ni de que la radio este encendida.
/// </remarks>
public class CapturaRealFt710Pruebas
{
    private static readonly Modo Usb = TraductorDeModos.PorOmision.DesdeElEquipo("USB");
    private static readonly Modo Lsb = TraductorDeModos.PorOmision.DesdeElEquipo("LSB");

    private static async Task<(CanalCatDeCaptura Canal, ControlFt710 Control)> MontarAsync()
    {
        var canal = new CanalCatDeCaptura();

        // El sondeo de fondo no llega a dispararse: las pruebas leen cuando quieren.
        var control = new ControlFt710(canal, new OpcionesFt710 { IntervaloDeSondeo = TimeSpan.FromHours(1) });
        await control.ConectarAsync();
        return (canal, control);
    }

    [Fact]
    public async Task Con_la_captura_real_los_dos_vfo_llegan_al_estado()
    {
        var (_, control) = await MontarAsync();
        await using var __ = control;

        control.Should().BeAssignableTo<IEquipoConDosVfos>();
        var vfos = control.Vfos;

        // FA027555000 y FB027555000, MD02 y MD12, VS0, ST0, FT0.
        vfos.A.Frecuencia.Should().Be(Frecuencia.DesdeHercios(27_555_000));
        vfos.B.Frecuencia.Should().Be(Frecuencia.DesdeHercios(27_555_000));
        vfos.A.Modo.Should().Be(Usb);
        vfos.B.Modo.Should().Be(Usb);
        vfos.A.EsElActivo.Should().BeTrue();
        vfos.B.EsElActivo.Should().BeFalse();
        vfos.A.Transmite.Should().BeTrue();
        vfos.A.Recibe.Should().BeTrue();
        vfos.Split.Should().BeFalse();

        control.Estado.Vfo.Should().Be("VFO A");
        control.Estado.Frecuencia.Should().Be(Frecuencia.DesdeHercios(27_555_000));
    }

    [Fact]
    public async Task Fa_y_fb_distintos_llegan_cada_uno_a_su_vfo()
    {
        var (canal, control) = await MontarAsync();
        await using var __ = control;

        canal.Responder("FB;", "FB007074000;");
        canal.Responder("MD1;", "MD11;");
        var vfos = await control.LeerVfosAsync();

        vfos.A.Frecuencia.Should().Be(Frecuencia.DesdeHercios(27_555_000));
        vfos.B.Frecuencia.Should().Be(Frecuencia.DesdeHercios(7_074_000));
        vfos.B.Modo.Should().Be(Lsb);
        vfos.B.Banda.Should().Be(Banda.DesdeFrecuencia(Frecuencia.DesdeHercios(7_074_000)));
    }

    [Fact]
    public async Task Vs1_hace_activo_el_vfo_b()
    {
        var (canal, control) = await MontarAsync();
        await using var __ = control;

        canal.Responder("FB;", "FB007074000;");
        canal.Responder("VS;", "VS1;");
        // Lo que contesta la radio de verdad con VS1 y sin split (29-09-2026): FT0, porque FT0 es
        // «transmite la banda principal», y con VS1 la principal es el B.
        canal.Responder("FT;", "FT0;");
        var vfos = await control.LeerVfosAsync();

        vfos.B.EsElActivo.Should().BeTrue();
        vfos.B.Recibe.Should().BeTrue();
        vfos.B.Transmite.Should().BeTrue();
        vfos.A.EsElActivo.Should().BeFalse();
        control.Estado.Vfo.Should().Be("VFO B");
        control.Estado.Frecuencia.Should().Be(Frecuencia.DesdeHercios(7_074_000));
    }

    [Fact]
    public async Task Las_escrituras_de_vfo_solo_salen_cuando_se_piden_y_con_su_formato()
    {
        var (canal, control) = await MontarAsync();
        await using var __ = control;

        await control.PonerVfoActivoAsync(NombreDeVfo.B);
        await control.PonerFrecuenciaDeAsync(NombreDeVfo.B, Frecuencia.DesdeHercios(7_074_000));
        await control.PonerVfoActivoAsync(NombreDeVfo.A);
        await control.IgualarVfosAsync();

        canal.Recibidas.Should().ContainInOrder("VS1;", "FB007074000;", "VS0;", "AB;");
        canal.Recibidas.Should().NotContain("SV;", "igualar copia, no intercambia");
        control.Vfos.B.Frecuencia.Should().Be(Frecuencia.DesdeHercios(27_555_000), "AB; copia el A sobre el B");
    }

    [Fact]
    public async Task Conectar_y_leer_solo_manda_consultas_ya_hechas_a_la_radio_real()
    {
        var (canal, control) = await MontarAsync();
        await using var __ = control;
        await control.LeerVfosAsync();
        foreach (var mando in MandosFt710.Todos)
        {
            await control.LeerMandoAsync(mando.Mando);
        }

        var capturadas = CanalCatDeCaptura.ConsultasCapturadas();
        canal.Recibidas.Should().OnlyContain(orden => capturadas.Contains(orden));
        canal.Recibidas.Should().NotContain(["SV;", "TX1;", "AB;", "BA;"]);
    }

    [Fact]
    public async Task Un_no_admitido_no_borra_lo_que_ya_se_sabia()
    {
        var (canal, control) = await MontarAsync();
        await using var __ = control;

        canal.Responder("FB;", "?;");
        canal.Responder("MD1;", "?;");
        canal.Responder("ST;", "?;");
        canal.Responder("FT;", "?;");
        var vfos = await control.LeerVfosAsync();

        vfos.B.Frecuencia.Should().Be(Frecuencia.DesdeHercios(27_555_000), "«?;» no es una frecuencia");
        vfos.B.Modo.Should().Be(Usb);
        vfos.Split.Should().BeFalse();
        vfos.A.Transmite.Should().BeTrue("sin FT ni split se transmite por el activo");
        control.Estado.Conectado.Should().BeTrue("un «?;» no es perder la radio");
    }

    [Fact]
    public async Task Una_respuesta_de_otra_orden_no_se_toma_por_frecuencia()
    {
        var (canal, control) = await MontarAsync();
        await using var __ = control;

        // Respuesta atrasada del medidor en el hueco de FA: antes se leia como 3 Hz.
        canal.Responder("FA;", "SM0003;");
        var vfos = await control.LeerVfosAsync();

        vfos.A.Frecuencia.Should().Be(Frecuencia.DesdeHercios(27_555_000));
    }

    [Fact]
    public async Task Si_vs_no_contesta_el_equipo_no_esta()
    {
        var (canal, control) = await MontarAsync();
        await using var __ = control;

        canal.Responder("VS;", null);
        var leer = () => control.LeerVfosAsync();

        await leer.Should().ThrowAsync<EquipoNoContestaException>();
    }

    [Fact]
    public async Task El_equipo_real_admite_todos_los_mandos_y_solo_dos_en_el_segundo_vfo()
    {
        var (_, control) = await MontarAsync();
        await using var __ = control;

        // 27 el 27-09; con los de las teclas del frontal (28-09-2026), todos los de la tabla.
        control.Mandos.Should().HaveCount(MandosFt710.Todos.Count);
        control.MandosDe(VfoDelEquipo.Secundario).Should().BeEquivalentTo(
            [MandoDeEquipo.Volumen, MandoDeEquipo.SupresorDeRuido],
            "AG1 y NB1 contestan con su índice; SQ1, PA1 e IS1 con el del principal");
    }

    [Theory]
    [InlineData(MandoDeEquipo.Volumen, 0d)] // AG0000
    [InlineData(MandoDeEquipo.GananciaRf, 255d)] // RG0255
    [InlineData(MandoDeEquipo.GananciaMicrofono, 96d)] // MG096
    [InlineData(MandoDeEquipo.Potencia, 5d)] // PC005
    [InlineData(MandoDeEquipo.Silenciador, 0d)] // SQ0000
    [InlineData(MandoDeEquipo.Atenuador, 0d)] // RA00
    [InlineData(MandoDeEquipo.Preamplificador, 0d)] // PA00
    [InlineData(MandoDeEquipo.Agc, 6d)] // GT06
    [InlineData(MandoDeEquipo.SupresorDeRuido, 1d)] // NB01
    [InlineData(MandoDeEquipo.NivelSupresorDeRuido, 0d)] // NL0000
    [InlineData(MandoDeEquipo.ReductorDeRuido, 0d)] // NR00
    [InlineData(MandoDeEquipo.NivelReductorDeRuido, 1d)] // RL001
    [InlineData(MandoDeEquipo.MuescaAutomatica, 0d)] // BC00
    [InlineData(MandoDeEquipo.MuescaManual, 0d)] // BP00000
    [InlineData(MandoDeEquipo.FrecuenciaDeMuesca, 10d)] // BP01001 = 10 Hz
    [InlineData(MandoDeEquipo.Contorno, 0d)] // CO000000
    [InlineData(MandoDeEquipo.FrecuenciaDeContorno, 10d)] // CO010010
    [InlineData(MandoDeEquipo.AnchoDeFiltro, 20d)] // SH0020
    [InlineData(MandoDeEquipo.DesplazamientoFi, 0d)] // IS00+0000
    [InlineData(MandoDeEquipo.TonoCw, 700d)] // KP40
    [InlineData(MandoDeEquipo.VelocidadKeyer, 20d)] // KS020
    [InlineData(MandoDeEquipo.BreakIn, 0d)] // BI0
    [InlineData(MandoDeEquipo.Vox, 0d)] // VX0
    [InlineData(MandoDeEquipo.GananciaVox, 70d)] // VG070
    [InlineData(MandoDeEquipo.RetardoVox, 8d)] // VD08
    [InlineData(MandoDeEquipo.Sintonizador, 1d)] // AC001
    [InlineData(MandoDeEquipo.Split, 0d)] // ST0
    public async Task Cada_mando_se_lee_bien_de_la_respuesta_real(MandoDeEquipo mando, double esperado)
    {
        var (_, control) = await MontarAsync();
        await using var __ = control;

        (await control.LeerMandoAsync(mando)).Should().Be(esperado);
    }

    [Fact]
    public async Task Las_etiquetas_de_la_respuesta_real_son_las_del_manual()
    {
        var (_, control) = await MontarAsync();
        await using var __ = control;

        (await control.LeerAnchoDeFiltroEnHerciosAsync()).Should().Be(3000, "SH0020 en USB son 3000 Hz");
        control.Rango(MandoDeEquipo.RetardoVox)!.Etiquetas![8].Should().Be("500 ms");
        control.Rango(MandoDeEquipo.Agc)!.Etiquetas![6].Should().Be("Automático lento");
        control.Rango(MandoDeEquipo.Sintonizador)!.Etiquetas![1].Should().Be("Encendido");
    }

    [Fact]
    public async Task Los_medidores_crudos_son_tres_cifras_y_no_seis()
    {
        var (canal, control) = await MontarAsync();
        await using var __ = control;

        // Tal cual lo contestó la radio durante la validación.
        canal.Responder("RM1;", "RM1008000;");
        var medidores = await control.LeerMedidoresCrudosAsync();

        medidores["señal"].Should().Be(8);
        medidores.Values.Should().OnlyContain(valor => valor >= 0 && valor <= 255);
    }

    [Fact]
    public async Task El_agc_automatico_se_escribe_como_el_manual_manda()
    {
        var (canal, control) = await MontarAsync();
        await using var __ = control;

        await control.EscribirMandoAsync(MandoDeEquipo.Agc, 6);
        await control.EscribirMandoAsync(MandoDeEquipo.Agc, 3);

        // GT06 se LEE (automático lento) pero al escribir solo existe GT04, «automático».
        canal.Recibidas.Should().Contain("GT04;").And.Contain("GT03;").And.NotContain("GT06;");
    }

    [Fact]
    public async Task La_frecuencia_de_la_muesca_se_escribe_en_pasos_de_diez_hercios()
    {
        var (canal, control) = await MontarAsync();
        await using var __ = control;

        await control.EscribirMandoAsync(MandoDeEquipo.FrecuenciaDeMuesca, 1500);

        canal.Recibidas.Should().Contain("BP01150;");
    }

    [Fact]
    public async Task Cerrar_sin_haber_conectado_nunca_no_toca_el_puerto()
    {
        var canal = new CanalCatDeCaptura();
        var control = new ControlFt710(canal, new OpcionesFt710 { IntervaloDeSondeo = TimeSpan.FromHours(1) });

        await control.DisposeAsync();

        // Antes se abría el puerto configurado para mandarle «TX0;» a quien estuviera ahí.
        canal.Aperturas.Should().Be(0);
        canal.Recibidas.Should().BeEmpty();
    }

    [Fact]
    public async Task Con_ptt_pedido_y_el_canal_roto_se_reabre_y_se_baja_el_ptt()
    {
        var (canal, control) = await MontarAsync();
        await ((IPttDirecto)control).PonerPttDirectoAsync(true, CancellationToken.None);
        canal.Romper();

        await control.DisposeAsync();

        canal.Aperturas.Should().Be(2, "una al conectar y otra para bajar el PTT");
        canal.Recibidas.Should().EndWith("TX0;");
    }

    [Fact]
    public async Task Conectado_y_con_el_canal_roto_se_reabre_para_bajar_el_ptt()
    {
        var (canal, control) = await MontarAsync();
        canal.Romper();

        await control.DisposeAsync();

        canal.Aperturas.Should().Be(2);
        canal.Recibidas.Should().EndWith("TX0;");
    }

    [Fact]
    public async Task El_firmware_del_equipo_real_se_lee()
    {
        var (_, control) = await MontarAsync();
        await using var __ = control;

        var versiones = await control.LeerVersionesDeFirmwareAsync();

        versiones["unidad principal"].Should().Be("01.12");
        versiones["procesador de señal"].Should().Be("01.01");
    }
}
