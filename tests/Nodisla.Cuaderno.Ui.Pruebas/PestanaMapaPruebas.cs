using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.CasosDeUso;
using Nodisla.Cuaderno.Dominio.Dxcc;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Ui.Desarrollo;
using Nodisla.Cuaderno.Ui.Mapa;
using Nodisla.Cuaderno.Ui.VistaModelos;

namespace Nodisla.Cuaderno.Ui.Pruebas;

/// <summary>Los mandos de la pestaña Mapa: filtros de contactos, trayecto y quitar trayecto.</summary>
public class PestanaMapaPruebas
{
    private static readonly DateTimeOffset Origen = new(2025, 10, 13, 16, 0, 0, TimeSpan.Zero);

    private static async Task<(VistaModeloMapa Mapa, RepositorioQsoEnMemoria Repositorio)> MontarAsync()
    {
        var repositorio = new RepositorioQsoEnMemoria(
        [
            new Qso { Call = Indicativo.Crudo("ON4BW"), Band = Banda.Parse("10m"), Mode = Modo.Crudo("SSB", "USB"), InicioUtc = Origen, Gridsquare = Locator.Parse("JO20") },
            new Qso { Call = Indicativo.Crudo("Z36T"), Band = Banda.Parse("15m"), Mode = Modo.Crudo("SSB", "USB"), InicioUtc = Origen.AddMinutes(5), Gridsquare = Locator.Parse("KN01") },
        ]);
        var mapa = new VistaModeloMapa(new PuntosDelCuaderno(repositorio, ResolutorDxcc.Predeterminado), ResolutorDxcc.Predeterminado);
        mapa.FijarEstacion(Locator.Parse("JN00AA"), "EA8DLF");
        await mapa.CargarAsync();
        return (mapa, repositorio);
    }

    [Fact]
    public async Task La_casilla_Contactos_quita_y_pone_las_marcas_del_cuaderno()
    {
        var (mapa, _) = await MontarAsync();
        mapa.Marcas.Count(m => m.Clase == ClaseDeMarca.Contacto).Should().Be(2);

        mapa.MostrarContactos = false;
        mapa.Marcas.Should().NotContain(m => m.Clase == ClaseDeMarca.Contacto);
        mapa.Resumen.Should().Be("Mapa vacío");

        mapa.MostrarContactos = true;
        mapa.Marcas.Count(m => m.Clase == ClaseDeMarca.Contacto).Should().Be(2);
    }

    [Fact]
    public async Task Un_contacto_nuevo_sale_al_recargar()
    {
        var (mapa, repositorio) = await MontarAsync();
        await repositorio.AnadirAsync(new Qso
        {
            Call = Indicativo.Crudo("PY4JW"), Band = Banda.Parse("10m"), Mode = Modo.Crudo("SSB", "USB"),
            InicioUtc = Origen.AddMinutes(9), Gridsquare = Locator.Parse("GH63"),
        });

        await mapa.CargarAsync();

        mapa.Marcas.Should().Contain(m => m.Etiqueta == "PY4JW");
    }

    [Fact]
    public async Task Quitar_trayecto_solo_se_enciende_con_trayecto()
    {
        var (mapa, _) = await MontarAsync();
        mapa.QuitarTrayectoCommand.CanExecute(null).Should().BeFalse();

        mapa.TrazarHasta(Coordenada.Desde(Locator.Parse("JO20")), "ON4BW");
        mapa.Trayectos.Should().ContainSingle();
        mapa.QuitarTrayectoCommand.CanExecute(null).Should().BeTrue();

        mapa.QuitarTrayectoCommand.Execute(null);
        mapa.Trayectos.Should().BeEmpty();
        mapa.QuitarTrayectoCommand.CanExecute(null).Should().BeFalse();
    }
}
