using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nodisla.Cuaderno.Datos.Migraciones
{
    /// <inheritdoc />
    public partial class Inicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "estacion",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    nombre_perfil = table.Column<string>(type: "TEXT", nullable: false, collation: "NOCASE"),
                    station_callsign = table.Column<string>(type: "TEXT", nullable: false, collation: "NOCASE"),
                    @operator = table.Column<string>(name: "operator", type: "TEXT", nullable: true, collation: "NOCASE"),
                    owner_callsign = table.Column<string>(type: "TEXT", nullable: true, collation: "NOCASE"),
                    my_name = table.Column<string>(type: "TEXT", nullable: true),
                    my_street = table.Column<string>(type: "TEXT", nullable: true),
                    my_city = table.Column<string>(type: "TEXT", nullable: true),
                    my_postal_code = table.Column<string>(type: "TEXT", nullable: true),
                    my_state = table.Column<string>(type: "TEXT", nullable: true, collation: "NOCASE"),
                    my_cnty = table.Column<string>(type: "TEXT", nullable: true, collation: "NOCASE"),
                    my_country = table.Column<string>(type: "TEXT", nullable: true, collation: "NOCASE"),
                    my_dxcc = table.Column<int>(type: "INTEGER", nullable: true),
                    my_cq_zone = table.Column<int>(type: "INTEGER", nullable: true),
                    my_itu_zone = table.Column<int>(type: "INTEGER", nullable: true),
                    my_iota = table.Column<string>(type: "TEXT", nullable: true, collation: "NOCASE"),
                    my_gridsquare = table.Column<string>(type: "TEXT", nullable: false, collation: "NOCASE"),
                    my_lat = table.Column<double>(type: "REAL", nullable: true),
                    my_lon = table.Column<double>(type: "REAL", nullable: true),
                    my_altitude = table.Column<double>(type: "REAL", nullable: true),
                    my_rig = table.Column<string>(type: "TEXT", nullable: true),
                    my_antenna = table.Column<string>(type: "TEXT", nullable: true),
                    my_sig = table.Column<string>(type: "TEXT", nullable: true),
                    my_sig_info = table.Column<string>(type: "TEXT", nullable: true),
                    tx_pwr_defecto = table.Column<double>(type: "REAL", nullable: true),
                    predeterminado = table.Column<bool>(type: "INTEGER", nullable: false),
                    activo = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_estacion", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "qso",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    uuid = table.Column<string>(type: "TEXT", nullable: false),
                    estacion_id = table.Column<long>(type: "INTEGER", nullable: true),
                    call = table.Column<string>(type: "TEXT", nullable: false, collation: "NOCASE"),
                    band = table.Column<string>(type: "TEXT", nullable: false, collation: "NOCASE"),
                    mode = table.Column<string>(type: "TEXT", nullable: false, collation: "NOCASE"),
                    qso_inicio_utc = table.Column<string>(type: "TEXT", nullable: false),
                    qso_fin_utc = table.Column<string>(type: "TEXT", nullable: true),
                    freq = table.Column<double>(type: "REAL", nullable: false),
                    freq_rx = table.Column<double>(type: "REAL", nullable: true),
                    band_rx = table.Column<string>(type: "TEXT", nullable: false, collation: "NOCASE"),
                    rst_sent = table.Column<string>(type: "TEXT", nullable: false),
                    rst_rcvd = table.Column<string>(type: "TEXT", nullable: false),
                    tx_pwr = table.Column<double>(type: "REAL", nullable: true),
                    rx_pwr = table.Column<double>(type: "REAL", nullable: true),
                    prop_mode = table.Column<string>(type: "TEXT", nullable: true, collation: "NOCASE"),
                    sat_name = table.Column<string>(type: "TEXT", nullable: true, collation: "NOCASE"),
                    sat_mode = table.Column<string>(type: "TEXT", nullable: true, collation: "NOCASE"),
                    name = table.Column<string>(type: "TEXT", nullable: true),
                    address = table.Column<string>(type: "TEXT", nullable: true),
                    qth = table.Column<string>(type: "TEXT", nullable: true),
                    email = table.Column<string>(type: "TEXT", nullable: true),
                    web = table.Column<string>(type: "TEXT", nullable: true),
                    gridsquare = table.Column<string>(type: "TEXT", nullable: false, collation: "NOCASE"),
                    gridsquare_ext = table.Column<string>(type: "TEXT", nullable: true, collation: "NOCASE"),
                    lat = table.Column<double>(type: "REAL", nullable: true),
                    lon = table.Column<double>(type: "REAL", nullable: true),
                    altitude = table.Column<double>(type: "REAL", nullable: true),
                    cont = table.Column<string>(type: "TEXT", nullable: true, collation: "NOCASE"),
                    country = table.Column<string>(type: "TEXT", nullable: true, collation: "NOCASE"),
                    dxcc = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    cqz = table.Column<int>(type: "INTEGER", nullable: true),
                    ituz = table.Column<int>(type: "INTEGER", nullable: true),
                    pfx = table.Column<string>(type: "TEXT", nullable: true, collation: "NOCASE"),
                    state = table.Column<string>(type: "TEXT", nullable: true, collation: "NOCASE"),
                    cnty = table.Column<string>(type: "TEXT", nullable: true, collation: "NOCASE"),
                    region = table.Column<string>(type: "TEXT", nullable: true, collation: "NOCASE"),
                    darc_dok = table.Column<string>(type: "TEXT", nullable: true, collation: "NOCASE"),
                    age = table.Column<double>(type: "REAL", nullable: true),
                    rig = table.Column<string>(type: "TEXT", nullable: true),
                    silent_key = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    eq_call = table.Column<string>(type: "TEXT", nullable: true, collation: "NOCASE"),
                    contacted_op = table.Column<string>(type: "TEXT", nullable: true, collation: "NOCASE"),
                    qsl_via = table.Column<string>(type: "TEXT", nullable: true, collation: "NOCASE"),
                    station_callsign = table.Column<string>(type: "TEXT", nullable: false, collation: "NOCASE"),
                    @operator = table.Column<string>(name: "operator", type: "TEXT", nullable: true, collation: "NOCASE"),
                    owner_callsign = table.Column<string>(type: "TEXT", nullable: true, collation: "NOCASE"),
                    my_gridsquare = table.Column<string>(type: "TEXT", nullable: false, collation: "NOCASE"),
                    my_city = table.Column<string>(type: "TEXT", nullable: true),
                    my_state = table.Column<string>(type: "TEXT", nullable: true, collation: "NOCASE"),
                    my_cnty = table.Column<string>(type: "TEXT", nullable: true, collation: "NOCASE"),
                    my_country = table.Column<string>(type: "TEXT", nullable: true, collation: "NOCASE"),
                    my_dxcc = table.Column<int>(type: "INTEGER", nullable: true),
                    my_cq_zone = table.Column<int>(type: "INTEGER", nullable: true),
                    my_itu_zone = table.Column<int>(type: "INTEGER", nullable: true),
                    my_lat = table.Column<double>(type: "REAL", nullable: true),
                    my_lon = table.Column<double>(type: "REAL", nullable: true),
                    my_altitude = table.Column<double>(type: "REAL", nullable: true),
                    my_rig = table.Column<string>(type: "TEXT", nullable: true),
                    my_antenna = table.Column<string>(type: "TEXT", nullable: true),
                    contest_id = table.Column<string>(type: "TEXT", nullable: true, collation: "NOCASE"),
                    stx_string = table.Column<string>(type: "TEXT", nullable: true),
                    stx = table.Column<int>(type: "INTEGER", nullable: true),
                    srx_string = table.Column<string>(type: "TEXT", nullable: true),
                    srx = table.Column<int>(type: "INTEGER", nullable: true),
                    comment = table.Column<string>(type: "TEXT", nullable: true),
                    notes = table.Column<string>(type: "TEXT", nullable: true),
                    origen = table.Column<string>(type: "TEXT", nullable: true, collation: "NOCASE"),
                    creado_utc = table.Column<string>(type: "TEXT", nullable: false),
                    modificado_utc = table.Column<string>(type: "TEXT", nullable: false),
                    submode = table.Column<string>(type: "TEXT", nullable: true, collation: "NOCASE")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_qso", x => x.id);
                    table.ForeignKey(
                        name: "FK_qso_estacion_estacion_id",
                        column: x => x.estacion_id,
                        principalTable: "estacion",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "qso_campo_extra",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    qso_id = table.Column<long>(type: "INTEGER", nullable: false),
                    campo = table.Column<string>(type: "TEXT", nullable: false, collation: "NOCASE"),
                    valor = table.Column<string>(type: "TEXT", nullable: false),
                    tipo_adif = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_qso_campo_extra", x => x.id);
                    table.ForeignKey(
                        name: "FK_qso_campo_extra_qso_qso_id",
                        column: x => x.qso_id,
                        principalTable: "qso",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "qso_confirmacion",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    qso_id = table.Column<long>(type: "INTEGER", nullable: false),
                    servicio = table.Column<string>(type: "TEXT", nullable: false, collation: "NOCASE"),
                    enviada = table.Column<string>(type: "TEXT", nullable: false),
                    recibida = table.Column<string>(type: "TEXT", nullable: false),
                    fecha_envio_utc = table.Column<string>(type: "TEXT", nullable: true),
                    fecha_recep_utc = table.Column<string>(type: "TEXT", nullable: true),
                    via_envio = table.Column<string>(type: "TEXT", nullable: false),
                    nota = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_qso_confirmacion", x => x.id);
                    table.ForeignKey(
                        name: "FK_qso_confirmacion_qso_qso_id",
                        column: x => x.qso_id,
                        principalTable: "qso",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "qso_referencia",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    qso_id = table.Column<long>(type: "INTEGER", nullable: false),
                    award_code = table.Column<string>(type: "TEXT", nullable: false, collation: "NOCASE"),
                    programa = table.Column<string>(type: "TEXT", nullable: true, collation: "NOCASE"),
                    referencia = table.Column<string>(type: "TEXT", nullable: false, collation: "NOCASE"),
                    propia = table.Column<bool>(type: "INTEGER", nullable: false),
                    descripcion = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_qso_referencia", x => x.id);
                    table.ForeignKey(
                        name: "FK_qso_referencia_qso_qso_id",
                        column: x => x.qso_id,
                        principalTable: "qso",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_estacion_nombre",
                table: "estacion",
                column: "nombre_perfil",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_qso_band_mode_fecha",
                table: "qso",
                columns: new[] { "band", "mode", "qso_inicio_utc" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "ix_qso_call",
                table: "qso",
                columns: new[] { "call", "qso_inicio_utc" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_qso_contest",
                table: "qso",
                columns: new[] { "contest_id", "qso_inicio_utc" },
                filter: "contest_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_qso_cq",
                table: "qso",
                columns: new[] { "cqz", "band", "mode" },
                filter: "cqz IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_qso_dxcc_band_mode",
                table: "qso",
                columns: new[] { "dxcc", "band", "mode" });

            migrationBuilder.CreateIndex(
                name: "ix_qso_dxcc_fecha",
                table: "qso",
                columns: new[] { "dxcc", "qso_inicio_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_qso_estacion",
                table: "qso",
                columns: new[] { "estacion_id", "qso_inicio_utc" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_qso_estado",
                table: "qso",
                columns: new[] { "dxcc", "state" },
                filter: "state IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_qso_fecha",
                table: "qso",
                column: "qso_inicio_utc",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "ix_qso_grid",
                table: "qso",
                column: "gridsquare",
                filter: "gridsquare <> ''");

            migrationBuilder.CreateIndex(
                name: "ix_qso_itu",
                table: "qso",
                columns: new[] { "ituz", "band", "mode" },
                filter: "ituz IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_qso_natural",
                table: "qso",
                columns: new[] { "call", "band", "mode", "qso_inicio_utc" },
                unique: true,
                descending: new[] { false, false, false, true });

            migrationBuilder.CreateIndex(
                name: "ux_qso_uuid",
                table: "qso",
                column: "uuid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_campo_extra",
                table: "qso_campo_extra",
                columns: new[] { "qso_id", "campo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_conf_servicio_env",
                table: "qso_confirmacion",
                columns: new[] { "servicio", "enviada", "qso_id" });

            migrationBuilder.CreateIndex(
                name: "ix_conf_servicio_rec",
                table: "qso_confirmacion",
                columns: new[] { "servicio", "recibida", "qso_id" });

            migrationBuilder.CreateIndex(
                name: "ux_conf_qso_servicio",
                table: "qso_confirmacion",
                columns: new[] { "qso_id", "servicio" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ref_award_ref",
                table: "qso_referencia",
                columns: new[] { "award_code", "referencia", "propia" });

            migrationBuilder.CreateIndex(
                name: "ix_ref_propia",
                table: "qso_referencia",
                columns: new[] { "propia", "award_code", "referencia" });

            migrationBuilder.CreateIndex(
                name: "ix_ref_qso",
                table: "qso_referencia",
                column: "qso_id");

            // Busqueda por texto libre. Es una tabla FTS5 de contenido externo: no duplica el
            // texto, lee de qso, y tres disparadores la mantienen al dia pase lo que pase
            // (tambien si alguien escribe con SQL directo).
            migrationBuilder.Sql(
                """
                CREATE VIRTUAL TABLE qso_fts USING fts5 (
                    call, name, qth, comment, notes, country,
                    content = 'qso', content_rowid = 'id', tokenize = 'unicode61'
                );
                """);

            migrationBuilder.Sql(
                """
                CREATE TRIGGER qso_fts_alta AFTER INSERT ON qso BEGIN
                    INSERT INTO qso_fts (rowid, call, name, qth, comment, notes, country)
                    VALUES (new.id, new.call, new.name, new.qth, new.comment, new.notes, new.country);
                END;
                """);

            migrationBuilder.Sql(
                """
                CREATE TRIGGER qso_fts_baja AFTER DELETE ON qso BEGIN
                    INSERT INTO qso_fts (qso_fts, rowid, call, name, qth, comment, notes, country)
                    VALUES ('delete', old.id, old.call, old.name, old.qth, old.comment, old.notes, old.country);
                END;
                """);

            migrationBuilder.Sql(
                """
                CREATE TRIGGER qso_fts_cambio AFTER UPDATE ON qso BEGIN
                    INSERT INTO qso_fts (qso_fts, rowid, call, name, qth, comment, notes, country)
                    VALUES ('delete', old.id, old.call, old.name, old.qth, old.comment, old.notes, old.country);
                    INSERT INTO qso_fts (rowid, call, name, qth, comment, notes, country)
                    VALUES (new.id, new.call, new.name, new.qth, new.comment, new.notes, new.country);
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS qso_fts_cambio;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS qso_fts_baja;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS qso_fts_alta;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS qso_fts;");

            migrationBuilder.DropTable(
                name: "qso_campo_extra");

            migrationBuilder.DropTable(
                name: "qso_confirmacion");

            migrationBuilder.DropTable(
                name: "qso_referencia");

            migrationBuilder.DropTable(
                name: "qso");

            migrationBuilder.DropTable(
                name: "estacion");
        }
    }
}
