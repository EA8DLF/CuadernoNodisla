using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.CasosDeUso;
using Nodisla.Cuaderno.Aplicacion.Pruebas.Dobles;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Entidades;

namespace Nodisla.Cuaderno.Aplicacion.Pruebas;

/// <summary>
/// Pruebas de la importacion de un ADIF. Lo que se vigila aqui es que no se pierda nada: ni un
/// registro del fichero ni una confirmacion de las que ya estaban en el cuaderno.
/// </summary>
public sealed class ImportarAdifPruebas
{
    private static readonly DateTimeOffset Instante = new(2024, 5, 18, 21, 14, 37, TimeSpan.Zero);
    private static readonly DateTimeOffset Recepcion = new(2024, 11, 20, 0, 0, 0, TimeSpan.Zero);

    private readonly RepositorioQsoDoble _cuaderno = new();

    // ── Copias repetidas dentro del propio fichero ───────────────────────────

    [Fact]
    public async Task Dos_registros_de_la_misma_clave_entran_como_un_solo_contacto()
    {
        var caso = Caso(SinLaQsl, ConLaQsl);

        var resultado = await caso.DesdeAsync(Stream.Null);

        _cuaderno.Contenido.Should().ContainSingle();
        resultado.RegistrosLeidos.Should().Be(2);
        resultado.FundidosEnElFichero.Should().Be(1);
        resultado.ContactosDistintos.Should().Be(1);
        resultado.Anadidos.Should().Be(1);
        resultado.SinPerdidas.Should().BeTrue();
    }

    [Fact]
    public async Task Al_fundir_las_dos_copias_del_fichero_se_rescata_la_qsl_recibida()
    {
        var caso = Caso(SinLaQsl, ConLaQsl);

        var resultado = await caso.DesdeAsync(Stream.Null);

        resultado.ConfirmacionesRecuperadas.Should().Be(1);
        var papel = _cuaderno.Contenido[0].Confirmaciones.Should().ContainSingle().Subject;
        papel.Recibido.Should().Be(EstadoDeConfirmacion.Confirmado);
        papel.RecibidoUtc.Should().Be(Recepcion);
    }

    [Fact]
    public async Task Los_datos_distintos_de_las_dos_copias_salen_como_choques()
    {
        var caso = Caso(
            () => Ayuda.Qso(inicioUtc: Instante),
            () =>
            {
                var q = Ayuda.Qso(inicioUtc: Instante);
                q.Name = "Luis";
                return q;
            },
            () =>
            {
                var q = Ayuda.Qso(inicioUtc: Instante);
                q.Name = "Ana";
                return q;
            });

        var resultado = await caso.DesdeAsync(Stream.Null);

        resultado.FundidosEnElFichero.Should().Be(2);
        resultado.Choques.Should().ContainSingle().Which.Campo.Should().Be("NAME");
        _cuaderno.Contenido.Should().ContainSingle().Which.Name.Should().Be("Luis");
    }

    [Fact]
    public async Task Los_contactos_de_claves_distintas_entran_todos()
    {
        var caso = Caso(
            () => Ayuda.Qso(call: "EA1ABC", inicioUtc: Instante),
            () => Ayuda.Qso(call: "EA2DEF", inicioUtc: Instante),
            () => Ayuda.Qso(call: "EA1ABC", inicioUtc: Instante.AddMinutes(1)));

        var resultado = await caso.DesdeAsync(Stream.Null);

        _cuaderno.Contenido.Should().HaveCount(3);
        resultado.FundidosEnElFichero.Should().Be(0);
        resultado.Anadidos.Should().Be(3);
        resultado.ContactosDistintos.Should().Be(3);
        resultado.SinPerdidas.Should().BeTrue();
    }

    // ── Reimportar el mismo fichero ──────────────────────────────────────────

    [Fact]
    public async Task Importar_dos_veces_el_mismo_fichero_no_duplica_nada()
    {
        var caso = Caso(ConLaQsl);

        var primera = await caso.DesdeAsync(Stream.Null);
        var segunda = await caso.DesdeAsync(Stream.Null);

        primera.Anadidos.Should().Be(1);
        segunda.Anadidos.Should().Be(0);
        segunda.YaEstaban.Should().Be(1);
        segunda.FundidosConElCuaderno.Should().Be(0);
        segunda.SinPerdidas.Should().BeTrue();
        _cuaderno.Contenido.Should().ContainSingle();
    }

