using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Servicios.Credenciales;
using Nodisla.Cuaderno.Servicios.Eqsl;
using Nodisla.Cuaderno.Servicios.Pruebas.Dobles;
using Nodisla.Cuaderno.Servicios.Red;

namespace Nodisla.Cuaderno.Servicios.Pruebas;

/// <summary>eQSL.cc: subida con lista blanca de campos, descarga en dos pasos y el AG.</summary>
public class EqslPruebas
{
    private const string Buzon = """
        eQSL.cc InBox
        <ADIF_VER:5>3.1.0 <EOH>

        <CALL:5>EA1AB <BAND:3>20m <MODE:2>CW <QSO_DATE:8>20260501 <TIME_ON:6>121500
        <APP_EQSL_AG:1>Y <QSLRDATE:8>20260510 <GRIDSQUARE:4>IN80 <EOR>

        <CALL:5>EA2CD <BAND:3>40m <MODE:3>SSB <QSO_DATE:8>20260502 <TIME_ON:6>083000
        <APP_EQSL_AG:1>N <EOR>
        """;

    private const string PaginaDelBuzon = """
        <html><body>
        Your ADIF log file has been built<br>
        <a href="downloadedfiles/EA8DLF_1234.adi">EA8DLF_1234.adi</a>
        <a href="downloadedfiles/EA8DLF_1234.txt">EA8DLF_1234.txt</a>
        </body></html>
        """;

    private static (ServicioEqsl Servicio, ManejadorFalso Manejador) Montar(params string[] respuestas)
    {
        var credenciales = new AlmacenDeCredencialesEnMemoria();
        credenciales.Guardar(ClavesDeCredencial.EqslContrasena, "secreta");
        var manejador = ManejadorFalso.PorTurnos(respuestas);
        var servicio = new ServicioEqsl(
            new FabricaFalsa(manejador),
            credenciales,
            new OpcionesEqsl { Usuario = "EA8DLF" },
            new PoliticaDeReintentos(esperar: (_, _) => Task.CompletedTask));
        return (servicio, manejador);
    }

    [Fact]
    public void Solo_el_ag_cuenta_como_confirmacion_verificada()
    {
        var confirmaciones = ServicioEqsl.Interpretar(Buzon);

        confirmaciones.Should().HaveCount(2);
        confirmaciones[0].Verificada.Should().BeTrue();   // con AG
        confirmaciones[1].Verificada.Should().BeFalse();  // sin AG
        confirmaciones[0].Medio.Should().Be(MedioDeConfirmacion.Eqsl);
    }

    [Fact]
    public void El_enlace_del_buzon_se_resuelve_contra_la_raiz_del_sitio()
    {
        // Desde 2019 los ficheros cuelgan por encima de /qslcard/.
        var enlace = ServicioEqsl.ExtraerEnlace(PaginaDelBuzon, new Uri("https://www.eqsl.cc/"));

        enlace.Should().Be(new Uri("https://www.eqsl.cc/downloadedfiles/EA8DLF_1234.adi"));
    }

    [Fact]
    public void Si_no_hay_enlace_al_adi_no_se_inventa_ninguno()
    {
        ServicioEqsl.ExtraerEnlace("<html>Error: no match</html>", new Uri("https://www.eqsl.cc/"))
            .Should().BeNull();
    }

