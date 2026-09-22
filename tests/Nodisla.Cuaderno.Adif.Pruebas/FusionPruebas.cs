using FluentAssertions;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Adif.Pruebas;

/// <summary>
/// La fusion de dos contactos con la misma clave natural.
/// </summary>
/// <remarks>
/// Vive en las pruebas de ADIF porque el caso que la obliga a existir es la importacion: un
/// respaldo de Log4OM trae pares de registros que son el mismo contacto visto dos veces y con
/// datos distintos en cada copia. Su sitio natural seria el proyecto de pruebas del dominio,
/// que no es mio.
/// </remarks>
public class FusionPruebas
{
    private const string Cabecera = "<ADIF_VER:5>3.1.5 <EOH>\n";

    // ── Campos escalares ─────────────────────────────────────────────────────

    [Fact]
    public void Un_valor_gana_siempre_al_hueco()
    {
        var destino = Contacto();
        var origen = Contacto();
        origen.Name = "JOSE";
        origen.TxPwr = 100;
        origen.Gridsquare = Locator.Parse("IL27HX");

        var fusion = FusionDeQso.Fundir(destino, origen);

        destino.Name.Should().Be("JOSE");
        destino.TxPwr.Should().Be(100);
        destino.Gridsquare.Valor.Should().Be("IL27HX");
        fusion.HuboCambios.Should().BeTrue();
        fusion.Choques.Should().BeEmpty();
    }

    [Fact]
    public void El_hueco_no_borra_lo_que_ya_habia()
    {
        var destino = Contacto();
        destino.Name = "JOSE";
        destino.TxPwr = 100;

        var fusion = FusionDeQso.Fundir(destino, Contacto());

        destino.Name.Should().Be("JOSE");
        destino.TxPwr.Should().Be(100);
        fusion.HuboCambios.Should().BeFalse();
    }

    [Fact]
    public void Dos_valores_distintos_no_se_pisan_en_silencio()
    {
        var destino = Contacto();
        destino.Name = "JOSE";
        destino.Sfi = 124;
        var origen = Contacto();
        origen.Name = "JOSE MARIA";
        origen.Sfi = 0;

        var fusion = FusionDeQso.Fundir(destino, origen);

        destino.Name.Should().Be("JOSE", "gana el primero");
        destino.Sfi.Should().Be(124);
        fusion.Choques.Should().Contain(c => c.Campo == "NAME" && c.Conservado == "JOSE" && c.Descartado == "JOSE MARIA");
        fusion.Choques.Should().Contain(c => c.Campo == "SFI" && c.Conservado == "124" && c.Descartado == "0");
        fusion.Choques.Should().OnlyContain(c => c.ClaveNatural == destino.ClaveNatural);
    }

    [Fact]
    public void El_submodo_de_una_copia_completa_a_la_otra_sin_ser_un_choque()
    {
        var destino = Contacto();
        destino.Mode = Modo.Parse("MFSK");
        var origen = Contacto();
        origen.Mode = Modo.Parse("MFSK", "FT4");

        var fusion = FusionDeQso.Fundir(destino, origen);

        destino.Mode.ToString().Should().Be("MFSK/FT4");
        fusion.Choques.Should().BeEmpty();
    }

    [Fact]
    public void Dos_submodos_distintos_si_son_un_choque()
    {
        var destino = Contacto();
        destino.Mode = Modo.Parse("MFSK", "FT4");
        var origen = Contacto();
        origen.Mode = Modo.Parse("MFSK", "JS8");

        var fusion = FusionDeQso.Fundir(destino, origen);

        destino.Mode.Submodo.Should().Be("FT4");
        fusion.Choques.Should().Contain(c => c.Campo == "MODE");
    }

    // ── Confirmaciones: lo que de verdad importa ─────────────────────────────

