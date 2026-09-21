using FluentAssertions;
using Microsoft.Data.Sqlite;
using Nodisla.Cuaderno.Datos.Informes;
using Nodisla.Cuaderno.Datos.Repositorios;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Datos.Pruebas;

/// <summary>Las consultas de informe con Dapper y la copia de seguridad previa a migrar.</summary>
public sealed class InformesYCopiasPruebas : IAsyncLifetime
{
    private CuadernoDePrueba cuaderno = null!;
    private ContextoCuaderno contexto = null!;

    /// <inheritdoc/>
    public async Task InitializeAsync()
    {
        cuaderno = await CuadernoDePrueba.CrearAsync();
        contexto = cuaderno.CrearContexto();
    }

    /// <inheritdoc/>
    public async Task DisposeAsync() => await cuaderno.DisposeAsync();

    private async Task SembrarAsync()
    {
        var repositorio = new RepositorioQso(contexto);

        var confirmado = FabricaDeContactos.Crear(banda: "20m", modo: "MFSK", submodo: "FT8");
        confirmado.Confirmaciones.Add(new QsoConfirmacion
        {
            Medio = MedioDeConfirmacion.Lotw,
            Recibido = EstadoDeConfirmacion.Confirmado,
        });
        await repositorio.AnadirAsync(confirmado);

        await repositorio.AnadirAsync(FabricaDeContactos.Crear(
            call: "F5XYZ", banda: "20m", modo: "SSB", submodo: null, dxcc: 227));
        await repositorio.AnadirAsync(FabricaDeContactos.Crear(
            call: "G0ABC", banda: "40m", modo: "CW", submodo: null, dxcc: 223));
    }

    [Fact]
    public async Task ElResumenPorBandaCuentaContactosYConfirmaciones()
    {
        await SembrarAsync();
        var informes = new ConsultasDeInforme(contexto);

        var filas = await informes.PorBandaAsync();

        filas.Should().HaveCount(2);
        filas.Single(f => f.Band == "20m").Contactos.Should().Be(2);
        filas.Single(f => f.Band == "20m").Confirmados.Should().Be(1);
        filas.Single(f => f.Band == "40m").Confirmados.Should().Be(0);
    }

    [Fact]
    public async Task ElResumenPorModoAgrupaPorModoPrincipal()
    {
        await SembrarAsync();
        var informes = new ConsultasDeInforme(contexto);

        var filas = await informes.PorModoAsync();

        filas.Select(f => f.Mode).Should().BeEquivalentTo(["MFSK", "SSB", "CW"]);
    }

    [Fact]
    public async Task LaMatrizDxccPorBandaSaleDeUnaSolaConsulta()
    {
        await SembrarAsync();
        var informes = new ConsultasDeInforme(contexto);

        var casillas = await informes.MatrizDxccPorBandaAsync();

        casillas.Should().HaveCount(3);
        casillas.Single(c => c.Dxcc == 230 && c.Band == "20m").Confirmados.Should().Be(1);
    }

    [Fact]
    public async Task LaNovedadDistingueEntidadBandaYHueco()
    {
        await SembrarAsync();
        var informes = new ConsultasDeInforme(contexto);

        var enElMismoHueco = await informes.ConsultarNovedadAsync(
            230, Banda.Parse("20m"), Modo.Parse("MFSK", "FT8"));
        enElMismoHueco.Visto.Should().BeTrue();
        enElMismoHueco.VistoEnBanda.Should().BeTrue();
        enElMismoHueco.VistoEnHueco.Should().BeTrue();

        var otraBanda = await informes.ConsultarNovedadAsync(230, Banda.Parse("15m"), Modo.Parse("CW"));
        otraBanda.Visto.Should().BeTrue();
        otraBanda.VistoEnBanda.Should().BeFalse();
        otraBanda.VistoEnHueco.Should().BeFalse();

        var nueva = await informes.ConsultarNovedadAsync(1, Banda.Parse("20m"), Modo.Parse("CW"));
        nueva.Visto.Should().BeFalse();
    }

    [Fact]
    public async Task LosTotalesResumenElCuaderno()
    {
        await SembrarAsync();
        var informes = new ConsultasDeInforme(contexto);

        var totales = await informes.TotalesAsync();

        totales.Contactos.Should().Be(3);
        totales.Indicativos.Should().Be(3);
        totales.Entidades.Should().Be(3);
        totales.PrimeroUtc.Should().Be("2026-05-23 09:56:31");
    }

    [Fact]
    public async Task LaCopiaDeSeguridadEsUnCuadernoCompletoYUtilizable()
    {
        await SembrarAsync();
        var migrador = new MigradorDeCuaderno(contexto, cuaderno.Opciones);

        var copia = await migrador.CrearCopiaAsync();

        File.Exists(copia).Should().BeTrue();
        Path.GetDirectoryName(copia).Should().Be(cuaderno.Opciones.CarpetaDeCopiasEfectiva);

        await using var conexion = new SqliteConnection($"Data Source={copia}");
        await conexion.OpenAsync();
        await using var orden = conexion.CreateCommand();
        orden.CommandText = "SELECT COUNT(*) FROM qso";
        (await orden.ExecuteScalarAsync()).Should().Be(3L);
    }

    [Fact]
    public async Task NoSeVuelveAMigrarSiNoHayNadaPendiente()
    {
        var migrador = new MigradorDeCuaderno(contexto, cuaderno.Opciones);

        // La migracion inicial ya se aplico al crear el cuaderno de prueba.
        (await migrador.AplicarMigracionesAsync()).Should().BeNull();
        Directory.Exists(cuaderno.Opciones.CarpetaDeCopiasEfectiva).Should().BeFalse();
    }

    [Fact]
    public async Task LasCopiasViejasSeVanBorrando()
    {
        cuaderno.Opciones.CopiasAConservar = 2;
        var migrador = new MigradorDeCuaderno(contexto, cuaderno.Opciones);

        for (var i = 0; i < 4; i++)
        {
            await migrador.CrearCopiaAsync();
        }

        Directory.GetFiles(cuaderno.Opciones.CarpetaDeCopiasEfectiva).Should().HaveCount(2);
    }
}
