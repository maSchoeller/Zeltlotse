using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zeltlotse.Finanzen.Migrationen
{
    /// <inheritdoc />
    internal partial class Belege : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "beleg",
                schema: "finanzen",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "uuid", nullable: false),
                    FreizeitId = table.Column<Guid>(type: "uuid", nullable: false),
                    ErfasstAm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Kategorie = table.Column<int>(type: "integer", nullable: false),
                    Betrag = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    Bezeichnung = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    PersonId = table.Column<Guid>(type: "uuid", nullable: false),
                    Erstattet = table.Column<bool>(type: "boolean", nullable: false),
                    DateiArt = table.Column<int>(type: "integer", nullable: true),
                    Dateiname = table.Column<string>(type: "character varying(205)", maxLength: 205, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_beleg", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_beleg_FreizeitId",
                schema: "finanzen",
                table: "beleg",
                column: "FreizeitId");

            migrationBuilder.CreateIndex(
                name: "IX_beleg_OrganisationId",
                schema: "finanzen",
                table: "beleg",
                column: "OrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_beleg_PersonId",
                schema: "finanzen",
                table: "beleg",
                column: "PersonId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "beleg",
                schema: "finanzen");
        }
    }
}
