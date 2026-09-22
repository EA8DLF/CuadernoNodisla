using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Nodisla.Cuaderno.Datos.Pruebas;

/// <summary>Lo que la migracion inicial tiene que dejar creado.</summary>
public sealed class EsquemaPruebas : IAsyncLifetime
{
    private CuadernoDePrueba cuaderno = null!;

    /// <inheritdoc/>
    public async Task InitializeAsync() => cuaderno = await CuadernoDePrueba.CrearAsync();

    /// <inheritdoc/>
    public async Task DisposeAsync() => await cuaderno.DisposeAsync();

    [Fact]
    public async Task LaMigracionCreaLasTablas()
    {
        var nombres = await NombresDeSqliteMaster("table");

        nombres.Should().Contain(["estacion", "qso", "qso_confirmacion", "qso_referencia",
            "qso_campo_extra", "qso_fts"]);
    }

    [Fact]
    public async Task LaMigracionCreaTodosLosIndicesEsperados()
    {
        var indices = await NombresDeSqliteMaster("index");

        indices.Should().Contain(
        [
            "ux_qso_natural",
            "ux_qso_uuid",
            "ix_qso_call",
            "ix_qso_dxcc_band_mode",
            "ix_qso_dxcc_fecha",
            "ix_qso_fecha",
            "ix_qso_band_mode_fecha",
            "ix_qso_grid",
            "ix_qso_estado",
            "ix_qso_cq",
            "ix_qso_itu",
            "ix_qso_contest",
            "ix_qso_estacion",
            "ux_conf_qso_servicio",
            "ix_conf_servicio_rec",
            "ix_conf_servicio_env",
            "ix_ref_award_ref",
            "ix_ref_qso",
            "ix_ref_propia",
            "ix_ref_programa",
            "ux_campo_extra",
            "ux_estacion_nombre",
        ]);
    }

    [Fact]
    public async Task LaMigracionCreaLosDisparadoresDelIndiceDeTexto()
    {
        var disparadores = await NombresDeSqliteMaster("trigger");

        disparadores.Should().Contain(["qso_fts_alta", "qso_fts_baja", "qso_fts_cambio"]);
    }

    [Fact]
    public async Task LaBaseQuedaEnModoWalYConClavesAjenas()
    {
        await using var conexion = await cuaderno.AbrirConexionAsync();

        (await Escalar(conexion, "PRAGMA journal_mode;")).Should().Be("wal");
        (await Escalar(conexion, "PRAGMA foreign_keys;")).Should().Be("1");
    }

    [Fact]
    public async Task ElEsquemaTieneTodasLasColumnasDelModelo()
    {
        await using var contexto = cuaderno.CrearContexto();
        await using var conexion = await cuaderno.AbrirConexionAsync();

        foreach (var entidad in contexto.Model.GetEntityTypes())
        {
            var tabla = entidad.GetTableName();
            tabla.Should().NotBeNull();

            var columnas = await ColumnasDe(conexion, tabla!);
            foreach (var propiedad in entidad.GetProperties())
            {
                columnas.Should().Contain(
                    propiedad.GetColumnName(),
                    $"la propiedad {entidad.ClrType.Name}.{propiedad.Name} necesita su columna");
            }
        }
    }

    [Fact]
    public async Task LaClaveNaturalNoAdmiteDuplicados()
    {
        await using var contexto = cuaderno.CrearContexto();

        contexto.Qsos.Add(FabricaDeContactos.Crear());
        await contexto.SaveChangesAsync();

        contexto.Qsos.Add(FabricaDeContactos.Crear());
        var guardar = async () => await contexto.SaveChangesAsync();

        await guardar.Should().ThrowAsync<DbUpdateException>();
    }

    private async Task<List<string>> NombresDeSqliteMaster(string tipo)
    {
        await using var conexion = await cuaderno.AbrirConexionAsync();
        await using var orden = conexion.CreateCommand();
        orden.CommandText = "SELECT name FROM sqlite_master WHERE type = $tipo";
        orden.Parameters.AddWithValue("$tipo", tipo);

        var nombres = new List<string>();
        await using var lector = await orden.ExecuteReaderAsync();
        while (await lector.ReadAsync())
        {
            nombres.Add(lector.GetString(0));
        }

        return nombres;
    }

    private static async Task<List<string>> ColumnasDe(SqliteConnection conexion, string tabla)
    {
        await using var orden = conexion.CreateCommand();
        orden.CommandText = $"SELECT name FROM pragma_table_info('{tabla}')";

        var columnas = new List<string>();
        await using var lector = await orden.ExecuteReaderAsync();
        while (await lector.ReadAsync())
        {
            columnas.Add(lector.GetString(0));
        }

        return columnas;
    }

    private static async Task<string?> Escalar(SqliteConnection conexion, string sql)
    {
        await using var orden = conexion.CreateCommand();
        orden.CommandText = sql;
        var valor = await orden.ExecuteScalarAsync();
        return valor?.ToString();
    }
}