    [Fact]
    public async Task El_contacto_que_ya_esta_y_no_aporta_nada_no_se_vuelve_a_guardar()
    {
        _cuaderno.Sembrar(ConLaQsl());
        var caso = Caso(ConLaQsl);

        var resultado = await caso.DesdeAsync(Stream.Null);

        resultado.YaEstaban.Should().Be(1);
        resultado.FundidosConElCuaderno.Should().Be(0);
        resultado.Anadidos.Should().Be(0);
        resultado.SinPerdidas.Should().BeTrue();
        _cuaderno.Actualizaciones.Should().Be(0);
    }

    // ── Fundir con lo que ya hay en el cuaderno ──────────────────────────────

    [Fact]
    public async Task El_contacto_del_cuaderno_gana_lo_que_le_faltaba()
    {
        _cuaderno.Sembrar(SinLaQsl());
        var caso = Caso(ConLaQsl);

        var resultado = await caso.DesdeAsync(Stream.Null);

        resultado.FundidosConElCuaderno.Should().Be(1);
        resultado.Anadidos.Should().Be(0);
        resultado.YaEstaban.Should().Be(0);
        resultado.ConfirmacionesRecuperadas.Should().Be(1);
        resultado.SinPerdidas.Should().BeTrue();
        _cuaderno.Actualizaciones.Should().Be(1);

        var guardado = _cuaderno.Contenido.Should().ContainSingle().Subject;
        guardado.Name.Should().Be("Ana");
        guardado.Qth.Should().Be("Las Palmas");
        guardado.Confirmaciones.Should().ContainSingle()
            .Which.Recibido.Should().Be(EstadoDeConfirmacion.Confirmado);
    }

