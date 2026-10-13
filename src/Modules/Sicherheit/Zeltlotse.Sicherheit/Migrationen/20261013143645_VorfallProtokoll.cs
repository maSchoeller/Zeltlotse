using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zeltlotse.Sicherheit.Migrationen
{
    /// <inheritdoc />
    internal partial class VorfallProtokoll : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "vorfall_eintrag",
                schema: "sicherheit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "uuid", nullable: false),
                    FreizeitId = table.Column<Guid>(type: "uuid", nullable: false),
                    PersonId = table.Column<Guid>(type: "uuid", nullable: false),
                    VerfasstVon = table.Column<Guid>(type: "uuid", nullable: false),
                    Art = table.Column<short>(type: "smallint", nullable: false),
                    Text = table.Column<byte[]>(type: "bytea", nullable: false),
                    ErfasstAm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ArchiviertAm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_vorfall_eintrag", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_vorfall_eintrag_FreizeitId_PersonId",
                schema: "sicherheit",
                table: "vorfall_eintrag",
                columns: new[] { "FreizeitId", "PersonId" });

            migrationBuilder.CreateIndex(
                name: "IX_vorfall_eintrag_OrganisationId",
                schema: "sicherheit",
                table: "vorfall_eintrag",
                column: "OrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_vorfall_eintrag_PersonId",
                schema: "sicherheit",
                table: "vorfall_eintrag",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_vorfall_eintrag_VerfasstVon",
                schema: "sicherheit",
                table: "vorfall_eintrag",
                column: "VerfasstVon");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "vorfall_eintrag",
                schema: "sicherheit");
        }
    }
}
