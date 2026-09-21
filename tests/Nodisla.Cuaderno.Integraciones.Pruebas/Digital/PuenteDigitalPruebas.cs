using System.Net;
using System.Net.Sockets;
using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Integraciones.Digital;

namespace Nodisla.Cuaderno.Integraciones.Pruebas.Digital;

/// <summary>El puente entero: instancias, dialectos, capacidades y el socket de verdad.</summary>
public class PuenteDigitalPruebas
{
    private static readonly DateTimeOffset Ahora = new(2026, 9, 21, 10, 30, 0, TimeSpan.Zero);

    private static readonly TimeSpan Periodo = new(10, 30, 15);

    [Fact]
    public void Dos_instancias_a_la_vez_no_se_mezclan()
    {
        var receptor = new ReceptorWsjt();
        var estados = new List<EstadoDigital>();
        receptor.EstadoRecibido += (_, e) => estados.Add(e);

        receptor.Recibir(Datagramas.Latido("WSJT-X"), null, Ahora);
        receptor.Recibir(Datagramas.Latido("JTDX"), null, Ahora);
        receptor.Recibir(Datagramas.EstadoWsjtX(), null, Ahora);
        receptor.Recibir(Datagramas.EstadoJtdx(), null, Ahora);

        receptor.Instancias.Should().HaveCount(2);

        receptor.TryInstancia("WSJT-X", out var wsjt).Should().BeTrue();
        receptor.TryInstancia("JTDX", out var jtdx).Should().BeTrue();

        wsjt.Dialecto.Should().Be(DialectoDigital.WsjtX);
        jtdx.Dialecto.Should().Be(DialectoDigital.Jtdx);
        wsjt.Estado.Frecuencia.Megahercios.Should().Be(14.074m);
        jtdx.Estado.Frecuencia.Megahercios.Should().Be(7.074m);
        wsjt.Estado.Llamado.Valor.Should().Be("K1ABC");
        jtdx.Estado.Llamado.Valor.Should().Be("W9XYZ");
        estados.Should().HaveCount(2);
    }

    [Fact]
    public void El_dialecto_se_deduce_del_identificador()
    {
        DeteccionDeDialecto.PorIdentificador("WSJT-X").Should().Be(DialectoDigital.WsjtX);
        DeteccionDeDialecto.PorIdentificador("JTDX").Should().Be(DialectoDigital.Jtdx);
        DeteccionDeDialecto.PorIdentificador("MSHV").Should().Be(DialectoDigital.Mshv);
        DeteccionDeDialecto.PorIdentificador("JS8Call").Should().Be(DialectoDigital.Js8Call);
        DeteccionDeDialecto.PorIdentificador("otra cosa").Should().Be(DialectoDigital.Desconocido);
        DeteccionDeDialecto.PorIdentificador(null).Should().Be(DialectoDigital.Desconocido);
    }

    [Fact]
    public void Una_instancia_sin_nombre_reconocible_se_delata_por_su_mensaje_de_estado()
    {
        var receptor = new ReceptorWsjt();

        receptor.Recibir(Datagramas.EstadoJtdx("Radio del salon"), null, Ahora);

        receptor.TryInstancia("Radio del salon", out var instancia).Should().BeTrue();
        instancia.Dialecto.Should().Be(DialectoDigital.Jtdx);
    }

    [Fact]
    public void El_mensaje_50_delata_a_jtdx_aunque_el_nombre_no_diga_nada()
    {
        var receptor = new ReceptorWsjt();

        receptor.Recibir(Datagramas.FijarTxDeltaFreq("equipo 2", 1500), null, Ahora);

        receptor.TryInstancia("equipo 2", out var instancia).Should().BeTrue();
        instancia.Dialecto.Should().Be(DialectoDigital.Jtdx);
    }

    [Fact]
    public void Jtdx_no_resalta_indicativos_y_wsjtx_si()
    {
        DeteccionDeDialecto.De(DialectoDigital.WsjtX).PuedeResaltar.Should().BeTrue();
        DeteccionDeDialecto.De(DialectoDigital.Jtdx).PuedeResaltar.Should().BeFalse();
        DeteccionDeDialecto.De(DialectoDigital.Jtdx).PuedeCambiarTonoTx.Should().BeTrue();
        DeteccionDeDialecto.De(DialectoDigital.WsjtX).PuedeCambiarTonoTx.Should().BeFalse();
    }

