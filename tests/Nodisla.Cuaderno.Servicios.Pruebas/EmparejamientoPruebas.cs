using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Servicios.Emparejamiento;
using Nodisla.Cuaderno.Servicios.Pruebas.Dobles;

namespace Nodisla.Cuaderno.Servicios.Pruebas;

/// <summary>
/// Emparejar es la parte dificil: aqui se comprueba la tolerancia de tiempo, la equivalencia
/// de modos y, sobre todo, que lo que no casa no se tira.
/// </summary>
public class EmparejamientoPruebas
{
    private static readonly EmparejadorDeConfirmaciones Emparejador = new();

    private static ConfirmacionDescargada Confirmacion(
        string indicativo,
        string banda,
        string modo,
        string cuando,
        MedioDeConfirmacion medio = MedioDeConfirmacion.Lotw,
        bool verificada = true)
    {
        Banda.TryParse(banda, out var b);
        return new ConfirmacionDescargada(
            Indicativo.Crudo(indicativo),
            b,
            modo,
            DateTimeOffset.Parse(cuando, System.Globalization.CultureInfo.InvariantCulture).ToUniversalTime(),
            medio,
            null,
            verificada);
    }

    [Fact]
    public void Lotw_admite_media_hora_de_diferencia()
    {
        var qso = CuadernoDePrueba.Contacto("EA1AB", "20m", "CW", "2026-05-01T12:00:00Z", id: 1);
        var confirmacion = Confirmacion("EA1AB", "20m", "CW", "2026-05-01T12:29:00Z");

        var resultado = Emparejador.Emparejar([qso], [confirmacion]);

        resultado.Parejas.Should().HaveCount(1);
        resultado.SinPareja.Should().BeEmpty();
        resultado.Parejas[0].Desfase.Should().Be(TimeSpan.FromMinutes(29));
    }

    [Fact]
    public void Pasada_la_media_hora_lotw_ya_no_empareja()
    {
        var qso = CuadernoDePrueba.Contacto("EA1AB", "20m", "CW", "2026-05-01T12:00:00Z", id: 1);
        var confirmacion = Confirmacion("EA1AB", "20m", "CW", "2026-05-01T12:31:00Z");

        var resultado = Emparejador.Emparejar([qso], [confirmacion]);

        resultado.Parejas.Should().BeEmpty();
        resultado.SinPareja.Should().ContainSingle();
    }

    [Fact]
    public void Ft4_del_servicio_casa_con_mfsk_ft4_del_cuaderno()
    {
        var qso = CuadernoDePrueba.Contacto("EA1AB", "20m", "MFSK", "2026-05-01T12:00:00Z", id: 1, submodo: "FT4");
        var confirmacion = Confirmacion("EA1AB", "20m", "FT4", "2026-05-01T12:00:00Z");

        Emparejador.Emparejar([qso], [confirmacion]).Parejas.Should().HaveCount(1);
    }

    [Fact]
    public void Mfsk_del_servicio_casa_con_mfsk_ft4_del_cuaderno()
    {
        var qso = CuadernoDePrueba.Contacto("EA1AB", "20m", "MFSK", "2026-05-01T12:00:00Z", id: 1, submodo: "FT4");
        var confirmacion = Confirmacion("EA1AB", "20m", "MFSK", "2026-05-01T12:00:00Z");

        Emparejador.Emparejar([qso], [confirmacion]).Parejas.Should().HaveCount(1);
    }

    [Fact]
    public void Rtty_del_cuaderno_casa_con_data_del_servicio_por_grupo_de_modo()
    {
        var qso = CuadernoDePrueba.Contacto("EA1AB", "20m", "RTTY", "2026-05-01T12:00:00Z", id: 1);
        var confirmacion = Confirmacion("EA1AB", "20m", "DATA", "2026-05-01T12:00:00Z");

        Emparejador.Emparejar([qso], [confirmacion]).Parejas.Should().HaveCount(1);
    }

    [Fact]
    public void La_telegrafia_no_casa_con_la_fonia_aunque_coincida_todo_lo_demas()
    {
        var qso = CuadernoDePrueba.Contacto("EA1AB", "20m", "CW", "2026-05-01T12:00:00Z", id: 1);
        var confirmacion = Confirmacion("EA1AB", "20m", "SSB", "2026-05-01T12:00:00Z");

        var resultado = Emparejador.Emparejar([qso], [confirmacion]);

        resultado.Parejas.Should().BeEmpty();
        resultado.SinPareja.Should().ContainSingle();
    }

