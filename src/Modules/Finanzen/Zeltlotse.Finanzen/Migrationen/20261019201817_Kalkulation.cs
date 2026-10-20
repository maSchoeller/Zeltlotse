using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zeltlotse.Finanzen.Migrationen
{
    /// <inheritdoc />
    internal partial class Kalkulation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "einnahmeposten",
                schema: "finanzen",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "uuid", nullable: false),
                    FreizeitId = table.Column<Guid>(type: "uuid", nullable: false),
                    Bezeichnung = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Betrag = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_einnahmeposten", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "kalkulation",
                schema: "finanzen",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "uuid", nullable: false),
                    FreizeitId = table.Column<Guid>(type: "uuid", nullable: false),
                    Teilnehmende = table.Column<int>(type: "integer", nullable: false),
                    Mitarbeitende = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_kalkulation", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "kalkulation_kategorie",
                schema: "finanzen",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "uuid", nullable: false),
                    FreizeitId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kategorie = table.Column<int>(type: "integer", nullable: false),
                    Fixkosten = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    ProPersonUndTag = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_kalkulation_kategorie", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_einnahmeposten_FreizeitId",
                schema: "finanzen",
                table: "einnahmeposten",
                column: "FreizeitId");

            migrationBuilder.CreateIndex(
                name: "IX_einnahmeposten_OrganisationId",
                schema: "finanzen",
                table: "einnahmeposten",
                column: "OrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_kalkulation_FreizeitId",
                schema: "finanzen",
                table: "kalkulation",
                column: "FreizeitId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_kalkulation_OrganisationId",
                schema: "finanzen",
                table: "kalkulation",
                column: "OrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_kalkulation_kategorie_FreizeitId_Kategorie",
                schema: "finanzen",
                table: "kalkulation_kategorie",
                columns: new[] { "FreizeitId", "Kategorie" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_kalkulation_kategorie_OrganisationId",
                schema: "finanzen",
                table: "kalkulation_kategorie",
                column: "OrganisationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "einnahmeposten",
                schema: "finanzen");

            migrationBuilder.DropTable(
                name: "kalkulation",
                schema: "finanzen");

            migrationBuilder.DropTable(
                name: "kalkulation_kategorie",
                schema: "finanzen");
        }
    }
}
