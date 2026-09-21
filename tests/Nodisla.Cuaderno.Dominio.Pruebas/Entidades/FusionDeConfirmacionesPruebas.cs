using FluentAssertions;
using Nodisla.Cuaderno.Dominio.Entidades;

namespace Nodisla.Cuaderno.Dominio.Pruebas.Entidades;

/// <summary>
/// Pruebas de la parte que justifica toda la fusion: las confirmaciones. Perder una es perder
/// un diploma, asi que aqui se comprueba via a via, estado a estado y en los dos sentidos.
/// </summary>
public sealed class FusionDeConfirmacionesPruebas
{
    private static readonly DateTimeOffset Temprano = new(2024, 6, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Tarde = new(2024, 11, 20, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Los ocho estados ordenados de menos a mas avanzado, declarados aqui a mano para que la
    /// prueba no repita la tabla del codigo que examina.
    /// </summary>
    private static readonly EstadoDeConfirmacion[] DeMenosAMas =
    [
        EstadoDeConfirmacion.Ninguno,
        EstadoDeConfirmacion.Rechazado,
        EstadoDeConfirmacion.Devuelto,
        EstadoDeConfirmacion.Invalido,
        EstadoDeConfirmacion.Pendiente,
        EstadoDeConfirmacion.Solicitado,
        EstadoDeConfirmacion.Confirmado,
        EstadoDeConfirmacion.Verificado,
    ];

    /// <summary>Los 64 pares posibles de estados con el que debe ganar cada uno.</summary>
    public static TheoryData<EstadoDeConfirmacion, EstadoDeConfirmacion, EstadoDeConfirmacion> Pares
    {
        get
        {
            var datos = new TheoryData<EstadoDeConfirmacion, EstadoDeConfirmacion, EstadoDeConfirmacion>();
            for (var i = 0; i < DeMenosAMas.Length; i++)
            {
                for (var j = 0; j < DeMenosAMas.Length; j++)
                {
                    datos.Add(DeMenosAMas[i], DeMenosAMas[j], DeMenosAMas[Math.Max(i, j)]);
                }
            }
            return datos;
        }
    }

    // ── La escala de estados ─────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(Pares))]
    public void Gana_el_estado_mas_avanzado_de_cada_par(
        EstadoDeConfirmacion a, EstadoDeConfirmacion b, EstadoDeConfirmacion esperado)
    {
        FusionDeQso.MasAvanzado(a, b).Should().Be(esperado);
        FusionDeQso.MasAvanzado(b, a).Should().Be(esperado);
    }

    [Theory]
    [MemberData(nameof(Pares))]
    public void Al_fundir_una_via_gana_el_estado_mas_avanzado_del_par(
        EstadoDeConfirmacion a, EstadoDeConfirmacion b, EstadoDeConfirmacion esperado)
    {
        var destino = AyudaDeFusion.Qso()
            .Con(AyudaDeFusion.Confirmacion(MedioDeConfirmacion.Lotw, enviado: a, recibido: a));
        var origen = AyudaDeFusion.Qso()
            .Con(AyudaDeFusion.Confirmacion(MedioDeConfirmacion.Lotw, enviado: b, recibido: b));

        FusionDeQso.Fundir(destino, origen);

        var fundida = destino.Confirmaciones.Should().ContainSingle().Subject;
        fundida.Enviado.Should().Be(esperado);
        fundida.Recibido.Should().Be(esperado);
    }

    [Fact]
    public void La_escala_de_avance_pone_lo_que_no_progresa_entre_ninguno_y_pendiente()
    {
        var avances = DeMenosAMas.Select(FusionDeQso.Avance).ToList();

        avances.Should().BeInAscendingOrder();
        avances.Should().OnlyHaveUniqueItems();
        FusionDeQso.Avance(EstadoDeConfirmacion.Ninguno).Should().Be(0);
        FusionDeQso.Avance(EstadoDeConfirmacion.Rechazado)
            .Should().BeGreaterThan(FusionDeQso.Avance(EstadoDeConfirmacion.Ninguno));
        FusionDeQso.Avance(EstadoDeConfirmacion.Devuelto)
            .Should().BeLessThan(FusionDeQso.Avance(EstadoDeConfirmacion.Pendiente));
        FusionDeQso.Avance(EstadoDeConfirmacion.Invalido)
            .Should().BeLessThan(FusionDeQso.Avance(EstadoDeConfirmacion.Pendiente));
        FusionDeQso.Avance(EstadoDeConfirmacion.Verificado)
            .Should().BeGreaterThan(FusionDeQso.Avance(EstadoDeConfirmacion.Confirmado));
    }

    // ── Se funde por medio, no por posicion ──────────────────────────────────

    [Fact]
    public void Las_confirmaciones_se_emparejan_por_medio_aunque_vengan_en_otro_orden()
    {
        var destino = AyudaDeFusion.Qso()
            .Con(AyudaDeFusion.Confirmacion(MedioDeConfirmacion.Lotw, recibido: EstadoDeConfirmacion.Confirmado))
            .Con(AyudaDeFusion.Confirmacion(MedioDeConfirmacion.Papel, recibido: EstadoDeConfirmacion.Pendiente));

        var origen = AyudaDeFusion.Qso()
            .Con(AyudaDeFusion.Confirmacion(MedioDeConfirmacion.Papel, recibido: EstadoDeConfirmacion.Confirmado))
            .Con(AyudaDeFusion.Confirmacion(MedioDeConfirmacion.Lotw, recibido: EstadoDeConfirmacion.Pendiente));

        FusionDeQso.Fundir(destino, origen);

        destino.Confirmaciones.Should().HaveCount(2);
        destino.Confirmaciones.Single(c => c.Medio == MedioDeConfirmacion.Lotw)
            .Recibido.Should().Be(EstadoDeConfirmacion.Confirmado);
        destino.Confirmaciones.Single(c => c.Medio == MedioDeConfirmacion.Papel)
            .Recibido.Should().Be(EstadoDeConfirmacion.Confirmado);
    }

    [Fact]
    public void El_medio_que_solo_tiene_el_origen_se_copia_entero()
    {
        var destino = AyudaDeFusion.Qso();
        var origen = AyudaDeFusion.Qso()
            .Con(AyudaDeFusion.Confirmacion(
                MedioDeConfirmacion.Eqsl,
                enviado: EstadoDeConfirmacion.Confirmado,
                recibido: EstadoDeConfirmacion.Verificado,
                enviadoUtc: Temprano,
                recibidoUtc: Tarde,
                nota: "AG"));

        var resultado = FusionDeQso.Fundir(destino, origen);

        var copiada = destino.Confirmaciones.Should().ContainSingle().Subject;
        copiada.Medio.Should().Be(MedioDeConfirmacion.Eqsl);
        copiada.Enviado.Should().Be(EstadoDeConfirmacion.Confirmado);
        copiada.Recibido.Should().Be(EstadoDeConfirmacion.Verificado);
        copiada.EnviadoUtc.Should().Be(Temprano);
        copiada.RecibidoUtc.Should().Be(Tarde);
        copiada.Nota.Should().Be("AG");
        copiada.Should().NotBeSameAs(origen.Confirmaciones[0]);
        resultado.HuboCambios.Should().BeTrue();
        resultado.RecuperoConfirmacion.Should().BeTrue();
    }

    [Fact]
    public void El_medio_que_solo_tiene_el_destino_se_queda_como_estaba()
    {
        var destino = AyudaDeFusion.Qso()
            .Con(AyudaDeFusion.Confirmacion(
                MedioDeConfirmacion.ClubLog,
                recibido: EstadoDeConfirmacion.Confirmado,
                recibidoUtc: Tarde));
        var origen = AyudaDeFusion.Qso();

        var resultado = FusionDeQso.Fundir(destino, origen);

        var unica = destino.Confirmaciones.Should().ContainSingle().Subject;
        unica.Medio.Should().Be(MedioDeConfirmacion.ClubLog);
        unica.Recibido.Should().Be(EstadoDeConfirmacion.Confirmado);
        unica.RecibidoUtc.Should().Be(Tarde);
        resultado.HuboCambios.Should().BeFalse();
    }

    [Fact]
    public void Cada_medio_se_funde_por_su_cuenta_sin_mezclarse_con_los_demas()
    {
        var destino = AyudaDeFusion.Qso()
            .Con(AyudaDeFusion.Confirmacion(MedioDeConfirmacion.Papel, recibido: EstadoDeConfirmacion.Pendiente))
            .Con(AyudaDeFusion.Confirmacion(MedioDeConfirmacion.Lotw, recibido: EstadoDeConfirmacion.Confirmado));

        var origen = AyudaDeFusion.Qso()
            .Con(AyudaDeFusion.Confirmacion(MedioDeConfirmacion.QrzCom, recibido: EstadoDeConfirmacion.Confirmado))
            .Con(AyudaDeFusion.Confirmacion(MedioDeConfirmacion.Lotw, recibido: EstadoDeConfirmacion.Verificado));

        FusionDeQso.Fundir(destino, origen);

        destino.Confirmaciones.Select(c => c.Medio).Should().BeEquivalentTo(
        [
            MedioDeConfirmacion.Papel,
            MedioDeConfirmacion.Lotw,
            MedioDeConfirmacion.QrzCom,
        ]);
        destino.Confirmaciones.Single(c => c.Medio == MedioDeConfirmacion.Papel)
            .Recibido.Should().Be(EstadoDeConfirmacion.Pendiente);
        destino.Confirmaciones.Single(c => c.Medio == MedioDeConfirmacion.Lotw)
            .Recibido.Should().Be(EstadoDeConfirmacion.Verificado);
    }

    // ── El caso real del cuaderno de EA8DLF ──────────────────────────────────

    [Fact]
    public void La_qsl_recibida_gana_a_la_no_recibida_y_se_queda_con_su_fecha()
    {
        var destino = AyudaDeFusion.Qso()
            .Con(AyudaDeFusion.Confirmacion(
                MedioDeConfirmacion.Papel,
                enviado: EstadoDeConfirmacion.Confirmado,
                enviadoUtc: Temprano,
                recibido: EstadoDeConfirmacion.Pendiente));

        var origen = AyudaDeFusion.Qso()
            .Con(AyudaDeFusion.Confirmacion(
                MedioDeConfirmacion.Papel,
                enviado: EstadoDeConfirmacion.Confirmado,
                enviadoUtc: Temprano,
                recibido: EstadoDeConfirmacion.Confirmado,
                recibidoUtc: Tarde));

        var resultado = FusionDeQso.Fundir(destino, origen);

        var papel = destino.Confirmaciones.Should().ContainSingle().Subject;
        papel.Recibido.Should().Be(EstadoDeConfirmacion.Confirmado);
        papel.RecibidoUtc.Should().Be(Tarde);
        papel.EstaConfirmada.Should().BeTrue();
        resultado.HuboCambios.Should().BeTrue();
        resultado.RecuperoConfirmacion.Should().BeTrue();
        resultado.Choques.Should().BeEmpty();
    }

    [Fact]
    public void La_fecha_del_estado_ganador_se_conserva_cuando_el_destino_no_la_tiene()
    {
        var destino = AyudaDeFusion.Qso()
            .Con(AyudaDeFusion.Confirmacion(MedioDeConfirmacion.Lotw, recibido: EstadoDeConfirmacion.Ninguno));
        var origen = AyudaDeFusion.Qso()
            .Con(AyudaDeFusion.Confirmacion(
                MedioDeConfirmacion.Lotw,
                recibido: EstadoDeConfirmacion.Verificado,
                recibidoUtc: Tarde));

        FusionDeQso.Fundir(destino, origen);

        var lotw = destino.Confirmaciones.Should().ContainSingle().Subject;
        lotw.Recibido.Should().Be(EstadoDeConfirmacion.Verificado);
        lotw.RecibidoUtc.Should().Be(Tarde);
    }

    [Fact]
    public void La_fecha_del_destino_se_mantiene_cuando_el_destino_ya_va_por_delante()
    {
        var destino = AyudaDeFusion.Qso()
            .Con(AyudaDeFusion.Confirmacion(
                MedioDeConfirmacion.Lotw,
                recibido: EstadoDeConfirmacion.Verificado,
                recibidoUtc: Tarde));
        var origen = AyudaDeFusion.Qso()
            .Con(AyudaDeFusion.Confirmacion(
                MedioDeConfirmacion.Lotw,
                recibido: EstadoDeConfirmacion.Pendiente,
                recibidoUtc: Temprano));

        var resultado = FusionDeQso.Fundir(destino, origen);

        var lotw = destino.Confirmaciones.Should().ContainSingle().Subject;
        lotw.Recibido.Should().Be(EstadoDeConfirmacion.Verificado);
        lotw.RecibidoUtc.Should().Be(Tarde);
        resultado.RecuperoConfirmacion.Should().BeFalse();
    }

    [Fact]
    public void La_fecha_que_acompana_al_estado_ganador_gana_a_la_del_estado_perdedor()
    {
        var destino = AyudaDeFusion.Qso()
            .Con(AyudaDeFusion.Confirmacion(
                MedioDeConfirmacion.Lotw,
                recibido: EstadoDeConfirmacion.Pendiente,
                recibidoUtc: Temprano));
        var origen = AyudaDeFusion.Qso()
            .Con(AyudaDeFusion.Confirmacion(
                MedioDeConfirmacion.Lotw,
                recibido: EstadoDeConfirmacion.Confirmado,
                recibidoUtc: Tarde));

        FusionDeQso.Fundir(destino, origen);

        var lotw = destino.Confirmaciones.Should().ContainSingle().Subject;
        lotw.Recibido.Should().Be(EstadoDeConfirmacion.Confirmado);
        lotw.RecibidoUtc.Should().Be(Tarde);
    }

    [Fact]
    public void La_fecha_del_estado_ganador_manda_aunque_sea_la_mas_antigua_de_las_dos()
    {
        var destino = AyudaDeFusion.Qso()
            .Con(AyudaDeFusion.Confirmacion(
                MedioDeConfirmacion.Lotw,
                recibido: EstadoDeConfirmacion.Confirmado,
                recibidoUtc: Temprano));
        var origen = AyudaDeFusion.Qso()
            .Con(AyudaDeFusion.Confirmacion(
                MedioDeConfirmacion.Lotw,
                recibido: EstadoDeConfirmacion.Pendiente,
                recibidoUtc: Tarde));

        var resultado = FusionDeQso.Fundir(destino, origen);

        var lotw = destino.Confirmaciones.Should().ContainSingle().Subject;
        lotw.Recibido.Should().Be(EstadoDeConfirmacion.Confirmado);
        lotw.RecibidoUtc.Should().Be(Temprano);
        resultado.RecuperoConfirmacion.Should().BeFalse();
    }

    [Fact]
    public void Con_el_mismo_estado_en_los_dos_lados_se_queda_la_fecha_mas_antigua()
    {
        var destino = AyudaDeFusion.Qso()
            .Con(AyudaDeFusion.Confirmacion(
                MedioDeConfirmacion.Lotw,
                recibido: EstadoDeConfirmacion.Confirmado,
                recibidoUtc: Tarde));
        var origen = AyudaDeFusion.Qso()
            .Con(AyudaDeFusion.Confirmacion(
                MedioDeConfirmacion.Lotw,
                recibido: EstadoDeConfirmacion.Confirmado,
                recibidoUtc: Temprano));

        FusionDeQso.Fundir(destino, origen);

        destino.Confirmaciones.Should().ContainSingle().Which.RecibidoUtc.Should().Be(Temprano);
    }

    [Fact]
    public void Con_el_mismo_estado_y_dos_fechas_la_eleccion_no_depende_del_orden()
    {
        var conLaTarde = () => AyudaDeFusion.Qso().Con(AyudaDeFusion.Confirmacion(
            MedioDeConfirmacion.Lotw, recibido: EstadoDeConfirmacion.Confirmado, recibidoUtc: Tarde));
        var conLaTemprana = () => AyudaDeFusion.Qso().Con(AyudaDeFusion.Confirmacion(
            MedioDeConfirmacion.Lotw, recibido: EstadoDeConfirmacion.Confirmado, recibidoUtc: Temprano));

        var unSentido = conLaTarde();
        FusionDeQso.Fundir(unSentido, conLaTemprana());

        var elOtro = conLaTemprana();
        FusionDeQso.Fundir(elOtro, conLaTarde());

        elOtro.Retrato().Should().Equal(unSentido.Retrato());
        elOtro.Confirmaciones[0].RecibidoUtc.Should().Be(Temprano);
    }

    // ── Via y nota de la confirmacion ────────────────────────────────────────

    [Fact]
    public void La_via_de_envio_del_origen_rellena_el_hueco_del_destino()
    {
        var destino = AyudaDeFusion.Qso()
            .Con(AyudaDeFusion.Confirmacion(MedioDeConfirmacion.Papel, enviado: EstadoDeConfirmacion.Confirmado));
        var origen = AyudaDeFusion.Qso()
            .Con(AyudaDeFusion.Confirmacion(
                MedioDeConfirmacion.Papel,
                enviado: EstadoDeConfirmacion.Confirmado,
                via: ViaDeEnvio.Buro));

        var resultado = FusionDeQso.Fundir(destino, origen);

        destino.Confirmaciones[0].Via.Should().Be(ViaDeEnvio.Buro);
        resultado.HuboCambios.Should().BeTrue();
        resultado.Choques.Should().BeEmpty();
    }

    [Fact]
    public void Dos_vias_de_envio_distintas_dejan_choque_con_el_medio_por_delante()
    {
        var destino = AyudaDeFusion.Qso()
            .Con(AyudaDeFusion.Confirmacion(MedioDeConfirmacion.Papel, via: ViaDeEnvio.Buro));
        var origen = AyudaDeFusion.Qso()
            .Con(AyudaDeFusion.Confirmacion(MedioDeConfirmacion.Papel, via: ViaDeEnvio.Directo));

        var resultado = FusionDeQso.Fundir(destino, origen);

        destino.Confirmaciones[0].Via.Should().Be(ViaDeEnvio.Buro);
        var choque = resultado.Choques.Should().ContainSingle().Subject;
        choque.Campo.Should().Be("Papel.Via");
        choque.Conservado.Should().Be("Buro");
        choque.Descartado.Should().Be("Directo");
    }

    [Fact]
    public void Dos_notas_distintas_en_el_mismo_medio_dejan_choque()
    {
        var destino = AyudaDeFusion.Qso()
            .Con(AyudaDeFusion.Confirmacion(MedioDeConfirmacion.Eqsl, nota: "AG"));
        var origen = AyudaDeFusion.Qso()
            .Con(AyudaDeFusion.Confirmacion(MedioDeConfirmacion.Eqsl, nota: "No AG"));

        var resultado = FusionDeQso.Fundir(destino, origen);

        destino.Confirmaciones[0].Nota.Should().Be("AG");
        resultado.Choques.Should().ContainSingle().Which.Campo.Should().Be("Eqsl.Nota");
    }

    // ── RecuperoConfirmacion ─────────────────────────────────────────────────

    [Fact]
    public void Recupero_confirmacion_cuando_el_origen_mejora_lo_recibido()
    {
        var destino = AyudaDeFusion.Qso()
            .Con(AyudaDeFusion.Confirmacion(MedioDeConfirmacion.Lotw, recibido: EstadoDeConfirmacion.Pendiente));
        var origen = AyudaDeFusion.Qso()
            .Con(AyudaDeFusion.Confirmacion(MedioDeConfirmacion.Lotw, recibido: EstadoDeConfirmacion.Confirmado));

        FusionDeQso.Fundir(destino, origen).RecuperoConfirmacion.Should().BeTrue();
    }

    [Fact]
    public void Recupero_confirmacion_cuando_el_origen_mejora_lo_enviado()
    {
        var destino = AyudaDeFusion.Qso()
            .Con(AyudaDeFusion.Confirmacion(MedioDeConfirmacion.Papel, enviado: EstadoDeConfirmacion.Pendiente));
        var origen = AyudaDeFusion.Qso()
            .Con(AyudaDeFusion.Confirmacion(MedioDeConfirmacion.Papel, enviado: EstadoDeConfirmacion.Confirmado));

        FusionDeQso.Fundir(destino, origen).RecuperoConfirmacion.Should().BeTrue();
    }

    [Fact]
    public void No_recupero_confirmacion_cuando_el_origen_no_trae_ninguna()
    {
        var destino = AyudaDeFusion.Qso()
            .Con(AyudaDeFusion.Confirmacion(MedioDeConfirmacion.Lotw, recibido: EstadoDeConfirmacion.Confirmado));
        var origen = AyudaDeFusion.Qso();
        origen.Name = "Ana";

        var resultado = FusionDeQso.Fundir(destino, origen);

        resultado.HuboCambios.Should().BeTrue();
        resultado.RecuperoConfirmacion.Should().BeFalse();
    }

    [Fact]
    public void No_recupero_confirmacion_cuando_el_origen_trae_lo_mismo()
    {
        var destino = AyudaDeFusion.Qso()
            .Con(AyudaDeFusion.Confirmacion(
                MedioDeConfirmacion.Lotw, recibido: EstadoDeConfirmacion.Confirmado, recibidoUtc: Tarde));
        var origen = AyudaDeFusion.Qso()
            .Con(AyudaDeFusion.Confirmacion(
                MedioDeConfirmacion.Lotw, recibido: EstadoDeConfirmacion.Confirmado, recibidoUtc: Tarde));

        var resultado = FusionDeQso.Fundir(destino, origen);

        resultado.HuboCambios.Should().BeFalse();
        resultado.RecuperoConfirmacion.Should().BeFalse();
    }

    [Fact]
    public void No_recupero_confirmacion_cuando_el_medio_nuevo_no_dice_nada()
    {
        var destino = AyudaDeFusion.Qso();
        var origen = AyudaDeFusion.Qso()
            .Con(AyudaDeFusion.Confirmacion(MedioDeConfirmacion.HrdLog, nota: "sin subir"));

        var resultado = FusionDeQso.Fundir(destino, origen);

        destino.Confirmaciones.Should().ContainSingle();
        resultado.HuboCambios.Should().BeTrue();
        resultado.RecuperoConfirmacion.Should().BeFalse();
    }

    // ── Conmutatividad ───────────────────────────────────────────────────────

    /// <summary>
    /// Repartos de confirmaciones entre las dos copias. Van por nombre y no por objeto para que
    /// xUnit pueda nombrar cada caso en el informe; el contenido lo arma <see cref="Reparto"/>.
    /// </summary>
    public static TheoryData<string> Repartos =>
    [
        "el caso real: una copia con la QSL sin recibir y la otra recibida con fecha",
        "cada copia sabe de un medio distinto",
        "una copia sabe lo enviado y la otra lo recibido",
        "una copia no trae ninguna confirmacion",
        "varios medios cruzados con estados desiguales",
        "estados que no son progreso frente a estados que si lo son",
        "el estado ganador trae la fecha mas antigua de las dos",
        "el mismo estado en los dos lados con dos fechas distintas",
    ];

    [Theory]
    [MemberData(nameof(Repartos))]
    public void Fundir_en_un_sentido_o_en_el_otro_deja_las_mismas_confirmaciones(string reparto)
    {
        var (deA, deB) = Reparto(reparto);

        var aMasB = Armar(deA);
        FusionDeQso.Fundir(aMasB, Armar(deB));

        var bMasA = Armar(deB);
        FusionDeQso.Fundir(bMasA, Armar(deA));

        bMasA.Retrato().Should().Equal(aMasB.Retrato());
    }

    /// <summary>Confirmaciones que lleva cada copia en el reparto indicado.</summary>
    private static (QsoConfirmacion[] DeA, QsoConfirmacion[] DeB) Reparto(string nombre) => nombre switch
    {
        "el caso real: una copia con la QSL sin recibir y la otra recibida con fecha" =>
        (
            [AyudaDeFusion.Confirmacion(
                MedioDeConfirmacion.Papel,
                enviado: EstadoDeConfirmacion.Confirmado,
                enviadoUtc: Temprano,
                recibido: EstadoDeConfirmacion.Pendiente)],
            [AyudaDeFusion.Confirmacion(
                MedioDeConfirmacion.Papel,
                enviado: EstadoDeConfirmacion.Confirmado,
                enviadoUtc: Temprano,
                recibido: EstadoDeConfirmacion.Confirmado,
                recibidoUtc: Tarde)]
        ),
        "cada copia sabe de un medio distinto" =>
        (
            [AyudaDeFusion.Confirmacion(
                MedioDeConfirmacion.Lotw, recibido: EstadoDeConfirmacion.Verificado, recibidoUtc: Tarde)],
            [AyudaDeFusion.Confirmacion(
                MedioDeConfirmacion.Papel, recibido: EstadoDeConfirmacion.Confirmado, recibidoUtc: Temprano)]
        ),
        "una copia sabe lo enviado y la otra lo recibido" =>
        (
            [AyudaDeFusion.Confirmacion(
                MedioDeConfirmacion.Lotw, enviado: EstadoDeConfirmacion.Confirmado, enviadoUtc: Temprano)],
            [AyudaDeFusion.Confirmacion(
                MedioDeConfirmacion.Lotw, recibido: EstadoDeConfirmacion.Verificado, recibidoUtc: Tarde)]
        ),
        "una copia no trae ninguna confirmacion" =>
        (
            [],
            [AyudaDeFusion.Confirmacion(
                MedioDeConfirmacion.Eqsl, recibido: EstadoDeConfirmacion.Confirmado, recibidoUtc: Tarde)]
        ),
        "varios medios cruzados con estados desiguales" =>
        (
            [
                AyudaDeFusion.Confirmacion(MedioDeConfirmacion.Papel, recibido: EstadoDeConfirmacion.Pendiente),
                AyudaDeFusion.Confirmacion(
                    MedioDeConfirmacion.Lotw, recibido: EstadoDeConfirmacion.Verificado, recibidoUtc: Tarde),
            ],
            [
                AyudaDeFusion.Confirmacion(MedioDeConfirmacion.Lotw, recibido: EstadoDeConfirmacion.Confirmado),
                AyudaDeFusion.Confirmacion(
                    MedioDeConfirmacion.QrzCom, recibido: EstadoDeConfirmacion.Confirmado, recibidoUtc: Temprano),
            ]
        ),
        "estados que no son progreso frente a estados que si lo son" =>
        (
            [AyudaDeFusion.Confirmacion(MedioDeConfirmacion.Papel, enviado: EstadoDeConfirmacion.Devuelto)],
            [AyudaDeFusion.Confirmacion(MedioDeConfirmacion.Papel, enviado: EstadoDeConfirmacion.Solicitado)]
        ),
        "el estado ganador trae la fecha mas antigua de las dos" =>
        (
            [AyudaDeFusion.Confirmacion(
                MedioDeConfirmacion.Lotw, recibido: EstadoDeConfirmacion.Confirmado, recibidoUtc: Temprano)],
            [AyudaDeFusion.Confirmacion(
                MedioDeConfirmacion.Lotw, recibido: EstadoDeConfirmacion.Pendiente, recibidoUtc: Tarde)]
        ),
        "el mismo estado en los dos lados con dos fechas distintas" =>
        (
            [AyudaDeFusion.Confirmacion(
                MedioDeConfirmacion.Eqsl, recibido: EstadoDeConfirmacion.Confirmado, recibidoUtc: Tarde)],
            [AyudaDeFusion.Confirmacion(
                MedioDeConfirmacion.Eqsl, recibido: EstadoDeConfirmacion.Confirmado, recibidoUtc: Temprano)]
        ),
        _ => throw new ArgumentOutOfRangeException(nameof(nombre), nombre, "Reparto desconocido."),
    };

    [Fact]
    public void Fundir_en_los_dos_sentidos_deja_la_misma_fecha_aunque_el_estado_perdedor_sea_mas_antiguo()
    {
        var conFecha = () => AyudaDeFusion.Qso().Con(AyudaDeFusion.Confirmacion(
            MedioDeConfirmacion.Lotw, recibido: EstadoDeConfirmacion.Confirmado, recibidoUtc: Tarde));
        var pendiente = () => AyudaDeFusion.Qso().Con(AyudaDeFusion.Confirmacion(
            MedioDeConfirmacion.Lotw, recibido: EstadoDeConfirmacion.Pendiente, recibidoUtc: Temprano));

        var unSentido = conFecha();
        FusionDeQso.Fundir(unSentido, pendiente());

        var elOtro = pendiente();
        FusionDeQso.Fundir(elOtro, conFecha());

        elOtro.Retrato().Should().Equal(unSentido.Retrato());
        elOtro.Confirmaciones[0].RecibidoUtc.Should().Be(Tarde);
    }

    [Fact]
    public void Fundir_dos_veces_seguidas_no_cambia_nada_mas()
    {
        var destino = AyudaDeFusion.Qso()
            .Con(AyudaDeFusion.Confirmacion(MedioDeConfirmacion.Papel, recibido: EstadoDeConfirmacion.Pendiente));
        var origen = AyudaDeFusion.Qso()
            .Con(AyudaDeFusion.Confirmacion(
                MedioDeConfirmacion.Papel, recibido: EstadoDeConfirmacion.Confirmado, recibidoUtc: Tarde));

        FusionDeQso.Fundir(destino, origen);
        var retratoTrasLaPrimera = destino.Retrato();

        var segunda = FusionDeQso.Fundir(destino, origen.Clon());

        segunda.HuboCambios.Should().BeFalse();
        segunda.RecuperoConfirmacion.Should().BeFalse();
        destino.Retrato().Should().Equal(retratoTrasLaPrimera);
    }

    /// <summary>Contacto de la clave de siempre con las confirmaciones que se le pasen, recien copiadas.</summary>
    private static Qso Armar(IEnumerable<QsoConfirmacion> confirmaciones)
    {
        var qso = AyudaDeFusion.Qso();
        foreach (var c in confirmaciones)
        {
            qso.Con(AyudaDeFusion.Confirmacion(
                c.Medio, c.Enviado, c.Recibido, c.EnviadoUtc, c.RecibidoUtc, c.Via, c.Nota));
        }
        return qso;
    }
}
