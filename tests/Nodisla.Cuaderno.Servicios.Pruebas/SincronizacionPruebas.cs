using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Servicios.Credenciales;
using Nodisla.Cuaderno.Servicios.Emparejamiento;
using Nodisla.Cuaderno.Servicios.Lotw;
using Nodisla.Cuaderno.Servicios.Pruebas.Dobles;
using Nodisla.Cuaderno.Servicios.Red;

namespace Nodisla.Cuaderno.Servicios.Pruebas;

/// <summary>El recorrido entero: descargar, emparejar y aplicar, sin tocar la red.</summary>
public class SincronizacionPruebas
{
    private const string InformeDeLotw = """
        <PROGRAMID:4>LoTW
        <EOH>
        <CALL:5>EA1AB <BAND:3>20m <MODE:2>CW <QSO_DATE:8>20260501 <TIME_ON:6>121500
        <QSL_RCVD:1>Y <QSLRDATE:8>20260510 <EOR>
        <CALL:5>EA9ZZ <BAND:3>15m <MODE:2>CW <QSO_DATE:8>20260601 <TIME_ON:6>090000
        <QSL_RCVD:1>Y <QSLRDATE:8>20260610 <EOR>
        """;

    private static ServicioLotw ServicioConInforme(string informe)
    {
        var credenciales = new AlmacenDeCredencialesEnMemoria();
        credenciales.Guardar(ClavesDeCredencial.LotwContrasena, "secreta");
        return new ServicioLotw(
            new FabricaFalsa(ManejadorFalso.ConTexto(informe)),
            credenciales,
            new OpcionesLotw { Usuario = "EA8DLF", UbicacionDeEstacion = "EA8DLF" },
            reintentos: new PoliticaDeReintentos(esperar: (_, _) => Task.CompletedTask));
    }

    [Fact]
    public async Task Lo_que_casa_se_aplica_y_lo_que_no_se_devuelve_sin_pareja()
    {
        var qso = CuadernoDePrueba.Contacto("EA1AB", "20m", "CW", "2026-05-01T12:10:00Z", id: 1);
        var sincronizador = new SincronizadorDeConfirmaciones();

        var resultado = await sincronizador.SincronizarAsync(
            ServicioConInforme(InformeDeLotw), [qso], desdeUtc: null);

        resultado.Descargadas.Should().Be(2);
        resultado.Emparejadas.Should().Be(1);
        resultado.SinPareja.Should().ContainSingle()
            .Which.Call.Valor.Should().Be("EA9ZZ");

        qso.Confirmaciones.Single().Recibido.Should().Be(EstadoDeConfirmacion.Verificado);
        sincronizador.Modificados.Should().ContainSingle();
    }

    [Fact]
    public async Task Sincronizar_dos_veces_no_cambia_nada_la_segunda()
    {
        var qso = CuadernoDePrueba.Contacto("EA1AB", "20m", "CW", "2026-05-01T12:10:00Z", id: 1);
        var sincronizador = new SincronizadorDeConfirmaciones();
        var servicio = ServicioConInforme(InformeDeLotw);

        await sincronizador.SincronizarAsync(servicio, [qso], null);
        var segunda = await sincronizador.SincronizarAsync(servicio, [qso], null);

        segunda.Emparejadas.Should().Be(1);
        sincronizador.Modificados.Should().BeEmpty();
        qso.Confirmaciones.Should().HaveCount(1);
    }

    [Fact]
    public async Task La_sincronizacion_no_altera_los_datos_del_contacto()
    {
        var qso = CuadernoDePrueba.Contacto("EA1AB", "20m", "CW", "2026-05-01T12:10:00Z", id: 1);
        var antes = qso.ClaveNatural;

        await new SincronizadorDeConfirmaciones()
            .SincronizarAsync(ServicioConInforme(InformeDeLotw), [qso], null);

        qso.ClaveNatural.Should().Be(antes);
    }

    [Fact]
    public async Task El_resultado_dice_cuanto_tardo()
    {
        var resultado = await new SincronizadorDeConfirmaciones()
            .SincronizarAsync(ServicioConInforme(InformeDeLotw), [], null);

        resultado.Duracion.Should().BeGreaterThanOrEqualTo(TimeSpan.Zero);
        resultado.SinPareja.Should().HaveCount(2);
    }
}
