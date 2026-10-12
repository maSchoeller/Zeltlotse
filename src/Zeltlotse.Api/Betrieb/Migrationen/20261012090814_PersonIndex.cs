using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zeltlotse.Api.Betrieb.Migrationen
{
    /// <inheritdoc />
    internal partial class PersonIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_sicherheitsprotokoll_PersonId",
                schema: "betrieb",
                table: "sicherheitsprotokoll",
                column: "PersonId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_sicherheitsprotokoll_PersonId",
                schema: "betrieb",
                table: "sicherheitsprotokoll");
        }
    }
}
