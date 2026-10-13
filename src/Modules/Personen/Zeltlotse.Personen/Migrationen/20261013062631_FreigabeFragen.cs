using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zeltlotse.Personen.Migrationen
{
    /// <inheritdoc />
    internal partial class FreigabeFragen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string[]>(
                name: "Fragen",
                schema: "personen",
                table: "freigabe",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Fragen",
                schema: "personen",
                table: "freigabe");
        }
    }
}
