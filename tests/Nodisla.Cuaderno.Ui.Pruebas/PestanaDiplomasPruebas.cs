using System.IO;
using FluentAssertions;
using Nodisla.Cuaderno.Dominio.Dxcc;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Ui.Desarrollo;
using Nodisla.Cuaderno.Ui.VistaModelos;

namespace Nodisla.Cuaderno.Ui.Pruebas;

/// <summary>
/// Los mandos de la pestaña Diplomas: marcar uno lo guarda y lo calcula, «Ver detalle» trae
/// sus referencias y «Recontar» vuelve a calcular. Con el motor de desarrollo y una carpeta
/// temporal, sin la base de diplomas de verdad.
/// </summary>
public sealed class PestanaDiplomasPruebas : IDisposable
{
    private readonly string _carpeta = Path.Combine(Path.GetTempPath(), "cuaderno-diplomas-" + Guid.NewGuid().ToString("N"));

    public PestanaDiplomasPruebas() => Directory.CreateDirectory(_carpeta);

    public void Dispose()
    {
        try { Directory.Delete(_carpeta, recursive: true); }
        catch (IOException) { }
    }

    private static readonly DateTimeOffset Origen = new(2025, 10, 13, 16, 0, 0, TimeSpan.Zero);

    private DiplomasDeDesarrollo Motor() => new(
        [
            new Qso { Call = Indicativo.Crudo("ON4BW"), Band = Banda.Parse("10m"), Mode = Modo.Crudo("SSB", "USB"), InicioUtc = Origen, Dxcc = 209 },
            new Qso { Call = Indicativo.Crudo("Z36T"), Band = Banda.Parse("15m"), Mode = Modo.Crudo("SSB", "USB"), InicioUtc = Origen.AddMinutes(5), Dxcc = 502 },
        ],
        ResolutorDxcc.Predeterminado,
        _carpeta);

    private static async Task Esperar(Func<bool> condicion)
    {
        // El marcado dispara un guardado asincrono sin espera propia (viene de una casilla).
        for (var i = 0; i < 200 && !condicion(); i++) await Task.Delay(10);
    }

    [Fact]
    public async Task Marcar_un_diploma_lo_guarda_y_sale_en_progreso()
    {
        var modelo = new VistaModeloDiplomas(Motor());
        await modelo.CargarAsync();
        modelo.SinEleccion.Should().BeTrue();

        var dxcc = modelo.Catalogo.First(f => f.Clave.StartsWith("DXCC/", StringComparison.Ordinal));
        dxcc.Elegido = true;
        await Esperar(() => modelo.Mios.Count > 0);

        modelo.Mios.Should().ContainSingle().Which.Clave.Should().Be(dxcc.Clave);
        (await Motor().MisDiplomasAsync()).Should().Contain(dxcc.Clave, "la elección se guarda y sobrevive al cierre");
    }

    [Fact]
    public async Task Ver_detalle_trae_las_referencias_y_recontar_no_pierde_la_eleccion()
    {
        var modelo = new VistaModeloDiplomas(Motor());
        await modelo.CargarAsync();
        var dxcc = modelo.Catalogo.First(f => f.Clave.StartsWith("DXCC/", StringComparison.Ordinal));
        dxcc.Elegido = true;
        await Esperar(() => modelo.Mios.Count > 0);

        await modelo.VerElDetalleCommand.ExecuteAsync(dxcc);

        modelo.TituloDelDetalle.Should().Contain(dxcc.Nombre);
        modelo.TotalDeReferencias.Should().BeGreaterThan(0);
        modelo.Referencias.Should().NotBeEmpty();

        await modelo.RefrescarCommand.ExecuteAsync(null);
        modelo.Mios.Should().ContainSingle();
    }
}
