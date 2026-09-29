using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.CasosDeUso;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Ui.Ajustes;
using Nodisla.Cuaderno.Ui.Desarrollo;
using Nodisla.Cuaderno.Ui.VistaModelos;

namespace Nodisla.Cuaderno.Ui.Pruebas;

/// <summary>
/// El nombre del corresponsal, sacado de su ficha de QRZ.com, junto al DX Call de la pestaña
/// Digital. Pedido por el operador el 28-09-2026.
/// </summary>
public sealed class NombreDelCorresponsalPruebas
{
    private sealed class ConsultaFija : IConsultaIndicativo
    {
        public string Nombre => "QRZ.com";
        public bool EstaDisponible => true;

        public Task<FichaIndicativo?> ConsultarAsync(Indicativo indicativo, CancellationToken ct = default) =>
            Task.FromResult<FichaIndicativo?>(new FichaIndicativo
            {
                Indicativo = indicativo,
                Nombre = "Nombre de " + indicativo.Valor,
                Fuente = "QRZ.com",
            });
    }

    private static VistaModeloModemPropio Montar(CompletadorDeQso? completador)
    {
        var reloj = new RelojManual(MontajeDeLaPestanaDigital.Mediodia);
        var cuaderno = new RepositorioQsoEnMemoria([]);
        return new VistaModeloModemPropio(
            new VistaModeloRelojDigital(reloj, null),
            new AjustesDelPrograma(),
            new ConsultarTrabajadoAntes(cuaderno),
            new RegistrarQso(cuaderno, new RepositorioEstacionEnMemoria()),
            EstadoDelCorrector.NoProcede,
            new ModemApuntador(reloj),
            entrada: null,
            salida: new SalidaDeAudioSimulada())
        {
            ConfirmarQueVaATransmitir = _ => false,
            Completador = completador,
            EsperarAntesDelNombre = (_, _) => Task.CompletedTask,
        };
    }

    [Fact]
    public async Task Al_poner_el_DX_Call_sale_su_nombre_de_QRZ()
    {
        var modelo = Montar(new CompletadorDeQso(new ConsultaFija(), esperar: (_, _) => Task.CompletedTask));

        modelo.Corresponsal = "F5UKW";
        await EsperaALaVentana.DrenarAsync();

        modelo.NombreDelCorresponsal.Should().Be("Nombre de F5UKW");
    }

    [Fact]
    public async Task Al_cambiar_de_corresponsal_el_nombre_anterior_no_se_queda()
    {
        var modelo = Montar(new CompletadorDeQso(new ConsultaFija(), esperar: (_, _) => Task.CompletedTask));

        modelo.Corresponsal = "F5UKW";
        await EsperaALaVentana.DrenarAsync();
        modelo.Corresponsal = string.Empty;
        await EsperaALaVentana.DrenarAsync();

        modelo.NombreDelCorresponsal.Should().BeEmpty();
    }

    [Fact]
    public async Task Sin_ficha_disponible_no_sale_nombre_ni_falla()
    {
        var modelo = Montar(completador: null);

        modelo.Corresponsal = "F5UKW";
        await EsperaALaVentana.DrenarAsync();

        modelo.NombreDelCorresponsal.Should().BeEmpty();
    }
}
