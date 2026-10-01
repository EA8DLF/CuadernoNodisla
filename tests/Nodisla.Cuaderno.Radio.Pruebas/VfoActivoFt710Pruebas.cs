using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Radio.Control;
using Nodisla.Cuaderno.Radio.Control.Ft710;
using Nodisla.Cuaderno.Radio.Pruebas.Dobles;

namespace Nodisla.Cuaderno.Radio.Pruebas;

/// <summary>
/// El VFO activo manda: spots, modo, estado y split, con las respuestas REALES del FT-710 de
/// EA8DLF (29-09-2026).
/// </summary>
/// <remarks>
/// Lo visto en la radio: <c>FA</c>/<c>FB</c> son siempre el A y el B, pero <c>MD0</c> y
/// <c>FT0</c> son la banda <b>principal</b>, que es el VFO elegido con <c>VS</c>. Con VS1 y
/// <c>MD01</c> cambio el modo del B (OI paso a LSB); con VS1 sin split, <c>FT0</c>; con VS0 y
/// split, <c>FT1</c> (transmite el B). Cambiar <c>VS</c> quita el split.
/// </remarks>
public class VfoActivoFt710Pruebas
{
    private static readonly Modo Usb = TraductorDeModos.PorOmision.DesdeElEquipo("USB");
    private static readonly Modo Lsb = TraductorDeModos.PorOmision.DesdeElEquipo("LSB");

    private static async Task<(CanalCatDeCaptura Canal, ControlFt710 Control)> MontarAsync()
    {
        var canal = new CanalCatDeCaptura();
        var control = new ControlFt710(canal, new OpcionesFt710 { IntervaloDeSondeo = TimeSpan.FromHours(1) });
        await control.ConectarAsync();
        return (canal, control);
    }

    private static void ConBActivo(CanalCatDeCaptura canal)
    {
        // Respuestas reales con VS1 tras poner LSB en el B (29-09-2026, 12:21:25).
        canal.Responder("VS;", "VS1;");
        canal.Responder("FA;", "FA014224300;");
        canal.Responder("FB;", "FB027556400;");
        canal.Responder("MD0;", "MD01;");
        canal.Responder("MD1;", "MD12;");
        canal.Responder("FT;", "FT0;");
        canal.Responder("ST;", "ST0;");
        canal.Responder("IF;", "IF000014224300+000000200000;");
    }

    [Fact]
    public async Task Un_spot_con_el_a_activo_escribe_fa_y_no_toca_el_b()
    {
        var (canal, control) = await MontarAsync();
        await using var _ = control;
        var desde = canal.Recibidas.Count;

        await control.PonerFrecuenciaAsync(Frecuencia.DesdeHercios(14_074_000));

        var escritas = canal.Recibidas.Skip(desde).ToList();
        escritas.Should().Contain("FA014074000;");
        escritas.Should().NotContain(o => o.StartsWith("FB0", StringComparison.Ordinal));
        control.Estado.Frecuencia.Should().Be(Frecuencia.DesdeHercios(14_074_000));
    }

    [Fact]
    public async Task Un_spot_con_el_b_activo_escribe_fb_y_el_modo_en_md0()
    {
        var (canal, control) = await MontarAsync();
        await using var _ = control;
        ConBActivo(canal);
        await control.LeerEstadoAsync();
        var desde = canal.Recibidas.Count;

        await control.PonerFrecuenciaAsync(Frecuencia.DesdeHercios(7_074_000));
        await control.PonerModoAsync(Lsb);

        var escritas = canal.Recibidas.Skip(desde).ToList();
        escritas.Should().Contain("FB007074000;");
        escritas.Should().NotContain(o => o.StartsWith("FA0", StringComparison.Ordinal));
        escritas.Should().Contain("MD01;", "MD0 es la banda principal, que con VS1 es el B");
        escritas.Should().NotContain(o => o.StartsWith("MD1", StringComparison.Ordinal) && o.Length == 5);
    }

    [Fact]
    public async Task El_spot_pregunta_vs_en_el_momento_y_no_se_fia_del_sondeo_anterior()
    {
        var (canal, control) = await MontarAsync();
        await using var _ = control;
        control.Estado.Vfo.Should().Be("VFO A");

        // El operador pulsa A/B en la radio justo antes del doble clic en el cluster.
        canal.Responder("VS;", "VS1;");
        await control.PonerFrecuenciaAsync(Frecuencia.DesdeHercios(21_074_000));

        canal.Recibidas.Should().Contain("FB021074000;");
        canal.Recibidas.Should().NotContain("FA021074000;");
    }

    [Fact]
    public async Task El_estado_sigue_al_vfo_activo()
    {
        var (canal, control) = await MontarAsync();
        await using var _ = control;
        ConBActivo(canal);

        var vfos = await control.LeerVfosAsync();

        control.Estado.Vfo.Should().Be("VFO B");
        control.Estado.Frecuencia.Should().Be(Frecuencia.DesdeHercios(27_556_400));
        control.Estado.Modo.Should().Be(Lsb, "el modo del B llega por MD0 con VS1");
        vfos.B.Modo.Should().Be(Lsb);
        vfos.A.Modo.Should().Be(Usb, "el del A llega por MD1 con VS1");
        vfos.B.Transmite.Should().BeTrue("FT0 con VS1: transmite la principal, el B");
        vfos.A.Transmite.Should().BeFalse();
    }

    [Fact]
    public async Task Con_split_se_apunta_la_de_transmision_y_la_de_recepcion_aparte()
    {
        var (canal, control) = await MontarAsync();
        await using var _ = control;

        // Real (29-09-2026, 12:21:42): VS0, ST1 y FT1 -> se recibe por A y se transmite por B.
        canal.Responder("VS;", "VS0;");
        canal.Responder("FA;", "FA014074700;");
        canal.Responder("FB;", "FB007072600;");
        canal.Responder("ST;", "ST1;");
        canal.Responder("FT;", "FT1;");
        var vfos = await control.LeerVfosAsync();

        control.Estado.Frecuencia.Should().Be(Frecuencia.DesdeHercios(7_072_600), "FREQ = transmision");
        control.Estado.FrecuenciaRx.Should().Be(Frecuencia.DesdeHercios(14_074_700), "FREQ_RX = recepcion, el activo");
        vfos.A.Recibe.Should().BeTrue();
        vfos.B.Transmite.Should().BeTrue();
    }

    [Fact]
    public async Task El_sondeo_no_pisa_una_escritura_recien_hecha()
    {
        var (canal, control) = await MontarAsync();
        await using var _ = control;
        var avisos = new List<long>();
        control.EstadoCambiado += (_, e) => { lock (avisos) avisos.Add(e.Frecuencia.Hercios); };

        // El sondeo empieza una pasada y se queda a medio leer FA (con el valor viejo).
        var llegoFa = canal.PararEn("FA;");
        var sondeo = Task.Run(() => control.LeerEstadoAsync());
        await llegoFa;

        // Doble clic en el cluster mientras tanto: no puede colarse en medio de la pasada.
        var spot = Task.Run(() => control.PonerFrecuenciaAsync(Frecuencia.DesdeHercios(14_074_000)));
        await Task.Yield();
        canal.Recibidas.Should().NotContain("FA014074000;", "la escritura espera a que acabe la pasada del sondeo");

        canal.Soltar();
        await sondeo;
        await spot;

        control.Estado.Frecuencia.Should().Be(Frecuencia.DesdeHercios(14_074_000));
        lock (avisos)
        {
            avisos.Last().Should().Be(14_074_000, "lo ultimo que se publica es lo recien escrito, no la lectura vieja");
        }
    }
}
