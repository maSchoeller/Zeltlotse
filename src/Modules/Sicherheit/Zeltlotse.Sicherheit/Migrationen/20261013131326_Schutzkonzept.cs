using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zeltlotse.Sicherheit.Migrationen
{
    /// <inheritdoc />
    internal partial class Schutzkonzept : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "schutzkonzept",
                schema: "sicherheit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "uuid", nullable: false),
                    FreizeitId = table.Column<Guid>(type: "uuid", nullable: false),
                    DokumentId = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_schutzkonzept", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "schutzkonzept_bestaetigung",
                schema: "sicherheit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "uuid", nullable: false),
                    FreizeitId = table.Column<Guid>(type: "uuid", nullable: false),
                    DokumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    PersonId = table.Column<Guid>(type: "uuid", nullable: false),
                    GelesenAm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_schutzkonzept_bestaetigung", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_schutzkonzept_FreizeitId",
                schema: "sicherheit",
                table: "schutzkonzept",
                column: "FreizeitId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_schutzkonzept_OrganisationId",
                schema: "sicherheit",
                table: "schutzkonzept",
                column: "OrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_schutzkonzept_bestaetigung_FreizeitId_DokumentId_PersonId",
                schema: "sicherheit",
                table: "schutzkonzept_bestaetigung",
                columns: new[] { "FreizeitId", "DokumentId", "PersonId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_schutzkonzept_bestaetigung_OrganisationId",
                schema: "sicherheit",
                table: "schutzkonzept_bestaetigung",
                column: "OrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_schutzkonzept_bestaetigung_PersonId",
                schema: "sicherheit",
                table: "schutzkonzept_bestaetigung",
                column: "PersonId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "schutzkonzept",
                schema: "sicherheit");

            migrationBuilder.DropTable(
                name: "schutzkonzept_bestaetigung",
                schema: "sicherheit");
        }
    }
}
