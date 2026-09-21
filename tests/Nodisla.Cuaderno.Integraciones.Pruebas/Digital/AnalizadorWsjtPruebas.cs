using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Integraciones.Digital;

namespace Nodisla.Cuaderno.Integraciones.Pruebas.Digital;

/// <summary>La lectura de los datagramas, empezando por lo que hay que rechazar.</summary>
public class AnalizadorWsjtPruebas
{
    [Fact]
    public void Un_datagrama_vacio_se_descarta_sin_lanzar()
    {
        var resultado = AnalizadorWsjt.Analizar([]);

        resultado.HayMensaje.Should().BeFalse();
        resultado.Motivo.Should().Be(MotivoDeDescarte.DemasiadoCorto);
    }

    [Fact]
    public void Una_magia_que_no_es_la_del_protocolo_se_descarta()
    {
        var basura = ConstructorDeDatagramas.Nuevo(0, "WSJT-X", magia: 0xDEAD_BEEF).ABytes();

        var resultado = AnalizadorWsjt.Analizar(basura);

        resultado.HayMensaje.Should().BeFalse();
        resultado.Motivo.Should().Be(MotivoDeDescarte.MagiaIncorrecta);
        resultado.Detalle.Should().Contain("ADBCCBDA");
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(1u)]
    [InlineData(4u)]
    [InlineData(99u)]
    public void Un_esquema_que_no_se_sabe_leer_se_descarta(uint esquema)
    {
        var datagrama = ConstructorDeDatagramas.Nuevo(0, "WSJT-X", esquema).U32(3).Txt("2.7.0").ABytes();

        var resultado = AnalizadorWsjt.Analizar(datagrama);

        resultado.HayMensaje.Should().BeFalse();
        resultado.Motivo.Should().Be(MotivoDeDescarte.EsquemaNoAdmitido);
        resultado.Esquema.Should().Be(esquema);
    }

    [Theory]
    [InlineData(2u)]
    [InlineData(3u)]
    public void Los_esquemas_2_y_3_si_se_leen(uint esquema)
    {
        var datagrama = ConstructorDeDatagramas.Nuevo(0, "WSJT-X", esquema).U32(3).Txt("2.7.0").ABytes();

        AnalizadorWsjt.Analizar(datagrama).HayMensaje.Should().BeTrue();
    }

    [Fact]
    public void Un_tipo_desconocido_no_rompe_nada_y_conserva_el_identificador()
    {
        var datagrama = ConstructorDeDatagramas.Nuevo(9999, "WSJT-X").Crudo(1, 2, 3, 4).ABytes();

        var resultado = AnalizadorWsjt.Analizar(datagrama);

        resultado.HayMensaje.Should().BeTrue();
        resultado.Mensaje.Should().BeOfType<MensajeSinDetallar>()
            .Which.Cuerpo.Should().Equal([1, 2, 3, 4]);
        resultado.Mensaje!.Id.Should().Be("WSJT-X");
    }

    /// <summary>
    /// La prueba que de verdad importa: cortar el datagrama por todos los sitios posibles y
    /// comprobar que en ninguno de ellos salta una excepcion. Es exactamente lo que pasa
    /// cuando el programa del otro lado cambia de version.
    /// </summary>
    [Fact]
    public void Un_datagrama_truncado_a_cualquier_longitud_no_lanza()
    {
        byte[][] completos =
        [
            Datagramas.Latido("WSJT-X"),
            Datagramas.EstadoWsjtX(),
            Datagramas.EstadoJtdx(),
            Datagramas.Decodificacion("WSJT-X", "CQ EA8DLF IL18", TimeSpan.FromHours(10)),
            Datagramas.QsoWsjtX(),
            Datagramas.QsoJtdx(),
        ];

        foreach (var completo in completos)
        {
            for (var n = 0; n <= completo.Length; n++)
            {
                var recorte = completo.AsSpan(0, n).ToArray();
                var accion = () => AnalizadorWsjt.Analizar(recorte);
                accion.Should().NotThrow($"un datagrama de {n} bytes no puede tirar el analizador");
            }
        }
    }

    [Fact]
    public void Un_estado_truncado_conserva_los_campos_que_si_llegaron()
    {
        var completo = Datagramas.EstadoWsjtX();
        // Se corta justo despues de la frecuencia del dial y el modo.
        var recorte = completo.AsSpan(0, 4 + 4 + 4 + 4 + 6 + 8 + 4 + 3).ToArray();

        var resultado = AnalizadorWsjt.Analizar(recorte);

        var estado = resultado.Mensaje.Should().BeOfType<EstadoWsjt>().Subject;
        estado.Id.Should().Be("WSJT-X");
        estado.DialHz.Should().Be(14_074_000);
        estado.Modo.Should().Be("FT8");
        estado.IndicativoDx.Should().BeNull();
        estado.ModoDeOperacionEspecial.Should().BeNull();
        estado.TxPrimero.Should().BeNull();
    }

    [Fact]
    public void El_estado_de_wsjtx_trae_los_cinco_campos_del_final()
    {
        var resultado = AnalizadorWsjt.Analizar(Datagramas.EstadoWsjtX(), DialectoDigital.WsjtX);

        var estado = resultado.Mensaje.Should().BeOfType<EstadoWsjt>().Subject;
        estado.ModoDeOperacionEspecial.Should().Be(2);
        estado.ToleranciaDeFrecuencia.Should().Be(50);
        estado.PeriodoTr.Should().Be(15);
        estado.NombreDeConfiguracion.Should().Be("Default");
        estado.MensajeTx.Should().Be("CQ EA8DLF IL18");
        estado.TxPrimero.Should().BeNull();
        estado.TerminoComoJtdx.Should().BeFalse();
    }

