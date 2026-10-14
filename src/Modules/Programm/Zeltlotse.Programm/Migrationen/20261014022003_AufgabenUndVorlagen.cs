using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zeltlotse.Programm.Migrationen
{
    /// <inheritdoc />
    internal partial class AufgabenUndVorlagen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "aufgaben_vorlage",
                schema: "programm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Titel = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Phase = table.Column<short>(type: "smallint", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_aufgaben_vorlage", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "aufgaben_vorlagen_uebernahme",
                schema: "programm",
                columns: table => new
                {
                    FreizeitId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "uuid", nullable: false),
                    UebernommenAm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_aufgaben_vorlagen_uebernahme", x => x.FreizeitId);
                });

            migrationBuilder.CreateTable(
                name: "aufgabe",
                schema: "programm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "uuid", nullable: false),
                    FreizeitId = table.Column<Guid>(type: "uuid", nullable: false),
                    Titel = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Phase = table.Column<short>(type: "smallint", nullable: false),
                    ProgrammpunktId = table.Column<Guid>(type: "uuid", nullable: true),
                    PersonId = table.Column<Guid>(type: "uuid", nullable: true),
                    Erledigt = table.Column<bool>(type: "boolean", nullable: false),
                    VorlageId = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_aufgabe", x => x.Id);
                    table.ForeignKey(
                        name: "FK_aufgabe_aufgaben_vorlage_VorlageId",
                        column: x => x.VorlageId,
                        principalSchema: "programm",
                        principalTable: "aufgaben_vorlage",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_aufgabe_programmpunkt_ProgrammpunktId",
                        column: x => x.ProgrammpunktId,
                        principalSchema: "programm",
                        principalTable: "programmpunkt",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_aufgabe_FreizeitId",
                schema: "programm",
                table: "aufgabe",
                column: "FreizeitId");

            migrationBuilder.CreateIndex(
                name: "IX_aufgabe_OrganisationId",
                schema: "programm",
                table: "aufgabe",
                column: "OrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_aufgabe_PersonId",
                schema: "programm",
                table: "aufgabe",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_aufgabe_ProgrammpunktId",
                schema: "programm",
                table: "aufgabe",
                column: "ProgrammpunktId");

            migrationBuilder.CreateIndex(
                name: "IX_aufgabe_VorlageId",
                schema: "programm",
                table: "aufgabe",
                column: "VorlageId");

            migrationBuilder.CreateIndex(
                name: "IX_aufgaben_vorlage_OrganisationId",
                schema: "programm",
                table: "aufgaben_vorlage",
                column: "OrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_aufgaben_vorlagen_uebernahme_OrganisationId",
                schema: "programm",
                table: "aufgaben_vorlagen_uebernahme",
                column: "OrganisationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "aufgabe",
                schema: "programm");

            migrationBuilder.DropTable(
                name: "aufgaben_vorlagen_uebernahme",
                schema: "programm");

            migrationBuilder.DropTable(
                name: "aufgaben_vorlage",
                schema: "programm");
        }
    }
}