    [Fact]
    public void Mshv_saca_varias_decodificaciones_en_el_mismo_ciclo()
    {
        var receptor = new ReceptorWsjt();
        var decodificaciones = new List<DecodificacionDigital>();
        receptor.Decodificado += (_, d) => decodificaciones.Add(d);

        var periodo = new TimeSpan(10, 30, 15);
        string[] mensajes =
        [
            "CQ EA8DLF IL18",
            "CQ DX K1ABC FN42",
            "EA8DLF W9XYZ -12",
            "W9XYZ EA8DLF R-09",
            "K1ABC LZ2HV KN12",
        ];

        var tono = 500u;
        foreach (var mensaje in mensajes)
        {
            receptor.Recibir(Datagramas.Decodificacion("MSHV", mensaje, periodo, delta: tono), null, Ahora);
            tono += 120;
        }

        decodificaciones.Should().HaveCount(5);
        decodificaciones.Should().OnlyContain(d => d.Dialecto == DialectoDigital.Mshv);
        decodificaciones.Select(d => d.InstanteUtc).Distinct().Should().ContainSingle(
            "todas son del mismo periodo de transmision");
        decodificaciones.Select(d => d.TonoHz).Should().OnlyHaveUniqueItems();
        decodificaciones[0].EsCq.Should().BeTrue();
        decodificaciones[0].Llamante.Valor.Should().Be("EA8DLF");
        decodificaciones[2].Llamado.Valor.Should().Be("EA8DLF");
    }

    [Fact]
    public void Mshv_cierra_varios_contactos_en_el_mismo_ciclo()
    {
        var receptor = new ReceptorWsjt();
        var qsos = new List<Qso>();
        receptor.QsoRegistrado += (_, q) => qsos.Add(q);

        receptor.Recibir(Datagramas.QsoWsjtX("MSHV", "K1ABC"), null, Ahora);
        receptor.Recibir(Datagramas.QsoWsjtX("MSHV", "W9XYZ"), null, Ahora);
        receptor.Recibir(Datagramas.QsoWsjtX("MSHV", "LZ2HV"), null, Ahora);

        qsos.Should().HaveCount(3);
        qsos.Select(q => q.Call.Valor).Should().Equal("K1ABC", "W9XYZ", "LZ2HV");
        qsos.Should().OnlyContain(q => q.Origen == "MSHV");
    }

    [Fact]
    public void El_contacto_llega_armado_y_listo_para_guardarlo()
    {
        var receptor = new ReceptorWsjt();
        Qso? guardado = null;
        receptor.QsoRegistrado += (_, q) => guardado = q;

        receptor.Recibir(Datagramas.Latido("WSJT-X"), null, Ahora);
        receptor.Recibir(Datagramas.QsoWsjtX(), null, Ahora);

        guardado.Should().NotBeNull();
        guardado!.Call.Valor.Should().Be("K1ABC");
        guardado.Band.Nombre.Should().Be("20m");
        guardado.Freq.Megahercios.Should().Be(14.075m);
        guardado.Mode.Principal.Should().Be("FT8", "en ADIF FT8 es modo principal");
        guardado.Mode.Submodo.Should().BeNull();
        guardado.RstSent.Texto.Should().Be("-15");
        guardado.RstRcvd.Texto.Should().Be("+03");
        guardado.RstSent.Decibelios.Should().Be(-15);
        guardado.Gridsquare.Valor.Should().Be("FN42");
        guardado.MyGridsquare.Valor.Should().Be("IL18");
        guardado.StationCallsign.Valor.Should().Be("EA8DLF");
        guardado.TxPwr.Should().Be(25);
        guardado.StxString.Should().Be("001");
        guardado.InicioUtc.Offset.Should().Be(TimeSpan.Zero);
        guardado.FinUtc!.Value.Offset.Should().Be(TimeSpan.Zero);
        guardado.Origen.Should().Be("WSJT-X");
    }

    [Fact]
    public void Ft4_se_guarda_como_submodo_de_mfsk()
    {
        var receptor = new ReceptorWsjt();
        Qso? guardado = null;
        receptor.QsoRegistrado += (_, q) => guardado = q;

        receptor.Recibir(Datagramas.QsoJtdx(), null, Ahora);

        guardado!.Mode.Principal.Should().Be("MFSK");
        guardado.Mode.Submodo.Should().Be("FT4");
        guardado.Band.Nombre.Should().Be("40m");
        guardado.Origen.Should().Be("JTDX");
    }

    [Fact]
    public void Una_instancia_que_se_cierra_se_da_por_perdida()
    {
        var receptor = new ReceptorWsjt();
        var perdidas = new List<string>();
        receptor.InstanciaPerdida += (_, i) => perdidas.Add(i);

        receptor.Recibir(Datagramas.Latido("WSJT-X"), null, Ahora);
        receptor.Recibir(Datagramas.Cierre("WSJT-X"), null, Ahora);

        perdidas.Should().Equal("WSJT-X");
        receptor.Instancias.Should().BeEmpty();
    }