    [Fact]
    public void El_estado_de_jtdx_pone_tx_primero_donde_wsjtx_pone_el_modo_especial()
    {
        var resultado = AnalizadorWsjt.Analizar(Datagramas.EstadoJtdx(txPrimero: true));

        var estado = resultado.Mensaje.Should().BeOfType<EstadoWsjt>().Subject;
        estado.TxPrimero.Should().BeTrue();
        estado.ModoDeOperacionEspecial.Should().BeNull();
        estado.ToleranciaDeFrecuencia.Should().BeNull();
        estado.NombreDeConfiguracion.Should().BeNull();
        estado.TerminoComoJtdx.Should().BeTrue();
        estado.DialHz.Should().Be(7_074_000);
        estado.IndicativoDx.Should().Be("W9XYZ");
    }

    [Fact]
    public void El_estado_de_jtdx_se_lee_igual_cuando_ya_se_sabe_que_es_jtdx()
    {
        var resultado = AnalizadorWsjt.Analizar(Datagramas.EstadoJtdx(txPrimero: false), DialectoDigital.Jtdx);

        resultado.Mensaje.Should().BeOfType<EstadoWsjt>()
            .Which.TxPrimero.Should().BeFalse();
    }

    [Fact]
    public void El_contacto_de_wsjtx_trae_los_campos_de_concurso()
    {
        var resultado = AnalizadorWsjt.Analizar(Datagramas.QsoWsjtX());

        var qso = resultado.Mensaje.Should().BeOfType<QsoRegistradoWsjt>().Subject;
        qso.TraiaCamposDeConcurso.Should().BeTrue();
        qso.IntercambioEnviado.Should().Be("001");
        qso.IntercambioRecibido.Should().Be("002");
        qso.ModoDePropagacion.Should().Be("SAT");
        qso.InicioUtc.Should().Be(new DateTimeOffset(2026, 9, 21, 10, 14, 15, TimeSpan.Zero));
        qso.FinUtc.Should().Be(new DateTimeOffset(2026, 9, 21, 10, 15, 30, TimeSpan.Zero));
    }

    [Fact]
    public void El_contacto_de_jtdx_se_acaba_sin_los_campos_de_concurso()
    {
        var resultado = AnalizadorWsjt.Analizar(Datagramas.QsoJtdx(), DialectoDigital.Jtdx);

        var qso = resultado.Mensaje.Should().BeOfType<QsoRegistradoWsjt>().Subject;
        qso.TraiaCamposDeConcurso.Should().BeFalse();
        qso.IntercambioEnviado.Should().BeNull();
        qso.ModoDePropagacion.Should().BeNull();
        qso.IndicativoDx.Should().Be("W9XYZ");
        qso.MiLocator.Should().Be("IL18");
    }

    [Fact]
    public void La_decodificacion_se_lee_con_su_desfase_y_su_tono()
    {
        var datagrama = Datagramas.Decodificacion(
            "WSJT-X", "CQ EA8DLF IL18", new TimeSpan(10, 30, 15), snr: -7, desfase: -0.4, delta: 1780);

        var decodificacion = AnalizadorWsjt.Analizar(datagrama).Mensaje
            .Should().BeOfType<DecodificacionWsjt>().Subject;

        decodificacion.Decibelios.Should().Be(-7);
        decodificacion.DesfaseSegundos.Should().BeApproximately(-0.4, 1e-9);
        decodificacion.DeltaFrecuenciaHz.Should().Be(1780);
        decodificacion.HoraDelDia.Should().Be(new TimeSpan(10, 30, 15));
        decodificacion.Mensaje.Should().Be("CQ EA8DLF IL18");
        decodificacion.BajaConfianza.Should().BeFalse();
        decodificacion.FueraDeAire.Should().BeFalse();
    }

    [Fact]
    public void Los_tipos_50_y_51_solo_los_tiene_jtdx_y_prueban_el_dialecto()
    {
        var cincuenta = AnalizadorWsjt.Analizar(Datagramas.FijarTxDeltaFreq("JTDX", 1500));
        var cincuentaYUno = AnalizadorWsjt.Analizar(Datagramas.DispararCq("JTDX", "DX"));

        cincuenta.Mensaje!.Tipo.Should().Be(TipoMensajeWsjt.FijarTxDeltaFreq);
        cincuenta.DialectoProbado.Should().Be(DialectoDigital.Jtdx);
        cincuentaYUno.Mensaje!.Tipo.Should().Be(TipoMensajeWsjt.DispararCq);
        cincuentaYUno.DialectoProbado.Should().Be(DialectoDigital.Jtdx);
    }

    [Fact]
    public void Un_texto_que_declara_mas_longitud_de_la_que_trae_se_da_por_truncado()
    {
        // La longitud del identificador dice 100 bytes pero solo vienen cuatro.
        var datagrama = new ConstructorDeDatagramas()
            .U32(ConstructorDeDatagramas.Magia)
            .U32(3)
            .U32(0)
            .U32(100)
            .Crudo((byte)'J', (byte)'T', (byte)'D', (byte)'X')
            .ABytes();

        var resultado = AnalizadorWsjt.Analizar(datagrama);

        resultado.HayMensaje.Should().BeFalse();
        resultado.Motivo.Should().Be(MotivoDeDescarte.Truncado);
    }

    [Fact]
    public void Un_identificador_nulo_se_lee_como_cadena_vacia()
    {
        var datagrama = ConstructorDeDatagramas.Nuevo(6, string.Empty).ABytes();

        AnalizadorWsjt.Analizar(datagrama).Mensaje.Should().BeOfType<CierreWsjt>()
            .Which.Id.Should().BeEmpty();
    }
}
