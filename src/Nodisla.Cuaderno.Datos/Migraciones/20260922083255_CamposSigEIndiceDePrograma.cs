using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nodisla.Cuaderno.Datos.Migraciones
{
    /// <inheritdoc />
    public partial class CamposSigEIndiceDePrograma : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "sig",
                table: "qso",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE");

            migrationBuilder.AddColumn<string>(
                name: "sig_info",
                table: "qso",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE");

            migrationBuilder.CreateIndex(
                name: "ix_ref_programa",
                table: "qso_referencia",
                columns: new[] { "programa", "referencia" },
                filter: "programa IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_ref_programa",
                table: "qso_referencia");

            migrationBuilder.DropColumn(
                name: "sig",
                table: "qso");

            migrationBuilder.DropColumn(
                name: "sig_info",
                table: "qso");
        }
    }
}
