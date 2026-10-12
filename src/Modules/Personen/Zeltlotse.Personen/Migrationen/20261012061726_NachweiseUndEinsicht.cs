using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zeltlotse.Personen.Migrationen
{
    /// <inheritdoc />
    internal partial class NachweiseUndEinsicht : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "nachweis",
                schema: "personen",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PersonId = table.Column<Guid>(type: "uuid", nullable: false),
                    Art = table.Column<short>(type: "smallint", nullable: false),
                    Ausstellungsdatum = table.Column<DateOnly>(type: "date", nullable: false),
                    HochgeladenAm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Ablehnungshinweis = table.Column<byte[]>(type: "bytea", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_nachweis", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "nachweis_einsicht",
                schema: "personen",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "uuid", nullable: false),
                    PersonId = table.Column<Guid>(type: "uuid", nullable: false),
                    FreizeitId = table.Column<Guid>(type: "uuid", nullable: true),
                    AngefragtVon = table.Column<Guid>(type: "uuid", nullable: true),
                    PruefendePersonId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    AngefragtAm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FreigegebenAm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FreigegebenBis = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AbgeschlossenAm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    NurArten = table.Column<short[]>(type: "smallint[]", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    Folgenummer = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_nachweis_einsicht", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "nachweis_einsicht_posten",
                schema: "personen",
                columns: table => new
                {
                    EinsichtId = table.Column<Guid>(type: "uuid", nullable: false),
                    NachweisId = table.Column<Guid>(type: "uuid", nullable: false),
                    Art = table.Column<short>(type: "smallint", nullable: false),
                    Ergebnis = table.Column<short>(type: "smallint", nullable: false),
                    EntschiedenAm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_nachweis_einsicht_posten", x => new { x.EinsichtId, x.NachweisId });
                    table.ForeignKey(
                        name: "FK_nachweis_einsicht_posten_nachweis_einsicht_EinsichtId",
                        column: x => x.EinsichtId,
                        principalSchema: "personen",
                        principalTable: "nachweis_einsicht",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_nachweis_PersonId_Art",
                schema: "personen",
                table: "nachweis",
                columns: new[] { "PersonId", "Art" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_nachweis_einsicht_offen",
                schema: "personen",
                table: "nachweis_einsicht",
                columns: new[] { "PersonId", "OrganisationId" },
                unique: true,
                filter: "\"Status\" <> 3");

            migrationBuilder.CreateIndex(
                name: "IX_nachweis_einsicht_OrganisationId_PersonId",
                schema: "personen",
                table: "nachweis_einsicht",
                columns: new[] { "OrganisationId", "PersonId" });

            migrationBuilder.CreateIndex(
                name: "IX_nachweis_einsicht_PersonId",
                schema: "personen",
                table: "nachweis_einsicht",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_nachweis_einsicht_posten_NachweisId",
                schema: "personen",
                table: "nachweis_einsicht_posten",
                column: "NachweisId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "nachweis",
                schema: "personen");

            migrationBuilder.DropTable(
                name: "nachweis_einsicht_posten",
                schema: "personen");

            migrationBuilder.DropTable(
                name: "nachweis_einsicht",
                schema: "personen");
        }
    }
}
