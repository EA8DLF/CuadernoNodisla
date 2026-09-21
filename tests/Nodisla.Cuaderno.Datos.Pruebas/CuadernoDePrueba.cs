using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Datos.Pruebas;

/// <summary>
/// Un cuaderno de verdad en un fichero temporal. No se usa SQLite en memoria a proposito:
/// hay que comprobar WAL, FTS5, disparadores y claves ajenas tal y como se comportan en disco.
/// </summary>
public sealed class CuadernoDePrueba : IAsyncDisposable
{
    private readonly List<ContextoCuaderno> contextos = [];
    private readonly string carpeta;

    private CuadernoDePrueba(string carpeta, OpcionesCuaderno opciones)
    {
        this.carpeta = carpeta;
        Opciones = opciones;
    }

    /// <summary>Rutas del cuaderno de prueba.</summary>
    public OpcionesCuaderno Opciones { get; }

    /// <summary>Crea el fichero y le aplica la migracion inicial.</summary>
    public static async Task<CuadernoDePrueba> CrearAsync()
    {
        var carpeta = Path.Combine(
            Path.GetTempPath(),
            "cuaderno-nodisla-pruebas",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(carpeta);

        var opciones = new OpcionesCuaderno { Ruta = Path.Combine(carpeta, "cuaderno.sqlite") };
        var cuaderno = new CuadernoDePrueba(carpeta, opciones);

        var contexto = cuaderno.CrearContexto();
        var migrador = new MigradorDeCuaderno(contexto, opciones);
        await migrador.AplicarMigracionesAsync();
        return cuaderno;
    }

    /// <summary>Abre un contexto nuevo sobre el mismo fichero.</summary>
    public ContextoCuaderno CrearContexto()
    {
        var contexto = new ContextoCuaderno(
            new DbContextOptionsBuilder<ContextoCuaderno>()
                .UseSqlite(Opciones.CadenaDeConexion)
                .Options);
        contextos.Add(contexto);
        return contexto;
    }

    /// <summary>Abre una conexion suelta para mirar el esquema con SQL crudo.</summary>
    public async Task<SqliteConnection> AbrirConexionAsync()
    {
        var conexion = new SqliteConnection(Opciones.CadenaDeConexion);
        await conexion.OpenAsync();
        return conexion;
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        foreach (var contexto in contextos)
        {
            await contexto.DisposeAsync();
        }

        SqliteConnection.ClearAllPools();

        try
        {
            Directory.Delete(carpeta, recursive: true);
        }
        catch (IOException)
        {
            // Si Windows aun tiene el fichero cogido, se queda en el temporal; no es grave.
        }
    }
}

/// <summary>Contactos de ejemplo para las pruebas.</summary>
public static class FabricaDeContactos
{
    /// <summary>Instante de referencia, ya recortado al segundo.</summary>
    public static readonly DateTimeOffset Instante =
        new(2026, 5, 23, 9, 56, 31, TimeSpan.Zero);

    /// <summary>Crea un contacto completo y valido.</summary>
    public static Qso Crear(
        string call = "DL1ABC",
        string banda = "20m",
        string modo = "MFSK",
        string? submodo = "FT8",
        DateTimeOffset? inicio = null,
        int dxcc = 230)
    {
        return new Qso
        {
            Call = Indicativo.Parse(call),
            Band = Banda.Parse(banda),
            Mode = Modo.Parse(modo, submodo),
            InicioUtc = inicio ?? Instante,
            Freq = Frecuencia.DesdeMegahercios(14.074m),
            RstSent = Informe.Parse("-12"),
            RstRcvd = Informe.Parse("-09"),
            Gridsquare = Locator.Parse("JO31"),
            Dxcc = dxcc,
            Country = "Germany",
            Name = "Hans",
            Qth = "Koln",
            Comentario = "Contacto de prueba",
            Notas = "Sin novedad",
            StationCallsign = Indicativo.Parse("EA8DLF"),
            MyGridsquare = Locator.Parse("IL18sn"),
            Operator = "EA8DLF",
            TxPwr = 50,
            Origen = "MANUAL",
        };
    }

    /// <summary>Genera contactos distintos entre si, con clave natural unica.</summary>
    public static IEnumerable<Qso> Generar(int cuantos)
    {
        string[] bandas = ["160m", "80m", "40m", "20m", "15m", "10m", "2m"];
        string[][] modos =
        [
            ["SSB", null!],
            ["CW", null!],
            ["MFSK", "FT8"],
            ["MFSK", "FT4"],
            ["RTTY", null!],
        ];

        for (var i = 0; i < cuantos; i++)
        {
            var modo = modos[i % modos.Length];
            yield return Crear(
                call: $"EA{i % 10}{(char)('A' + (i % 26))}{(char)('A' + (i / 26 % 26))}{(char)('A' + (i / 676 % 26))}",
                banda: bandas[i % bandas.Length],
                modo: modo[0],
                submodo: modo[1],
                inicio: Instante.AddMinutes(i),
                dxcc: 200 + (i % 50));
        }
    }
}
