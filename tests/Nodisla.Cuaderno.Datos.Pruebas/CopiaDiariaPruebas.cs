using FluentAssertions;
using Microsoft.Data.Sqlite;

namespace Nodisla.Cuaderno.Datos.Pruebas;

/// <summary>
/// La copia periodica del cuaderno: una al dia como mucho, hecha con el cuaderno en uso.
/// </summary>
/// <remarks>
/// Hasta noviembre de 2026 el cuaderno solo se copiaba antes de una migracion: en la carpeta
/// de copias del operador habia tres, todas viejas. Un disco que falla entre dos versiones
/// del programa se llevaba meses de contactos.
/// </remarks>
public sealed class CopiaDiariaPruebas : IAsyncLifetime
{
    private static readonly DateTime Hoy = new(2026, 11, 15, 12, 0, 0, DateTimeKind.Utc);

    private CuadernoDePrueba cuaderno = null!;
    private ContextoCuaderno contexto = null!;

    public async Task InitializeAsync()
    {
        cuaderno = await CuadernoDePrueba.CrearAsync();
        contexto = cuaderno.CrearContexto();
    }

    public async Task DisposeAsync() => await cuaderno.DisposeAsync();

    private MigradorDeCuaderno Migrador() => new(contexto, cuaderno.Opciones);

    private string[] Copias() =>
        Directory.Exists(cuaderno.Opciones.CarpetaDeCopiasEfectiva)
            ? Directory.GetFiles(cuaderno.Opciones.CarpetaDeCopiasEfectiva)
            : [];

    [Fact]
    public async Task SinCopiasSeHaceLaPrimera()
    {
        var copia = await Migrador().CrearCopiaSiHaceFaltaAsync(TimeSpan.FromHours(24), Hoy);

        copia.Should().NotBeNull();
        Path.GetFileName(copia).Should().Be("cuaderno-20261115-120000.sqlite");
        Copias().Should().ContainSingle();
    }

    [Fact]
    public async Task ConUnaCopiaDeHaceMenosDeUnDiaNoSeHaceOtra()
    {
        var migrador = Migrador();
        await migrador.CrearCopiaSiHaceFaltaAsync(TimeSpan.FromHours(24), Hoy);

        var otra = await migrador.CrearCopiaSiHaceFaltaAsync(TimeSpan.FromHours(24), Hoy.AddHours(23));

        otra.Should().BeNull();
        Copias().Should().ContainSingle();
    }

    [Fact]
    public async Task PasadoUnDiaSeHaceOtra()
    {
        var migrador = Migrador();
        await migrador.CrearCopiaSiHaceFaltaAsync(TimeSpan.FromHours(24), Hoy);

        var otra = await migrador.CrearCopiaSiHaceFaltaAsync(TimeSpan.FromHours(24), Hoy.AddHours(25));

        otra.Should().NotBeNull();
        Copias().Should().HaveCount(2);
    }

    [Fact]
    public async Task LaCopiaSeHaceConElCuadernoAbiertoYEscribiendo()
    {
        // Otra conexion con una escritura sin volcar: es como esta el cuaderno mientras se
        // opera. Copiar el fichero a pelo se dejaria fuera lo que hay en el WAL.
        await using var otra = await cuaderno.AbrirConexionAsync();
        await using (var orden = otra.CreateCommand())
        {
            orden.CommandText = "CREATE TABLE prueba_de_copia (n INTEGER); INSERT INTO prueba_de_copia VALUES (7);";
            await orden.ExecuteNonQueryAsync();
        }

        var copia = await Migrador().CrearCopiaSiHaceFaltaAsync(TimeSpan.FromHours(24), Hoy);

        await using var conexion = new SqliteConnection($"Data Source={copia};Mode=ReadOnly;Pooling=False");
        await conexion.OpenAsync();
        await using var lectura = conexion.CreateCommand();
        lectura.CommandText = "SELECT n FROM prueba_de_copia";
        (await lectura.ExecuteScalarAsync()).Should().Be(7L);
    }

    [Fact]
    public async Task LaCopiaDiariaTambienPodaLasViejas()
    {
        cuaderno.Opciones.CopiasAConservar = 2;
        var migrador = Migrador();

        for (var dia = 0; dia < 4; dia++)
        {
            await migrador.CrearCopiaSiHaceFaltaAsync(TimeSpan.FromHours(24), Hoy.AddDays(dia));
        }

        Copias().Should().HaveCount(2);
    }

    [Fact]
    public async Task SinCuadernoNoHayNadaQueCopiar()
    {
        var opciones = new OpcionesCuaderno
        {
            Ruta = Path.Combine(Path.GetDirectoryName(cuaderno.Opciones.Ruta)!, "no-existe.sqlite"),
        };

        var copia = await new MigradorDeCuaderno(contexto, opciones)
            .CrearCopiaSiHaceFaltaAsync(TimeSpan.FromHours(24), Hoy);

        copia.Should().BeNull();
        Directory.Exists(opciones.CarpetaDeCopiasEfectiva).Should().BeFalse();
    }
}
