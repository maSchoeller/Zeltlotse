using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zeltlotse.Testmodul.Migrationen
{
    /// <inheritdoc />
    internal partial class Vertraulich : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "Vertraulich",
                schema: "testmodul",
                table: "notiz",
                type: "bytea",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Vertraulich",
                schema: "testmodul",
                table: "notiz");
        }
    }
}
