using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.CasosDeUso;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Ui.Desarrollo;
using Nodisla.Cuaderno.Ui.VistaModelos;

namespace Nodisla.Cuaderno.Ui.Pruebas;

/// <summary>
/// Con split, el contacto se apunta con FREQ = transmision y FREQ_RX = recepcion, como Log4OM.
/// Frecuencias del FT-710 real con split (29-09-2026): recibe A 14.074.700, transmite B 7.072.600.
/// </summary>
public sealed class EntradaConSplitPruebas
{
    private readonly RepositorioQsoEnMemoria _cuaderno = new([]);
    private readonly RepositorioEstacionEnMemoria _estaciones = new();
    private readonly AvisosDeQsos _avisos = new();

    private VistaModeloEntradaQso Formulario() =>
        new(
            new RegistrarQso(_cuaderno, _estaciones, null, _avisos),
            new EditarQso(_cuaderno, _estaciones, _avisos),
            new ConsultarTrabajadoAntes(_cuaderno))
        {
            Esperar = (_, _) => Task.CompletedTask,
        };

    private async Task<Dominio.Entidades.Qso> GuardarAsync(VistaModeloEntradaQso formulario)
    {
        formulario.Indicativo = "EA8XYZ";
        await formulario.GuardarCommand.ExecuteAsync(null);
        return (await _cuaderno.BuscarAsync(new CriterioQso(), 0, 10)).Elementos.Single();
    }

    [Fact]
    public async Task Con_split_se_guarda_freq_de_transmision_y_freq_rx_de_recepcion()
    {
        var formulario = Formulario();
        formulario.SeguirAlDial(Frecuencia.DesdeHercios(7_072_600), Modo.Parse("SSB"), Frecuencia.DesdeHercios(14_074_700));

        var qso = await GuardarAsync(formulario);

        qso.Freq.Should().Be(Frecuencia.DesdeHercios(7_072_600));
        qso.FreqRx.Should().Be(Frecuencia.DesdeHercios(14_074_700));
    }

    [Fact]
    public async Task Sin_split_no_hay_freq_rx()
    {
        var formulario = Formulario();
        formulario.SeguirAlDial(Frecuencia.DesdeHercios(14_074_000), Modo.Parse("FT8"), null);

        var qso = await GuardarAsync(formulario);

        qso.Freq.Should().Be(Frecuencia.DesdeHercios(14_074_000));
        qso.FreqRx.Should().BeNull();
    }

    [Fact]
    public async Task Si_el_operador_teclea_otra_frecuencia_la_de_recepcion_no_se_apunta()
    {
        var formulario = Formulario();
        formulario.SeguirAlDial(Frecuencia.DesdeHercios(7_072_600), Modo.Parse("SSB"), Frecuencia.DesdeHercios(14_074_700));
        formulario.Frecuencia = "7.100000";

        var qso = await GuardarAsync(formulario);

        qso.FreqRx.Should().BeNull();
    }
}
