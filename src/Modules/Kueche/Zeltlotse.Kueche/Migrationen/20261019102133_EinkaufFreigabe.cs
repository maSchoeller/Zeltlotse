using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zeltlotse.Kueche.Migrationen
{
    /// <inheritdoc />
    internal partial class EinkaufFreigabe : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Freigegeben",
                schema: "kueche",
                table: "einkaufsposten",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "Gekauft",
                schema: "kueche",
                table: "einkaufsposten",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "posten_stand",
                schema: "kueche",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "uuid", nullable: false),
                    FreizeitId = table.Column<Guid>(type: "uuid", nullable: false),
                    GerichtId = table.Column<Guid>(type: "uuid", nullable: false),
                    ZutatId = table.Column<Guid>(type: "uuid", nullable: false),
                    Freigegeben = table.Column<bool>(type: "boolean", nullable: false),
                    Gekauft = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_posten_stand", x => x.Id);
                    table.ForeignKey(
                        name: "FK_posten_stand_gericht_GerichtId",
                        column: x => x.GerichtId,
                        principalSchema: "kueche",
                        principalTable: "gericht",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_posten_stand_zutat_ZutatId",
                        column: x => x.ZutatId,
                        principalSchema: "kueche",
                        principalTable: "zutat",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_posten_stand_FreizeitId",
                schema: "kueche",
                table: "posten_stand",
                column: "FreizeitId");

            migrationBuilder.CreateIndex(
                name: "IX_posten_stand_GerichtId_ZutatId",
                schema: "kueche",
                table: "posten_stand",
                columns: new[] { "GerichtId", "ZutatId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_posten_stand_OrganisationId",
                schema: "kueche",
                table: "posten_stand",
                column: "OrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_posten_stand_ZutatId",
                schema: "kueche",
                table: "posten_stand",
                column: "ZutatId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "posten_stand",
                schema: "kueche");

            migrationBuilder.DropColumn(
                name: "Freigegeben",
                schema: "kueche",
                table: "einkaufsposten");

            migrationBuilder.DropColumn(
                name: "Gekauft",
                schema: "kueche",
                table: "einkaufsposten");
        }
    }
}
