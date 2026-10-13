using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zeltlotse.Programm.Migrationen
{
    /// <inheritdoc />
    internal partial class Programmpunkte : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "programmpunkt",
                schema: "programm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "uuid", nullable: false),
                    FreizeitId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tag = table.Column<DateOnly>(type: "date", nullable: false),
                    Beginn = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    Titel = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Mahlzeit = table.Column<short>(type: "smallint", nullable: true),
                    PersonId = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_programmpunkt", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_programmpunkt_FreizeitId_Tag",
                schema: "programm",
                table: "programmpunkt",
                columns: new[] { "FreizeitId", "Tag" });

            migrationBuilder.CreateIndex(
                name: "IX_programmpunkt_FreizeitId_Tag_Mahlzeit",
                schema: "programm",
                table: "programmpunkt",
                columns: new[] { "FreizeitId", "Tag", "Mahlzeit" },
                unique: true,
                filter: "\"Mahlzeit\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_programmpunkt_OrganisationId",
                schema: "programm",
                table: "programmpunkt",
                column: "OrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_programmpunkt_PersonId",
                schema: "programm",
                table: "programmpunkt",
                column: "PersonId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "programmpunkt",
                schema: "programm");
        }
    }
}