    [Fact]
    public void Una_instancia_que_calla_demasiado_tiempo_se_da_por_perdida()
    {
        var receptor = new ReceptorWsjt();
        var perdidas = new List<string>();
        receptor.InstanciaPerdida += (_, i) => perdidas.Add(i);

        receptor.Recibir(Datagramas.Latido("JTDX"), null, Ahora);

        receptor.CaducarInactivas(TimeSpan.FromMinutes(2), Ahora.AddMinutes(1));
        perdidas.Should().BeEmpty();

        receptor.CaducarInactivas(TimeSpan.FromMinutes(2), Ahora.AddMinutes(3));
        perdidas.Should().Equal("JTDX");
    }

    [Fact]
    public void Un_suscriptor_que_lanza_no_tira_el_bucle()
    {
        var receptor = new ReceptorWsjt();
        var recibidas = 0;
        receptor.Decodificado += (_, _) => throw new InvalidOperationException("a proposito");
        receptor.Decodificado += (_, _) => recibidas++;

        var accion = () => receptor.Recibir(
            Datagramas.Decodificacion("WSJT-X", "CQ EA8DLF IL18", TimeSpan.FromHours(10)), null, Ahora);

        accion.Should().NotThrow();
        recibidas.Should().Be(1);
    }

    [Fact]
    public void La_basura_se_descarta_con_traza_y_no_rompe_el_bucle()
    {
        var registro = new RegistroDePrueba();
        var receptor = new ReceptorWsjt(registro);

        receptor.Recibir([1, 2, 3], null, Ahora).Should().BeFalse();
        receptor.Recibir([], null, Ahora).Should().BeFalse();
        receptor.Recibir(Datagramas.Latido("WSJT-X"), null, Ahora).Should().BeTrue();

        registro.Lineas.Should().Contain(t => t.Contains("descartado", StringComparison.Ordinal));
        receptor.Instancias.Should().HaveCount(1);
    }

    [Fact]
    public void La_hora_del_periodo_se_completa_con_la_fecha_de_hoy_en_utc()
    {
        var medianoche = new DateTimeOffset(2026, 9, 21, 0, 0, 30, TimeSpan.Zero);

        // Un periodo de las 23:59:45 recibido a las 00:00:30 es de ayer, no de hoy.
        ReceptorWsjt.InstanteDe(new TimeSpan(23, 59, 45), medianoche)
            .Should().Be(new DateTimeOffset(2026, 9, 20, 23, 59, 45, TimeSpan.Zero));

        ReceptorWsjt.InstanteDe(new TimeSpan(0, 0, 15), medianoche)
            .Should().Be(new DateTimeOffset(2026, 9, 21, 0, 0, 15, TimeSpan.Zero));
    }

