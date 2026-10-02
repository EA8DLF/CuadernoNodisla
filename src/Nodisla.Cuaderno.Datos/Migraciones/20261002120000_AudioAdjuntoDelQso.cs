using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nodisla.Cuaderno.Datos.Migraciones
{
    /// <summary>
    /// Audio de la recepcion adjunto a un contacto: el nombre del fichero, que vive en la
    /// carpeta de audio de los datos. Columna nueva y opcional: los contactos de antes quedan sin
    /// audio y nada mas cambia.
    /// </summary>
    public partial class AudioAdjuntoDelQso : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "audio_adjunto",
                table: "qso",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "audio_adjunto",
                table: "qso");
        }
    }
}
