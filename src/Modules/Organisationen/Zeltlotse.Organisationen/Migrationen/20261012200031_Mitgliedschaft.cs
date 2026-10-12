using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zeltlotse.Organisationen.Migrationen
{
    /// <inheritdoc />
    internal partial class Mitgliedschaft : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "mitgliedschaft",
                schema: "organisationen",
                columns: table => new
                {
                    OrganisationId = table.Column<Guid>(type: "uuid", nullable: false),
                    PersonId = table.Column<Guid>(type: "uuid", nullable: false),
                    SeitAm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mitgliedschaft", x => new { x.OrganisationId, x.PersonId });
                });

            migrationBuilder.CreateIndex(
                name: "IX_mitgliedschaft_PersonId",
                schema: "organisationen",
                table: "mitgliedschaft",
                column: "PersonId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mitgliedschaft",
                schema: "organisationen");
        }
    }
}
