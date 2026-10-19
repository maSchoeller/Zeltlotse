using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zeltlotse.Kueche.Migrationen
{
    /// <inheritdoc />
    internal partial class Restetag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "mahlzeit_stand",
                schema: "kueche",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "uuid", nullable: false),
                    FreizeitId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProgrammpunktId = table.Column<Guid>(type: "uuid", nullable: false),
                    Restetag = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mahlzeit_stand", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_mahlzeit_stand_FreizeitId_ProgrammpunktId",
                schema: "kueche",
                table: "mahlzeit_stand",
                columns: new[] { "FreizeitId", "ProgrammpunktId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_mahlzeit_stand_OrganisationId",
                schema: "kueche",
                table: "mahlzeit_stand",
                column: "OrganisationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mahlzeit_stand",
                schema: "kueche");
        }
    }
}
