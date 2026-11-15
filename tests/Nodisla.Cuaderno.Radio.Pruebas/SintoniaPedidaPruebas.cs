using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Radio.Control.Ft710;
using Nodisla.Cuaderno.Radio.Ptt;
using Nodisla.Cuaderno.Radio.Pruebas.Dobles;

namespace Nodisla.Cuaderno.Radio.Pruebas;

/// <summary>
/// Una sintonia pedida que no llega a salir no se queda apuntada para la siguiente transmision.
/// </summary>
/// <remarks>
/// Encontrado al buscar estados que se queden pegados (15-11-2026), no visto en el registro de
/// ese dia. <c>PrepararSintonia</c> apunta que la <b>siguiente</b> subida de PTT es un TUNE
/// (<c>AC003;</c>, portadora). Si la antena se denegaba —fuera del plan de banda, bloqueo tras
/// un corte, otra transmision en curso— el apunte se quedaba, y la siguiente vez que alguien
/// pedia antena —la voz de Fonia, por ejemplo— el equipo, en vez de <c>TX1;</c>, sintonizaba.
/// </remarks>
public class SintoniaPedidaPruebas
{
    private static OpcionesFt710 Opciones(TimeSpan? validez = null)
    {
        var opciones = new OpcionesFt710
        {
            IntervaloDeSondeo = TimeSpan.FromHours(1),
            EsperaDeOrden = TimeSpan.FromMilliseconds(200),
            ComprobarElCodecDeAudio = false,
        };
        if (validez is { } v) opciones.ValidezDeLaSintoniaPedida = v;
        return opciones;
    }

    private static OpcionesDelVigilante OpcionesDelVigilante() => new()
    {
        TiempoMaximo = TimeSpan.FromSeconds(60),
        TiempoSinLatido = TimeSpan.FromSeconds(60),
        PasoDeVigilancia = TimeSpan.FromMilliseconds(5),
        EsperaDeSuelta = TimeSpan.FromMilliseconds(500),
        EngancharseAlCierreDelProceso = false,
        Seguridad = SeguridadDePrueba.SinPlanNiRoe,
    };

    [Fact]
    public async Task Una_sintonia_pedida_que_no_salio_caduca_y_la_voz_sube_con_TX1()
    {
        var canal = new CanalCatDeCaptura();
        await using var control = new ControlFt710(canal, Opciones(TimeSpan.FromMilliseconds(30)));
        await using var vigilante = new VigilantePtt(control, OpcionesDelVigilante());
        await control.ConectarAsync();

        // Se pide la sintonia, pero la antena no llega a pedirse (denegada, o fallo antes).
        control.PrepararSintonia();
        await Task.Delay(TimeSpan.FromMilliseconds(150));

        await using (await vigilante.PedirAntenaAsync("Fonía"))
        {
            canal.Recibidas.Should().Contain("TX1;");
            canal.Recibidas.Should().NotContain("AC003;", "eso seria sintonizar en vez de dar paso a la voz");
        }
    }

    [Fact]
    public async Task Una_bajada_que_se_cruza_no_convierte_el_TUNE_en_una_transmision_normal()
    {
        // La cola de la transmision anterior puede bajar el PTT justo despues de pedirse el
        // TUNE: si esa bajada borrase el apunte, saldria TX1; —portadora del modo que haya— en
        // vez de la sintonia.
        var canal = new CanalCatDeCaptura();
        await using var control = new ControlFt710(canal, Opciones());
        await using var vigilante = new VigilantePtt(control, OpcionesDelVigilante());
        await control.ConectarAsync();

        control.PrepararSintonia();
        await vigilante.SoltarYaAsync(MotivoDeSuelta.Panico);

        await using (await vigilante.PedirAntenaAsync("TUNE"))
        {
            canal.Recibidas.Should().Contain("AC003;");
            canal.Recibidas.Should().NotContain("TX1;");
        }
    }

    [Fact]
    public async Task La_sintonia_pedida_justo_antes_de_pedir_antena_sigue_sintonizando()
    {
        var canal = new CanalCatDeCaptura();
        await using var control = new ControlFt710(canal, Opciones());
        await using var vigilante = new VigilantePtt(control, OpcionesDelVigilante());
        await control.ConectarAsync();

        control.PrepararSintonia();
        await using (await vigilante.PedirAntenaAsync("TUNE"))
        {
            canal.Recibidas.Should().Contain("AC003;");
            canal.Recibidas.Should().NotContain("TX1;");
        }
    }
}
