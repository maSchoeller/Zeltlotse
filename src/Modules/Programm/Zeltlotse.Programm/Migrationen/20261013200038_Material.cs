using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zeltlotse.Programm.Migrationen
{
    /// <inheritdoc />
    internal partial class Material : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "material",
                schema: "programm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "uuid", nullable: false),
                    FreizeitId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProgrammpunktId = table.Column<Guid>(type: "uuid", nullable: false),
                    Bezeichnung = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Menge = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    PersonId = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_material", x => x.Id);
                    table.ForeignKey(
                        name: "FK_material_programmpunkt_ProgrammpunktId",
                        column: x => x.ProgrammpunktId,
                        principalSchema: "programm",
                        principalTable: "programmpunkt",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_material_FreizeitId",
                schema: "programm",
                table: "material",
                column: "FreizeitId");

            migrationBuilder.CreateIndex(
                name: "IX_material_OrganisationId",
                schema: "programm",
                table: "material",
                column: "OrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_material_PersonId",
                schema: "programm",
                table: "material",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_material_ProgrammpunktId",
                schema: "programm",
                table: "material",
                column: "ProgrammpunktId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "material",
                schema: "programm");
        }
    }
}
