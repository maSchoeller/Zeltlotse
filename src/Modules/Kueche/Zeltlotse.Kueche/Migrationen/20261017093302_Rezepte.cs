using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zeltlotse.Kueche.Migrationen
{
    /// <inheritdoc />
    internal partial class Rezepte : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "rezept",
                schema: "kueche",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "uuid", nullable: false),
                    FreizeitId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    BasisPersonen = table.Column<int>(type: "integer", nullable: false),
                    Zubereitung = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rezept", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "zutat",
                schema: "kueche",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "uuid", nullable: false),
                    FreizeitId = table.Column<Guid>(type: "uuid", nullable: false),
                    RezeptId = table.Column<Guid>(type: "uuid", nullable: false),
                    Bezeichnung = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Menge = table.Column<decimal>(type: "numeric(10,3)", precision: 10, scale: 3, nullable: true),
                    Einheit = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    Allergene = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_zutat", x => x.Id);
                    table.ForeignKey(
                        name: "FK_zutat_rezept_RezeptId",
                        column: x => x.RezeptId,
                        principalSchema: "kueche",
                        principalTable: "rezept",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_rezept_FreizeitId",
                schema: "kueche",
                table: "rezept",
                column: "FreizeitId");

            migrationBuilder.CreateIndex(
                name: "IX_rezept_OrganisationId",
                schema: "kueche",
                table: "rezept",
                column: "OrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_zutat_FreizeitId",
                schema: "kueche",
                table: "zutat",
                column: "FreizeitId");

            migrationBuilder.CreateIndex(
                name: "IX_zutat_OrganisationId",
                schema: "kueche",
                table: "zutat",
                column: "OrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_zutat_RezeptId",
                schema: "kueche",
                table: "zutat",
                column: "RezeptId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "zutat",
                schema: "kueche");

            migrationBuilder.DropTable(
                name: "rezept",
                schema: "kueche");
        }
    }
}
