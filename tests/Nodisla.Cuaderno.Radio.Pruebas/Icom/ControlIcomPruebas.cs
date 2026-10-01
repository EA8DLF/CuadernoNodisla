using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Radio.Control;
using Nodisla.Cuaderno.Radio.Control.Ft710;
using Nodisla.Cuaderno.Radio.Control.Icom;
using Nodisla.Cuaderno.Radio.Modelos;

namespace Nodisla.Cuaderno.Radio.Pruebas.Icom;

/// <summary>
/// El control CI-V de ICOM contra una radio de mentira que contesta como los manuales. No hay
/// radios ICOM: esto prueba el protocolo y los botones, no la radio real.
/// </summary>
public class ControlIcomPruebas
{
    public static TheoryData<string> Claves() => new(ModelosIcom.Todos.Select(p => p.Modelo.Clave));

    private static PerfilIcom Perfil(string clave) => ModelosIcom.Todos.Single(p => p.Modelo.Clave == clave);

    private static async Task<(IcomDeMentira Radio, CanalCivDeMentira Canal, ControlIcom Control)> MontarAsync(string clave = "icom-ic7300", bool eco = true)
    {
        var radio = new IcomDeMentira(Perfil(clave)) { ConEco = eco };
        var canal = new CanalCivDeMentira(radio);
        var opciones = new OpcionesFt710
        {
            // Sin sondeo durante la prueba: cada paso se lee a mano.
            IntervaloDeSondeo = TimeSpan.FromHours(1),
            EsperaDeOrden = TimeSpan.FromMilliseconds(300),
            Esperar = (_, _) => Task.CompletedTask,
        };
        var control = new ControlIcom(canal, radio.Perfil, opciones);
        await control.ConectarAsync();
        radio.Olvidar();
        return (radio, canal, control);
    }

    // ── Tramas ───────────────────────────────────────────────────────────────

    [Fact]
    public void La_frecuencia_va_en_bcd_al_reves_en_cinco_bytes()
    {
        Hex.De(BcdCiv.Frecuencia(14_074_000)).Should().Be("00 40 07 14 00");
        Hex.De(BcdCiv.Frecuencia(1_296_200_000)).Should().Be("00 00 20 96 12");
        BcdCiv.Hercios(Hex.ABytes("00 40 07 14 00")).Should().Be(14_074_000);
        Hex.De(BcdCiv.Numero(255, 2)).Should().Be("02 55");
        BcdCiv.Numero(Hex.ABytes("01 28")).Should().Be(128);
    }

