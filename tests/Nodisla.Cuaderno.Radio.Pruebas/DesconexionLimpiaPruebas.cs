using FluentAssertions;
using Microsoft.Extensions.Logging;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Radio.Control;
using Nodisla.Cuaderno.Radio.Control.Ft710;
using Nodisla.Cuaderno.Radio.Ptt;
using Nodisla.Cuaderno.Radio.Pruebas.Dobles;

namespace Nodisla.Cuaderno.Radio.Pruebas;

/// <summary>
/// Desconectar baja el PTT una vez, cierra, y ahi se acaba: no se vuelve a abrir el puerto.
/// </summary>
/// <remarks>
/// El fallo (15-11-2026): cada «prueba de conexión» dejaba en el registro «Falló la vía
/// «FT-710: TX0 por el canal abierto» … El canal COM4 a 38400 no está abierto» y despues «PTT
/// abajo antes de desconectar por «FT-710: reabrir el puerto y TX0»». La prueba desconecta y
/// luego desecha el control; desechar vuelve a desconectar, y esa segunda vez, con el canal ya
/// cerrado <b>a proposito</b> y el PTT ya bajado, recorria las vias de emergencia y volvia a
/// abrir el puerto solo para mandar otro <c>TX0;</c>. Lo mismo al cambiar de control en los
/// ajustes.
/// </remarks>
public class DesconexionLimpiaPruebas
{
    private static OpcionesFt710 Opciones() => new()
    {
        IntervaloDeSondeo = TimeSpan.FromHours(1),
        EsperaDeOrden = TimeSpan.FromMilliseconds(200),
        ComprobarElCodecDeAudio = false,
    };

    private static OpcionesDelVigilante OpcionesDelVigilante() => new()
    {
        TiempoMaximo = TimeSpan.FromSeconds(60),
        TiempoSinLatido = TimeSpan.FromSeconds(60),
        PasoDeVigilancia = TimeSpan.FromMilliseconds(5),
        EsperaDeSuelta = TimeSpan.FromMilliseconds(500),
        EngancharseAlCierreDelProceso = false,
        Seguridad = SeguridadDePrueba.SinPlanNiRoe,
    };

    private static void NoHayAvisos(RegistroDeMentira registro) =>
        registro.Apuntes.Where(apunte => apunte.Nivel >= LogLevel.Warning)
            .Select(apunte => apunte.Mensaje)
            .Should().BeEmpty("una desconexión normal no es un incidente");

    [Fact]
    public async Task Probar_la_conexion_abre_el_puerto_una_sola_vez_y_no_deja_avisos()
    {
        // Lo que hace «Probar» en los ajustes: conectar, leer, desconectar y desechar.
        var registro = new RegistroDeMentira();
        var canal = new CanalCatDeCaptura();
        var control = new ControlFt710(canal, Opciones(), registro);

        await control.ConectarAsync();
        await control.DesconectarAsync();
        await control.DisposeAsync();

        canal.Aperturas.Should().Be(1, "tras bajar el PTT y cerrar no hay nada que reabrir");
        canal.Recibidas.Count(orden => orden == "TX0;").Should().Be(1);
        NoHayAvisos(registro);
    }

    [Fact]
    public async Task Desconectar_dos_veces_no_vuelve_a_abrir_el_puerto()
    {
        var registro = new RegistroDeMentira();
        var canal = new CanalCatDeCaptura();
        await using var control = new ControlFt710(canal, Opciones(), registro);

        await control.ConectarAsync();
        await control.DesconectarAsync();
        await control.DesconectarAsync();

        canal.Aperturas.Should().Be(1);
        canal.Abierto.Should().BeFalse();
        NoHayAvisos(registro);
    }

    [Fact]
    public async Task Cambiar_de_control_en_los_ajustes_no_vuelve_a_abrir_el_puerto_del_que_se_va()
    {
        var registro = new RegistroDeMentira();
        var canal = new CanalCatDeCaptura();
        var control = new ControlFt710(canal, Opciones(), registro);
        await using var conmutable = new ControlEquipoConmutable(control, registro);
        await conmutable.ConectarAsync();

        await conmutable.SustituirAsync(new ControlNulo(registro));

        canal.Aperturas.Should().Be(1);
        canal.Abierto.Should().BeFalse();
        NoHayAvisos(registro);
    }

    [Fact]
    public async Task El_panico_con_el_equipo_desconectado_no_abre_el_puerto()
    {
        var registro = new RegistroDeMentira();
        var canal = new CanalCatDeCaptura();
        await using var control = new ControlFt710(canal, Opciones(), registro);
        await using var vigilante = new VigilantePtt(control, OpcionesDelVigilante(), registro);
        await control.ConectarAsync();
        await control.DesconectarAsync();

        await vigilante.SoltarYaAsync(MotivoDeSuelta.Panico);

        canal.Aperturas.Should().Be(1);
        canal.Abierto.Should().BeFalse("el pánico no puede dejar cogido un puerto que estaba cerrado");
        NoHayAvisos(registro);
    }

    [Fact]
    public async Task Tras_desconectar_se_puede_volver_a_conectar_y_transmitir()
    {
        var registro = new RegistroDeMentira();
        var canal = new CanalCatDeCaptura();
        await using var control = new ControlFt710(canal, Opciones(), registro);
        await using var vigilante = new VigilantePtt(control, OpcionesDelVigilante(), registro);
        await control.ConectarAsync();
        await control.DesconectarAsync();
        await vigilante.SoltarYaAsync(MotivoDeSuelta.Panico);

        await control.ConectarAsync();
        await using (await vigilante.PedirAntenaAsync("prueba"))
        {
            canal.Recibidas.Should().Contain("TX1;");
            control.Estado.Transmitiendo.Should().BeTrue();
        }

        canal.Recibidas.Last().Should().Be("TX0;");
        control.Estado.Transmitiendo.Should().BeFalse();
    }

    [Fact]
    public async Task Si_el_canal_se_rompe_en_antena_desconectar_si_reabre_para_bajar_el_PTT()
    {
        // La red de seguridad no se toca: con PTT pedido y el canal roto, se reabre y se baja.
        var registro = new RegistroDeMentira();
        var canal = new CanalCatDeCaptura();
        await using var control = new ControlFt710(canal, Opciones(), registro);
        await control.ConectarAsync();
        await ((IPttDirecto)control).PonerPttDirectoAsync(true, CancellationToken.None);
        canal.Romper();

        await control.DesconectarAsync();

        canal.Aperturas.Should().Be(2);
        canal.Recibidas.Last().Should().Be("TX0;");
        canal.Abierto.Should().BeFalse();
    }
}
