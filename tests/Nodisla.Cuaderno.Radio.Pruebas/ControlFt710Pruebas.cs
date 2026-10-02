using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Radio.Control;
using Nodisla.Cuaderno.Radio.Control.Ft710;
using Nodisla.Cuaderno.Radio.Ptt;
using Nodisla.Cuaderno.Radio.Pruebas.Dobles;

namespace Nodisla.Cuaderno.Radio.Pruebas;

/// <summary>
/// El control nativo del FT-710, probado contra un equipo de mentira que contesta lo mismo que
/// contesto el de verdad.
/// </summary>
public class ControlFt710Pruebas
{
    private static async Task<(Ft710DeMentira Equipo, ControlFt710 Control)> MontarAsync(
        bool conectar = true,
        OpcionesFt710? opciones = null)
    {
        var equipo = new Ft710DeMentira();
        var ajustes = opciones ?? new OpcionesFt710
        {
            IntervaloDeSondeo = TimeSpan.FromMilliseconds(50),
            EsperaDeOrden = TimeSpan.FromMilliseconds(500),
            EsperaDeReconexion = TimeSpan.FromMilliseconds(50),
        };

        var canal = new CanalTcpCat("127.0.0.1", equipo.Puerto, ajustes.EsperaDeOrden);
        var control = new ControlFt710(canal, ajustes);
        if (conectar)
        {
            await control.ConectarAsync();
        }

        return (equipo, control);
    }

    [Fact]
    public async Task Se_identifica_el_equipo_por_su_respuesta_a_id()
    {
        var (equipo, control) = await MontarAsync();
        await using var _ = equipo;
        await using var __ = control;

        control.NombreDelEquipo.Should().Be("Yaesu FT-710");
        control.Via.Should().Be(ViaDeControl.CatNativo, "esto no es Hamlib ni OmniRig: es el CAT del equipo");
        equipo.Recibidas.Should().Contain("ID;");
    }

    [Fact]
    public async Task El_acoplador_no_se_sintoniza_sin_pedir_antena()
    {
        var (equipo, control) = await MontarAsync();
        await using var _ = equipo;
        await using var __ = control;

        var rango = control.Rango(MandoDeEquipo.Sintonizador);
        rango!.TransmiteAlAccionar.Should().BeTrue("sintonizar el acoplador emite portadora");

        // Sin transmisión pedida al vigilante no hay quien suelte el PTT si esto se atasca.
        var aPelo = () => control.EscribirMandoAsync(MandoDeEquipo.Sintonizador, 2);

        await aPelo.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*IVigilantePtt*");
        equipo.Recibidas.Should().NotContain("AC002;");
    }

    [Fact]
    public async Task El_acoplador_se_sintoniza_dentro_de_una_transmision_vigilada()
    {
        var (equipo, control) = await MontarAsync();
        await using var _ = equipo;
        await using var __ = control;
        await using var vigilante = new VigilantePtt(control, new OpcionesDelVigilante
        {
            TiempoMaximo = TimeSpan.FromSeconds(10),
            TiempoSinLatido = TimeSpan.FromSeconds(10),
            EngancharseAlCierreDelProceso = false,
            Seguridad = Dobles.SeguridadDePrueba.SinPlanNiRoe,
        });

        await using (await vigilante.PedirAntenaAsync("sintonizar el acoplador"))
        {
            await control.EscribirMandoAsync(MandoDeEquipo.Sintonizador, 2);
        }

        // «Tuning Start» es AC003 segun el manual CAT; AC002 es un guion y no hacia nada.
        await equipo.EsperarOrdenAsync("AC003;", EsperaDeSenales.PlazoDeSeguridad);

        // Y pase lo que pase con el acoplador, el PTT queda abajo.
        await equipo.EsperarAntenaAsync(enAntena: false, EsperaDeSenales.PlazoDeSeguridad);
        equipo.EnAntena.Should().BeFalse();
    }

