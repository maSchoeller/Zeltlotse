using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zeltlotse.Inventar.Migrationen
{
    /// <inheritdoc />
    internal partial class InventarUndReservierung : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "inventar_artikel",
                schema: "inventar",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Bezeichnung = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Menge = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inventar_artikel", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "reservierung",
                schema: "inventar",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ArtikelId = table.Column<Guid>(type: "uuid", nullable: false),
                    FreizeitId = table.Column<Guid>(type: "uuid", nullable: false),
                    Von = table.Column<DateOnly>(type: "date", nullable: false),
                    Bis = table.Column<DateOnly>(type: "date", nullable: false),
                    MaterialId = table.Column<Guid>(type: "uuid", nullable: true),
                    Folgenummer = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reservierung", x => x.Id);
                    table.ForeignKey(
                        name: "FK_reservierung_inventar_artikel_ArtikelId",
                        column: x => x.ArtikelId,
                        principalSchema: "inventar",
                        principalTable: "inventar_artikel",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_inventar_artikel_OrganisationId",
                schema: "inventar",
                table: "inventar_artikel",
                column: "OrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_reservierung_ArtikelId",
                schema: "inventar",
                table: "reservierung",
                column: "ArtikelId");

            migrationBuilder.CreateIndex(
                name: "IX_reservierung_FreizeitId",
                schema: "inventar",
                table: "reservierung",
                column: "FreizeitId");

            migrationBuilder.CreateIndex(
                name: "IX_reservierung_OrganisationId",
                schema: "inventar",
                table: "reservierung",
                column: "OrganisationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "reservierung",
                schema: "inventar");

            migrationBuilder.DropTable(
                name: "inventar_artikel",
                schema: "inventar");
        }
    }
}
