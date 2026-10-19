using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zeltlotse.Kueche.Migrationen
{
    /// <inheritdoc />
    internal partial class Einkaufsliste : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "einkaufsposten",
                schema: "kueche",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "uuid", nullable: false),
                    FreizeitId = table.Column<Guid>(type: "uuid", nullable: false),
                    Bezeichnung = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Menge = table.Column<decimal>(type: "numeric(10,3)", precision: 10, scale: 3, nullable: true),
                    Einheit = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    Tag = table.Column<DateOnly>(type: "date", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_einkaufsposten", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "gericht",
                schema: "kueche",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "uuid", nullable: false),
                    FreizeitId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProgrammpunktId = table.Column<Guid>(type: "uuid", nullable: false),
                    RezeptId = table.Column<Guid>(type: "uuid", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gericht", x => x.Id);
                    table.ForeignKey(
                        name: "FK_gericht_rezept_RezeptId",
                        column: x => x.RezeptId,
                        principalSchema: "kueche",
                        principalTable: "rezept",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "menge_von_hand",
                schema: "kueche",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "uuid", nullable: false),
                    FreizeitId = table.Column<Guid>(type: "uuid", nullable: false),
                    GerichtId = table.Column<Guid>(type: "uuid", nullable: false),
                    ZutatId = table.Column<Guid>(type: "uuid", nullable: false),
                    Menge = table.Column<decimal>(type: "numeric(10,3)", precision: 10, scale: 3, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_menge_von_hand", x => x.Id);
                    table.ForeignKey(
                        name: "FK_menge_von_hand_gericht_GerichtId",
                        column: x => x.GerichtId,
                        principalSchema: "kueche",
                        principalTable: "gericht",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_menge_von_hand_zutat_ZutatId",
                        column: x => x.ZutatId,
                        principalSchema: "kueche",
                        principalTable: "zutat",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_einkaufsposten_FreizeitId",
                schema: "kueche",
                table: "einkaufsposten",
                column: "FreizeitId");

            migrationBuilder.CreateIndex(
                name: "IX_einkaufsposten_OrganisationId",
                schema: "kueche",
                table: "einkaufsposten",
                column: "OrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_gericht_FreizeitId",
                schema: "kueche",
                table: "gericht",
                column: "FreizeitId");

            migrationBuilder.CreateIndex(
                name: "IX_gericht_OrganisationId",
                schema: "kueche",
                table: "gericht",
                column: "OrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_gericht_RezeptId",
                schema: "kueche",
                table: "gericht",
                column: "RezeptId");

            migrationBuilder.CreateIndex(
                name: "IX_menge_von_hand_FreizeitId",
                schema: "kueche",
                table: "menge_von_hand",
                column: "FreizeitId");

            migrationBuilder.CreateIndex(
                name: "IX_menge_von_hand_GerichtId_ZutatId",
                schema: "kueche",
                table: "menge_von_hand",
                columns: new[] { "GerichtId", "ZutatId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_menge_von_hand_OrganisationId",
                schema: "kueche",
                table: "menge_von_hand",
                column: "OrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_menge_von_hand_ZutatId",
                schema: "kueche",
                table: "menge_von_hand",
                column: "ZutatId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "einkaufsposten",
                schema: "kueche");

            migrationBuilder.DropTable(
                name: "menge_von_hand",
                schema: "kueche");

            migrationBuilder.DropTable(
                name: "gericht",
                schema: "kueche");
        }
    }
}
