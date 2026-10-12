using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zeltlotse.Personen.Migrationen
{
    /// <inheritdoc />
    internal partial class Herkunft : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Herkunft",
                schema: "personen",
                table: "person",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Herkunft",
                schema: "personen",
                table: "person");
        }
    }
}
