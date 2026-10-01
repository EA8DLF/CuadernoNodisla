using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nodisla.Cuaderno.Datos.Migraciones
{
    /// <inheritdoc />
    public partial class DiplomasEmitidos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "diploma_emitido",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    serie = table.Column<string>(type: "TEXT", nullable: false, collation: "NOCASE"),
                    numero = table.Column<int>(type: "INTEGER", nullable: false),
                    origen = table.Column<string>(type: "TEXT", nullable: false, collation: "NOCASE"),
                    indicativo = table.Column<string>(type: "TEXT", nullable: false, collation: "NOCASE"),
                    nombre = table.Column<string>(type: "TEXT", nullable: true),
                    nombre_del_diploma = table.Column<string>(type: "TEXT", nullable: false),
                    categoria = table.Column<string>(type: "TEXT", nullable: true),
                    emitido_utc = table.Column<string>(type: "TEXT", nullable: false),
                    plantilla_id = table.Column<string>(type: "TEXT", nullable: true),
                    plantilla_nombre = table.Column<string>(type: "TEXT", nullable: true),
                    referencias = table.Column<int>(type: "INTEGER", nullable: false),
                    qsos = table.Column<int>(type: "INTEGER", nullable: false),
                    correo = table.Column<string>(type: "TEXT", nullable: true),
                    enviado_utc = table.Column<string>(type: "TEXT", nullable: true),
                    datos = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_diploma_emitido", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_diploma_emitido",
                table: "diploma_emitido",
                column: "emitido_utc",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "ix_diploma_indicativo",
                table: "diploma_emitido",
                column: "indicativo");

            migrationBuilder.CreateIndex(
                name: "ux_diploma_serie_numero",
                table: "diploma_emitido",
                columns: new[] { "serie", "numero" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "diploma_emitido");
        }
    }
}
