using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zeltlotse.Sicherheit.Migrationen
{
    /// <inheritdoc />
    internal partial class VorfallEreignis : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "Folgenummer",
                schema: "sicherheit",
                table: "vorfall_eintrag",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Folgenummer",
                schema: "sicherheit",
                table: "vorfall_eintrag");
        }
    }
}
