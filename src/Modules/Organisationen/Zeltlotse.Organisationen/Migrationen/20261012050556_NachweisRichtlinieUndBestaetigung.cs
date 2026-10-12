using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zeltlotse.Organisationen.Migrationen
{
    /// <inheritdoc />
    internal partial class NachweisRichtlinieUndBestaetigung : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "nachweis_bestaetigung",
                schema: "organisationen",
                columns: table => new
                {
                    OrganisationId = table.Column<Guid>(type: "uuid", nullable: false),
                    PersonId = table.Column<Guid>(type: "uuid", nullable: false),
                    Art = table.Column<short>(type: "smallint", nullable: false),
                    EingesehenAm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    GueltigBis = table.Column<DateOnly>(type: "date", nullable: false),
                    GeprueftVon = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_nachweis_bestaetigung", x => new { x.OrganisationId, x.PersonId, x.Art });
                });

            migrationBuilder.CreateTable(
                name: "nachweis_richtlinie",
                schema: "organisationen",
                columns: table => new
                {
                    OrganisationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Art = table.Column<short>(type: "smallint", nullable: false),
                    Pflicht = table.Column<bool>(type: "boolean", nullable: false),
                    GueltigkeitMonate = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_nachweis_richtlinie", x => new { x.OrganisationId, x.Art });
                });

            migrationBuilder.CreateIndex(
                name: "IX_nachweis_bestaetigung_PersonId",
                schema: "organisationen",
                table: "nachweis_bestaetigung",
                column: "PersonId");

            // FR-9, NFR-5: bestehende Organisationen bekommen die Vorgabewerte (neue entstehen mit ihnen).
            migrationBuilder.Sql(
                """
                INSERT INTO organisationen.nachweis_richtlinie ("OrganisationId", "Art", "Pflicht", "GueltigkeitMonate")
                SELECT o."Id", v.art, v.pflicht, v.monate
                FROM organisationen.organisation o
                CROSS JOIN (VALUES (1::smallint, true, 60), (2::smallint, false, 36), (3::smallint, false, 24), (4::smallint, false, 60)) AS v(art, pflicht, monate)
                ON CONFLICT DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "nachweis_bestaetigung",
                schema: "organisationen");

            migrationBuilder.DropTable(
                name: "nachweis_richtlinie",
                schema: "organisationen");
        }
    }
}
