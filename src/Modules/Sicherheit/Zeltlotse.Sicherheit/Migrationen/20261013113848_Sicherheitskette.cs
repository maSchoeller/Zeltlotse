using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zeltlotse.Sicherheit.Migrationen
{
    /// <inheritdoc />
    internal partial class Sicherheitskette : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sicherheitskette",
                schema: "sicherheit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "uuid", nullable: false),
                    FreizeitId = table.Column<Guid>(type: "uuid", nullable: false),
                    WasTun = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    LeitungTelefon = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    HeimatName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    HeimatTelefon = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sicherheitskette", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_sicherheitskette_FreizeitId",
                schema: "sicherheit",
                table: "sicherheitskette",
                column: "FreizeitId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sicherheitskette_OrganisationId",
                schema: "sicherheit",
                table: "sicherheitskette",
                column: "OrganisationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sicherheitskette",
                schema: "sicherheit");
        }
    }
}