    [Fact]
    public void Otra_banda_no_casa()
    {
        var qso = CuadernoDePrueba.Contacto("EA1AB", "20m", "CW", "2026-05-01T12:00:00Z", id: 1);
        var confirmacion = Confirmacion("EA1AB", "40m", "CW", "2026-05-01T12:00:00Z");

        Emparejador.Emparejar([qso], [confirmacion]).SinPareja.Should().ContainSingle();
    }

    [Fact]
    public void Una_banda_que_no_se_pudo_deducir_no_descarta_el_contacto()
    {
        // Pasa cuando la frecuencia cae en el borde de la tabla de ADIF y no hay banda.
        var qso = CuadernoDePrueba.Contacto("EA1AB", "20m", "CW", "2026-05-01T12:00:00Z", id: 1);
        var confirmacion = Confirmacion("EA1AB", "banda-inexistente", "CW", "2026-05-01T12:00:00Z");

        confirmacion.Band.EsVacia.Should().BeTrue();
        Emparejador.Emparejar([qso], [confirmacion]).Parejas.Should().HaveCount(1);
    }

    [Fact]
    public void Entre_varios_candidatos_gana_el_mas_cercano_en_el_tiempo()
    {
        var lejos = CuadernoDePrueba.Contacto("EA1AB", "20m", "CW", "2026-05-01T12:00:00Z", id: 1);
        var cerca = CuadernoDePrueba.Contacto("EA1AB", "20m", "CW", "2026-05-01T12:20:00Z", id: 2);
        var confirmacion = Confirmacion("EA1AB", "20m", "CW", "2026-05-01T12:22:00Z");

        var resultado = Emparejador.Emparejar([lejos, cerca], [confirmacion]);

        resultado.Parejas.Should().HaveCount(1);
        resultado.Parejas[0].Qso.Id.Should().Be(2);
    }

    [Fact]
    public void Un_contacto_no_se_empareja_dos_veces()
    {
        var qso = CuadernoDePrueba.Contacto("EA1AB", "20m", "CW", "2026-05-01T12:00:00Z", id: 1);
        var una = Confirmacion("EA1AB", "20m", "CW", "2026-05-01T12:00:00Z");
        var otra = Confirmacion("EA1AB", "20m", "CW", "2026-05-01T12:01:00Z");

        var resultado = Emparejador.Emparejar([qso], [una, otra]);

        resultado.Parejas.Should().HaveCount(1);
        resultado.SinPareja.Should().HaveCount(1);
    }

    [Fact]
    public void Las_confirmaciones_sin_pareja_se_devuelven_enteras_y_no_se_tiran()
    {
        var qso = CuadernoDePrueba.Contacto("EA1AB", "20m", "CW", "2026-05-01T12:00:00Z", id: 1);
        // La hora del cuaderno esta mal puesta: es justo el caso que hay que ensenar.
        var confirmacion = Confirmacion("EA1AB", "20m", "CW", "2026-05-01T18:00:00Z");

        var resultado = Emparejador.Emparejar([qso], [confirmacion]);

        resultado.SinPareja.Should().ContainSingle()
            .Which.Should().BeSameAs(confirmacion);
    }

    [Fact]
    public void La_tolerancia_es_la_del_medio_cuando_no_se_indica_otra()
    {
        ToleranciaDeEmparejamiento.Para(MedioDeConfirmacion.Lotw).Tiempo
            .Should().Be(TimeSpan.FromMinutes(30));
        ToleranciaDeEmparejamiento.Para(MedioDeConfirmacion.Eqsl).Tiempo
            .Should().Be(TimeSpan.FromMinutes(15));
    }

    [Fact]
    public void Con_eqsl_no_vale_media_hora()
    {
        var qso = CuadernoDePrueba.Contacto("EA1AB", "20m", "CW", "2026-05-01T12:00:00Z", id: 1);
        var confirmacion = Confirmacion(
            "EA1AB", "20m", "CW", "2026-05-01T12:20:00Z", MedioDeConfirmacion.Eqsl);

        Emparejador.Emparejar([qso], [confirmacion]).SinPareja.Should().ContainSingle();
    }
}