    [Fact]
    public void La_confirmacion_recibida_de_una_copia_no_se_pierde()
    {
        // Es el caso literal del respaldo real: una copia con la QSL sin recibir y otra con
        // la QSL recibida y su fecha. Saltarse la segunda copia perderia el diploma.
        var destino = Contacto();
        destino.Confirmaciones.Add(new QsoConfirmacion
        {
            Medio = MedioDeConfirmacion.Lotw,
            Recibido = EstadoDeConfirmacion.Ninguno,
        });

        var origen = Contacto();
        origen.Confirmaciones.Add(new QsoConfirmacion
        {
            Medio = MedioDeConfirmacion.Lotw,
            Recibido = EstadoDeConfirmacion.Confirmado,
            RecibidoUtc = new DateTimeOffset(2023, 3, 11, 0, 0, 0, TimeSpan.Zero),
        });

        var fusion = FusionDeQso.Fundir(destino, origen);

        var lotw = destino.Confirmaciones.Single(c => c.Medio == MedioDeConfirmacion.Lotw);
        lotw.Recibido.Should().Be(EstadoDeConfirmacion.Confirmado);
        lotw.EstaConfirmada.Should().BeTrue();
        lotw.RecibidoUtc.Should().Be(new DateTimeOffset(2023, 3, 11, 0, 0, 0, TimeSpan.Zero));
        fusion.RecuperoConfirmacion.Should().BeTrue();
        fusion.HuboCambios.Should().BeTrue();
    }

    [Theory]
    [InlineData(EstadoDeConfirmacion.Ninguno, EstadoDeConfirmacion.Pendiente, EstadoDeConfirmacion.Pendiente)]
    [InlineData(EstadoDeConfirmacion.Pendiente, EstadoDeConfirmacion.Solicitado, EstadoDeConfirmacion.Solicitado)]
    [InlineData(EstadoDeConfirmacion.Solicitado, EstadoDeConfirmacion.Confirmado, EstadoDeConfirmacion.Confirmado)]
    [InlineData(EstadoDeConfirmacion.Confirmado, EstadoDeConfirmacion.Verificado, EstadoDeConfirmacion.Verificado)]
    [InlineData(EstadoDeConfirmacion.Verificado, EstadoDeConfirmacion.Ninguno, EstadoDeConfirmacion.Verificado)]
    [InlineData(EstadoDeConfirmacion.Ninguno, EstadoDeConfirmacion.Rechazado, EstadoDeConfirmacion.Rechazado)]
    public void Gana_el_estado_mas_avanzado_de_cada_via(
        EstadoDeConfirmacion a, EstadoDeConfirmacion b, EstadoDeConfirmacion esperado)
    {
        FusionDeQso.MasAvanzado(a, b).Should().Be(esperado);
        FusionDeQso.MasAvanzado(b, a).Should().Be(esperado, "el orden no puede cambiar el resultado");
    }

    [Fact]
    public void Las_vias_que_falten_se_copian_enteras()
    {
        var destino = Contacto();
        destino.Confirmaciones.Add(new QsoConfirmacion { Medio = MedioDeConfirmacion.Papel });

        var origen = Contacto();
        origen.Confirmaciones.Add(new QsoConfirmacion
        {
            Medio = MedioDeConfirmacion.Eqsl,
            Recibido = EstadoDeConfirmacion.Confirmado,
            RecibidoUtc = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
        });

        FusionDeQso.Fundir(destino, origen);

        destino.Confirmaciones.Should().HaveCount(2);
        destino.Confirmaciones.Single(c => c.Medio == MedioDeConfirmacion.Eqsl)
            .EstaConfirmada.Should().BeTrue();
    }

    [Fact]
    public void Fundir_en_un_sentido_o_en_el_otro_deja_las_mismas_confirmaciones()
    {
        var haciaDelante = ConPar(out var segunda);
        FusionDeQso.Fundir(haciaDelante, segunda);

        var haciaAtras = ConParAlReves(out var primera);
        FusionDeQso.Fundir(haciaAtras, primera);

        Resumen(haciaDelante).Should().Equal(Resumen(haciaAtras));
    }

