using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nodisla.Cuaderno.Datos.Migraciones
{
    /// <inheritdoc />
    public partial class CamposDeAntenaCondicionesYNaturaleza : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "a_index",
                table: "qso",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "ant_az",
                table: "qso",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "ant_el",
                table: "qso",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "distance",
                table: "qso",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "iota_island_id",
                table: "qso",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "k_index",
                table: "qso",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "my_name",
                table: "qso",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "qslmsg",
                table: "qso",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "qso_complete",
                table: "qso",
                type: "TEXT",
                nullable: true,
                collation: "NOCASE");

            migrationBuilder.AddColumn<bool>(
                name: "qso_random",
                table: "qso",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "sfi",
                table: "qso",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "swl",
                table: "qso",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "a_index",
                table: "qso");

            migrationBuilder.DropColumn(
                name: "ant_az",
                table: "qso");

            migrationBuilder.DropColumn(
                name: "ant_el",
                table: "qso");

            migrationBuilder.DropColumn(
                name: "distance",
                table: "qso");

            migrationBuilder.DropColumn(
                name: "iota_island_id",
                table: "qso");

            migrationBuilder.DropColumn(
                name: "k_index",
                table: "qso");

            migrationBuilder.DropColumn(
                name: "my_name",
                table: "qso");

            migrationBuilder.DropColumn(
                name: "qslmsg",
                table: "qso");

            migrationBuilder.DropColumn(
                name: "qso_complete",
                table: "qso");

            migrationBuilder.DropColumn(
                name: "qso_random",
                table: "qso");

            migrationBuilder.DropColumn(
                name: "sfi",
                table: "qso");

            migrationBuilder.DropColumn(
                name: "swl",
                table: "qso");
        }
    }
}
