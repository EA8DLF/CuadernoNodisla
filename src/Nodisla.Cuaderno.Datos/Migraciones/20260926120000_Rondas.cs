using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nodisla.Cuaderno.Datos.Migraciones
{
    /// <inheritdoc />
    public partial class Rondas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ronda_de_control",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    nombre = table.Column<string>(type: "TEXT", nullable: false),
                    club_o_evento = table.Column<string>(type: "TEXT", nullable: true),
                    band = table.Column<string>(type: "TEXT", nullable: false, collation: "NOCASE"),
                    mode = table.Column<string>(type: "TEXT", nullable: false, collation: "NOCASE"),
                    freq = table.Column<double>(type: "REAL", nullable: false),
                    estacion_id = table.Column<long>(type: "INTEGER", nullable: true),
                    notas = table.Column<string>(type: "TEXT", nullable: true),
                    inicio_utc = table.Column<string>(type: "TEXT", nullable: false),
                    fin_utc = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ronda_de_control", x => x.id);
                    table.ForeignKey(
                        name: "FK_ronda_de_control_estacion_estacion_id",
                        column: x => x.estacion_id,
                        principalTable: "estacion",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "participante_de_ronda",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ronda_id = table.Column<long>(type: "INTEGER", nullable: false),
                    call = table.Column<string>(type: "TEXT", nullable: false, collation: "NOCASE"),
                    entrada_utc = table.Column<string>(type: "TEXT", nullable: false),
                    rst_enviado = table.Column<string>(type: "TEXT", nullable: false),
                    rst_recibido = table.Column<string>(type: "TEXT", nullable: false),
                    comentario = table.Column<string>(type: "TEXT", nullable: true),
                    dxcc = table.Column<int>(type: "INTEGER", nullable: true),
                    pais = table.Column<string>(type: "TEXT", nullable: true),
                    trabajado = table.Column<bool>(type: "INTEGER", nullable: false),
                    qso_id = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_participante_de_ronda", x => x.id);
                    table.ForeignKey(
                        name: "FK_participante_de_ronda_ronda_de_control_ronda_id",
                        column: x => x.ronda_id,
                        principalTable: "ronda_de_control",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ronda_abierta",
                table: "ronda_de_control",
                column: "fin_utc",
                filter: "fin_utc IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_ronda_inicio",
                table: "ronda_de_control",
                column: "inicio_utc",
                descending: new[] { true });

            migrationBuilder.CreateIndex(
                name: "IX_ronda_de_control_estacion_id",
                table: "ronda_de_control",
                column: "estacion_id");

            migrationBuilder.CreateIndex(
                name: "ix_participante_ronda",
                table: "participante_de_ronda",
                columns: new[] { "ronda_id", "entrada_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "participante_de_ronda");

            migrationBuilder.DropTable(
                name: "ronda_de_control");
        }
    }
}