    [Fact]
    public void Entre_dos_fechas_del_mismo_estado_se_conserva_la_mas_antigua()
    {
        var a = Contacto();
        a.Confirmaciones.Add(new QsoConfirmacion
        {
            Medio = MedioDeConfirmacion.Lotw,
            Recibido = EstadoDeConfirmacion.Confirmado,
            RecibidoUtc = new DateTimeOffset(2024, 5, 1, 0, 0, 0, TimeSpan.Zero),
        });
        var b = Contacto();
        b.Confirmaciones.Add(new QsoConfirmacion
        {
            Medio = MedioDeConfirmacion.Lotw,
            Recibido = EstadoDeConfirmacion.Confirmado,
            RecibidoUtc = new DateTimeOffset(2024, 3, 1, 0, 0, 0, TimeSpan.Zero),
        });

        FusionDeQso.Fundir(a, b);

        a.Confirmaciones.Single().RecibidoUtc
            .Should().Be(new DateTimeOffset(2024, 3, 1, 0, 0, 0, TimeSpan.Zero));
    }

    // ── Colecciones ──────────────────────────────────────────────────────────

    [Fact]
    public void Las_referencias_se_unen_sin_repetirse()
    {
        var destino = Contacto();
        destino.Referencias.Add(new QsoReferencia
        {
            Tipo = TipoDeReferencia.Iota, Codigo = "AF-004", Lado = LadoDeReferencia.Corresponsal,
        });

        var origen = Contacto();
        origen.Referencias.Add(new QsoReferencia
        {
            Tipo = TipoDeReferencia.Iota, Codigo = "AF-004", Lado = LadoDeReferencia.Corresponsal,
        });
        origen.Referencias.Add(new QsoReferencia
        {
            Tipo = TipoDeReferencia.Pota, Codigo = "ES-0001", Lado = LadoDeReferencia.Corresponsal,
        });

        FusionDeQso.Fundir(destino, origen);

        destino.Referencias.Should().HaveCount(2);
        destino.Referencias.Should().ContainSingle(r => r.Tipo == TipoDeReferencia.Pota);
    }

    [Fact]
    public void Los_campos_extra_se_unen_y_los_que_discrepan_se_anotan()
    {
        var destino = Contacto();
        destino.CamposExtra.Add(new QsoCampoExtra { Nombre = "APP_X_A", Valor = "1" });

        var origen = Contacto();
        origen.CamposExtra.Add(new QsoCampoExtra { Nombre = "APP_X_A", Valor = "2" });
        origen.CamposExtra.Add(new QsoCampoExtra { Nombre = "APP_X_B", Valor = "3" });

        var fusion = FusionDeQso.Fundir(destino, origen);

        destino.CamposExtra.Should().HaveCount(2);
        destino.CamposExtra.Single(e => e.Nombre == "APP_X_A").Valor.Should().Be("1");
        fusion.Choques.Should().Contain(c => c.Campo == "APP_X_A");
    }

    // ── Comportamiento general ───────────────────────────────────────────────

    [Fact]
    public void Fundir_dos_veces_lo_mismo_no_cambia_nada_la_segunda()
    {
        var destino = ConPar(out var segunda);
        FusionDeQso.Fundir(destino, segunda).HuboCambios.Should().BeTrue();
        FusionDeQso.Fundir(destino, segunda).HuboCambios.Should().BeFalse();
    }

    [Fact]
    public void La_fusion_no_toca_el_contacto_de_origen()
    {
        var destino = Contacto();
        destino.Name = "JOSE";
        var origen = Contacto();

        FusionDeQso.Fundir(destino, origen);

        origen.Name.Should().BeNull();
        origen.Confirmaciones.Should().BeEmpty();
    }

    [Fact]
    public void La_identidad_del_contacto_de_destino_no_se_altera()
    {
        var destino = Contacto();
        var uuid = destino.Uuid;
        var origen = Contacto();
        origen.Name = "JOSE";

        FusionDeQso.Fundir(destino, origen);

        destino.Uuid.Should().Be(uuid, "el identificador estable no cambia al fundir");
    }

