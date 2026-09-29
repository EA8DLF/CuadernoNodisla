using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Radio.Control;
using Nodisla.Cuaderno.Radio.Control.Ft710;
using Nodisla.Cuaderno.Radio.Control.Yaesu;

namespace Nodisla.Cuaderno.Radio.Pruebas;

/// <summary>
/// CAT antiguo de Yaesu (FT-817/818/857/897) contra un doble que contesta como dice el manual
/// de operacion. <b>Sin probar con la radio.</b>
/// </summary>
public class YaesuBinarioPruebas
{
    private sealed class CanalBinarioDeMentira : ICanalBinario
    {
        private readonly object _candado = new();
        private readonly List<byte[]> _recibidos = [];

        internal byte[] FrecuenciaYModo { get; set; } = [0x01, 0x42, 0x34, 0x56, 0x01];

        internal byte EstadoRx { get; set; } = 0x09;

        internal IReadOnlyList<byte[]> Recibidos
        {
            get
            {
                lock (_candado) return _recibidos.ToList();
            }
        }

        public bool Abierto { get; private set; }

        public string Descripcion => "FT-817 de mentira";

        public Task AbrirAsync(CancellationToken ct = default)
        {
            Abierto = true;
            return Task.CompletedTask;
        }

        public Task<byte[]?> PreguntarAsync(byte[] bloque, int bytes, CancellationToken ct = default)
        {
            lock (_candado) _recibidos.Add(bloque);
            byte[]? respuesta = bloque[4] switch
            {
                ControlYaesuBinario.Orden.LeerFrecuenciaYModo => FrecuenciaYModo,
                ControlYaesuBinario.Orden.EstadoDeRecepcion => [EstadoRx],
                ControlYaesuBinario.Orden.EstadoDeTransmision => [0x7F],
                _ => null,
            };
            return Task.FromResult(respuesta);
        }

        public Task MandarAsync(byte[] bloque, CancellationToken ct = default)
        {
            MandarSincrono(bloque);
            return Task.CompletedTask;
        }

        public void MandarSincrono(byte[] bloque)
        {
            lock (_candado) _recibidos.Add(bloque);
        }

        public void Cerrar() => Abierto = false;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static async Task<(CanalBinarioDeMentira Canal, ControlYaesuBinario Control)> ConectarAsync()
    {
        var canal = new CanalBinarioDeMentira();
        var control = new ControlYaesuBinario(canal, ProveedorYaesuBinario.Ft817, new OpcionesFt710 { IntervaloDeSondeo = TimeSpan.FromHours(1) });
        await control.ConectarAsync();
        return (canal, control);
    }

    [Theory]
    [InlineData(14_234_560L, new byte[] { 0x01, 0x42, 0x34, 0x56 })]
    [InlineData(7_074_000L, new byte[] { 0x00, 0x70, 0x74, 0x00 })]
    [InlineData(432_100_000L, new byte[] { 0x43, 0x21, 0x00, 0x00 })]
    public void La_frecuencia_va_en_BCD_en_decenas_de_hercio(long hercios, byte[] bcd)
    {
        ControlYaesuBinario.ABcd(hercios).Should().Equal(bcd);
        ControlYaesuBinario.DesdeBcd(bcd).Should().Be(hercios);
    }

    [Fact]
    public async Task Al_conectar_lee_frecuencia_modo_y_medidor_S()
    {
        var (_, control) = await ConectarAsync();
        await using var _ = control;

        control.Estado.Conectado.Should().BeTrue();
        control.Estado.Frecuencia.Should().Be(Frecuencia.DesdeHercios(14_234_560));
        control.Estado.Modo.NombreUsual.Should().Be("USB");
        control.Estado.SenalRecibida.Should().Be(9);
        control.Modelo.ProbadoConRadio.Should().BeFalse();
    }

    [Fact]
    public async Task Poner_frecuencia_y_modo_manda_los_bloques_del_manual()
    {
        var (canal, control) = await ConectarAsync();
        await using var _ = control;

        await control.PonerFrecuenciaAsync(Frecuencia.DesdeHercios(7_074_000));
        await control.PonerModoDeTeclaAsync(TeclaDeModo.Cw);

        canal.Recibidos.Should().ContainEquivalentOf(new byte[] { 0x00, 0x70, 0x74, 0x00, 0x01 });
        canal.Recibidos.Should().ContainEquivalentOf(new byte[] { 0x02, 0x00, 0x00, 0x00, 0x07 });
    }

    [Fact]
    public async Task El_PTT_solo_sube_por_el_vigilante_y_baja_con_88()
    {
        var (canal, control) = await ConectarAsync();
        await using var _ = control;

        var saltarseElVigilante = () => ((IControlEquipo)control).PonerPttAsync(true);
        await saltarseElVigilante.Should().ThrowAsync<InvalidOperationException>();

        var ptt = (IPttDirecto)control;
        await ptt.PonerPttDirectoAsync(true, CancellationToken.None);
        await ptt.PonerPttDirectoAsync(false, CancellationToken.None);

        canal.Recibidos.Select(b => b[4]).Should().ContainInOrder(ControlYaesuBinario.Orden.PttSi, ControlYaesuBinario.Orden.PttNo);
        control.ViasDeSuelta.Should().NotBeEmpty();
        control.ViasDeSuelta.Should().Contain(v => v.SoltarSincrono != null);
    }

    [Fact]
    public async Task Split_se_acciona_y_se_recuerda_porque_el_equipo_no_lo_dice()
    {
        var (canal, control) = await ConectarAsync();
        await using var _ = control;

        (await control.LeerMandoAsync(MandoDeEquipo.Split)).Should().BeNull("no se sabe hasta que se toca");
        await control.EscribirMandoAsync(MandoDeEquipo.Split, 1);

        canal.Recibidos.Select(b => b[4]).Should().Contain(ControlYaesuBinario.Orden.SplitSi);
        (await control.LeerMandoAsync(MandoDeEquipo.Split)).Should().Be(1);
    }
}
