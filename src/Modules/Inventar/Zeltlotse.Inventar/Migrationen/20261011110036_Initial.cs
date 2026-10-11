using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zeltlotse.Inventar.Migrationen
{
    /// <inheritdoc />
    internal partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "inventar");

            migrationBuilder.CreateTable(
                name: "inbox",
                schema: "inventar",
                columns: table => new
                {
                    EreignisId = table.Column<Guid>(type: "uuid", nullable: false),
                    Empfaenger = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    VerarbeitetAm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inbox", x => new { x.EreignisId, x.Empfaenger });
                });

            migrationBuilder.CreateTable(
                name: "inbox_stand",
                schema: "inventar",
                columns: table => new
                {
                    DatensatzId = table.Column<Guid>(type: "uuid", nullable: false),
                    Empfaenger = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Folgenummer = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inbox_stand", x => new { x.DatensatzId, x.Empfaenger });
                });

            migrationBuilder.CreateTable(
                name: "outbox",
                schema: "inventar",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Typ = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Inhalt = table.Column<string>(type: "jsonb", nullable: false),
                    Zeitpunkt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "uuid", nullable: true),
                    DatensatzId = table.Column<Guid>(type: "uuid", nullable: false),
                    Folgenummer = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_zustellung",
                schema: "inventar",
                columns: table => new
                {
                    EreignisId = table.Column<Guid>(type: "uuid", nullable: false),
                    Empfaenger = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Versuche = table.Column<int>(type: "integer", nullable: false),
                    NaechsterVersuch = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ErledigtAm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LetzterFehler = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_zustellung", x => new { x.EreignisId, x.Empfaenger });
                    table.ForeignKey(
                        name: "FK_outbox_zustellung_outbox_EreignisId",
                        column: x => x.EreignisId,
                        principalSchema: "inventar",
                        principalTable: "outbox",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_outbox_DatensatzId_Folgenummer",
                schema: "inventar",
                table: "outbox",
                columns: new[] { "DatensatzId", "Folgenummer" });

            migrationBuilder.CreateIndex(
                name: "IX_outbox_zustellung_ErledigtAm_NaechsterVersuch",
                schema: "inventar",
                table: "outbox_zustellung",
                columns: new[] { "ErledigtAm", "NaechsterVersuch" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inbox",
                schema: "inventar");

            migrationBuilder.DropTable(
                name: "inbox_stand",
                schema: "inventar");

            migrationBuilder.DropTable(
                name: "outbox_zustellung",
                schema: "inventar");

            migrationBuilder.DropTable(
                name: "outbox",
                schema: "inventar");
        }
    }
}
