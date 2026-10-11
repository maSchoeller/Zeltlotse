using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zeltlotse.Konten.Migrationen
{
    /// <inheritdoc />
    internal partial class GlobalAdmin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "GlobalAdmin",
                schema: "konten",
                table: "konto",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GlobalAdmin",
                schema: "konten",
                table: "konto");
        }
    }
}
