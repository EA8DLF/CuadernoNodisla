using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Radio.Control.Ft710;
using Nodisla.Cuaderno.Radio.Pruebas.Dobles;

namespace Nodisla.Cuaderno.Radio.Pruebas;

/// <summary>
/// La botonera lateral (HAM): teclas de banda (<c>BS</c>, manual CAT 2306-C), de modo y de
/// memoria, contra las respuestas reales del FT-710 de EA8DLF. Todo va al VFO activo y nada
/// transmite.
/// </summary>
public class BotoneraFt710Pruebas
{
    private static readonly string[] Transmiten = ["TX1", "TX2", "MX1", "AC003", "KY", "PS0"];

    private static async Task<(CanalCatDeCaptura Canal, ControlFt710 Control)> MontarAsync()
    {
        var canal = new CanalCatDeCaptura();
        var control = new ControlFt710(canal, new OpcionesFt710
        {
            IntervaloDeSondeo = TimeSpan.FromHours(1),
            Esperar = (_, _) => Task.CompletedTask,
        });
        await control.ConectarAsync();
        return (canal, control);
    }

    [Fact]
    public void Las_teclas_de_banda_son_las_del_manual_sin_70_MHz()
    {
        ControlFt710.BandasFt710.Select(b => b.Rotulo).Should().Equal(
            "1.8", "3.5", "5", "7", "10", "14", "18", "21", "24", "28", "50", "GEN");
        ControlFt710.BandasFt710.Select(b => b.Banda.Nombre ?? string.Empty).Should().Equal(
            "160m", "80m", "60m", "40m", "30m", "20m", "17m", "15m", "12m", "10m", "6m", string.Empty);
        ControlFt710.BandasFt710.Should().NotContain(b => b.Banda.Nombre == "4m", "el FT-710 no tiene 70 MHz");
        ControlFt710.BandasFt710.Last().EsCoberturaGeneral.Should().BeTrue();
    }

    [Theory]
    [InlineData(0, "BS00;")]
    [InlineData(1, "BS01;")]
    [InlineData(2, "BS02;")]
    [InlineData(3, "BS03;")]
    [InlineData(4, "BS04;")]
    [InlineData(5, "BS05;")]
    [InlineData(6, "BS06;")]
    [InlineData(7, "BS07;")]
    [InlineData(8, "BS08;")]
    [InlineData(9, "BS09;")]
    [InlineData(10, "BS10;")]
    [InlineData(11, "BS11;")]
    public async Task Cada_banda_manda_BS_como_la_tecla_BAND_y_no_escribe_frecuencia(int indice, string orden)
    {
        var (canal, control) = await MontarAsync();
        await using var _ = control;
        var desde = canal.Recibidas.Count;

        await control.IrABandaAsync(ControlFt710.BandasFt710[indice]);

        var mandadas = canal.Recibidas.Skip(desde).ToList();
        mandadas.Should().Contain(orden);

        // Es la pila de banda del equipo: ni FA ni FB escritos a mano, ni VS cambiado.
        mandadas.Should().NotContain(o => o.Length == 12 && (o.StartsWith("FA", StringComparison.Ordinal) || o.StartsWith("FB", StringComparison.Ordinal)));
        mandadas.Should().NotContain(o => o == "VS0;" || o == "VS1;");
        mandadas.Should().NotContain(o => Transmiten.Any(t => o.StartsWith(t, StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Con_el_VFO_B_activo_la_banda_y_el_modo_van_al_activo()
    {
        var (canal, control) = await MontarAsync();
        await using var _ = control;
        await control.PonerVfoActivoAsync(NombreDeVfo.B);
        var desde = canal.Recibidas.Count;

        await control.IrABandaAsync(ControlFt710.BandasFt710[3]);
        await control.PonerModoDeTeclaAsync(TeclaDeModo.Cw);

        var mandadas = canal.Recibidas.Skip(desde).ToList();

        // BS no lleva VFO: va al que manda, como la tecla. Y en el FT-710 MD0 es el VFO activo
        // (con VS1, el B): docs/15-botones-ft710-validados.md.
        mandadas.Should().Contain("BS03;");
        mandadas.Should().Contain("MD03;");
        mandadas.Should().NotContain(o => o.StartsWith("MD1", StringComparison.Ordinal) && o.Length == 5);
        mandadas.Should().NotContain(o => o == "VS0;");
    }

    [Theory]
    [InlineData(TeclaDeModo.Lsb, 7_100_000, "MD01;")]
    [InlineData(TeclaDeModo.Usb, 14_200_000, "MD02;")]
    [InlineData(TeclaDeModo.Cw, 7_010_000, "MD03;")]
    [InlineData(TeclaDeModo.Fm, 29_600_000, "MD04;")]
    [InlineData(TeclaDeModo.Am, 27_555_000, "MD05;")]
    [InlineData(TeclaDeModo.Datos, 7_074_000, "MD08;")]
    [InlineData(TeclaDeModo.Datos, 14_074_000, "MD0C;")]
    public void Cada_modo_es_su_orden_MD0(TeclaDeModo modo, long hercios, string orden) =>
        ControlFt710.OrdenDeModo(modo, Frecuencia.DesdeHercios(hercios)).Should().Be(orden);

    [Fact]
    public async Task Los_modos_y_las_memorias_no_transmiten()
    {
        var (canal, control) = await MontarAsync();
        await using var _ = control;
        var desde = canal.Recibidas.Count;

        foreach (var modo in Enum.GetValues<TeclaDeModo>())
        {
            await control.PonerModoDeTeclaAsync(modo);
        }

        for (var m = 1; m <= 6; m++)
        {
            await control.IrAMemoriaAsync(m);
        }

        var mandadas = canal.Recibidas.Skip(desde).ToList();
        mandadas.Should().Contain(["MC001;", "MC002;", "MC003;", "MC004;", "MC005;", "MC006;"]);
        mandadas.Should().NotContain(o => Transmiten.Any(t => o.StartsWith(t, StringComparison.Ordinal)));
    }
}