    [Fact]
    public async Task El_puente_escucha_de_verdad_por_el_socket()
    {
        var puerto = PuertoLibre();
        await using var puente = new PuenteDigitalUdp(new OpcionesPuenteDigital { Puerto = puerto });

        var recibido = new TaskCompletionSource<EstadoDigital>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        puente.EstadoRecibido += (_, e) => recibido.TrySetResult(e);

        await puente.ArrancarAsync();

        using var emisor = new UdpClient(AddressFamily.InterNetwork);
        var datagrama = Datagramas.EstadoWsjtX();
        await emisor.SendAsync(datagrama, datagrama.Length, new IPEndPoint(IPAddress.Loopback, puerto));

        var terminada = await Task.WhenAny(recibido.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        terminada.Should().Be(recibido.Task, "el datagrama tenia que haber llegado");

        var estado = await recibido.Task;
        estado.Identificador.Should().Be("WSJT-X");
        estado.Dialecto.Should().Be(DialectoDigital.WsjtX);
        estado.Frecuencia.Megahercios.Should().Be(14.074m);
        puente.Instancias.Should().HaveCount(1);
        puente.Capacidades("WSJT-X").PuedeResaltar.Should().BeTrue();

        await puente.PararAsync();
    }

    [Fact]
    public async Task Resaltar_devuelve_falso_cuando_el_dialecto_no_lo_admite()
    {
        await using var puente = new PuenteDigitalUdp(new OpcionesPuenteDigital { Puerto = PuertoLibre() });

        puente.Admitir(Datagramas.Latido("JTDX"), new IPEndPoint(IPAddress.Loopback, 2237));

        var resultado = await puente.ResaltarAsync(
            "JTDX", Nodisla.Cuaderno.Dominio.Valores.Indicativo.Parse("EA8DLF"), true);

        resultado.Should().BeFalse("JTDX nunca tuvo el mensaje de resaltado");
        puente.Capacidades("JTDX").PuedeResaltar.Should().BeFalse();
    }

    [Fact]
    public async Task Resaltar_devuelve_falso_cuando_la_instancia_no_existe()
    {
        await using var puente = new PuenteDigitalUdp(new OpcionesPuenteDigital { Puerto = PuertoLibre() });

        var resultado = await puente.ResaltarAsync(
            "nadie", Nodisla.Cuaderno.Dominio.Valores.Indicativo.Parse("EA8DLF"), true);

        resultado.Should().BeFalse();
    }

    [Fact]
    public void La_respuesta_a_jtdx_se_queda_sin_los_dos_campos_que_anadio_wsjtx()
    {
        // Es el unico mensaje divergente que enviamos nosotros: aqui equivocarse significa
        // mandarle dos bytes de basura a la radio.
        var paraWsjtX = ConstructorDeMensajesWsjt.Respuesta(
            Decodificacion("WSJT-X"), DialectoDigital.WsjtX);
        var paraJtdx = ConstructorDeMensajesWsjt.Respuesta(
            Decodificacion("JTDX"), DialectoDigital.Jtdx);

        (paraWsjtX.Length - "WSJT-X".Length).Should().Be(paraJtdx.Length - "JTDX".Length + 2);

        var leido = AnalizadorWsjt.Analizar(paraJtdx);
        leido.Mensaje!.Tipo.Should().Be(TipoMensajeWsjt.Respuesta);
        leido.Mensaje.Id.Should().Be("JTDX");
    }

    [Fact]
    public void La_respuesta_devuelve_el_indicador_del_programa_y_no_el_nombre_del_modo()
    {
        // WSJT-X reconstruye la linea de su ventana con el caracter que el mando, asi que
        // mandarle «FT8» donde el habia dicho «~» seria que no encontrase nada.
        var datagrama = ConstructorDeMensajesWsjt.Respuesta(
            Decodificacion("WSJT-X"), DialectoDigital.WsjtX);

        var cuerpo = AnalizadorWsjt.Analizar(datagrama).Mensaje
            .Should().BeOfType<MensajeSinDetallar>().Subject.Cuerpo;

        var texto = System.Text.Encoding.UTF8.GetString(cuerpo);
        texto.Should().Contain("~");
        texto.Should().NotContain("FT8");
    }

    [Fact]
    public void El_indicador_de_una_letra_no_se_cuela_en_la_columna_de_modo()
    {
        var receptor = new ReceptorWsjt();
        var decodificaciones = new List<DecodificacionDigital>();
        receptor.Decodificado += (_, d) => decodificaciones.Add(d);

        // Antes de que llegue ningun estado no se sabe el modo: vacio, que es la verdad.
        receptor.Recibir(
            Datagramas.Decodificacion("WSJT-X", "CQ EA8DLF IL18", Periodo), null, Ahora);

        // Con el estado ya recibido, la decodificacion hereda el modo de verdad.
        receptor.Recibir(Datagramas.EstadoWsjtX(), null, Ahora);
        receptor.Recibir(
            Datagramas.Decodificacion("WSJT-X", "CQ DX K1ABC FN42", Periodo), null, Ahora);

        decodificaciones[0].IndicadorDelPrograma.Should().Be("~");
        decodificaciones[0].Modo.EsVacio.Should().BeTrue();

        decodificaciones[1].IndicadorDelPrograma.Should().Be("~");
        decodificaciones[1].Modo.Principal.Should().Be("FT8");
        decodificaciones[1].Modo.NombreUsual.Should().Be("FT8");
    }

    [Fact]
    public void Cuando_el_programa_manda_el_nombre_del_modo_se_usa_ese()
    {
        var receptor = new ReceptorWsjt();
        DecodificacionDigital? recibida = null;
        receptor.Decodificado += (_, d) => recibida = d;

        // MSHV manda el nombre en vez del caracter, y entonces no hace falta el estado.
        receptor.Recibir(
            Datagramas.Decodificacion("MSHV", "CQ EA8DLF IL18", Periodo, modo: "FT4"), null, Ahora);

        recibida!.IndicadorDelPrograma.Should().Be("FT4");
        recibida.Modo.Principal.Should().Be("MFSK");
        recibida.Modo.Submodo.Should().Be("FT4");
    }

    private static DecodificacionDigital Decodificacion(string identificador) => new(
        identificador,
        "CQ EA8DLF IL18",
        -12,
        0.2,
        1234,
        Nodisla.Cuaderno.Dominio.Valores.Modo.Parse("FT8"),
        new DateTimeOffset(2026, 9, 21, 10, 30, 15, TimeSpan.Zero),
        DialectoDigital.WsjtX)
    {
        IndicadorDelPrograma = "~",
    };

    [Fact]
    public void Los_mensajes_50_y_51_se_arman_tal_y_como_los_espera_jtdx()
    {
        var cincuenta = ConstructorDeMensajesWsjt.FijarTxDeltaFreq("JTDX", 1500);
        var cincuentaYUno = ConstructorDeMensajesWsjt.DispararCq("JTDX", "DX", true, true);

        cincuenta.Should().Equal(Datagramas.FijarTxDeltaFreq("JTDX", 1500));
        cincuentaYUno.Should().Equal(Datagramas.DispararCq("JTDX", "DX"));
    }

    [Fact]
    public async Task Los_mensajes_propios_de_jtdx_no_se_le_mandan_a_wsjtx()
    {
        await using var puente = new PuenteDigitalUdp(new OpcionesPuenteDigital { Puerto = PuertoLibre() });
        puente.Admitir(Datagramas.Latido("WSJT-X"), new IPEndPoint(IPAddress.Loopback, 2237));

        (await puente.FijarTxDeltaFreqAsync("WSJT-X", 1500)).Should().BeFalse();
        (await puente.DispararCqAsync("WSJT-X", "DX", true, true)).Should().BeFalse();
    }

    [Fact]
    public void El_estado_recoge_lo_que_cada_dialecto_informa_y_deja_nulo_lo_demas()
    {
        var receptor = new ReceptorWsjt();
        var estados = new List<EstadoDigital>();
        receptor.EstadoRecibido += (_, e) => estados.Add(e);

        receptor.Recibir(Datagramas.EstadoWsjtX(), null, Ahora);
        receptor.Recibir(Datagramas.EstadoJtdx(txPrimero: true), null, Ahora);

        var wsjt = estados[0];
        wsjt.TonoRxHz.Should().Be(1500);
        wsjt.TonoTxHz.Should().Be(1200);
        wsjt.TransmisionHabilitada.Should().BeTrue();
        wsjt.VigilanteDisparado.Should().BeFalse();
        wsjt.ModoEspecial.Should().Be("EU VHF");
        wsjt.Configuracion.Should().Be("Default");
        wsjt.MensajeEnTransmision.Should().Be("CQ EA8DLF IL18");
        wsjt.LocatorDelCorresponsal.Valor.Should().Be("FN42");
        wsjt.TransmiteElPrimero.Should().BeNull("WSJT-X no informa de eso en el estado");

        var jtdx = estados[1];
        jtdx.TransmiteElPrimero.Should().BeTrue();
        jtdx.ModoEspecial.Should().BeNull("JTDX no tiene ese campo");
        jtdx.Configuracion.Should().BeNull();
        jtdx.MensajeEnTransmision.Should().BeNull();
    }

    [Fact]
    public void El_adif_del_contacto_sale_por_su_propio_aviso()
    {
        const string texto = "<call:5>K1ABC <band:3>20m <mode:3>FT8 <eor>";
        var receptor = new ReceptorWsjt();
        var recibidos = new List<string>();
        receptor.AdifRecibido += (_, a) => recibidos.Add(a);

        receptor.Recibir(Datagramas.Adif("WSJT-X", texto), null, Ahora);
        receptor.Recibir(Datagramas.Adif("WSJT-X", "   "), null, Ahora);

        recibidos.Should().Equal(texto);
    }

    [Fact]
    public async Task Responder_usa_el_identificador_que_viaja_en_la_decodificacion()
    {
        await using var puente = new PuenteDigitalUdp(new OpcionesPuenteDigital { Puerto = PuertoLibre() });

        (await puente.ResponderAAsync(Decodificacion("WSJT-X")))
            .Should().BeFalse("esa instancia no ha dado senales de vida");

        puente.Admitir(Datagramas.Latido("WSJT-X"), new IPEndPoint(IPAddress.Loopback, 2237));
        puente.Capacidades("WSJT-X").PuedeResponder.Should().BeTrue();
        puente.Capacidades("nadie").Should().Be(CapacidadesDigitales.Desconocidas);
    }

    private static int PuertoLibre()
    {
        using var sonda = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        sonda.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)sonda.LocalEndPoint!).Port;
    }
}