    [Fact]
    public async Task La_fusion_con_el_cuaderno_respeta_el_uuid_y_el_identificador_del_que_ya_estaba()
    {
        var yaEstaba = _cuaderno.Sembrar(SinLaQsl());
        var uuid = yaEstaba.Uuid;
        var id = yaEstaba.Id;
        var caso = Caso(ConLaQsl);

        await caso.DesdeAsync(Stream.Null);

        var guardado = _cuaderno.Contenido.Should().ContainSingle().Subject;
        guardado.Uuid.Should().Be(uuid);
        guardado.Id.Should().Be(id);
        guardado.ModificadoUtc.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Un_fichero_con_contactos_nuevos_y_viejos_a_la_vez_cuadra_la_cuenta()
    {
        _cuaderno.Sembrar(SinLaQsl());
        _cuaderno.Sembrar(Ayuda.Qso(call: "EA2DEF", inicioUtc: Instante));

        var caso = Caso(
            ConLaQsl,
            () => Ayuda.Qso(call: "EA2DEF", inicioUtc: Instante),
            () => Ayuda.Qso(call: "EA3GHI", inicioUtc: Instante));

        var resultado = await caso.DesdeAsync(Stream.Null);

        resultado.RegistrosLeidos.Should().Be(3);
        resultado.ContactosDistintos.Should().Be(3);
        resultado.FundidosConElCuaderno.Should().Be(1);
        resultado.YaEstaban.Should().Be(1);
        resultado.Anadidos.Should().Be(1);
        resultado.SinPerdidas.Should().BeTrue();
        _cuaderno.Contenido.Should().HaveCount(3);
    }

    // ── La optimizacion del cuaderno vacio ───────────────────────────────────

    [Fact]
    public async Task Con_el_cuaderno_vacio_no_se_pregunta_por_duplicados()
    {
        var caso = Caso(
            () => Ayuda.Qso(call: "EA1ABC", inicioUtc: Instante),
            () => Ayuda.Qso(call: "EA2DEF", inicioUtc: Instante),
            () => Ayuda.Qso(call: "EA3GHI", inicioUtc: Instante));

        var resultado = await caso.DesdeAsync(Stream.Null);

        _cuaderno.ConsultasDeDuplicado.Should().Be(0);
        _cuaderno.Conteos.Should().Be(1);
        resultado.Anadidos.Should().Be(3);
    }

    [Fact]
    public async Task Con_el_cuaderno_ocupado_se_pregunta_una_vez_por_contacto()
    {
        _cuaderno.Sembrar(Ayuda.Qso(call: "EA9ZZZ", inicioUtc: Instante));

        var caso = Caso(
            () => Ayuda.Qso(call: "EA1ABC", inicioUtc: Instante),
            () => Ayuda.Qso(call: "EA2DEF", inicioUtc: Instante));

        await caso.DesdeAsync(Stream.Null);

        _cuaderno.ConsultasDeDuplicado.Should().Be(2);
    }

    // ── Avisos y flujo ───────────────────────────────────────────────────────

    [Fact]
    public async Task Los_avisos_del_analizador_llegan_al_resultado()
    {
        var lector = new LectorAdifDoble(ConLaQsl)
        {
            Avisos = [new AvisoAdif(1, "CNTY", "Condado con rotulo de adorno.", NivelDeAviso.Informativo)],
        };
        var caso = new ImportarAdif(lector, _cuaderno);

        var resultado = await caso.DesdeAsync(Stream.Null);

        resultado.Avisos.Should().ContainSingle().Which.Campo.Should().Be("CNTY");
        resultado.Duracion.Should().BeGreaterThanOrEqualTo(TimeSpan.Zero);
    }

    [Fact]
    public async Task El_fichero_vacio_no_hace_nada_y_no_se_queja()
    {
        var caso = Caso();

        var resultado = await caso.DesdeAsync(Stream.Null);

        resultado.RegistrosLeidos.Should().Be(0);
        resultado.ContactosDistintos.Should().Be(0);
        resultado.Anadidos.Should().Be(0);
        resultado.SinPerdidas.Should().BeTrue();
        _cuaderno.Contenido.Should().BeEmpty();
    }

    [Fact]
    public async Task El_resumen_cuenta_lo_que_ha_pasado()
    {
        var caso = Caso(SinLaQsl, ConLaQsl);

        var resultado = await caso.DesdeAsync(Stream.Null);

        resultado.Resumen.Should().Contain("2").And.Contain("registros leidos");
    }

    [Fact]
    public async Task Importar_exige_un_flujo()
    {
        var caso = Caso(ConLaQsl);

        var sinFlujo = async () => await caso.DesdeAsync(null!);

        await sinFlujo.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public void El_caso_de_uso_exige_sus_dos_puertos()
    {
        var sinLector = () => new ImportarAdif(null!, _cuaderno);
        var sinRepositorio = () => new ImportarAdif(new LectorAdifDoble(), null!);

        sinLector.Should().Throw<ArgumentNullException>();
        sinRepositorio.Should().Throw<ArgumentNullException>();
    }

    // ── Cancelacion ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Un_testigo_ya_cancelado_corta_la_importacion()
    {
        var caso = Caso(SinLaQsl, ConLaQsl);
        using var fuente = new CancellationTokenSource();
        await fuente.CancelAsync();

        var importar = async () => await caso.DesdeAsync(Stream.Null, fuente.Token);

        await importar.Should().ThrowAsync<OperationCanceledException>();
        _cuaderno.Contenido.Should().BeEmpty();
    }

    // ── Ayudas ───────────────────────────────────────────────────────────────

    private ImportarAdif Caso(params Func<Qso>[] plantillas) =>
        new(new LectorAdifDoble(plantillas), _cuaderno);

    /// <summary>La copia pobre: sin nombre, sin QTH y con la QSL de papel sin recibir.</summary>
    private static Qso SinLaQsl()
    {
        var qso = Ayuda.Qso(inicioUtc: Instante);
        qso.Con(Ayuda.Confirmacion(
            MedioDeConfirmacion.Papel,
            enviado: EstadoDeConfirmacion.Confirmado,
            recibido: EstadoDeConfirmacion.Pendiente));
        return qso;
    }

    /// <summary>La copia rica: con nombre, con QTH y con la QSL recibida y fechada.</summary>
    private static Qso ConLaQsl()
    {
        var qso = Ayuda.Qso(inicioUtc: Instante);
        qso.Name = "Ana";
        qso.Qth = "Las Palmas";
        qso.Con(Ayuda.Confirmacion(
            MedioDeConfirmacion.Papel,
            enviado: EstadoDeConfirmacion.Confirmado,
            recibido: EstadoDeConfirmacion.Confirmado,
            recibidoUtc: Recepcion));
        return qso;
    }
}
