using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zeltlotse.Freizeiten.Migrationen
{
    /// <inheritdoc />
    internal partial class Module : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Finanzen",
                schema: "freizeiten",
                table: "freizeit",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "TeilnehmerZugang",
                schema: "freizeiten",
                table: "freizeit",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "Verpflegung",
                schema: "freizeiten",
                table: "freizeit",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Finanzen",
                schema: "freizeiten",
                table: "freizeit");

            migrationBuilder.DropColumn(
                name: "TeilnehmerZugang",
                schema: "freizeiten",
                table: "freizeit");

            migrationBuilder.DropColumn(
                name: "Verpflegung",
                schema: "freizeiten",
                table: "freizeit");
        }
    }
}