    [Fact]
    public void El_analizador_corta_tramas_partidas_y_descarta_colisiones()
    {
        var analizador = new AnalizadorDeTramasCiv();
        analizador.Anadir(Hex.ABytes("FE FE E0 94")).Should().BeEmpty();
        var tramas = analizador.Anadir(Hex.ABytes("FB FD FE FE FC FC FD FE FE FE E0 94 03 00 40 07 14 00 FD"));
        tramas.Should().HaveCount(2);
        tramas[0].EsBien.Should().BeTrue();
        tramas[1].Cuerpo.Should().Equal(Hex.ABytes("03 00 40 07 14 00"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task El_eco_del_bus_no_se_toma_por_respuesta(bool eco)
    {
        var (_, _, control) = await MontarAsync(eco: eco);
        control.Estado.Frecuencia.Should().Be(Frecuencia.DesdeHercios(14_074_000));
        control.Estado.Conectado.Should().BeTrue();
    }

    // ── Conexion por modelo ──────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(Claves))]
    public async Task Cada_modelo_conecta_lee_estado_y_ensena_solo_sus_mandos(string clave)
    {
        var (radio, _, control) = await MontarAsync(clave);
        var perfil = radio.Perfil;

        control.Modelo.Clave.Should().Be(clave);
        control.Modelo.ProbadoConRadio.Should().BeFalse("no hay radios ICOM: todo es según el manual");
        control.Estado.Modo.Should().Be(Modo.Parse("FT8"), "USB con datos (USB-D) se apunta como datos");
        control.Mandos.Should().Contain([MandoDeEquipo.Volumen, MandoDeEquipo.Potencia, MandoDeEquipo.Atenuador, MandoDeEquipo.Agc, MandoDeEquipo.Split, MandoDeEquipo.Rit]);
        control.Mandos.Contains(MandoDeEquipo.Xit).Should().Be(perfil.Xit);
        control.Mandos.Contains(MandoDeEquipo.Sintonizador).Should().Be(perfil.Sintonizador);
        control.Mandos.Contains(MandoDeEquipo.Apf).Should().Be(perfil.Apf);
        control.Rango(MandoDeEquipo.Atenuador)!.Etiquetas.Should().HaveCount(perfil.Atenuadores.Count);
        control.Rango(MandoDeEquipo.Potencia)!.Maximo.Should().Be(perfil.PotenciaMaxima);
        radio.HaTransmitido.Should().BeFalse();
    }

    [Fact]
    public async Task Los_mandos_que_contestan_FA_se_quedan_fuera()
    {
        var radio = new IcomDeMentira(Perfil("icom-ic7300"));
        var canal = new CanalCivDeMentira(radio);
        await using var control = new ControlIcom(canal, radio.Perfil, new OpcionesFt710 { IntervaloDeSondeo = TimeSpan.FromHours(1) });

        // Esta radio no contesta a 16 50 (bloqueo del dial).
        radio.NoAdmitir(0x16, 0x50);
        await control.ConectarAsync();
        control.Mandos.Should().NotContain(MandoDeEquipo.Bloqueo);
        control.Rango(MandoDeEquipo.Bloqueo).Should().BeNull();
    }

    [Fact]
    public async Task El_ic7610_reparte_A_en_MAIN_y_B_en_SUB()
    {
        var (radio, _, control) = await MontarAsync("icom-ic7610");
        radio.SecundarioElegido = true;
        var vfos = await control.LeerVfosAsync();
        vfos.A.Frecuencia.Should().Be(Frecuencia.DesdeHercios(14_074_000));
        vfos.B.Frecuencia.Should().Be(Frecuencia.DesdeHercios(7_074_000));
        vfos.B.EsElActivo.Should().BeTrue();
        control.Estado.Vfo.Should().Be("SUB");
    }

    [Fact]
    public async Task Con_split_se_apunta_la_frecuencia_de_transmision()
    {
        var (radio, _, control) = await MontarAsync();
        radio.Split = true;
        var estado = await control.LeerEstadoAsync();
        estado.Frecuencia.Should().Be(Frecuencia.DesdeHercios(7_074_000));
        estado.FrecuenciaRx.Should().Be(Frecuencia.DesdeHercios(14_074_000));
        control.Vfos.B.Transmite.Should().BeTrue();
    }

    // ── Transceive ───────────────────────────────────────────────────────────

    [Fact]
    public async Task El_aviso_transceive_mueve_la_frecuencia_sin_esperar_al_sondeo()
    {
        var (_, canal, control) = await MontarAsync();
        List<EstadoDelEquipo> avisos = [];
        control.EstadoCambiado += (_, e) => avisos.Add(e);

        canal.Transceive([0x00, .. BcdCiv.Frecuencia(14_075_500)]);
        canal.Transceive([0x01, 0x03, 0x01]);

        control.Estado.Frecuencia.Should().Be(Frecuencia.DesdeHercios(14_075_500));
        control.Estado.Modo.Should().Be(Modo.Parse("CW"));
        avisos.Should().HaveCount(2);
    }

    // ── Cada boton, su trama ─────────────────────────────────────────────────

    public static TheoryData<string, string, string> Botones() => new()
    {
        { "icom-ic7300", "frecuencia", "05 00 50 07 14 00" },
        { "icom-ic7300", "modo-usb", "06 01|1A 06 00 00" },
        { "icom-ic7300", "tecla-datos", "06 01|1A 06 01 01" },
        { "icom-ic7300", "tecla-cw", "06 03|1A 06 00 00" },
        { "icom-ic7300", "potencia-50", "14 0A 01 28" },
        { "icom-ic7300", "volumen-100", "14 01 02 55" },
        { "icom-ic7300", "atenuador-1", "11 20" },
        { "icom-ic7610", "atenuador-4", "11 12" },
        { "icom-ic7300", "preamp-2", "16 02 02" },
        { "icom-ic7300", "agc-rapido", "16 12 01" },
        { "icom-ic7300", "nb", "16 22 01" },
        { "icom-ic7300", "nr", "16 40 01" },
        { "icom-ic7300", "anf", "16 41 01" },
        { "icom-ic7300", "muesca", "16 48 01" },
        { "icom-ic7300", "breakin-total", "16 47 02" },
        { "icom-ic7300", "bloqueo", "16 50 01" },
        { "icom-ic7610", "apf-medio", "16 32 02" },
        { "icom-ic7300", "filtro-10", "1A 03 10" },
        { "icom-ic7300", "split", "0F 01" },
        { "icom-ic7300", "rit", "21 01 01" },
        { "icom-ic7300", "xit", "21 02 01" },
        { "icom-ic7300", "rit-menos-150", "21 00 50 01 01" },
        { "icom-ic7300", "acoplador-en-linea", "1C 01 01" },
        { "icom-ic7300", "vfo-b", "07 01" },
        { "icom-ic7610", "vfo-b", "07 D1" },
        { "icom-ic7300", "intercambiar", "07 B0" },
        { "icom-ic7300", "igualar", "07 A0" },
        { "icom-ic7300", "frecuencia-de-b", "25 01 00 50 07 14 00" },
        { "icom-ic7610", "frecuencia-de-b", "25 01 00 50 07 14 00" },
        { "icom-ic7300", "modo-de-b", "26 01 03 00 01" },
        { "icom-ic7300", "m-a-vfo", "0A" },
        { "icom-ic7300", "vfo-o-memoria", "08" },
        { "icom-ic7300", "borrar-clarificador", "21 00 00 00 00" },
        { "icom-ic7300", "memoria-12", "08 00 12" },
        { "icom-ic7300", "banda-40", "1A 01 03 01|05 00 00 05 07 00|06 03 02|1A 06 00 00" },
        { "icom-ic7100", "banda-40", "05 00 00 10 07 00|06 00|1A 06 00 00" },
        { "icom-ic9700", "banda-40", "1A 01 02 01|05 00 00 30 32 04|06 01 01|1A 06 00 00" },
    };

    [Theory]
    [MemberData(nameof(Botones))]
    public async Task Cada_boton_manda_su_trama_y_ninguno_transmite(string clave, string boton, string esperado)
    {
        var (radio, _, control) = await MontarAsync(clave);
        radio.Pila[0x03] = (7_050_000, 0x03, 0x02, false);
        radio.Pila[0x02] = (432_300_000, 0x01, 0x01, false);

        Task accion = boton switch
        {
            "frecuencia" => control.PonerFrecuenciaAsync(Frecuencia.DesdeHercios(14_075_000)),
            "modo-usb" => control.PonerModoAsync(Modo.Parse("SSB", "USB")),
            "tecla-datos" => control.PonerModoDeTeclaAsync(TeclaDeModo.Datos),
            "tecla-cw" => control.PonerModoDeTeclaAsync(TeclaDeModo.Cw),
            "potencia-50" => control.EscribirMandoAsync(MandoDeEquipo.Potencia, 50),
            "volumen-100" => control.EscribirMandoAsync(MandoDeEquipo.Volumen, 100),
            "atenuador-1" => control.EscribirMandoAsync(MandoDeEquipo.Atenuador, 1),
            "atenuador-4" => control.EscribirMandoAsync(MandoDeEquipo.Atenuador, 4),
            "preamp-2" => control.EscribirMandoAsync(MandoDeEquipo.Preamplificador, 2),
            "agc-rapido" => control.EscribirMandoAsync(MandoDeEquipo.Agc, 0),
            "nb" => control.EscribirMandoAsync(MandoDeEquipo.SupresorDeRuido, 1),
            "nr" => control.EscribirMandoAsync(MandoDeEquipo.ReductorDeRuido, 1),
            "anf" => control.EscribirMandoAsync(MandoDeEquipo.MuescaAutomatica, 1),
            "muesca" => control.EscribirMandoAsync(MandoDeEquipo.MuescaManual, 1),
            "breakin-total" => control.EscribirMandoAsync(MandoDeEquipo.BreakIn, 2),
            "bloqueo" => control.EscribirMandoAsync(MandoDeEquipo.Bloqueo, 1),
            "apf-medio" => control.EscribirMandoAsync(MandoDeEquipo.Apf, 2),
            "filtro-10" => control.EscribirMandoAsync(MandoDeEquipo.AnchoDeFiltro, 10),
            "split" => control.EscribirMandoAsync(MandoDeEquipo.Split, 1),
            "rit" => control.EscribirMandoAsync(MandoDeEquipo.Rit, 1),
            "xit" => control.EscribirMandoAsync(MandoDeEquipo.Xit, 1),
            "rit-menos-150" => control.EscribirMandoAsync(MandoDeEquipo.DesplazamientoRit, -150),
            "acoplador-en-linea" => control.EscribirMandoAsync(MandoDeEquipo.Sintonizador, 1),
            "vfo-b" => control.PonerVfoActivoAsync(NombreDeVfo.B),
            "intercambiar" => control.IntercambiarVfosAsync(),
            "igualar" => control.IgualarVfosAsync(),
            "frecuencia-de-b" => control.PonerFrecuenciaDeAsync(NombreDeVfo.B, Frecuencia.DesdeHercios(14_075_000)),
            "modo-de-b" => control.PonerModoDeAsync(NombreDeVfo.B, Modo.Parse("CW")),
            "m-a-vfo" => control.PulsarAsync(TeclaDelEquipo.MemoriaAVfo),
            "vfo-o-memoria" => control.PulsarAsync(TeclaDelEquipo.VfoOMemoria),
            "borrar-clarificador" => control.PulsarAsync(TeclaDelEquipo.BorrarClarificador),
            "memoria-12" => control.IrAMemoriaAsync(12),
            "banda-40" => control.IrABandaAsync(control.TeclasDeBanda.First(t => t.Rotulo is "7" or "430")),
            _ => throw new ArgumentOutOfRangeException(nameof(boton)),
        };
        await accion;

        // Las tramas esperadas salen en ese orden; entre medias solo hay lecturas (sin datos o de estado).
        var escritas = radio.RecibidasEnHex.ToList();
        var posicion = 0;
        foreach (var trama in esperado.Split('|'))
        {
            var encontrada = escritas.IndexOf(trama, posicion);
            encontrada.Should().BeGreaterThanOrEqualTo(0, $"«{boton}» debe mandar {trama}; mandó: {string.Join(" / ", escritas)}");
            posicion = encontrada + 1;
        }

        radio.HaTransmitido.Should().BeFalse($"«{boton}» no puede poner la radio en antena");
        escritas.Should().NotContain(t => t.StartsWith("1C 00 01") || t.StartsWith("1C 01 02") || t.StartsWith("18 00"));
    }

    // ── PTT y sintonia ───────────────────────────────────────────────────────

    [Fact]
    public async Task Subir_el_ptt_por_fuera_del_vigilante_se_rechaza_y_no_transmite()
    {
        var (radio, _, control) = await MontarAsync();
        var subir = () => ((IControlEquipo)control).PonerPttAsync(true);
        await subir.Should().ThrowAsync<InvalidOperationException>();
        radio.HaTransmitido.Should().BeFalse();

        await ((IControlEquipo)control).PonerPttAsync(false);
        radio.RecibidasEnHex.Should().Contain("1C 00 00");
    }

    [Fact]
    public async Task El_ptt_del_vigilante_manda_1C_00_01_y_lo_baja_con_1C_00_00()
    {
        var (radio, _, control) = await MontarAsync();
        IPttDirecto directo = control;
        await directo.PonerPttDirectoAsync(true, CancellationToken.None);
        radio.EnAntena.Should().BeTrue();
        control.Estado.Transmitiendo.Should().BeTrue();

        await directo.PonerPttDirectoAsync(false, CancellationToken.None);
        radio.EnAntena.Should().BeFalse();
        radio.RecibidasEnHex.Should().ContainInOrder("1C 00 01", "1C 00 00");
    }

    [Fact]
    public async Task El_tune_solo_emite_dentro_del_vigilante()
    {
        var (radio, _, control) = await MontarAsync();
        var sinVigilante = () => control.EscribirMandoAsync(MandoDeEquipo.Sintonizador, 2);
        await sinVigilante.Should().ThrowAsync<InvalidOperationException>();
        radio.HaTransmitido.Should().BeFalse();

        control.Should().BeAssignableTo<IEquipoConSintonia>().Which.VeElFinDeLaSintonia.Should().BeTrue();
        control.PrepararSintonia();
        await ((IPttDirecto)control).PonerPttDirectoAsync(true, CancellationToken.None);
        radio.Acoplador.Should().Be(0x02);
        radio.Acoplador = 0x01; // la radio termina
        (await control.EsperarFinDeSintoniaAsync(() => { }, TimeSpan.FromSeconds(1))).Should().BeTrue();
        await ((IPttDirecto)control).PonerPttDirectoAsync(false, CancellationToken.None);
        radio.RecibidasEnHex.Should().ContainInOrder("1C 01 02", "1C 00 00");
    }

    [Fact]
    public async Task Las_vias_de_suelta_bajan_el_ptt_por_ci_v()
    {
        var (radio, _, control) = await MontarAsync();
        control.ViasDeSuelta.Should().HaveCount(2);
        await control.ViasDeSuelta[0].SoltarAsync(CancellationToken.None);
        control.ViasDeSuelta[0].SoltarSincrono!();
        radio.RecibidasEnHex.Should().OnlyContain(t => t == "1C 00 00");
    }

    // ── Seguridad ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("1C 00 01")]
    [InlineData("FE FE 94 E0 1C 00 01 FD")]
    [InlineData("1C 01 02")]
    [InlineData("18 00")]
    [InlineData("09")]
    [InlineData("0B")]
    [InlineData("17 43 51")]
    [InlineData("28 00 01")]
    [InlineData("1A 00 00 01 00 00 40 07 14 00")]
    [InlineData("1A 05 00 71 00")]
    public async Task La_orden_en_crudo_rechaza_lo_peligroso(string orden)
    {
        var (radio, _, control) = await MontarAsync();
        var mandar = () => control.OrdenEnCrudoAsync(orden);
        await mandar.Should().ThrowAsync<OrdenPeligrosaException>();
        radio.Recibidas.Should().BeEmpty();
    }

    [Fact]
    public async Task La_orden_en_crudo_deja_leer()
    {
        var (_, _, control) = await MontarAsync();
        (await control.OrdenEnCrudoAsync("FE FE 94 E0 03 FD")).Should().Be("03 00 40 07 14 00");
        (await control.OrdenEnCrudoAsync("1C 00")).Should().Be("1C 00 00");
    }

    [Fact]
    public async Task El_canal_no_deja_transmitir_ni_apagar_fuera_de_su_ambito()
    {
        var (radio, canal, _) = await MontarAsync();
        var ptt = () => canal.PreguntarAsync([0x1C, 0x00, 0x01]);
        var apagar = () => canal.PreguntarAsync([0x18, 0x00]);
        await ptt.Should().ThrowAsync<OrdenPeligrosaException>();
        await apagar.Should().ThrowAsync<OrdenPeligrosaException>();
        radio.Recibidas.Should().BeEmpty();
    }

    [Fact]
    public async Task Apagar_baja_el_ptt_y_manda_18_00()
    {
        var (radio, _, control) = await MontarAsync();
        await control.ApagarAsync();
        radio.Apagada.Should().BeTrue();
        radio.RecibidasEnHex.Should().ContainInOrder("1C 00 00", "18 00");
        radio.HaTransmitido.Should().BeFalse();
    }

    [Fact]
    public async Task Encender_manda_los_FE_del_manual_y_18_01()
    {
        var (_, canal, control) = await MontarAsync();
        await control.DesconectarAsync();
        canal.Escrito.Clear();
        (await control.EncenderAsync()).Should().BeNull();
        var despertar = canal.Escrito.First();
        despertar.TakeWhile(b => b == 0xFE).Count().Should().Be(150 + 2);
        Hex.De(despertar[^7..]).Should().Be("FE FE 94 E0 18 01 FD");
    }

    // ── Medidores y memorias ─────────────────────────────────────────────────

    [Fact]
    public async Task Los_medidores_se_pasan_a_unidades()
    {
        var (_, _, control) = await MontarAsync();
        var m = await control.LeerMedidoresAsync();
        m.UnidadesS.Should().Be(9);
        m.PotenciaVatios.Should().BeApproximately(100, 0.01);
        m.Roe.Should().BeApproximately(1.5, 0.01);
        m.TensionVoltios.Should().BeApproximately(10, 0.01);
        m.CorrienteAmperios.Should().BeApproximately(10, 0.01);
    }

    [Fact]
    public async Task Las_memorias_se_leen_sin_escribir_nada()
    {
        var (radio, _, control) = await MontarAsync();
        radio.Memorias[1] = (7_074_000, 0x01, "FT8 40");
        var memorias = await control.LeerMemoriasAsync();
        memorias.Should().HaveCount(99);
        memorias[0].Ocupada.Should().BeTrue();
        memorias[0].Frecuencia.Should().Be(Frecuencia.DesdeHercios(7_074_000));
        memorias[0].Etiqueta.Should().Be("FT8 40");
        memorias[1].Ocupada.Should().BeFalse();
        radio.RecibidasEnHex.Should().OnlyContain(t => t.StartsWith("1A 00 ") && t.Length == 11);
    }

    // ── Proveedor ────────────────────────────────────────────────────────────

    [Fact]
    public void El_catalogo_encuentra_los_icom_por_reflexion()
    {
        var claves = CatalogoDeModelos.De(Fabricante.Icom).Select(m => m.Clave);
        claves.Should().Contain(["icom-ic7300", "icom-ic705", "icom-ic7610", "icom-ic9700", "icom-ic7100", "icom-ic7851"]);
        CatalogoDeModelos.PorDireccionCiv(0x94)!.Nombre.Should().Be("IC-7300");
        CatalogoDeModelos.PorDireccionCiv(0xA4)!.Nombre.Should().Be("IC-705");
        CatalogoDeModelos.PorDireccionCiv(0x98)!.Nombre.Should().Be("IC-7610");
        CatalogoDeModelos.PorDireccionCiv(0xA2)!.Nombre.Should().Be("IC-9700");
        CatalogoDeModelos.PorDireccionCiv(0x88)!.Nombre.Should().Be("IC-7100");
        CatalogoDeModelos.PorDireccionCiv(0x8E)!.Nombre.Should().Be("IC-7851");
        CatalogoDeModelos.Todos.Where(m => m.Fabricante == Fabricante.Icom).Should().OnlyContain(m => !m.ProbadoConRadio);
    }

    [Fact]
    public void El_proveedor_crea_un_control_sin_conectar_con_la_direccion_del_operador()
    {
        var proveedor = new ProveedorIcom();
        var modelo = proveedor.Modelos.Single(m => m.Clave == "icom-ic7300");
        var control = proveedor.Crear(modelo, "COM99", 115200, new OpcionesDeRadio { DireccionCiv = 0x70 }, null);
        control.Should().BeOfType<ControlIcom>();
        control.Estado.Conectado.Should().BeFalse();
        ((IEquipoDeModelo)control).Modelo.Should().Be(modelo);
    }

    [Theory]
    [InlineData((byte)0x94)]
    [InlineData((byte)0xA2)]
    public async Task Identificar_pregunta_19_00_y_devuelve_la_direccion(byte direccion)
    {
        var radio = new IcomDeMentira(ModelosIcom.Todos.Single(p => p.Direccion == direccion));
        var hallada = await ProveedorIcom.IdentificarAsync(d => new CanalCivDeMentira(radio, d), null, null, CancellationToken.None);
        hallada.Should().Be(direccion);
        radio.RecibidasEnHex.Should().OnlyContain(t => t == "19 00");
        radio.HaTransmitido.Should().BeFalse();
    }

    [Fact]
    public async Task Identificar_devuelve_nulo_si_nadie_contesta()
    {
        var radio = new IcomDeMentira(Perfil("icom-ic7300")) { Direccion = 0x42 };

        // Esta radio no contesta a la difusion (solo a su direccion 42, que no es de fabrica).
        var hallada = await ProveedorIcom.IdentificarAsync(
            d => new CanalCivDeMentira(radio, d == 0x00 ? (byte)0x43 : d), null, null, CancellationToken.None);
        hallada.Should().BeNull();
    }
}
