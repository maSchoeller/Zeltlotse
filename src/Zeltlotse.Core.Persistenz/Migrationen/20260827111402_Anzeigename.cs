using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zeltlotse.Core.Persistenz.Migrationen
{
    /// <inheritdoc />
    public partial class Anzeigename : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "nutzer",
                type: "character varying(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "einladung",
                type: "character varying(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Name",
                table: "nutzer");

            migrationBuilder.DropColumn(
                name: "Name",
                table: "einladung");
        }
    }
}