    [Fact]
    public async Task La_descarga_son_dos_pasos_y_el_segundo_va_al_fichero()
    {
        var (servicio, manejador) = Montar(PaginaDelBuzon, Buzon);

        var confirmaciones = await servicio.DescargarAsync(
            DateTimeOffset.Parse("2026-05-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture));

        confirmaciones.Should().HaveCount(2);
        manejador.Direcciones.Should().HaveCount(2);
        manejador.Direcciones[0].Query.Should().Contain("RcvdSince=202605010000");
        manejador.Direcciones[0].Query.Should().Contain("ConfirmedOnly=1");
        manejador.Direcciones[1].AbsolutePath.Should().EndWith(".adi");
    }

    [Fact]
    public async Task La_subida_solo_manda_campos_que_eqsl_conoce()
    {
        var (servicio, manejador) = Montar("Result: 1 out of 1 records added");

        var qso = CuadernoDePrueba.Contacto("EA1AB", "20m", "CW", "2026-05-01T12:15:00Z");
        qso.Comentario = "eQSL rechazaría el registro por este campo";
        qso.Country = "Spain";

        var resultado = await servicio.SubirAsync([qso]);

        resultado.Enviados.Should().Be(1);
        var enviado = manejador.Cuerpos.Single()!;
        enviado.Should().Contain("<CALL:5>EA1AB");
        enviado.Should().NotContain("COMMENT");
        enviado.Should().NotContain("COUNTRY");
    }

    [Fact]
    public async Task El_apodo_de_estacion_viaja_en_cada_registro()
    {
        var credenciales = new AlmacenDeCredencialesEnMemoria();
        credenciales.Guardar(ClavesDeCredencial.EqslContrasena, "secreta");
        var manejador = ManejadorFalso.ConTexto("Result: 1 out of 1 records added");
        var servicio = new ServicioEqsl(
            new FabricaFalsa(manejador),
            credenciales,
            new OpcionesEqsl { Usuario = "EA8DLF", ApodoDeEstacion = "Casa" });

        await servicio.SubirAsync([CuadernoDePrueba.Contacto("EA1AB", "20m", "CW", "2026-05-01T12:15:00Z")]);

        manejador.Cuerpos.Single()!.Should().Contain("APP_EQSL_QTH_NICKNAME");
    }

    [Fact]
    public void Un_error_de_eqsl_tumba_el_lote_entero()
    {
        var qso = CuadernoDePrueba.Contacto("EA1AB", "20m", "CW", "2026-05-01T12:15:00Z");

        var resultado = ServicioEqsl.InterpretarSubida(
            "Error: No match on eQSL_User/eQSL_Pswd", [qso], TimeSpan.Zero);

        resultado.Enviados.Should().Be(0);
        resultado.Rechazados.Should().Be(1);
        resultado.Motivos[qso.ClaveNatural].Should().Contain("No match");
    }

    [Fact]
    public void Los_avisos_por_registro_van_en_avisos_y_no_se_atribuyen_a_un_contacto()
    {
        var uno = CuadernoDePrueba.Contacto("EA1AB", "20m", "CW", "2026-05-01T12:15:00Z");
        var dos = CuadernoDePrueba.Contacto("EA2CD", "20m", "CW", "2026-05-01T12:20:00Z");

        var resultado = ServicioEqsl.InterpretarSubida(
            "Warning: Y=2026 M=05 D=01 Bad record: Duplicate\nResult: 1 out of 2 records added",
            [uno, dos], TimeSpan.Zero);

        resultado.Enviados.Should().Be(1);
        resultado.Rechazados.Should().Be(1);
        resultado.Avisos.Should().ContainSingle().Which.Should().Contain("Duplicate");
        // eQSL solo da la fecha en el aviso: no hay forma honrada de decir de que contacto es.
        resultado.Motivos.Should().BeEmpty();
    }

    [Fact]
    public async Task La_subida_a_eqsl_avisa_del_avance()
    {
        var (servicio, _) = Montar("Result: 1 out of 1 records added");
        var progreso = new ProgresoDeMentira();

        await servicio.SubirAsync(
            [CuadernoDePrueba.Contacto("EA1AB", "20m", "CW", "2026-05-01T12:15:00Z")],
            progreso);

        progreso.Avisos.Should().NotBeEmpty();
        progreso.Avisos.Should().OnlyContain(a => a.Servicio == "eQSL.cc");
        progreso.Avisos[^1].Hecho.Should().Be(1);
    }

    [Fact]
    public void Eqsl_puede_subir_y_bajar_cuando_hay_credenciales()
    {
        var (servicio, _) = Montar(string.Empty);

        servicio.PuedeSubir.Should().BeTrue();
        servicio.PuedeDescargar.Should().BeTrue();
    }
}
