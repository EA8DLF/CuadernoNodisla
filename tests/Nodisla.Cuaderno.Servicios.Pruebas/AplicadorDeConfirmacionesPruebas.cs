using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Servicios.Emparejamiento;
using Nodisla.Cuaderno.Servicios.Pruebas.Dobles;

namespace Nodisla.Cuaderno.Servicios.Pruebas;

/// <summary>Bajar no pisa: una confirmacion solo puede mejorar el estado de un contacto.</summary>
public class AplicadorDeConfirmacionesPruebas
{
    private static ConfirmacionDescargada Confirmacion(
        bool verificada, MedioDeConfirmacion medio = MedioDeConfirmacion.Lotw) =>
        new(Indicativo.Crudo("EA1AB"),
            Banda.Parse("20m"),
            "CW",
            DateTimeOffset.Parse("2026-05-01T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
            medio,
            DateTimeOffset.Parse("2026-05-10T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
            verificada);

    [Fact]
    public void Una_confirmacion_nueva_crea_la_fila_del_medio()
    {
        var qso = CuadernoDePrueba.Contacto("EA1AB", "20m", "CW", "2026-05-01T12:00:00Z");

        AplicadorDeConfirmaciones.Aplicar(qso, Confirmacion(verificada: true)).Should().BeTrue();

        var fila = qso.Confirmaciones.Single();
        fila.Medio.Should().Be(MedioDeConfirmacion.Lotw);
        fila.Recibido.Should().Be(EstadoDeConfirmacion.Verificado);
        fila.RecibidoUtc.Should().NotBeNull();
    }

    [Fact]
    public void Una_confirmacion_sin_verificar_no_degrada_una_verificada()
    {
        var qso = CuadernoDePrueba.Contacto("EA1AB", "20m", "CW", "2026-05-01T12:00:00Z");
        qso.Confirmaciones.Add(new QsoConfirmacion
        {
            Medio = MedioDeConfirmacion.Lotw,
            Recibido = EstadoDeConfirmacion.Verificado,
            RecibidoUtc = DateTimeOffset.Parse("2026-01-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
        });

        var cambio = AplicadorDeConfirmaciones.Aplicar(qso, Confirmacion(verificada: false));

        cambio.Should().BeFalse();
        qso.Confirmaciones.Single().Recibido.Should().Be(EstadoDeConfirmacion.Verificado);
    }

    [Fact]
    public void Una_verificada_si_mejora_una_confirmada()
    {
        var qso = CuadernoDePrueba.Contacto("EA1AB", "20m", "CW", "2026-05-01T12:00:00Z");
        qso.Confirmaciones.Add(new QsoConfirmacion
        {
            Medio = MedioDeConfirmacion.Lotw,
            Recibido = EstadoDeConfirmacion.Confirmado,
        });

        AplicadorDeConfirmaciones.Aplicar(qso, Confirmacion(verificada: true)).Should().BeTrue();
        qso.Confirmaciones.Single().Recibido.Should().Be(EstadoDeConfirmacion.Verificado);
    }

    [Fact]
    public void La_fecha_original_de_la_confirmacion_no_se_pisa()
    {
        var original = DateTimeOffset.Parse("2026-01-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var qso = CuadernoDePrueba.Contacto("EA1AB", "20m", "CW", "2026-05-01T12:00:00Z");
        qso.Confirmaciones.Add(new QsoConfirmacion
        {
            Medio = MedioDeConfirmacion.Lotw,
            Recibido = EstadoDeConfirmacion.Confirmado,
            RecibidoUtc = original,
        });

        AplicadorDeConfirmaciones.Aplicar(qso, Confirmacion(verificada: true));

        qso.Confirmaciones.Single().RecibidoUtc.Should().Be(original);
    }

    [Fact]
    public void Cada_medio_tiene_su_propia_fila()
    {
        var qso = CuadernoDePrueba.Contacto("EA1AB", "20m", "CW", "2026-05-01T12:00:00Z");

        AplicadorDeConfirmaciones.Aplicar(qso, Confirmacion(true, MedioDeConfirmacion.Lotw));
        AplicadorDeConfirmaciones.Aplicar(qso, Confirmacion(false, MedioDeConfirmacion.Eqsl));

        qso.Confirmaciones.Should().HaveCount(2);
        qso.Confirmaciones.Single(c => c.Medio == MedioDeConfirmacion.Eqsl).Recibido
            .Should().Be(EstadoDeConfirmacion.Confirmado);
    }

    [Fact]
    public void Aplicar_no_toca_los_datos_del_contacto()
    {
        var qso = CuadernoDePrueba.Contacto("EA1AB", "20m", "CW", "2026-05-01T12:00:00Z");
        var antesBanda = qso.Band;
        var antesModo = qso.Mode;
        var antesInicio = qso.InicioUtc;
        var antesCall = qso.Call;

        AplicadorDeConfirmaciones.Aplicar(qso, Confirmacion(verificada: true));

        qso.Band.Should().Be(antesBanda);
        qso.Mode.Should().Be(antesModo);
        qso.InicioUtc.Should().Be(antesInicio);
        qso.Call.Should().Be(antesCall);
    }

    [Fact]
    public void Marcar_subido_no_devuelve_a_pendiente_lo_ya_confirmado()
    {
        var qso = CuadernoDePrueba.Contacto("EA1AB", "20m", "CW", "2026-05-01T12:00:00Z");
        qso.Confirmaciones.Add(new QsoConfirmacion
        {
            Medio = MedioDeConfirmacion.Lotw,
            Enviado = EstadoDeConfirmacion.Verificado,
            Recibido = EstadoDeConfirmacion.Verificado,
        });

        AplicadorDeConfirmaciones.MarcarSubido(qso, MedioDeConfirmacion.Lotw, DateTimeOffset.UtcNow);

        var fila = qso.Confirmaciones.Single();
        fila.Enviado.Should().Be(EstadoDeConfirmacion.Verificado);
        fila.Recibido.Should().Be(EstadoDeConfirmacion.Verificado);
    }

    [Fact]
    public void El_rango_deja_los_estados_negativos_por_debajo_de_ninguno()
    {
        AplicadorDeConfirmaciones.Rango(EstadoDeConfirmacion.Invalido)
            .Should().BeLessThan(AplicadorDeConfirmaciones.Rango(EstadoDeConfirmacion.Ninguno));
        AplicadorDeConfirmaciones.Rango(EstadoDeConfirmacion.Verificado)
            .Should().BeGreaterThan(AplicadorDeConfirmaciones.Rango(EstadoDeConfirmacion.Confirmado));
    }
}