    [Fact]
    public async Task Hay_mandos_que_solo_existen_en_el_vfo_principal()
    {
        var (equipo, control) = await MontarAsync();
        await using var _ = equipo;
        await using var __ = control;

        var principal = control.MandosDe(VfoDelEquipo.Principal);
        var segundo = control.MandosDe(VfoDelEquipo.Secundario);

        // Estos sí están en los dos: el equipo contesta AG1 y NB1 con su propio índice.
        segundo.Should().Contain(MandoDeEquipo.Volumen);
        segundo.Should().Contain(MandoDeEquipo.SupresorDeRuido);

        // SQ1, PA1 e IS1 contestan con el índice del principal (SQ0000, PA00, IS00+0000): no
        // son mandos aparte del segundo VFO, son el del primero con otra etiqueta.
        segundo.Should().NotContain(MandoDeEquipo.Silenciador);
        segundo.Should().NotContain(MandoDeEquipo.Preamplificador);
        segundo.Should().NotContain(MandoDeEquipo.DesplazamientoFi);

        // Y estos solo en el principal: SH1, RG1, GT1 y RA1 contestan «?;».
        principal.Should().Contain(MandoDeEquipo.AnchoDeFiltro);
        segundo.Should().NotContain(MandoDeEquipo.AnchoDeFiltro);
        segundo.Should().NotContain(MandoDeEquipo.GananciaRf);
        segundo.Should().NotContain(MandoDeEquipo.Agc);
        segundo.Should().NotContain(MandoDeEquipo.Atenuador);

        control.Rango(MandoDeEquipo.AnchoDeFiltro, VfoDelEquipo.Secundario).Should().BeNull();
        var escribir = () => control.EscribirMandoAsync(MandoDeEquipo.AnchoDeFiltro, 5, VfoDelEquipo.Secundario);
        await escribir.Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public async Task Se_acciona_el_segundo_vfo_con_su_propia_orden()
    {
        var (equipo, control) = await MontarAsync();
        await using var _ = equipo;
        await using var __ = control;

        await control.EscribirMandoAsync(MandoDeEquipo.Volumen, 120, VfoDelEquipo.Secundario);

        await equipo.EsperarOrdenAsync("AG1120;", EsperaDeSenales.PlazoDeSeguridad);
        equipo.Recibidas.Should().Contain("AG1120;").And.NotContain("AG0120;");
    }

    [Fact]
    public async Task No_se_ofrece_en_el_segundo_vfo_lo_que_contesta_con_el_indice_del_primero()
    {
        var (equipo, control) = await MontarAsync();
        await using var _ = equipo;
        await using var __ = control;

        // Rareza capturada del firmware: a «SQ1;» contesta «SQ0000;», con el índice del primero.
        // Eso es el silenciador del VFO A: enseñarlo como del B sería mentir.
        var silenciador = await control.LeerMandoAsync(MandoDeEquipo.Silenciador, VfoDelEquipo.Secundario);

        silenciador.Should().BeNull();
        control.Rango(MandoDeEquipo.Silenciador, VfoDelEquipo.Secundario).Should().BeNull();
    }

    [Fact]
    public async Task El_desplazamiento_de_fi_se_lee_con_signo_y_se_acciona_como_manda_el_manual()
    {
        var (equipo, control) = await MontarAsync();
        await using var _ = equipo;
        await using var __ = control;

        // IS00+0000: el valor viene con signo delante.
        (await control.LeerMandoAsync(MandoDeEquipo.DesplazamientoFi)).Should().Be(0d);

        // El manual CAT da -1200…+1200 Hz en pasos de 20; en la radio (28-09-2026) IS00+0100 e
        // IS00-0240 se leyeron de vuelta tal cual.
        var rango = control.Rango(MandoDeEquipo.DesplazamientoFi)!;
        rango.SoloLectura.Should().BeFalse();
        rango.Minimo.Should().Be(-1200);
        rango.Maximo.Should().Be(1200);
        rango.Paso.Should().Be(20);
        await control.EscribirMandoAsync(MandoDeEquipo.DesplazamientoFi, 100);
        await equipo.EsperarOrdenAsync("IS00+0100;", EsperaDeSenales.PlazoDeSeguridad);
    }

    [Fact]
    public async Task Se_lee_el_reloj_del_equipo()
    {
        var (equipo, control) = await MontarAsync();
        await using var _ = equipo;
        await using var __ = control;

        var reloj = await control.LeerRelojDelEquipoAsync();

        // DT020260921 + DT1071700 = 21-09-2026, 07:17:00.
        reloj.Should().NotBeNull();
        reloj!.Value.Year.Should().Be(2026);
        reloj.Value.Month.Should().Be(9);
        reloj.Value.Day.Should().Be(21);
        reloj.Value.Hour.Should().Be(7);
        reloj.Value.Minute.Should().Be(17);
    }

    [Fact]
    public async Task Se_leen_el_tono_el_repetidor_y_los_indicadores()
    {
        var (equipo, control) = await MontarAsync();
        await using var _ = equipo;
        await using var __ = control;

        (await control.LeerIndiceDeTonoAsync()).Should().Be(12);
        (await control.LeerDesplazamientoDeRepetidorAsync()).Should().Be(0);
        (await control.LeerIndicadoresAsync()).Should().Be("0000000");
    }

    [Fact]
    public async Task Se_lee_el_banco_de_memorias()
    {
        var (equipo, control) = await MontarAsync();
        await using var _ = equipo;
        await using var __ = control;

        var memorias = await control.LeerMemoriasAsync();

        // El canal son tres cifras. Con dos, el equipo contesta «?;» y parece que no sepa leer
        // memorias: ese fue el error que las dio por imposibles.
        memorias.Should().HaveCount(2);

        var primera = memorias.Single(memoria => memoria.Numero == 1);
        primera.Frecuencia.Should().Be(Frecuencia.DesdeHercios(7_000_000));
        primera.Modo.NombreUsual.Should().Be("LSB", "en 40 metros se trabaja en banda lateral inferior");
        primera.Etiqueta.Should().BeNull("esa memoria no tiene rótulo puesto");
        primera.Ocupada.Should().BeTrue();

        var otra = memorias.Single(memoria => memoria.Numero == 5);
        otra.Frecuencia.Should().Be(Frecuencia.DesdeHercios(14_074_000));
        otra.Etiqueta.Should().Be("FT8 20M");
    }

    [Fact]
    public async Task Una_memoria_vacia_no_es_un_fallo()
    {
        var (equipo, control) = await MontarAsync();
        await using var _ = equipo;
        await using var __ = control;

        // El simulador contesta «?;» a todos los canales menos al 001 y al 005, igual que el
        // equipo con las memorias vacías. Eso es información, no un error de comunicación.
        var memorias = await control.LeerMemoriasAsync();

        memorias.Should().OnlyContain(memoria => memoria.Ocupada);
        memorias.Select(memoria => memoria.Numero).Should().BeEquivalentTo(new[] { 1, 5 });
        control.Estado.Conectado.Should().BeTrue("un canal vacío no puede tumbar la conexión");
    }

    [Fact]
    public async Task Se_lee_la_version_del_firmware()
    {
        var (equipo, control) = await MontarAsync();
        await using var _ = equipo;
        await using var __ = control;

        var versiones = await control.LeerVersionesDeFirmwareAsync();

        versiones.Should().HaveCount(4);
        versiones["unidad principal"].Should().Be("01.12");
        versiones["unidad de pantalla"].Should().Be("01.08");
        versiones["receptor SDR"].Should().Be("01.04");
        versiones["procesador de señal"].Should().Be("01.01");
    }

    [Fact]
    public async Task El_ancho_de_filtro_se_ensena_en_hercios_segun_el_modo()
    {
        var (equipo, control) = await MontarAsync();
        await using var _ = equipo;
        await using var __ = control;

        // El equipo está en USB con SH0020: son 3000 Hz, no «índice 20».
        var rango = control.Rango(MandoDeEquipo.AnchoDeFiltro);
        rango!.Unidad.Should().Be("Hz");
        rango.EsDePosiciones.Should().BeTrue();
        rango.Maximo.Should().Be(23);
        rango.Etiquetas![20].Should().Be("3000 Hz");

        (await control.LeerAnchoDeFiltroEnHerciosAsync()).Should().Be(3000);

        // Y el índice cero no es un ancho: es «lo que ponga el equipo».
        rango.Etiquetas[0].Should().Be(AnchosDeFiltroFt710.PorOmision);
    }

    [Fact]
    public void El_ancho_de_filtro_depende_del_modo()
    {
        // La misma cifra no significa lo mismo en banda lateral que en telegrafía.
        AnchosDeFiltroFt710.Hercios(13, "USB").Should().Be(2400);
        AnchosDeFiltroFt710.Hercios(13, "CW").Should().Be(1200);
        AnchosDeFiltroFt710.Hercios(1, "AM-N").Should().Be(6000);
        AnchosDeFiltroFt710.Hercios(3, "FM").Should().Be(16000);

        // Cero es «por omisión» y no se convierte; un modo sin tabla tampoco.
        AnchosDeFiltroFt710.Hercios(0, "USB").Should().BeNull();
        AnchosDeFiltroFt710.Hercios(13, null).Should().BeNull();
        AnchosDeFiltroFt710.Etiquetas("mandanga").Should().BeNull();
    }

    [Fact]
    public async Task El_retardo_de_voz_se_ensena_en_milisegundos()
    {
        var (equipo, control) = await MontarAsync();
        await using var _ = equipo;
        await using var __ = control;

        // VD08 son 500 ms. El manual declara cuatro cifras y este firmware contesta con dos:
        // hay que entender las dos longitudes.
        (await control.LeerMandoAsync(MandoDeEquipo.RetardoVox)).Should().Be(8);
        control.Rango(MandoDeEquipo.RetardoVox)!.Unidad.Should().Be("ms");
        control.Rango(MandoDeEquipo.RetardoVox)!.Etiquetas![8].Should().Be("500 ms");

        RetardosDeVozFt710.Milisegundos(0).Should().Be(30);
        RetardosDeVozFt710.Milisegundos(6).Should().Be(300);
        RetardosDeVozFt710.Milisegundos(33).Should().Be(3000, "es el extremo que da el manual");
        RetardosDeVozFt710.Indice(500).Should().Be(8);
    }

    [Fact]
    public async Task Un_puerto_que_ya_no_responde_no_para_la_busqueda()
    {
        // El equipo apareció un día en COM3 a 115200 y dos días después en COM15 a 38400:
        // cambió el puerto y la velocidad a la vez. Lo guardado es una pista, no una certeza.
        var comprobacion = await AutodeteccionFt710.ComprobarAsync("COM_QUE_NO_EXISTE", 115200);

        comprobacion.Should().BeNull("si por ahí no contesta nadie, hay que volver a barrer");

        // Y las velocidades que se prueban incluyen la que apareció el tercer día.
        AutodeteccionFt710.Velocidades.Should().Contain(115200).And.Contain(38400);
    }

    [Fact]
    public void El_mapa_del_menu_trae_la_version_del_firmware()
    {
        MenuFt710.Procedencia!.Firmware.Should().Contain("01.12", "la unidad principal del equipo de Jose");
    }

    [Fact]
    public void Un_interrogante_no_prueba_que_la_orden_no_exista()
    {
        // Lección cara: «MT00;» contestaba «?;» y se dio por hecho que el equipo no sabía leer
        // memorias. Faltaba una cifra en el canal.
        OrdenesFt710.DiceQueNoLoAdmite("?").Should().BeTrue();
        OrdenesFt710.Sondeo.Should().Contain("MR001;", "el canal de memoria son tres cifras");
        OrdenesFt710.Sondeo.Should().NotContain("MT00;");
    }

    [Fact]
    public void El_audio_del_equipo_se_puede_preguntar_este_o_no_encendido()
    {
        // Hoy el equipo está apagado, así que lo normal es que no haya códec. Lo que se
        // comprueba es que preguntarlo no revienta y que las dos respuestas concuerdan.
        var encendido = AudioDelFt710.EquipoEncendido();
        var audio = AudioDelFt710.Buscar();

        encendido.Should().Be(audio is not null);
        AudioDelFt710.Describir(audio).Should().NotBeNullOrWhiteSpace();

        if (audio is not null)
        {
            audio.ContenedorUsb.Should().NotBe(Guid.Empty);
            foreach (var extremo in audio.Entradas.Concat(audio.Salidas))
            {
                extremo.Identificador.Should().StartWith("{0.0.");
            }
        }
    }

    [Fact]
    public void El_mapa_del_menu_dice_de_donde_sale()
    {
        var procedencia = MenuFt710.Procedencia;

        procedencia.Should().NotBeNull();
        procedencia!.LeidoDelEquipoReal.Should().BeTrue();
        procedencia.NingunaEntradaEsDeducida.Should().BeTrue();
        procedencia.FechaDeLectura.Should().Be("2026-09-21");
        procedencia.Identificador.Should().Be(ControlFt710.IdentificadorFt710);
        procedencia.Metodo.Should().Contain("SOLO LECTURA");
        procedencia.Cobertura.Should().Contain("completo", "el barrido completo del 22-09-2026 confirmó las mismas 296");
        procedencia.Entradas.Should().HaveCount(296);
    }

    [Fact]
    public async Task Un_interrogante_quita_el_mando_en_vez_de_ser_un_fallo()
    {
        var equipo = new Ft710DeMentira();
        await using var _ = equipo;

        // Como el equipo de verdad con las órdenes que no admite: contesta «?;».
        equipo.Responder("NR0;", "?;");

        var canal = new CanalTcpCat("127.0.0.1", equipo.Puerto, TimeSpan.FromMilliseconds(500));
        await using var control = new ControlFt710(canal, new OpcionesFt710
        {
            IntervaloDeSondeo = TimeSpan.FromSeconds(30),
        });
        await control.ConectarAsync();

        control.Mandos.Should().NotContain(MandoDeEquipo.ReductorDeRuido);
        control.Mandos.Should().Contain(MandoDeEquipo.Volumen);
        control.Rango(MandoDeEquipo.ReductorDeRuido).Should().BeNull();
        (await control.LeerMandoAsync(MandoDeEquipo.ReductorDeRuido)).Should().BeNull();

        var escribir = () => control.EscribirMandoAsync(MandoDeEquipo.ReductorDeRuido, 1);
        await escribir.Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public async Task Los_valores_se_enseñan_en_unidades_de_verdad()
    {
        var (equipo, control) = await MontarAsync();
        await using var _ = equipo;
        await using var __ = control;

        // KP40 es un índice de tono: son 700 Hz, no 40.
        (await control.LeerMandoAsync(MandoDeEquipo.TonoCw)).Should().Be(700d);
        control.Rango(MandoDeEquipo.TonoCw)!.Unidad.Should().Be("Hz");

        // PC100 sí son vatios, y KS020 palabras por minuto.
        (await control.LeerMandoAsync(MandoDeEquipo.Potencia)).Should().Be(100d);
        (await control.LeerMandoAsync(MandoDeEquipo.VelocidadKeyer)).Should().Be(20d);

        // CO011500 son hercios directos, eso sí está capturado.
        (await control.LeerMandoAsync(MandoDeEquipo.FrecuenciaDeContorno)).Should().Be(1500d);
        control.Rango(MandoDeEquipo.FrecuenciaDeContorno)!.Unidad.Should().Be("Hz");

        // BP01150: la muesca va en pasos de 10 Hz según el manual CAT, 1500 Hz.
        (await control.LeerMandoAsync(MandoDeEquipo.FrecuenciaDeMuesca)).Should().Be(1500d);
        control.Rango(MandoDeEquipo.FrecuenciaDeMuesca)!.Unidad.Should().Be("Hz");

        // AG0089 va de 0 a 255 tal cual.
        (await control.LeerMandoAsync(MandoDeEquipo.Volumen)).Should().Be(89d);
        control.Rango(MandoDeEquipo.Volumen)!.Maximo.Should().Be(255d);
    }

    [Fact]
    public async Task Al_accionar_un_mando_se_manda_el_numero_interno()
    {
        var (equipo, control) = await MontarAsync();
        await using var _ = equipo;
        await using var __ = control;

        await control.EscribirMandoAsync(MandoDeEquipo.TonoCw, 700);

        // Las órdenes de escritura no llevan respuesta, así que se espera a verla llegar.
        await equipo.EsperarOrdenAsync("KP40;", EsperaDeSenales.PlazoDeSeguridad);
        equipo.Recibidas.Should().Contain("KP40;");
    }

    [Fact]
    public async Task Un_valor_fuera_de_rango_se_ajusta_antes_de_salir_al_equipo()
    {
        var (equipo, control) = await MontarAsync();
        await using var _ = equipo;
        await using var __ = control;

        await control.EscribirMandoAsync(MandoDeEquipo.TonoCw, 5000);

        // El tope son 1050 Hz, que es el índice 75.
        await equipo.EsperarOrdenAsync("KP75;", EsperaDeSenales.PlazoDeSeguridad);
        equipo.Recibidas.Should().Contain("KP75;");
        equipo.Recibidas.Should().NotContain(orden => orden.StartsWith("KP4", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Una_frecuencia_fuera_de_banda_no_rompe_nada()
    {
        var (equipo, control) = await MontarAsync();
        await using var _ = equipo;
        await using var __ = control;

        var estado = await control.LeerEstadoAsync();

        // El dial de Jose estaba en 27.555 MHz, que no está en la tabla ADIF.
        estado.Frecuencia.Should().Be(Frecuencia.DesdeHercios(27_555_000));
        estado.Banda.EsVacia.Should().BeTrue("27.555 MHz no es ninguna banda de radioaficionado");
        estado.Conectado.Should().BeTrue();
        estado.Modo.NombreUsual.Should().Be("USB");
    }

    [Fact]
    public async Task Se_puede_poner_una_frecuencia_fuera_de_banda()
    {
        var (equipo, control) = await MontarAsync();
        await using var _ = equipo;
        await using var __ = control;

        await control.PonerFrecuenciaAsync(Frecuencia.DesdeHercios(27_555_000));

        equipo.Recibidas.Should().Contain("FA027555000;");
    }

    [Fact]
    public async Task La_valvula_de_escape_no_deja_pasar_ordenes_que_hacen_daño()
    {
        var (equipo, control) = await MontarAsync();
        await using var _ = equipo;
        await using var __ = control;

        var transmitir = () => control.OrdenEnCrudoAsync("TX1;");
        var apagar = () => control.OrdenEnCrudoAsync("PS0;");
        var escribirMemoria = () => control.OrdenEnCrudoAsync("MW001014250000;");
        var manipular = () => control.OrdenEnCrudoAsync("KY CQ;");

        await transmitir.Should().ThrowAsync<OrdenPeligrosaException>();
        await apagar.Should().ThrowAsync<OrdenPeligrosaException>();
        await escribirMemoria.Should().ThrowAsync<OrdenPeligrosaException>();
        await manipular.Should().ThrowAsync<OrdenPeligrosaException>();

        equipo.EnAntena.Should().BeFalse();
        equipo.Recibidas.Should().NotContain("TX1;");
    }

    [Fact]
    public async Task La_valvula_de_escape_sirve_para_lo_que_no_hace_daño()
    {
        var (equipo, control) = await MontarAsync();
        await using var _ = equipo;
        await using var __ = control;

        var respuesta = await control.OrdenEnCrudoAsync("FA");

        respuesta.Should().Be("FA027555000");
    }

    [Fact]
    public async Task El_ptt_no_se_puede_subir_sin_el_vigilante()
    {
        var (equipo, control) = await MontarAsync();
        await using var _ = equipo;
        await using var __ = control;

        var subir = () => ((IControlEquipo)control).PonerPttAsync(true);

        await subir.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*IVigilantePtt*");
        equipo.EnAntena.Should().BeFalse();
    }

    [Fact]
    public async Task Con_el_vigilante_se_sube_y_se_baja_el_ptt()
    {
        var (equipo, control) = await MontarAsync();
        await using var _ = equipo;
        await using var __ = control;
        await using var vigilante = new VigilantePtt(control, new OpcionesDelVigilante
        {
            TiempoMaximo = TimeSpan.FromSeconds(5),
            TiempoSinLatido = TimeSpan.FromSeconds(5),
            EngancharseAlCierreDelProceso = false,
            Seguridad = Dobles.SeguridadDePrueba.SinPlanNiRoe,
        });

        await using (await vigilante.PedirAntenaAsync("prueba contra el equipo de mentira"))
        {
            await equipo.EsperarAntenaAsync(enAntena: true, EsperaDeSenales.PlazoDeSeguridad);
            equipo.EnAntena.Should().BeTrue();
        }

        await equipo.EsperarAntenaAsync(enAntena: false, EsperaDeSenales.PlazoDeSeguridad);
        equipo.EnAntena.Should().BeFalse();
        equipo.Recibidas.Should().Contain("TX1;").And.Contain("TX0;");
    }

    [Fact]
    public async Task El_equipo_apagado_se_distingue_del_puerto_que_no_existe()
    {
        var equipo = new Ft710DeMentira(mudo: true);
        await using var _ = equipo;

        var canal = new CanalTcpCat("127.0.0.1", equipo.Puerto, TimeSpan.FromMilliseconds(300));
        await using var control = new ControlFt710(canal);

        var conectar = () => control.ConectarAsync();

        // El canal se abre —el cable está puesto— pero no contesta nadie: el equipo está apagado.
        await conectar.Should().ThrowAsync<EquipoNoContestaException>();
        control.Estado.Conectado.Should().BeFalse();
    }

    [Fact]
    public async Task Si_el_equipo_se_apaga_a_mitad_se_suelta_el_ptt()
    {
        var ajustes = new OpcionesFt710
        {
            IntervaloDeSondeo = TimeSpan.FromMilliseconds(50),
            EsperaDeOrden = TimeSpan.FromMilliseconds(500),
            EsperaDeReconexion = TimeSpan.FromMilliseconds(50),
        };

        var (equipo, control) = await MontarAsync(opciones: ajustes);
        await using var _ = equipo;
        await using var __ = control;
        await using var vigilante = new VigilantePtt(control, new OpcionesDelVigilante
        {
            TiempoMaximo = TimeSpan.FromSeconds(30),
            TiempoSinLatido = TimeSpan.FromSeconds(30),
            EsperaDeSuelta = TimeSpan.FromMilliseconds(400),
            EngancharseAlCierreDelProceso = false,
            Seguridad = Dobles.SeguridadDePrueba.SinPlanNiRoe,
        });

        var sueltas = new EsperaDeSueltas(vigilante);
        var transmision = await vigilante.PedirAntenaAsync("una transmisión que se queda sin equipo");
        await equipo.EsperarAntenaAsync(enAntena: true, EsperaDeSenales.PlazoDeSeguridad);

        // Jose apaga la radio en mitad de la transmisión.
        equipo.Enmudecer();

        // Se espera a la señal del vigilante, no a un plazo a ojo, y el plazo que se le da es
        // el que el propio código declara que tarda: la cota de detección más la de suelta.
        var plazo = ControlFt710.TiempoMaximoDeDeteccion(ajustes) + vigilante.PlazoDeSuelta;
        var motivo = await sueltas.PrimeraAsync(plazo);

        motivo.Should().Be(MotivoDeSuelta.EquipoPerdido);
        vigilante.EnAntena.Should().BeFalse("el vigilante no puede quedarse creyendo que sigue en antena");
        control.Estado.Conectado.Should().BeFalse();
        control.Estado.Transmitiendo.Should().BeFalse();
        transmision.EnAntena.Should().BeFalse();

        await transmision.DisposeAsync();
    }

    [Fact]
    public async Task Al_desconectar_se_baja_el_ptt_aunque_el_canal_este_roto()
    {
        var ajustes = new OpcionesFt710
        {
            IntervaloDeSondeo = TimeSpan.FromMilliseconds(50),
            EsperaDeOrden = TimeSpan.FromMilliseconds(500),
        };

        await using var equipo = new Ft710DeMentira();
        var canal = new CanalTcpCat("127.0.0.1", equipo.Puerto, ajustes.EsperaDeOrden);
        var control = new ControlFt710(canal, ajustes);
        await control.ConectarAsync();

        var vigilante = new VigilantePtt(control, new OpcionesDelVigilante
        {
            TiempoMaximo = TimeSpan.FromSeconds(30),
            TiempoSinLatido = TimeSpan.FromSeconds(30),
            EngancharseAlCierreDelProceso = false,
            Seguridad = Dobles.SeguridadDePrueba.SinPlanNiRoe,
        });

        var transmision = await vigilante.PedirAntenaAsync("prueba");
        await equipo.EsperarAntenaAsync(enAntena: true, EsperaDeSenales.PlazoDeSeguridad);

        // El canal se rompe justo antes de cerrar, que es lo que pasa de verdad cuando se
        // cancela el sondeo a mitad de una orden.
        canal.Cerrar();

        await control.DesconectarAsync();

        // Cerrar con el equipo en antena es lo peor que puede hacer este programa: si la vía
        // normal no está, hay que bajarlo por una de emergencia.
        await equipo.EsperarAntenaAsync(enAntena: false, EsperaDeSenales.PlazoDeSeguridad);
        equipo.EnAntena.Should().BeFalse();

        await transmision.DisposeAsync();
        await vigilante.DisposeAsync();
        await control.DisposeAsync();
    }

    [Fact]
    public async Task Cuando_el_equipo_vuelve_se_reconecta_solo()
    {
        var ajustes = new OpcionesFt710
        {
            IntervaloDeSondeo = TimeSpan.FromMilliseconds(50),
            EsperaDeOrden = TimeSpan.FromMilliseconds(500),
            EsperaDeReconexion = TimeSpan.FromMilliseconds(50),
        };

        var (equipo, control) = await MontarAsync(opciones: ajustes);
        await using var _ = equipo;
        await using var __ = control;
        var senales = new EsperaDeSenales(control);

        equipo.Enmudecer();
        await senales.PerdidaAsync();

        equipo.Despertar();

        // Se espera a que el control avise de que ha vuelto, no a que pase un rato. El plazo es
        // la cota que el propio control declara para reconectar, no un número a ojo.
        var canal = await senales.RecuperadaAsync(ControlFt710.TiempoMaximoDeReconexion(ajustes));

        canal.Should().NotBeNullOrWhiteSpace();
        control.Estado.Conectado.Should().BeTrue();
    }

    [Fact]
    public async Task El_menu_interno_se_lee_en_sus_tres_formas()
    {
        var (equipo, control) = await MontarAsync();
        await using var _ = equipo;
        await using var __ = control;

        var conSigno = await control.LeerDelMenuAsync("010101");
        var numero = await control.LeerDelMenuAsync("030101");
        var texto = await control.LeerDelMenuAsync("040101");

        conSigno!.Forma.Should().Be(FormaDeValorDeMenu.ConSigno);
        conSigno.Numero.Should().Be(0);

        numero!.Forma.Should().Be(FormaDeValorDeMenu.Numero);
        numero.Numero.Should().Be(20);

        texto!.Forma.Should().Be(FormaDeValorDeMenu.Texto);
        texto.Texto.Should().Be("FT-710");
    }

    [Fact]
    public async Task Del_menu_solo_se_lee_lo_que_el_equipo_tiene()
    {
        var (equipo, control) = await MontarAsync();
        await using var _ = equipo;
        await using var __ = control;

        // El grupo 05 y los siguientes contestan «?;» en este firmware.
        var inexistente = await control.LeerDelMenuAsync("050101");

        inexistente.Should().BeNull();
    }

    [Fact]
    public void El_mapa_del_menu_viaja_con_el_programa()
    {
        MenuFt710.Mapa.Should().NotBeEmpty("el mapa se sacó del equipo y va incrustado como recurso");
        MenuFt710.Mapa.Should().Contain(entrada => entrada.Indice == "040101" && entrada.Forma == FormaDeValorDeMenu.Texto);
        MenuFt710.Mapa.Should().Contain(entrada => entrada.Indice == "010101" && entrada.Forma == FormaDeValorDeMenu.ConSigno);
        MenuFt710.Mapa.Should().Contain(entrada => entrada.Forma == FormaDeValorDeMenu.Numero);
    }

    [Fact]
    public void La_lista_de_sondeo_no_tiene_ordenes_peligrosas()
    {
        // Ninguna orden que transmita, apague o cambie el equipo puede estar en el sondeo.
        foreach (var orden in OrdenesFt710.Sondeo)
        {
            orden.Should().NotStartWith("TX");
            orden.Should().NotStartWith("KY");
            orden.Should().NotStartWith("MW");
            orden.Should().NotStartWith("SV", "SV; no pregunta nada: intercambia los VFO");
            orden.Should().NotBe("PS0;");
        }
    }

    [Fact]
    public void El_medidor_s_se_reparte_de_forma_razonable()
    {
        var control = new ControlFt710(
            new CanalTcpCat("127.0.0.1", 1, TimeSpan.FromMilliseconds(10)),
            new OpcionesFt710 { LecturaDeS9 = 128 });

        control.AUnidadesS(0).Should().Be(0d);
        control.AUnidadesS(128).Should().Be(9d);
        control.AUnidadesS(255).Should().BeApproximately(19d, 0.01);
        control.AUnidadesS(64).Should().BeApproximately(4.5d, 0.01);
    }

    [Fact]
    public void Los_modos_del_equipo_se_traducen_a_adif()
    {
        ModosFt710.DesdeElEquipo('2').Should().Be("USB");
        ModosFt710.DesdeElEquipo('C').Should().Be("PKTUSB");
        ModosFt710.DesdeElEquipo('Z').Should().BeNull();
        ModosFt710.AlEquipo("USB").Should().Be('2');
        ModosFt710.AlEquipo("mandanga").Should().BeNull();
    }

    [Fact]
    public void El_equipo_encontrado_se_describe_por_su_identificador()
    {
        var ft710 = new EquipoEncontrado("COM3", 115200, "0800");
        var otro = new EquipoEncontrado("COM9", 4800, "0670");

        ft710.EsFt710.Should().BeTrue();
        ft710.Descripcion.Should().Contain("FT-710").And.Contain("COM3");
        otro.EsFt710.Should().BeFalse();
        otro.Descripcion.Should().Contain("ID0670");
    }
}