    [Fact]
    public async Task Dos_registros_del_mismo_fichero_se_funden_sin_perder_la_confirmacion()
    {
        // El par tal y como viene en un ADIF: misma clave natural, confirmaciones distintas.
        const string adif = Cabecera
            + "<CALL:6>EA8DLF <QSO_DATE:8>20260101 <TIME_ON:6>120000 <BAND:3>20m <MODE:3>SSB "
            + "<LOTW_QSL_RCVD:1>N <RST_SENT:2>59 <EOR>\n"
            + "<CALL:6>EA8DLF <QSO_DATE:8>20260101 <TIME_ON:6>120000 <BAND:3>20m <MODE:3>SSB "
            + "<LOTW_QSL_RCVD:1>Y <LOTW_QSLRDATE:8>20260210 <RST_RCVD:2>57 <EOR>\n";

        var lectura = await Ayudas.LeerAsync(adif);
        lectura.Qsos.Should().HaveCount(2);
        lectura.Qsos[0].ClaveNatural.Should().Be(lectura.Qsos[1].ClaveNatural);

        var fusion = FusionDeQso.Fundir(lectura.Qsos[0], lectura.Qsos[1]);
        var fundido = lectura.Qsos[0];

        fundido.Confirmaciones.Single(c => c.Medio == MedioDeConfirmacion.Lotw)
            .EstaConfirmada.Should().BeTrue("saltarse la segunda copia habria perdido el diploma");
        fundido.RstSent.Texto.Should().Be("59");
        fundido.RstRcvd.Texto.Should().Be("57", "cada copia sabia un informe");
        fusion.RecuperoConfirmacion.Should().BeTrue();

        // Y lo fundido se exporta entero.
        var salida = await Ayudas.ExportarAsync([fundido]);
        salida.Should().Contain("<LOTW_QSL_RCVD:1>Y").And.Contain("<LOTW_QSLRDATE:8>20260210");
        salida.Should().Contain("<RST_SENT:2>59").And.Contain("<RST_RCVD:2>57");
    }

    // ── Ayudas ───────────────────────────────────────────────────────────────

    private static Qso Contacto() => new()
    {
        Call = Indicativo.Parse("EA8DLF"),
        Band = Banda.Parse("20m"),
        Mode = Modo.Parse("SSB"),
        InicioUtc = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero),
    };

    private static Qso ConPar(out Qso segunda)
    {
        var primera = Contacto();
        primera.Name = "JOSE";
        primera.Confirmaciones.Add(new QsoConfirmacion
        {
            Medio = MedioDeConfirmacion.Lotw,
            Recibido = EstadoDeConfirmacion.Ninguno,
        });
        primera.Confirmaciones.Add(new QsoConfirmacion
        {
            Medio = MedioDeConfirmacion.Papel,
            Enviado = EstadoDeConfirmacion.Confirmado,
            EnviadoUtc = new DateTimeOffset(2024, 2, 1, 0, 0, 0, TimeSpan.Zero),
        });

        segunda = Contacto();
        segunda.Qth = "TELDE";
        segunda.Confirmaciones.Add(new QsoConfirmacion
        {
            Medio = MedioDeConfirmacion.Lotw,
            Recibido = EstadoDeConfirmacion.Verificado,
            RecibidoUtc = new DateTimeOffset(2024, 4, 1, 0, 0, 0, TimeSpan.Zero),
        });
        segunda.Confirmaciones.Add(new QsoConfirmacion
        {
            Medio = MedioDeConfirmacion.Eqsl,
            Recibido = EstadoDeConfirmacion.Confirmado,
        });
        return primera;
    }

    private static Qso ConParAlReves(out Qso primera)
    {
        var p = ConPar(out var s);
        primera = p;
        return s;
    }

    private static List<string> Resumen(Qso qso) =>
        qso.Confirmaciones
            .OrderBy(c => c.Medio)
            .Select(c => $"{c.Medio}|{c.Enviado}|{c.Recibido}|{c.EnviadoUtc:O}|{c.RecibidoUtc:O}")
            .ToList();
}
