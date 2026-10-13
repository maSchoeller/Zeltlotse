using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zeltlotse.Freizeiten.Migrationen
{
    /// <inheritdoc />
    internal partial class FragenAuswahl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string[]>(
                name: "FragenMitarbeitende",
                schema: "freizeiten",
                table: "freizeit",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            migrationBuilder.AddColumn<string[]>(
                name: "FragenTeilnehmende",
                schema: "freizeiten",
                table: "freizeit",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FragenMitarbeitende",
                schema: "freizeiten",
                table: "freizeit");

            migrationBuilder.DropColumn(
                name: "FragenTeilnehmende",
                schema: "freizeiten",
                table: "freizeit");
        }
    }
}
